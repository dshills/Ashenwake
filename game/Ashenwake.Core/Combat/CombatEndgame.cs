using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    private EndgameManifestRoom? EndgameRoom => _state.Endgame is { } e ? e.Manifest.Rooms[e.EncounterIndex] : null;
    private string EndgamePattern => EndgameRoom?.Pattern ?? "";
    private bool EndgameRule(string id) => _state.Endgame?.Manifest.RuleIds.Contains("fracture." + id) == true;
    private bool EndgameOvercharged => EndgameRule("fragment_overcharge") && (Tick - _state.Endgame!.StartedTick) % 120 < 30;
    public RoomDefinition Room => JsonData.Copy(ResolveRoom(_content, _state));
    private static RoomDefinition ResolveRoom(CombatContent content, CombatSnapshot state)
    {
        if (state.Endgame is null) return content.Room;
        var e = state.Endgame;
        if (content.Endgame is null || e.Manifest?.Rooms is null || e.EncounterIndex < 0 || e.EncounterIndex >= e.Manifest.Rooms.Length)
            throw new InvalidDataException("Invalid endgame room context.");
        return content.Endgame.Arenas.FirstOrDefault(a => a.Id == e.Manifest.Rooms[e.EncounterIndex]?.RoomId)?.Room
            ?? throw new InvalidDataException("Unknown endgame arena.");
    }
    private CampaignCombatEncounter? EndgameCampaignEncounter => EndgameRoom is { } room
        ? new(room.EncounterId, room.Name, "Endgame", 0, []) : null;
    private static readonly DamageFamily[] ResistanceFamilies = Enum.GetValues<DamageFamily>();
    private int FamilyResistance(DamageFamily family) => _state.ProgressionBuild.Resistances?.GetValueOrDefault(family) ?? 0;
    private DamageFamily HighestResistance => ResistanceFamilies.OrderByDescending(FamilyResistance).ThenBy(f => f).First();
    private DamageFamily LowestResistance => ResistanceFamilies.OrderBy(FamilyResistance).ThenBy(f => f).First();
    private int EndgameResistanceAdjustment(CombatActor target, DamageFamily family)
        => target.Id != 1 ? 0 : FamilyResistance(family) - (EndgameRule("resistance_inversion") && family == LowestResistance ? 1500 : 0);
    private int EndgameDamageBonus(Hit hit) => hit.OwnerId == 1 && EndgameRule("resistance_inversion") ? FamilyResistance(HighestResistance) / 4 : 0;
    private int EndgameFragmentPower(Hit hit, CombatActor? source)
        => EndgameOvercharged && hit.OwnerId == 1 && (hit.FragmentId != "" || source?.FragmentId is { Length: > 0 } || hit.ContentId is "effect.seismic_release" or "effect.overheated" or "skill.echo_storm") ? 15000 : 10000;
    private int FragmentAmount(int amount) => EndgameOvercharged ? amount * 3 / 2 : amount;
    private Position EndgameHastedMove(CombatActor actor, Position desired)
        => EndgameRule("burning_haste") && actor.Faction == CombatFaction.Enemy && actor.State is "Approach" or "Flee" or "Reposition" && actor.Statuses.Any(s => s.Id == "Burning" && s.ExpiresTick > Tick)
            ? new(actor.Position.X + (desired.X - actor.Position.X) * 5 / 4, actor.Position.Z + (desired.Z - actor.Position.Z) * 5 / 4) : desired;

    public static CombatSession CreateEndgameEncounter(string combatJson, EndgameCombatManifest manifest, int encounterIndex,
        int attempt, CombatSnapshot? previous = null, bool restoreAtAnchor = false)
    {
        var content = CombatContent.Parse(combatJson); EndgameCombatContent.ValidateManifest(content, manifest);
        if (encounterIndex < 0 || encounterIndex >= manifest.Rooms.Length || attempt < 0 || attempt >= (manifest.Kind == "Fracture" ? 3 : 2)) throw new InvalidDataException("Invalid endgame room/attempt.");
        var cleared = CreateEncounter(combatJson, manifest.Seed, "clear", previous, restoreAtAnchor);
        var state = cleared.Capture() with { EncounterId = manifest.Rooms[encounterIndex].EncounterId };
        state.Endgame = new() { Manifest = JsonData.Copy(manifest), EncounterIndex = encounterIndex, Attempt = attempt, StartedTick = state.Tick, NextPatternTick = state.Tick + 45 };
        state.Campaign = new() { StartedTick = state.Tick, NextHazardTick = 0 };
        state.Actors[0].Position = ResolveRoom(content, state).PlayerSpawn;
        var session = new CombatSession(content, state);
        foreach (var spawn in manifest.Rooms[encounterIndex].Spawns)
            if (session.AddCampaignActor(spawn.EnemyId, spawn.Position, spawn.EliteModifiers) is null) throw new InvalidDataException("Endgame manifest spawn could not be placed.");
        session.PopulateEndgameMechanics(); session.ValidateSnapshot(); return session;
    }
    public CombatEndgameView? EndgameView
    {
        get
        {
            if (_state.Endgame is not { } e) return null;
            var room = e.Manifest.Rooms[e.EncounterIndex]; bool hunt = e.Manifest.Kind == "GodHunt";
            int cycle = (int)((Tick - e.StartedTick) % 120); bool active = EndgameOvercharged;
            string counterplay = hunt ? _content.Endgame!.HuntPhases.Single(p => p.HuntId == e.Manifest.ContentId && p.Index == e.EncounterIndex).Counterplay : "Clear the marked elite room; inspect inherited traits before the final arena.";
            return new($"run.{e.Manifest.RunId}.room.{e.EncounterIndex}.attempt.{e.Attempt}", e.Manifest.RunId, e.EncounterIndex,
                e.Manifest.Rooms.Length, e.Attempt, e.Manifest.Tier, e.Manifest.RuleIds.ToArray(), hunt ? e.Manifest.ContentId : "", room.Name,
                hunt ? e.EncounterIndex + 1 : 0, hunt ? 3 : 0, counterplay, active, active ? 30 - cycle : 0,
                EndgameRule("fragment_overcharge") ? active ? 0 : 120 - cycle : 0, HighestResistance, LowestResistance, FamilyResistance(HighestResistance),
                FamilyResistance(LowestResistance) - (EndgameRule("resistance_inversion") ? 1500 : 0), e.Manifest.Rooms[^1].EliteModifiers.ToArray(),
                e.Hazards.Select(h => new EndgameHazardView(h.Id, h.Kind, h.Position, h.End, h.Radius, Tick < h.StartsTick ? "Warning" : "Active",
                    Math.Max(0, (Tick < h.StartsTick ? h.StartsTick : h.EndsTick) - Tick), h.ContentId, h.SourceId, h.Sequence)).ToArray(),
                e.Mechanisms.Select(m => new EndgameMechanismView(m.Id, m.Kind, m.Position, m.Radius, MechanismAvailable(m), MechanismPrompt(m))).ToArray());
        }
    }
    private void ObserveEndgameEvent(CombatEvent ev)
    {
        if (_state.Endgame is not { } e || Player.Health <= 0) return;
        if (ev.Kind == "Healed" && ev.ActorId == 1 && ev.TargetId == 1 && ev.Amount > 0 && EndgameRule("healing_echoes") && e.EchoesCreated < 8 && _state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
        {
            string id = "heal." + Tick + "." + ev.ActionId + "." + ev.ContentId;
            if (e.RuleEventIds.Contains(id)) return;
            var source = _state.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
            if (EndgameWarn(source, "endgame.healing_echo", Player.Position, 1450, 42, 1, Math.Min(24, 10 + ev.Amount / 10), DamageFamily.Void))
            { e.RuleEventIds.Add(id); e.EchoesCreated++; Emit("EndgameHealingEchoCreated", source.Id, 1, e.EchoesCreated, "fracture.healing_echoes"); }
        }
    }
    private void EndgameDeath(CombatActor actor)
    {
        if (_state.Endgame is not { } e) return;
        if (e.ActorMechanics.TryGetValue(actor.Id, out string? mechanic))
        {
            e.Hazards.RemoveAll(h => h.SourceId == actor.Id);
            if (mechanic is "RebuildingLimb" or "BroodChannel" or "MarkedEcho" or "SeedGuard" or "ContractSeal" or "AbsenceAnchor")
            { e.ExposedUntil = Tick + 150; Emit("HuntWeakPointBroken", 1, actor.Id, 150, mechanic); }
        }
        if (actor.Faction == CombatFaction.Enemy && actor.Elite && CampaignRewardEligible(actor) && EndgameRule("elite_hazards") && e.ScarsCreated < 8 && !e.RewardedEliteIds.Contains(actor.Id))
        {
            // A reserved western corridor always connects spawn and the exit, even if an elite dies inside it.
            var at = new Position(Math.Max(-2500, actor.Position.X), actor.Position.Z);
            if (EndgameWarn(actor, "endgame.elite_scar", at, 1200, 36, 1000000000 - Tick, 9, DamageFamily.Decay))
            { e.RewardedEliteIds.Add(actor.Id); e.ScarsCreated++; Emit("EndgameEliteScarCreated", actor.Id, amount: e.ScarsCreated, content: "fracture.elite_hazards"); }
        }
        if (!_state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)) SealEndgameVictory();
    }
    public void SealEndgameVictory()
    {
        if (_state.Endgame is not { } e || _state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
            throw new InvalidOperationException("Only actual endgame victory can seal an arena.");
        e.Hazards.Clear(); e.Mechanisms.Clear(); e.ExposedUntil = Tick;
        _state.Campaign!.Hazards.Clear(); _state.Campaign.SuppressedFragmentId = ""; _state.Campaign.SuppressedUntil = Tick;
        var enemies = _state.Actors.Where(a => a.Faction == CombatFaction.Enemy).Select(a => a.Id).ToHashSet();
        _state.Projectiles.RemoveAll(p => enemies.Contains(p.OwnerId)); _state.Areas.RemoveAll(a => enemies.Contains(a.OwnerId));
        foreach (var actor in _state.Actors) { actor.Pending = null; if (actor.Id == 1) actor.Statuses.Clear(); }
    }
    private bool EndgameWarn(CombatActor source, string id, Position position, int radius, int delay, long duration,
        int damage, DamageFamily family, Position? end = null, string status = "", int sequence = 0)
    {
        if (_state.Endgame is not { } e || e.Hazards.Count >= 32) { Budget(0); return false; }
        var room = ResolveRoom(_content, _state);
        Position Clamp(Position at) => new(Math.Clamp(at.X, -room.HalfWidth, room.HalfWidth), Math.Clamp(at.Z, -room.HalfDepth, room.HalfDepth));
        position = Clamp(position); end = end is { } endpoint ? Clamp(endpoint) : null;
        long action = _state.NextActionId++, starts = Tick + delay, ends = Math.Min(1000000000, starts + duration);
        e.Hazards.Add(new(_state.NextObjectId++, end.HasValue ? "Line" : "Circle", position, end ?? position, radius,
            starts, ends, starts, id, source.Id, damage, family, status, action, sequence));
        Emit("EndgameHazardWarned", source.Id, amount: delay, content: id, action: action); return true;
    }
    private void TickEndgame()
    {
        if (_state.Endgame is not { } e || Player.Health <= 0 || !_state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)) return;
        if (EndgameRule("fragment_overcharge") && (Tick - e.StartedTick) % 120 == 0) Emit("EndgameOverchargeStarted", 1, amount: 30, content: "fracture.fragment_overcharge");
        foreach (var h in e.Hazards.ToArray())
        {
            if (h.EndsTick <= Tick) { e.Hazards.Remove(h); continue; }
            if (h.NextTick > Tick) continue;
            e.Hazards[e.Hazards.IndexOf(h)] = h with { NextTick = Tick + 20 };
            if (h.ContentId == "hunt.vael.solar" && e.Mechanisms.Any(m => m.Kind == "CoolingGap" && Position.DistanceSquared(m.Position, Player.Position) <= (long)m.Radius * m.Radius)) continue;
            if (ContainsGeometry(h.Kind, h.Position, h.End, h.Radius, Player.Position))
                Enqueue(new(h.SourceId, h.SourceId, 1, h.Damage, h.Family, h.ContentId, h.ActionId, 0, Status: h.Status));
            Emit("EndgameHazardResolved", h.SourceId, content: h.ContentId, action: h.ActionId);
        }
        if (Tick >= e.NextPatternTick)
        {
            TickEndgameMechanics(); e.NextPatternTick = Tick + 90;
        }
    }
    private void ValidateEndgameSnapshot()
    {
        if (_state.Endgame is not { } e)
        { if (_state.EncounterId.StartsWith("endgame.", StringComparison.Ordinal)) throw new InvalidDataException("Endgame encounter lacks context."); return; }
        if (e.SchemaVersion != 1 || e.RulesVersion != "endgame-combat.1") throw new InvalidDataException("Unsupported endgame combat state.");
        EndgameCombatContent.ValidateManifest(_content, e.Manifest);
        void Check(bool value, string field) { if (!value) throw new InvalidDataException("Invalid endgame combat " + field); }
        Check(e.EncounterIndex >= 0 && e.EncounterIndex < e.Manifest.Rooms.Length && e.Attempt >= 0 && e.Attempt < (e.Manifest.Kind == "Fracture" ? 3 : 2) && _state.EncounterId == e.Manifest.Rooms[e.EncounterIndex].EncounterId, "room identity");
        Check(e.StartedTick >= 0 && e.StartedTick <= Tick && e.NextPatternTick >= 0 && e.NextPatternTick <= Tick + 90 && e.PatternCycle is >= 0 and <= 10000000 && e.ExposedUntil >= 0 && e.ExposedUntil <= Tick + 180, "timers");
        Check(e.Hazards is { Count: <= 32 } && e.Mechanisms is { Count: <= 8 } && e.ActorMechanics is { Count: <= 16 } && e.RewardedEliteIds is { Count: <= 8 } && e.RuleEventIds is { Count: <= 8 }, "budgets");
        Check(e.RuleEventIds.Count == e.EchoesCreated && e.RewardedEliteIds.Count == e.ScarsCreated && e.RuleEventIds.All(id => id is not null && id.Length <= 150) && e.RewardedEliteIds.All(id => _state.Actors.Any(a => a.Id == id && a.Faction == CombatFaction.Enemy && a.Elite)), "rule receipts");
        Check(e.EchoesCreated is >= 0 and <= 8 && e.ScarsCreated is >= 0 and <= 8 && (EndgameRule("healing_echoes") || e.EchoesCreated == 0) && (EndgameRule("elite_hazards") || e.ScarsCreated == 0) && e.CarriedTerm is >= 0 and <= 2 && e.DepositedTerms is >= 0 and <= 2, "rule counters");
        foreach (var h in e.Hazards)
            Check(h is not null && h.Id > 0 && h.Id < _state.NextObjectId && h.Kind is "Circle" or "Line" && h.Radius is >= 100 and <= 3000 && Math.Abs((long)h.Position.X) <= Room.HalfWidth && Math.Abs((long)h.End.X) <= Room.HalfWidth && Math.Abs((long)h.Position.Z) <= Room.HalfDepth && Math.Abs((long)h.End.Z) <= Room.HalfDepth && h.StartsTick >= e.StartedTick && h.StartsTick <= Tick + 180 && h.EndsTick > h.StartsTick && h.EndsTick <= 1000000000 && h.NextTick >= h.StartsTick && h.NextTick <= Tick + 180 && h.SourceId > 1 && _state.Actors.Any(a => a.Id == h.SourceId && a.Faction == CombatFaction.Enemy) && h.Damage is >= 0 and <= 1000 && Enum.IsDefined(h.Family) && (h.Status == "" || StatusIds.Contains(h.Status)) && h.ActionId > 0 && h.ActionId < _state.NextActionId && (h.ContentId.StartsWith("endgame.", StringComparison.Ordinal) || h.ContentId.StartsWith("hunt.", StringComparison.Ordinal)), "hazard");
        Check(e.ActorMechanics.All(p => _state.Actors.Any(a => a.Id == p.Key && a.Faction == CombatFaction.Enemy) && p.Value is "RebuildingLimb" or "BroodChannel" or "SeedGuard" or "MarkedEcho" or "FalseEcho" or "ContractSeal" or "AbsenceAnchor"), "mechanic ownership");
        Check(e.Mechanisms.All(m => m is not null && m.Id is >= 1000001 and <= 1000008 && m.Radius is >= 500 and <= 2000 && _spatial.CanOccupy(m.Position, 0) && m.Kind is "CoolingGap" or "SilentBell" or "BrokenTerm" or "OathPlinth" or "AnchorGlyph") && e.Mechanisms.Select(m => m.Id).Distinct().Count() == e.Mechanisms.Count, "interactions");
        var objects = _state.Inventory.Select(i => i.Id).Concat(_state.Loot.Select(l => l.Id)).Concat(_state.Projectiles.Select(p => p.Id)).Concat(_state.Areas.Select(a => a.Id)).Concat(_state.Campaign!.Hazards.Select(h => h.Id)).Concat(e.Hazards.Select(h => h.Id)).ToArray();
        Check(objects.Distinct().Count() == objects.Length, "object identity");
    }
}
