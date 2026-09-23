using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

/// <summary>Validated projection of permanent progression; numbers are affix/passive additions, not item base values.</summary>
public sealed record CombatProgressionBuild(string Discipline = "Vanguard", int Level = 1, int Offense = 0, int Defense = 0,
    int ResourceBonus = 0, int FlatDamage = 0, int Armor = 0, int CriticalBasisPoints = 0, int ForkCount = 0, int ChainCount = 0,
    bool BarrierOnDodge = false, bool SummonBurst = false, bool UltimateUnlocked = true, string[]? UnlockedMutations = null, string[]? PurifiedFragments = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool PyreTrail { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool OathReprisal { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool WidowEcho { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool VirulentWake { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool RallyingChorus { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool CinderCycle { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool UnspokenVerdict { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool WitnessVow { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool BorrowedHour { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool GriefsReprieve { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool Widowthorn { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool Emberwake { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SortedDictionary<DamageFamily, int>? Resistances { get; init; }
}

public sealed partial class CombatSession
{
    public static IReadOnlyList<string> Disciplines { get; } = Array.AsReadOnly(new[] { "Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden" });
    internal static readonly string[] EquipmentSlots = ["Head", "Shoulders", "Chest", "Gloves", "Belt", "Legs", "Boots", "Amulet", "Ring1", "Ring2", "MainHand", "OffHand"];
    internal static readonly string[] StatusIds = ["Burning", "Bleeding", "Poisoned", "Chilled", "Frozen", "Shocked", "Staggered", "Cursed", "Terrified", "Marked", "Vulnerable", "Rooted"];
    public CombatProgressionBuild ProgressionBuild => JsonData.Copy(_state.ProgressionBuild);
    private string Discipline => _state.ProgressionBuild.Discipline;
    private string ResourceName => Discipline switch { "Veilwalker" => "Exposure", "Arcanist" => "Instability", "Gravecaller" => "Remains", "Warden" => "Adaptation", _ => "Momentum" };
    private IEnumerable<CombatSkill> SelectedSkills => _content.Skills.Where(s => s.Discipline == Discipline);
    public void ApplyProgressionBuild(CombatProgressionBuild build)
    {
        ValidateProgressionBuild(build);
        if (build.Discipline != Discipline)
        {
            if (_state.EncounterId is not ("hub" or "clear") && _state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
                throw new InvalidOperationException("Retraining requires a cleared room or hub.");
            Player.Pending = null; _state.BufferedCommand = null; Player.RecoveryUntil = Tick; _state.Momentum = 0;
            _state.ThreatFamily = null; _state.ThreatStacks = 0;
            foreach (var mutation in _state.Mutations.Keys.Where(id => _content.Skills.Single(s => s.Id == id).Discipline != build.Discipline).ToArray()) _state.Mutations.Remove(mutation);
        }
        _state.ProgressionBuild = JsonData.Copy(build);
        ClearInactiveLegendaryEffects();
        foreach (var mutation in _state.Mutations.Where(pair => !MutationUnlocked(pair.Value)).Select(pair => pair.Key).ToArray()) _state.Mutations.Remove(mutation);
    }
    private void ValidateProgressionBuild(CombatProgressionBuild build)
    {
        if (build is null || build.Resistances is not null && (build.Resistances.Count > 9 || build.Resistances.Any(p => !Enum.IsDefined(p.Key) || p.Value is < 0 or > 7500)) || build.PurifiedFragments is not null && (build.PurifiedFragments.Length > 100 || build.PurifiedFragments.Any(id => !_content.Fragments.Any(f => f.Id == id)) || build.PurifiedFragments.Distinct().Count() != build.PurifiedFragments.Length) || !Disciplines.Contains(build.Discipline) || build.Level is < 1 or > 100 || build.Offense is < 0 or > 100 || build.Defense is < 0 or > 100 || build.ResourceBonus is < 0 or > 1000 || build.FlatDamage is < 0 or > 2000 || build.Armor is < 0 or > 10000 || build.CriticalBasisPoints is < 0 or > 7500 || build.ForkCount is < 0 or > 2 || build.ChainCount is < 0 or > 3 || build.ForkCount > 0 && build.ChainCount > 0 || build.UnlockedMutations is { Length: > 100 } || build.UnlockedMutations is not null && (build.UnlockedMutations.Any(id => !_content.Mutations.Any(m => m.Id == id)) || build.UnlockedMutations.Distinct().Count() != build.UnlockedMutations.Length))
            throw new InvalidDataException("Invalid permanent progression combat projection.");
    }
    private bool Purified(string id) => _state.ProgressionBuild.PurifiedFragments?.Contains(id) == true;
    private bool MutationUnlocked(string id) => _state.ProgressionBuild.UnlockedMutations is null || _state.ProgressionBuild.UnlockedMutations.Contains(id);
    private bool IsUltimate(CombatSkill skill) => skill.Id is "skill.cataclysm" or "skill.shadow_execution" or "skill.starfall" or "skill.procession" or "skill.primal_awakening";
    private bool SkillAvailable(CombatSkill skill) => skill.Discipline == Discipline && (!IsUltimate(skill) || _state.ProgressionBuild.UltimateUnlocked);
    private int GenerationAmount(int amount) => Math.Min(100, amount + (amount > 0 ? PassiveEffects.GenerationBonus(_state.ProgressionBuild.ResourceBonus) : 0));
    private bool CanPay(CombatSkill skill, int cost)
    {
        if (Discipline == "Arcanist" && skill.ResourceMode == "Heat") return _state.Momentum + cost <= 100;
        return _state.Momentum >= cost;
    }
    private void Pay(CombatSkill skill, int cost)
    {
        if (Discipline == "Arcanist" && skill.ResourceMode == "Heat") SetResource(Math.Min(100, _state.Momentum + cost), "Skill heat");
        else SetResource(_state.Momentum - cost, "Skill cost");
        if (Discipline == "Arcanist" && _state.Momentum >= 80 && skill.ResourceMode == "Heat")
        {
            Player.Health = Math.Max(1, Player.Health - 3); Emit("InstabilityBacklash", 1, 1, 3, skill.Id);
        }
    }
    private bool ClaimResourceAction(long action)
    {
        if (_state.ResourceActions.ContainsKey(action)) return false;
        _state.ResourceActions[action] = Tick + 300; return true;
    }
    private void TickProductionState()
    {
        TrimLegendaryState();
        foreach (var id in _state.ResourceActions.Where(p => p.Value <= Tick).Select(p => p.Key).ToArray()) _state.ResourceActions.Remove(id);
        if (_state.TemporaryLife > 0 && _state.TemporaryLifeUntil <= Tick)
        {
            var player = Player; _state.Actors[_state.Actors.IndexOf(player)] = player with { MaxHealth = Math.Max(1, player.MaxHealth - _state.TemporaryLife) };
            Player.Health = Math.Min(Player.Health, Player.MaxHealth); _state.TemporaryLife = 0;
        }
        if (_state.CapturedUntil <= Tick) _state.CapturedSkillId = "";
        if (Tick % 15 == 0)
        {
            if (Discipline == "Veilwalker" && Tick - _state.LastAggressionTick >= 60) SetResource(Math.Max(0, _state.Momentum - 4), "Passive decay");
            if (Discipline == "Arcanist" && Player.Pending is null) SetResource(Math.Max(0, _state.Momentum - 3), "Passive decay");
            if (Discipline == "Warden" && Tick - _state.LastAggressionTick >= 150) SetResource(Math.Max(0, _state.Momentum - 2), "Passive decay");
        }
        if (ActiveFragments().Any(f => f.Effect == "SeismicCharge") && Player.Health > 0)
        {
            if (Player.MoveX == 0 && Player.MoveZ == 0) _state.SeismicCharge = Math.Min(90, _state.SeismicCharge + 1);
            else if (_state.SeismicCharge >= 15)
            {
                var action = _state.NextActionId++;
                foreach (var target in Hostiles(Player, Player.Position, 2800)) Enqueue(new(1, 1, target.Id, _state.SeismicCharge / 2, DamageFamily.PhysicalCrush, "effect.seismic_release", action, 1, Reflected: true, Status: "Staggered"));
                Emit("FragmentTriggered", 1, amount: _state.SeismicCharge, content: "fragment.orrun_knuckle", action: action); _state.SeismicCharge = 0;
            }
        }
        else _state.SeismicCharge = 0;
        if (_state.ThreatUntil <= Tick) { _state.ThreatFamily = null; _state.ThreatStacks = 0; }
    }
    private int HealingAmount(int desired)
    {
        bool nearCorpse = _state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health == 0 && !_state.ConsumedCorpseIds.Contains(a.Id) && Position.DistanceSquared(a.Position, Player.Position) <= 3000L * 3000);
        return HasManifestation("manifestation.voracious_renewal") && nearCorpse ? desired * (Purified("fragment.nerve_ilyra") ? 7 : 6) / 8 : desired;
    }
    private void HealPlayer(int amount, string content, long action = 0)
    {
        if (Player.Health <= 0) return;
        int actual = Math.Min(content == "manifestation.voracious_renewal" ? amount : HealingAmount(amount), Player.MaxHealth - Player.Health); Player.Health += actual;
        if (actual > 0) Emit("Healed", 1, 1, actual, content, action);
    }
    private IEnumerable<Position> FalseSilhouettes()
    {
        if (!HasManifestation("manifestation.whispering_shadow") || !_state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)) return [];
        // Decoration positions never enter the actor table or targeting queries; remain far from combat warnings.
        return Tick % 180 < (Purified("fragment.heart_serath") ? 37 : 75) ? new[] { new Position(-10500, 8200), new Position(10500, -8200) }.Where(p => Position.DistanceSquared(p, Player.Position) > 5000L * 5000 && !_state.Actors.Any(a => a.Pending is not null && Position.DistanceSquared(p, a.Pending.Target) < 4000L * 4000) && !_state.Areas.Any(a => Position.DistanceSquared(p, a.Position) < (long)(a.Radius + 1500) * (a.Radius + 1500))).ToArray() : [];
    }
    private bool ActorVisible(CombatActor actor) => !actor.Hidden || HasManifestation("manifestation.whispering_shadow") || actor.Pending is not null || Position.DistanceSquared(actor.Position, Player.Position) <= 5000L * 5000;
    private void OnSummonExpired(CombatActor actor)
    {
        if (!_state.ProgressionBuild.SummonBurst) return;
        long action = _state.NextActionId++;
        foreach (var target in Hostiles(actor, actor.Position, 2300)) Enqueue(new(actor.Id, 1, target.Id, 24, DamageFamily.Decay, "effect.summon_burst", action, Math.Min(MaxChainDepth, actor.Generation + 1), Reflected: true));
        Emit("SummonExpiredBurst", actor.Id, content: "property.summon_burst", action: action);
    }
}
