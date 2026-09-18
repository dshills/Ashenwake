using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    private bool IsEndgameBoss(CombatActor actor) => _state.Endgame is not null && (actor.DefinitionId.StartsWith("boss.hunt_", StringComparison.Ordinal) || actor.DefinitionId.StartsWith("boss.fracture_", StringComparison.Ordinal));
    private bool EndgameMechanicNoRewards(CombatActor actor) => _state.Endgame?.ActorMechanics.ContainsKey(actor.Id) == true;
    private CombatActor? EndgameBoss => _state.Actors.FirstOrDefault(a => IsEndgameBoss(a) && a.Health > 0);
    private void PopulateEndgameMechanics()
    {
        var e = _state.Endgame!;
        void Pair(string definition, string mechanic)
        { AddEndgameMechanicActor(definition, new(500, -3500), mechanic); AddEndgameMechanicActor(definition, new(4200, 3500), mechanic); }
        switch (EndgamePattern)
        {
            case "Vael2": Pair("enemy.hunt_limb", "RebuildingLimb"); break;
            case "Vael3":
                e.Mechanisms.Add(new() { Id = 1000001, Kind = "CoolingGap", Position = new(-3500, -3500), Radius = 1800 });
                e.Mechanisms.Add(new() { Id = 1000002, Kind = "CoolingGap", Position = new(-3500, 3500), Radius = 1800 }); break;
            case "Ilyra2": Pair("enemy.brood_root", "BroodChannel"); break;
            case "Ilyra3": Pair("enemy.brood_root", "SeedGuard"); break;
            case "Serath1":
                AddEndgameMechanicActor("enemy.memory_copy", new(3000, -4000), "FalseEcho");
                AddEndgameMechanicActor("enemy.memory_copy", new(3000, 4000), "FalseEcho"); break;
            case "Serath2":
            case "Nhal2":
                AddEndgameMechanicActor("enemy.memory_copy", new(0, 3000), "MarkedEcho");
                AddEndgameMechanicActor("enemy.memory_copy", new(3500, -4000), "FalseEcho");
                AddEndgameMechanicActor("enemy.memory_copy", new(5500, 4000), "FalseEcho"); break;
            case "Serath3": e.Mechanisms.Add(new() { Id = 1000001, Kind = "SilentBell", Position = new(-1500, 4000) }); break;
            case "Orrun2":
                e.Mechanisms.Add(new() { Id = 1000001, Kind = "BrokenTerm", Position = new(-2500, -3300) });
                e.Mechanisms.Add(new() { Id = 1000002, Kind = "BrokenTerm", Position = new(-2500, 3300) });
                e.Mechanisms.Add(new() { Id = 1000003, Kind = "OathPlinth", Position = new(5000, -3500) });
                e.Mechanisms.Add(new() { Id = 1000004, Kind = "OathPlinth", Position = new(5000, 3500) }); break;
            case "Orrun3": Pair("enemy.contract_seal", "ContractSeal"); break;
            case "Nhal1":
                Pair("enemy.absence_anchor", "AbsenceAnchor");
                e.Mechanisms.Add(new() { Id = 1000001, Kind = "AnchorGlyph", Position = new(-4500, -4500), Radius = 1800 });
                e.Mechanisms.Add(new() { Id = 1000002, Kind = "AnchorGlyph", Position = new(-4500, 4500), Radius = 1800 }); break;
            case "Nhal3":
                e.Mechanisms.Add(new() { Id = 1000001, Kind = "SilentBell", Position = new(-1500, -4000) }); break;
        }
    }
    private CombatActor? AddEndgameMechanicActor(string definition, Position position, string mechanic)
    {
        if (_state.Endgame!.ActorMechanics.Count >= 16) return null;
        var actor = AddCampaignActor(definition, position, []);
        if (actor is not null) { _state.Endgame.ActorMechanics[actor.Id] = mechanic; actor.State = mechanic; }
        return actor;
    }
    private bool MechanismAvailable(EndgameMechanism m) => _state.Endgame is { } e && EndgameBoss is not null && m.Kind switch
    {
        "BrokenTerm" => !m.Used && e.CarriedTerm == 0,
        "OathPlinth" => !m.Used && e.CarriedTerm != 0,
        "SilentBell" => e.ExposedUntil <= Tick,
        _ => false
    };
    private string MechanismPrompt(EndgameMechanism m) => m.Kind switch
    {
        "BrokenTerm" => "Carry broken term",
        "OathPlinth" => "Deposit term",
        "SilentBell" => "Ring the silent bell",
        "CoolingGap" => "Solar refuge",
        "AnchorGlyph" => "True anchor glyph",
        _ => ""
    };
    private void InteractEndgameMechanism(CombatCommand command)
    {
        var e = _state.Endgame; var mechanism = e?.Mechanisms.FirstOrDefault(m => m.Id == command.TargetId);
        if (e is null || mechanism is null || !MechanismAvailable(mechanism) || Position.DistanceSquared(Player.Position, mechanism.Position) > (long)mechanism.Radius * mechanism.Radius || Stunned(Player))
        { Reject(command, "endgame_mechanism_unavailable"); return; }
        switch (mechanism.Kind)
        {
            case "BrokenTerm": mechanism.Used = true; e.CarriedTerm = mechanism.Id - 1000000; break;
            case "OathPlinth": mechanism.Used = true; e.CarriedTerm = 0; e.DepositedTerms++; break;
            case "SilentBell": e.ExposedUntil = Tick + 180; break;
        }
        Emit("HuntMechanismUsed", 1, mechanism.Id, e.DepositedTerms, mechanism.Kind);
    }
    private bool EndgameShielded(CombatActor actor)
    {
        if (!IsEndgameBoss(actor) || _state.Endgame is not { } e) return false;
        bool Alive(string role) => e.ActorMechanics.Any(p => p.Value == role && _state.Actors.Any(a => a.Id == p.Key && a.Health > 0));
        return EndgamePattern switch
        {
            "Vael2" => Alive("RebuildingLimb") && e.ExposedUntil <= Tick,
            "Ilyra2" => Alive("BroodChannel") && e.ExposedUntil <= Tick,
            "Ilyra3" => Alive("SeedGuard") && e.ExposedUntil <= Tick,
            "Serath2" or "Nhal2" => Alive("MarkedEcho"),
            "Serath3" or "Nhal3" => e.ExposedUntil <= Tick,
            "Orrun2" => e.DepositedTerms < 2,
            "Orrun3" => Alive("ContractSeal") && e.ExposedUntil <= Tick,
            "Nhal1" => Alive("AbsenceAnchor") && e.ExposedUntil <= Tick,
            _ => false
        };
    }
    private bool TryEndgameReform(CombatActor actor)
    {
        if (_state.Endgame?.ActorMechanics.GetValueOrDefault(actor.Id) != "FalseEcho" || _state.Campaign!.Actors[actor.Id].MirrorUsed) return false;
        _state.Campaign.Actors[actor.Id].MirrorUsed = true; actor.Health = actor.MaxHealth; actor.DeathProcessed = false;
        actor.Statuses.Clear(); actor.InvulnerableUntil = Tick + 36; actor.State = "FalseEcho";
        Emit("FalseMemoryReformed", actor.Id, content: "hunt.false_echo"); return true;
    }
    private bool ThinkEndgameActor(CombatActor actor)
    {
        if (_state.Endgame is not { } e) return false;
        if (e.ActorMechanics.TryGetValue(actor.Id, out string? mechanic)) { actor.State = mechanic; return true; }
        if (!IsEndgameBoss(actor)) return false;
        if (actor.Pending is not null || actor.RecoveryUntil > Tick || Player.Health <= 0) return true;
        if (TryEliteAbility(actor)) return true;
        var definition = _content.Enemies.Single(d => d.Id == actor.DefinitionId);
        actor.State = "HuntWindup"; actor.RecoveryUntil = Tick + definition.Windup + definition.Recovery;
        actor.Pending = new("endgame.boss_tell", 1, Player.Position, Tick + definition.Windup, _state.NextActionId++);
        string pattern = EndgamePattern;
        DamageFamily family = pattern.Contains("Vael", StringComparison.Ordinal) ? DamageFamily.Fire : pattern.Contains("Ilyra", StringComparison.Ordinal) ? DamageFamily.Venom : pattern.Contains("Orrun", StringComparison.Ordinal) ? DamageFamily.PhysicalCrush : DamageFamily.Void;
        EndgameWarn(actor, "hunt." + pattern.ToLowerInvariant() + ".strike", Player.Position, 1250, definition.Windup, 1, definition.Damage, family,
            status: family == DamageFamily.Venom ? "Poisoned" : "");
        return true;
    }
    private bool ResolveEndgameAbility(CombatActor actor, CombatPending pending)
        => _state.Endgame is not null && pending.SkillId == "endgame.boss_tell";
    private void TickEndgameMechanics()
    {
        if (_state.Endgame is not { } e || EndgameBoss is not { } boss) return;
        int cycle = e.PatternCycle++;
        void Line(string id, int z, int radius, int delay = 42, int duration = 1, DamageFamily family = DamageFamily.PhysicalCrush, int sequence = 0)
            => EndgameWarn(boss, id, new(-11500, z), radius, delay, duration, 18, family, new(11500, z), sequence: sequence);
        void Faults(string id)
        { for (int i = 0; i < 3; i++) Line(id, -4000 + i * 4000, 1000, 30 + i * 30, 1, DamageFamily.PhysicalCrush, i + 1); }
        string pattern = EndgamePattern;
        switch (pattern)
        {
            case "Vael1":
            case "FractureVael":
                for (int i = 0; i < 4; i++) if (i != cycle % 4)
                        EndgameWarn(boss, "hunt.vael.vent", new(-7500 + i * 5000, -9500), 1050, 42, 32, 13, DamageFamily.Fire, new(-7500 + i * 5000, 9500));
                break;
            case "Vael2":
                foreach (var limb in e.ActorMechanics.Where(p => p.Value == "RebuildingLimb").Select(p => _state.Actors.Single(a => a.Id == p.Key)).Where(a => a.Health > 0).Take(1 + cycle % 2))
                    EndgameWarn(limb, "hunt.vael.rebuild", limb.Position, 1700, 54, 1, 18, DamageFamily.Fire, Player.Position);
                break;
            case "Vael3": Line("hunt.vael.solar", Player.Position.Z, 2600, 60, 1, DamageFamily.Fire); break;
            case "Ilyra1":
            case "FractureIlyra":
                EndgameWarn(boss, "hunt.ilyra.jaw_trail", boss.Position, 800, 36, 1, 15, DamageFamily.Venom, Player.Position);
                EndgameWarn(boss, "hunt.ilyra.surfacing_jaw", Player.Position, 1600, 66, 1, 22, DamageFamily.PhysicalPierce); break;
            case "Ilyra2":
                var root = e.ActorMechanics.Where(p => p.Value == "BroodChannel").Select(p => _state.Actors.Single(a => a.Id == p.Key)).FirstOrDefault(a => a.Health > 0);
                if (root is not null)
                {
                    EndgameWarn(root, "hunt.ilyra.brood_channel", root.Position, 850, 48, 30, 12, DamageFamily.Venom, Player.Position, "Poisoned");
                    if (cycle < 4 && _state.Actors.Count(a => a.DefinitionId == "enemy.needle_swarm" && a.Health > 0) < 2)
                        AddCampaignActor("enemy.needle_swarm", new(5500, cycle % 2 == 0 ? -5500 : 5500), []);
                }
                break;
            case "Ilyra3":
                e.ExposedUntil = Tick + 36; Emit("HuntFeedingPause", boss.Id, amount: 36, content: "hunt.ilyra.seed");
                EndgameWarn(boss, "hunt.ilyra.seed_pulse", boss.Position, 2800, 54, 1, 24, DamageFamily.Venom, status: "Poisoned"); break;
            case "Serath1":
            case "FractureSerath":
                for (int i = 0; i < 3; i++) if (i != cycle % 3) Line("hunt.serath.procession", -4500 + i * 4500, 1200, 48, 30, DamageFamily.Void, i + 1);
                break;
            case "Serath2":
            case "Nhal2":
                foreach (var copy in e.ActorMechanics.Where(p => p.Value == "MarkedEcho").Select(p => _state.Actors.Single(a => a.Id == p.Key)).Where(a => a.Health > 0))
                    EndgameWarn(copy, "hunt.marked_repetition", Player.Position, 1800, 60, 1, 22, DamageFamily.Void);
                break;
            case "Serath3":
                for (int i = 0; i < 3; i++) if (i != cycle % 3) Line("hunt.serath.bell_pulse", -4500 + i * 4500, 1000, 42, 1, DamageFamily.Storm, i + 1);
                break;
            case "Orrun1": case "FractureOrrun": Faults("hunt.orrun.numbered_fault"); break;
            case "Orrun2":
                EndgameWarn(boss, "hunt.orrun.carried_term", Player.Position, 1400, 54, 1, 18, DamageFamily.PhysicalCrush); break;
            case "Orrun3":
                for (int i = 0; i < 3; i++) EndgameWarn(boss, "hunt.orrun.oathless_slam", Player.Position, 1500 + i * 450, 30 + i * 30, 1, 20, DamageFamily.PhysicalCrush, sequence: i + 1);
                boss.RecoveryUntil = Tick + 150; break;
            case "Nhal1":
            case "FractureNhal":
                for (int i = 0; i < 3; i++) if (i != (cycle + 1) % 3) Line("hunt.nhal.absent_lane", -4500 + i * 4500, 1100, 54, 1, DamageFamily.Void, i + 1);
                break;
            case "Nhal3":
                // Cue order is reversed, but the numbered physical rhythm stays 1, 2, 3.
                for (int i = 0; i < 3; i++) Line("hunt.nhal.remembered_rhythm", 4500 - i * 4500, 950, 36 + i * 30, 1, DamageFamily.Void, i + 1);
                break;
        }
    }
}
