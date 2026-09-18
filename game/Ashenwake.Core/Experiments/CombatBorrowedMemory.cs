using Ashenwake.Core.Content;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed record BorrowedMemoryState
{
    public int SchemaVersion { get; init; } = 1;
    public string RulesVersion { get; init; } = "borrowed-memory.1";
    public string ContentHash { get; init; } = "";
    public ExperimentRules Rules { get; init; } = null!;
    public long RunId { get; init; }
    public long StartedTick { get; init; }
    public string Status { get; set; } = "Pending";
    public int SourceActorId { get; set; }
    public int SourceRoom { get; set; }
    public Position MemoryPosition { get; set; }
    public long BoundTick { get; set; }
    public long ExpiresTick { get; set; }
    public long UsedTick { get; set; }
    public long EchoActionId { get; set; }
    public long HazardId { get; set; }
    public Position HazardPosition { get; set; }
    public long WarningUntil { get; set; }
    public long HazardUntil { get; set; }
}
public sealed record BorrowedMemoryView(string Status, string SuppressedMindId, int SourceActorId, Position MemoryPosition,
    int BindRadius, bool CanBind, bool CanRelease, string EchoSkillId, int RemainingTicks, long HazardId,
    Position HazardPosition, int HazardRadius, string HazardStage, int HazardRemainingTicks);

public sealed partial class CombatSession
{
    private bool BorrowedMindSuppressed => _state.Experiment?.Status is "Pending" or "Offered" or "Bound";
    private bool BorrowedEchoAvailable => _state.Experiment is { Status: "Bound" } e && e.ExpiresTick > Tick && _state.Areas.Count < MaxAreas;
    public BorrowedMemoryView? BorrowedMemory => _state.Experiment is not { } e ? null : new(e.Status,
        BorrowedMindSuppressed ? _state.Fragments.GetValueOrDefault("Mind", "") : "", e.SourceActorId, e.MemoryPosition, e.Rules.BindRadius,
        e.Status == "Offered" && Player.Health > 0 && Player.Pending is null && Player.RecoveryUntil <= Tick && _spatial.HasLineOfSight(Player.Position, e.MemoryPosition) && Position.DistanceSquared(Player.Position, e.MemoryPosition) <= (long)e.Rules.BindRadius * e.Rules.BindRadius,
        BorrowedMindSuppressed, BorrowedEchoAvailable ? e.Rules.EchoSkillId : "", (int)Math.Max(0, e.ExpiresTick - Tick),
        e.HazardId, e.HazardPosition, e.Rules.HazardRadius, e.HazardId == 0 || !_state.Areas.Any(a => a.Id == e.HazardId) ? "None" : Tick < e.WarningUntil ? "Warning" : "Active",
        (int)Math.Max(0, (Tick < e.WarningUntil ? e.WarningUntil : e.HazardUntil) - Tick));
    internal BorrowedMemoryState? CaptureBorrowedMemory() => _state.Experiment is null ? null : JsonData.Copy(_state.Experiment);
    internal void BeginBorrowedMemory(ExperimentRules rules)
    {
        ExperimentContent.ValidateRules(rules);
        if (!_content.Skills.Any(s => s.Id == rules.EchoSkillId && s.Discipline == "Echo" && s.Shape == "Area" && s.Cost == 0 && s.Generate == 0 && s.Family == DamageFamily.Storm)) throw new InvalidDataException("The authored Echo ability is unavailable.");
        if (_state.Experiment is not null || _state.Endgame is not { Manifest.Kind: "Fracture", EncounterIndex: 0 } e || Player.Health <= 0) throw new InvalidDataException("Borrowed Memory requires a newly entered Fracture.");
        _state.CapturedSkillId = ""; _state.CapturedUntil = Tick;
        _state.Experiment = new() { ContentHash = JsonData.Hash(rules), Rules = JsonData.Copy(rules), RunId = e.Manifest.RunId, StartedTick = Tick };
        ValidateBorrowedMemory();
    }
    internal BorrowedMemoryState? DetachBorrowedMemory()
    {
        var previous = CaptureBorrowedMemory();
        if (_state.Experiment?.Status == "Bound") { _state.CapturedSkillId = ""; _state.CapturedUntil = Tick; }
        _state.Experiment = null; return previous;
    }
    internal void RestoreBorrowedMemory(BorrowedMemoryState memory, bool changedRoom)
    {
        if (_state.Experiment is not null) throw new InvalidDataException("Duplicate memory context.");
        var next = JsonData.Copy(memory);
        if (changedRoom)
        {
            if (next.Status == "Offered") next.Status = "Released";
            next.HazardId = 0; next.WarningUntil = 0; next.HazardUntil = 0;
        }
        _state.Experiment = next;
        if (next.Status == "Bound") { _state.CapturedSkillId = next.Rules.EchoSkillId; _state.CapturedUntil = next.ExpiresTick; }
        UpdateBorrowedMemory(); ValidateBorrowedMemory();
    }
    internal bool BindBorrowedMemory(int sourceActorId)
    {
        if (_state.Experiment is not { Status: "Offered" } e || e.SourceActorId != sourceActorId || Player.Health <= 0 || Player.Pending is not null || Player.RecoveryUntil > Tick ||
            Position.DistanceSquared(Player.Position, e.MemoryPosition) > (long)e.Rules.BindRadius * e.Rules.BindRadius || !_spatial.HasLineOfSight(Player.Position, e.MemoryPosition)) return false;
        e.Status = "Bound"; e.BoundTick = Tick; e.ExpiresTick = Tick + e.Rules.EchoLifetimeTicks;
        _state.CapturedSkillId = e.Rules.EchoSkillId; _state.CapturedUntil = e.ExpiresTick; return true;
    }
    internal bool ReleaseBorrowedMemory()
    {
        if (!BorrowedMindSuppressed) return false;
        if (_state.Experiment!.Status == "Bound") { _state.CapturedSkillId = ""; _state.CapturedUntil = Tick; }
        _state.Experiment.Status = "Released"; return true;
    }
    private void OfferBorrowedMemory(CombatActor target)
    {
        if (_state.Experiment is not { Status: "Pending" } e || !target.Elite || !CampaignRewardEligible(target)) return;
        e.Status = "Offered"; e.SourceActorId = target.Id; e.SourceRoom = _state.Endgame!.EncounterIndex; e.MemoryPosition = target.Position;
        Emit("ExperimentMemoryDropped", 1, target.Id, content: e.Rules.Id);
    }
    private void ObserveBorrowedMemory(CombatEvent ev)
    {
        if (_state.Experiment is not { } e) return;
        if (ev.Kind == "EntityKilled" && ev.TargetId == 1) { e.Status = "Lost"; _state.CapturedSkillId = ""; _state.CapturedUntil = Tick; return; }
        if (ev.Kind != "CapturedAbilityUsed" || e.Status != "Bound") return;
        e.Status = "Spent"; e.UsedTick = Tick; e.EchoActionId = ev.ActionId;
        var source = _state.Actors.Where(a => a.Faction == CombatFaction.Enemy).OrderBy(a => a.Health <= 0).ThenBy(a => a.Id).FirstOrDefault();
        if (source is null || _state.Areas.Count >= MaxAreas) throw new InvalidDataException("Borrowed Echo hazard reservation is unavailable.");
        e.HazardId = _state.NextObjectId++; e.HazardPosition = Player.Position; e.WarningUntil = Tick + e.Rules.WarningTicks; e.HazardUntil = e.WarningUntil + e.Rules.HazardTicks;
        _state.Areas.Add(new(e.HazardId, source.Id, source.Id, Player.Position, e.Rules.HazardRadius, "enemy.stormbound", e.Rules.HazardDamage, DamageFamily.Storm, e.WarningUntil, e.HazardUntil, ev.ActionId, 0));
        Emit("ExperimentStormWarning", source.Id, 1, e.Rules.WarningTicks, e.Rules.Id, ev.ActionId);
    }
    private void UpdateBorrowedMemory()
    {
        if (_state.Experiment is not { } e) return;
        if (e.Status == "Bound" && e.ExpiresTick <= Tick) { e.Status = "Expired"; _state.CapturedSkillId = ""; _state.CapturedUntil = Tick; }
        if (Player.Health <= 0 && e.Status is "Pending" or "Offered" or "Bound") { e.Status = "Lost"; _state.CapturedSkillId = ""; _state.CapturedUntil = Tick; }
    }
    private void ValidateBorrowedMemory()
    {
        if (_state.Experiment is not { } e) return;
        ExperimentContent.ValidateRules(e.Rules);
        bool valid = e.SchemaVersion == 1 && e.RulesVersion == "borrowed-memory.1" && e.ContentHash == JsonData.Hash(e.Rules) && _state.Endgame is { Manifest.Kind: "Fracture" } g &&
            e.RunId == g.Manifest.RunId && e.StartedTick >= 0 && e.StartedTick <= Tick && e.Status is "Pending" or "Offered" or "Bound" or "Spent" or "Released" or "Expired" or "Lost" &&
            e.SourceActorId >= 0 && e.SourceActorId < _state.NextActorId && e.SourceRoom >= 0 && e.SourceRoom <= g.EncounterIndex &&
            e.BoundTick >= 0 && e.BoundTick <= Tick && e.ExpiresTick >= 0 && e.ExpiresTick <= Tick + e.Rules.EchoLifetimeTicks &&
            e.UsedTick >= 0 && e.UsedTick <= Tick && e.EchoActionId >= 0 && e.EchoActionId < _state.NextActionId &&
            e.HazardId >= 0 && e.HazardId < _state.NextObjectId && e.WarningUntil >= 0 && e.WarningUntil <= Tick + e.Rules.WarningTicks && e.HazardUntil >= 0 && e.HazardUntil <= Tick + e.Rules.WarningTicks + e.Rules.HazardTicks;
        if (!valid || e.Status == "Pending" && e.SourceActorId != 0 || e.Status is "Offered" or "Bound" or "Spent" or "Expired" && e.SourceActorId == 0 ||
            e.Status == "Bound" && (_state.CapturedSkillId != e.Rules.EchoSkillId || _state.CapturedUntil != e.ExpiresTick || e.EchoActionId != 0) ||
            e.Status == "Spent" && e.EchoActionId == 0 || e.EchoActionId > 0 && (e.BoundTick == 0 || e.UsedTick < e.BoundTick || e.Status is not ("Spent" or "Lost")) ||
            e.SourceRoom == _state.Endgame!.EncounterIndex && e.SourceActorId > 0 && !_spatial.CanOccupy(e.MemoryPosition, 0) ||
            e.SourceActorId > 0 && e.SourceRoom == _state.Endgame!.EncounterIndex && e.Status is "Offered" or "Bound" && !_state.Actors.Any(a => a.Id == e.SourceActorId && a.Faction == CombatFaction.Enemy && a.Elite && (a.Health == 0 || _state.ConsumedCorpseIds.Contains(a.Id))))
            throw new InvalidDataException("Invalid scoped Borrowed Memory state.");
        if (e.HazardId > 0)
        {
            var area = _state.Areas.SingleOrDefault(a => a.Id == e.HazardId);
            if (e.EchoActionId == 0 || e.WarningUntil != e.UsedTick + e.Rules.WarningTicks || e.HazardUntil != e.WarningUntil + e.Rules.HazardTicks ||
                !_spatial.CanOccupy(e.HazardPosition, 0) || area is null && e.HazardUntil > Tick && _state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0) ||
                area is not null && (area.Position != e.HazardPosition || area.ActionId != e.EchoActionId || area.Radius != e.Rules.HazardRadius || area.Damage != e.Rules.HazardDamage || area.Family != DamageFamily.Storm || area.SkillId != "enemy.stormbound" || area.ExpiresTick != e.HazardUntil))
                throw new InvalidDataException("Borrowed Echo warning and its ordinary damage area differ.");
        }
    }
}
