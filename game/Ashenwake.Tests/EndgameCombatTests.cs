using Xunit;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Tests;

public class EndgameCombatTests
{
    private static string Source(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        { string path = Path.Combine(directory.FullName, "content", name); if (File.Exists(path)) return File.ReadAllText(path); }
        throw new FileNotFoundException(name);
    }
    private static readonly Lazy<EndgameCombatContent> Content = new(() => EndgameCombatContent.Parse(
        CampaignCombatContent.Parse(Source("combat.json"), Source("campaign-combat.json")).CombatJson, Source("endgame-combat.json"), EndgameContent.Parse(Source("endgame.json"))));
    private static EndgameCombatContent Catalog => Content.Value;
    private static CombatSnapshot Build(string discipline = "Vanguard")
    {
        var session = CombatSession.CreateEncounter(Catalog.CombatJson, 42, "hub");
        session.ApplyProgressionBuild(new(discipline, Level: 20, Offense: 10, Defense: 8, FlatDamage: 12, Armor: 800,
            CriticalBasisPoints: 600)
        { Resistances = new() { [DamageFamily.Fire] = 2200, [DamageFamily.Frost] = 900, [DamageFamily.PhysicalCrush] = 1000 } });
        return session.Capture();
    }
    private static EndgameCombatManifest Fracture(string[] rules, ulong seed = 42, int tier = 4)
        => Catalog.CreateFractureManifest(new(1, seed, "act.grey_march", tier, rules, "Serath", "Materials"), 1);
    private static CombatSession Quiet(EndgameCombatManifest manifest)
    {
        var session = Catalog.CreateEncounter(manifest, 0, 0, Build()); var state = session.Capture();
        foreach (var actor in state.Actors.Where(a => a.Id != 1)) actor.RecoveryUntil = state.Tick + 3000;
        return CombatSession.Restore(Catalog.CombatJson, state);
    }
    [Theory]
    [InlineData(857, 1649, false)]
    [InlineData(809, 1735, false)]
    [InlineData(774, 1493, true)]
    public void Cleared_broken_causeway_collects_all_drops_without_corner_oscillation(int x, int z, bool secondCorner)
    {
        // Geometry, starts and drop positions from the imported and fresh earned-route stalls.
        var manifest = secondCorner
            ? Catalog.CreateFractureManifest(new(2, 11775387339963357740UL, "act.hollow_night", 1, ["fracture.burning_haste"], "Nhal", "Godwrought"), 2)
            : Catalog.CreateFractureManifest(new(8, 10208061212434327614UL, "act.grey_march", 8, ["fracture.resistance_inversion", "fracture.elite_hazards", "fracture.burning_haste"], "Nhal", "Godwrought"), 12);
        int index = secondCorner ? 0 : 2;
        var session = Catalog.CreateEncounter(manifest, index, 0, Build());
        Assert.Equal("arena.fracture_1", manifest.Rooms[index].RoomId);
        Assert.Equal(new Bounds(-500, -1200, 500, 1200), Assert.Single(session.Room.Obstacles));
        var state = session.Capture();
        foreach (var actor in state.Actors.Where(a => a.Faction == CombatFaction.Enemy)) { actor.Health = 0; actor.DeathProcessed = true; }
        state.Actors.Single(a => a.Id == 1).Position = new(x, z);
        state.Loot.Clear();
        Position[] positions = secondCorner ? [new(1200, -2300), new(2065, -3024)] : [new(1200, -2300), new(799, -832), new(1940, -1783), new(5200, -4000), new(8200, 6500)];
        foreach (var position in positions)
        {
            long id = state.NextObjectId++;
            state.Loot.Add(new(id, position, new(id, "item.cinder_edge", "Cinder Edge", "MainHand", "Common", 2, 0, 3000)));
        }
        session = CombatSession.Restore(Catalog.CombatJson, state);
        session.SealEndgameVictory();
        var recorder = new Ashenwake.Core.Serialization.CombatRecorder(session);
        int pickedUp = 0;
        for (int tick = 0; tick < 500 && session.View.Loot.Count > 0; tick++)
        {
            var player = session.View.Actors.Single(a => a.Id == 1);
            var loot = session.View.Loot.OrderBy(l => Position.DistanceSquared(player.Position, l.Position)).ThenBy(l => l.Id).First();
            var direction = CombatProductionSmoke.MovementDirection(player.Position, loot.Position, session.Room);
            pickedUp += recorder.Step(session, [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z), new(CombatCommandKind.Pickup, ItemId: loot.Id)]).Count(e => e.Kind == "LootPickedUp");
        }
        Assert.Equal(positions.Length, pickedUp);
        Assert.Empty(session.View.Loot);
        Assert.True(Ashenwake.Core.Serialization.CombatReplayRunner.Run(Catalog.CombatJson, recorder.Capture()).Success);
    }
    [Fact]
    public void Overlay_preserves_campaign_catalog_and_binds_actual_endgame_policy()
    {
        string campaign = CampaignCombatContent.Parse(Source("combat.json"), Source("campaign-combat.json")).CombatJson;
        Assert.DoesNotContain("\"endgame\"", JsonData.Write(CombatContent.Parse(campaign)));
        var before = CombatSession.CreateEncounter(campaign, 2, "hub");
        Assert.DoesNotContain("\"endgame\"", JsonData.Write(before.Capture()));
        Assert.Equal(before.StateHash, CombatSession.Restore(campaign, before.Capture()).StateHash);
        var policy = EndgameContent.Parse(Source("endgame.json")).Capture();
        policy = policy with { Version = "test.changed", Hunts = policy.Hunts.Select(h => h.Id == "hunt.false_vael" ? h with { RequiredTier = 4 } : h).ToArray() };
        var changed = EndgameCombatContent.Parse(campaign, Source("endgame-combat.json"), EndgameContent.Create(policy));
        Assert.Equal(4, changed.CreateHuntManifest("hunt.false_vael", 42, 1).Tier);
        Assert.NotEqual(Catalog.Hash, changed.Hash);
    }
    [Fact]
    public void Four_room_manifests_are_seeded_and_inheritance_records_every_source_under_shared_cap()
    {
        var hashes = new HashSet<string>();
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var manifest = Fracture(["fracture.inherited_boss"], seed);
            Assert.Equal(4, manifest.Rooms.Length); Assert.Equal(3, manifest.Inheritance.Length);
            Assert.Equal(manifest.Inheritance.Where(s => s.Selected).Select(s => s.Candidate), manifest.Rooms[3].EliteModifiers);
            Assert.True(manifest.Rooms[3].EliteModifiers.Length <= 2); CombatSession.ValidateEliteModifiers(manifest.Rooms[3].EliteModifiers);
            Assert.Equal(JsonData.Hash(manifest), JsonData.Hash(Fracture(["fracture.inherited_boss"], seed)));
            Catalog.ValidateManifest(manifest); hashes.Add(JsonData.Hash(manifest.Rooms));
            Assert.Throws<InvalidDataException>(() => Catalog.ValidateManifest(manifest with { Tier = 5 }));
        }
        Assert.True(hashes.Count > 90);
    }
    [Fact]
    public void Positive_healing_creates_a_delayed_capped_hostile_echo_that_damages_and_cleans_up()
    {
        var session = Quiet(Fracture(["fracture.healing_echoes"])); var state = session.Capture();
        state.Actors[0].Health = 80; session = CombatSession.Restore(Catalog.CombatJson, state);
        var events = session.Step([new(CombatCommandKind.Potion)]);
        Assert.Contains(events, e => e.Kind == "EndgameHealingEchoCreated"); Assert.Equal(1, session.Capture().Endgame!.EchoesCreated);
        Assert.Contains(session.View.Endgame!.Hazards, h => h.Stage == "Warning" && h.ContentId == "endgame.healing_echo");
        int health = session.View.Actors[0].Health;
        for (int i = 0; i < 44; i++) session.Step();
        Assert.True(session.View.Actors[0].Health < health); Assert.Equal(1, session.Capture().Endgame!.EchoesCreated);
        Assert.DoesNotContain(session.View.Endgame!.Hazards, h => h.ContentId == "endgame.healing_echo");
        var next = Catalog.CreateEncounter(session.Capture().Endgame!.Manifest, 1, 0, session.Capture());
        Assert.Equal(0, next.Capture().Endgame!.EchoesCreated); Assert.Empty(next.View.Endgame!.Hazards);
    }
    [Fact]
    public void Burning_haste_changes_actual_enemy_displacement()
    {
        CombatSession Prepare(string rule)
        {
            var s = Catalog.CreateEncounter(Fracture([rule]), 0, 0, Build()); var state = s.Capture();
            var enemy = state.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Role == "Melee");
            enemy.Position = new(-1500, 0); enemy.Statuses.Add(new() { Id = "Burning", SourceId = 1, OwnerId = 1, NextTick = 100, ExpiresTick = 150, ActionId = state.NextActionId++ });
            return CombatSession.Restore(Catalog.CombatJson, state);
        }
        var hasted = Prepare("fracture.burning_haste"); var baseline = Prepare("fracture.fragment_overcharge");
        int id = hasted.View.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Role == "Melee").Id;
        var start = hasted.View.Actors.Single(a => a.Id == id).Position; hasted.Step(); baseline.Step();
        Assert.True(Position.DistanceSquared(start, hasted.View.Actors.Single(a => a.Id == id).Position) > Position.DistanceSquared(start, baseline.View.Actors.Single(a => a.Id == id).Position));
    }
    [Fact]
    public void Inversion_uses_equipment_family_extremes_and_can_expose_a_zero_resistance()
    {
        var session = Quiet(Fracture(["fracture.resistance_inversion"]));
        Assert.Equal(DamageFamily.Fire, session.View.Endgame!.HighestResistanceFamily);
        Assert.Equal(2200, session.View.Endgame.HighestResistanceBasisPoints);
        Assert.Equal(DamageFamily.PhysicalSlash, session.View.Endgame.LowestResistanceFamily);
        Assert.Equal(-1500, session.View.Endgame.LowestResistanceBasisPoints);
        Assert.Equal(115, DamageRules.Resolve(new(100, Family: DamageFamily.PhysicalSlash, DefenseBasisPoints: -1500, MinimumDefenseBasisPoints: -1500)).HealthDamage);
        Assert.Equal(100, DamageRules.Resolve(new(100, DefenseBasisPoints: -1500)).HealthDamage);
    }
    [Fact]
    public void Overcharge_multiplies_fragment_owned_status_damage_without_buffing_ordinary_statuses()
    {
        int Damage(string fragment, int offset)
        {
            var session = Quiet(Fracture(["fracture.fragment_overcharge"])); var state = session.Capture();
            var target = state.Actors.First(a => a.Faction == CombatFaction.Enemy); state.Tick = offset;
            target.Statuses.Add(new() { Id = "Burning", FragmentId = fragment, SourceId = 1, OwnerId = 1, NextTick = offset, ExpiresTick = offset + 100, ActionId = state.NextActionId++ });
            session = CombatSession.Restore(Catalog.CombatJson, state);
            return session.Step().Single(e => e.Kind == "DamageApplied" && e.TargetId == target.Id && e.ContentId == "Burning").Amount;
        }
        Assert.True(Damage("fragment.eye_vael", 0) > Damage("fragment.eye_vael", 40));
        Assert.Equal(Damage("", 0), Damage("", 40));
    }
    [Fact]
    public void Actual_elite_death_leaves_one_persistent_scar_with_an_exit_corridor_and_retry_cleanup()
    {
        var session = Quiet(Fracture(["fracture.elite_hazards"])); var state = session.Capture();
        var elite = state.Actors.Single(a => a.Faction == CombatFaction.Enemy && a.Elite); elite.Health = 1;
        state.Actors[0].Position = new(elite.Position.X - 1000, elite.Position.Z);
        session = CombatSession.Restore(Catalog.CombatJson, state);
        session.Step([new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: elite.Id)]);
        for (int i = 0; i < 50; i++) session.Step();
        var scar = Assert.Single(session.View.Endgame!.Hazards, h => h.ContentId == "endgame.elite_scar");
        Assert.Equal("Active", scar.Stage); Assert.Equal(1, session.Capture().Endgame!.ScarsCreated);
        Assert.True(scar.Position.X - scar.Radius > -4500);
        var restored = CombatSession.Restore(Catalog.CombatJson, session.Capture());
        Assert.Equal(session.StateHash, restored.StateHash);
        var retry = Catalog.CreateEncounter(state.Endgame!.Manifest, 0, 1, session.Capture(), restoreAtAnchor: true);
        Assert.Empty(retry.View.Endgame!.Hazards); Assert.Equal(0, retry.Capture().Endgame!.ScarsCreated);
        Assert.NotEqual(session.View.Endgame.ContextKey, retry.View.Endgame.ContextKey);
        Assert.Null(CombatSession.CreateEncounter(Catalog.CombatJson, 42, "hub", retry.Capture()).View.Endgame);
    }
    [Fact]
    public void Healing_echoes_have_a_per_encounter_creation_cap_and_restore_rejects_tampered_bounds()
    {
        var session = Quiet(Fracture(["fracture.healing_echoes"]));
        for (int i = 0; i < 12; i++)
        {
            var state = session.Capture(); state.Actors[0].Health = 50; state.PotionReadyTick = state.Tick; state.PotionCharges = 3;
            session = CombatSession.Restore(Catalog.CombatJson, state);
            session.Step([new(CombatCommandKind.Potion)]);
        }
        Assert.Equal(8, session.Capture().Endgame!.EchoesCreated); Assert.Equal(8, session.View.Endgame!.Hazards.Length);
        var invalid = session.Capture(); invalid.Endgame!.Hazards.Add(invalid.Endgame.Hazards[0]);
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Catalog.CombatJson, invalid));
        invalid = session.Capture(); invalid.Endgame!.Hazards[0] = invalid.Endgame.Hazards[0] with { Position = new(int.MaxValue, 0) };
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Catalog.CombatJson, invalid));
        invalid = session.Capture(); invalid.Endgame!.Manifest.Rooms[0] = null!;
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Catalog.CombatJson, invalid));
        invalid = session.Capture(); invalid.Endgame!.EchoesCreated = 9;
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Catalog.CombatJson, invalid));
    }
    [Fact]
    public void Weak_points_and_carried_terms_control_actual_boss_immunity()
    {
        var manifest = Catalog.CreateHuntManifest("hunt.orrun_without_oath", 42, 1);
        var session = Catalog.CreateEncounter(manifest, 1, 0, Build()); var state = session.Capture();
        var boss = state.Actors.Single(a => a.DefinitionId == "boss.hunt_orrun"); state.Actors[0].Position = new(boss.Position.X - 1000, boss.Position.Z);
        session = CombatSession.Restore(Catalog.CombatJson, state);
        session.Step([new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: boss.Id)]); for (int i = 0; i < 8; i++) session.Step();
        Assert.Equal(boss.MaxHealth, session.View.Actors.Single(a => a.Id == boss.Id).Health);
        var rejected = session.Step([new(CombatCommandKind.InteractMechanism, TargetId: 1000003)]);
        Assert.Contains(rejected, e => e.Kind == "CommandRejected");
        for (int i = 0; i < 1000 && session.Capture().Endgame!.DepositedTerms < 2; i++) session.Step(EndgameCombatSmoke.Commands(session.View, session.Room));
        Assert.Equal(2, session.Capture().Endgame!.DepositedTerms);
        for (int i = 0; i < 300 && session.View.Actors.Single(a => a.Id == boss.Id).Health == boss.MaxHealth; i++) session.Step(EndgameCombatSmoke.Commands(session.View, session.Room));
        Assert.True(session.View.Actors.Single(a => a.Id == boss.Id).Health < boss.MaxHealth);
    }
    [Fact]
    public void Victory_seals_pending_hostile_effects_and_preserves_loot_and_context()
    {
        var manifest = Fracture(["fracture.healing_echoes"]); var session = Catalog.CreateEncounter(manifest, 0, 0, Build());
        Assert.Throws<InvalidOperationException>(session.SealEndgameVictory);
        while (session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0) && session.Tick < 6000)
            session.Step(EndgameCombatSmoke.Commands(session.View, session.Room));
        Assert.True(session.View.Actors[0].Health > 0); Assert.NotEmpty(session.View.Loot);
        string context = session.View.Endgame!.ContextKey; int health = session.View.Actors[0].Health;
        session.SealEndgameVictory(); for (int i = 0; i < 300; i++) session.Step();
        Assert.Equal(context, session.View.Endgame!.ContextKey); Assert.Equal(health, session.View.Actors[0].Health); Assert.Empty(session.View.Endgame.Hazards);
    }
    [Fact]
    public void Reference_policy_bootstraps_zero_momentum_area_mutations_through_the_free_dash_generator()
    {
        var body = CombatSession.Restore(Catalog.CombatJson, Build());
        body.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.reaping_arc")]);
        var manifest = Catalog.CreateFractureManifest(new(1, 15111065706836454697, "act.shattered_spine", 1, ["fracture.burning_haste"], "Vael", "Materials"), 1);
        var session = Catalog.CreateEncounter(manifest, 0, 0, body.Capture());
        Assert.Equal(0, session.View.Resource); var skills = new HashSet<string>();
        for (int tick = 0; tick < 3000 && session.View.Actors[0].Health > 0 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++)
            foreach (var ev in session.Step(EndgameCombatSmoke.Commands(session.View, session.Room))) if (ev.Kind == "AbilityStarted" && ev.ActorId == 1) skills.Add(ev.ContentId);
        Assert.Contains("skill.charge", skills); Assert.Contains(skills, id => id != "skill.charge");
        Assert.True(session.View.Actors[0].Health > 0); Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
    }
    [Fact]
    public void Reference_policy_finishes_a_safe_generator_cast_before_dodging_a_later_warning()
    {
        var manifest = Catalog.CreateFractureManifest(new(1, 15111065706836454697, "act.shattered_spine", 1, ["fracture.burning_haste"], "Vael", "Materials"), 1);
        var session = Catalog.CreateEncounter(manifest, 1, 0, Build()); var state = session.Capture();
        var target = state.Actors.First(a => a.DefinitionId == "enemy.contract_keeper");
        state.Actors[0].Position = new(target.Position.X - 1000, target.Position.Z);
        foreach (var actor in state.Actors.Where(a => a.Id != 1)) actor.RecoveryUntil = 3000;
        state.Actors[0].Pending = new("skill.charge", target.Id, target.Position, 14, state.NextActionId++);
        state.Actors[0].RecoveryUntil = 20; state.Actors[0].State = "Windup";
        state.Campaign!.Hazards.Add(new(state.NextObjectId++, "Circle", state.Actors[0].Position, state.Actors[0].Position, 1500, 34, "campaign.oathmark", target.Id, 16, DamageFamily.PhysicalCrush, "Rooted", state.NextActionId++));
        session = CombatSession.Restore(Catalog.CombatJson, state);
        Assert.DoesNotContain(EndgameCombatSmoke.Commands(session.View, session.Room), c => c.Kind == CombatCommandKind.Dodge);
        bool landed = false;
        for (int i = 0; i < 18; i++) landed |= session.Step(EndgameCombatSmoke.Commands(session.View, session.Room)).Any(e => e.Kind == "DamageApplied" && e.ContentId == "skill.charge" && e.Amount > 0);
        Assert.True(landed); Assert.True(session.View.Resource > 0);
    }
    public static IEnumerable<object[]> Hunts() => from discipline in CombatSession.Disciplines from hunt in EndgameContent.Default().Capture().Hunts select new object[] { discipline, hunt.Id };
    [Theory]
    [MemberData(nameof(Hunts))]
    public void Every_hunt_has_three_actual_playable_phases_and_save_continuation(string discipline, string huntId)
    {
        var manifest = Catalog.CreateHuntManifest(huntId, 42, 1); CombatSnapshot carry = Build(discipline);
        var observed = new HashSet<string>();
        for (int phase = 0; phase < 3; phase++)
        {
            var session = Catalog.CreateEncounter(manifest, phase, 0, carry);
            for (int tick = 0; tick < 9000 && session.View.Actors[0].Health > 0 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++)
            {
                foreach (var ev in session.Step(EndgameCombatSmoke.Commands(session.View, session.Room))) observed.Add(ev.Kind);
                if (tick == 120)
                {
                    var restored = CombatSession.Restore(Catalog.CombatJson, session.Capture());
                    var commands = EndgameCombatSmoke.Commands(session.View, session.Room);
                    Assert.Equal(JsonData.Hash(session.Step(commands)), JsonData.Hash(restored.Step(commands)));
                    Assert.Equal(session.StateHash, restored.StateHash); session = restored;
                }
            }
            Assert.True(session.View.Actors[0].Health > 0, discipline + " died in " + huntId + " phase " + phase);
            Assert.False(session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0), discipline + " stalled in " + huntId + " phase " + phase);
            Assert.Empty(session.View.Endgame!.Hazards); Assert.True(session.View.PeakEffects <= CombatSession.MaxEffectsPerTick);
            carry = session.Capture();
        }
        Assert.Contains("EndgameHazardWarned", observed);
        if (huntId is "hunt.orrun_without_oath" or "hunt.thousand_memories" or "hunt.nhal_reconstruction") Assert.Contains("HuntMechanismUsed", observed);
    }
    [Theory]
    [InlineData("Vanguard")]
    [InlineData("Veilwalker")]
    [InlineData("Arcanist")]
    [InlineData("Gravecaller")]
    [InlineData("Warden")]
    public void Reference_disciplines_complete_four_actual_rooms_with_inherited_elites(string discipline)
    {
        var manifest = Fracture(["fracture.inherited_boss", "fracture.fragment_overcharge"], 48); var carry = Build(discipline);
        for (int index = 0; index < 4; index++)
        {
            var s = Catalog.CreateEncounter(manifest, index, 0, carry);
            for (int tick = 0; tick < 10000 && s.View.Actors[0].Health > 0 && s.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++) s.Step(EndgameCombatSmoke.Commands(s.View, s.Room));
            Assert.True(s.View.Actors[0].Health > 0, discipline + " died in room " + index);
            Assert.False(s.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0), discipline + " stalled in room " + index);
            carry = s.Capture();
        }
    }
}
