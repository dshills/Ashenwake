using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Ashenwake.Tests;

public sealed class MidgameBuildComparisonTests(ITestOutputHelper output)
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));

    [Theory]
    [InlineData("Veilwalker", "poison", "campaign.plague_village", LegendaryEquipment.RotwakePower)]
    [InlineData("Warden", "summon", "campaign.extraction_floor", LegendaryEquipment.MourningPower)]
    [InlineData("Vanguard", "barrier", "campaign.extraction_floor", "rune.guard")]
    [InlineData("Arcanist", "resource", "campaign.extraction_floor", LegendaryEquipment.FurnacePower)]
    [InlineData("Gravecaller", "resource", "campaign.plague_village", LegendaryEquipment.FurnacePower)]
    public void CompatibleBuildPropertiesEngageInRealMidgameCombatWithEqualStatControls(string discipline, string archetype, string encounter, string property)
    {
        var content = CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json"));
        var enabled = LegalPropertyBuild(discipline, archetype);
        var disabled = enabled with { VirulentWake = false, RallyingChorus = false, BarrierOnDodge = false, CinderCycle = false };
        var results = new List<object>();
        foreach (bool powered in new[] { false, true })
        {
            var hub = content.CreateEncounter("hub"); hub.ApplyProgressionBuild(powered ? enabled : disabled);
            var initial = hub.Capture(); initial.Fragments.Clear(); initial.Equipment.Clear();
            // Carried resource is legal across room transitions. Both comparisons
            // begin at the same value, so summon/vent choices are available before
            // this small encounter ends; other classes begin at their normal zero.
            if (discipline is "Warden" or "Arcanist") initial.Momentum = 50;
            var session = content.CreateEncounter(encounter, previous: initial); var recorder = new CombatRecorder(session);
            int outgoing = 0, incoming = 0, triggers = 0, effectAmount = 0, supportResolutions = 0;
            var abilities = new SortedDictionary<string, int>(); int peakResource = 0, peakAllies = 0;
            for (int tick = 0; tick < 4500 && session.View.Actors[0].Health > 0 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++)
            {
                var events = recorder.Step(session, Commands(session, archetype));
                foreach (var cast in events.Where(e => e.Kind == "AbilityStarted" && e.ActorId == 1)) abilities[cast.ContentId] = abilities.GetValueOrDefault(cast.ContentId) + 1;
                peakResource = Math.Max(peakResource, session.View.Resource); peakAllies = Math.Max(peakAllies, session.View.Actors.Count(a => a.Faction == CombatFaction.Ally && a.Health > 0));
                outgoing += events.Where(e => e.Kind == "DamageApplied" && e.TargetId != 1).Sum(e => e.Amount);
                incoming += events.Where(e => e.Kind == "DamageApplied" && e.TargetId == 1).Sum(e => e.Amount);
                var effects = events.Where(e => e.ContentId == property && e.Kind is "LegendaryTriggered" or "BarrierGranted").ToArray();
                triggers += effects.Length; effectAmount += effects.Sum(e => e.Amount);
                supportResolutions += events.Count(e => e.Kind == "CampaignHazardResolved" && CombatSession.IsSupportHazard(e.ContentId));
                Assert.InRange(session.View.PeakEffects, 0, CombatSession.MaxEffectsPerTick);
                Assert.InRange(session.View.CampaignHazards!.Count, 0, 32);
            }
            output.WriteLine($"{discipline}/{archetype}/{(powered ? "enabled" : "control")}: ticks={session.Tick}, health={session.View.Actors[0].Health}, outgoing={outgoing}, incoming={incoming}, triggers={triggers}, amount={effectAmount}, supports={supportResolutions}");
            output.WriteLine($"Peak resource {peakResource}; peak allies {peakAllies}; casts {JsonData.Write(abilities)}");
            Assert.True(session.View.Actors[0].Health > 0, discipline + " died with " + archetype);
            Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
            Assert.Equal(powered, triggers > 0);
            Assert.Equal(session.StateHash, CombatSession.Restore(content.CombatJson, session.Capture()).StateHash);
            var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture()); Assert.True(replay.Success, replay.Detail);
            Assert.Equal(session.StateHash, replay.FinalHash);
            results.Add(new { powered, ticks = session.Tick, health = session.View.Actors[0].Health, outgoing, incoming, triggers, effectAmount, supportResolutions, hash = session.StateHash });
        }
        output.WriteLine(JsonData.Write(new { discipline, archetype, encounter, seed = 42, startingResource = discipline is "Warden" or "Arcanist" ? 50 : 0, comparison = "Controlled level-8 property toggles with identical base stats, no anatomy, and legal progression grants; adaptive public-command policy, not earned campaign balance or universal build ranking.", results }));
    }

    // Public progression operations validate the level budget, property ownership and
    // allowed slots. The combat fixture holds base gear statistics equal to isolate
    // each property rather than comparing different item armor/critical rolls.
    private static CombatProgressionBuild LegalPropertyBuild(string discipline, string archetype)
    {
        var content = ProgressionContent.Parse(Read("progression.json")); var progression = ProgressionSession.Create(content, discipline);
        Assert.True(progression.EarnExperience("level", (int)progression.ExperienceForLevel(8), 100).Success);
        for (int i = 0; i < 7; i++) Assert.True(progression.AllocatePassive("passive." + i, i % 2 == 0 ? "Offense" : "Defense").Success);
        string item = archetype switch { "poison" => LegendaryEquipment.Rotwake, "summon" => LegendaryEquipment.Mourning, "barrier" => "item.starter_chest", _ => LegendaryEquipment.Furnace };
        Assert.True(progression.GrantItem("item", item, ItemRarity.Legendary).Success);
        var definition = content.Capture().Items.Single(i => i.Id == item); var owned = Assert.Single(progression.Capture().Character.Items);
        Assert.True(progression.Equip("equip", owned.Id, definition.Slots[0]).Success);
        if (archetype == "barrier")
        {
            foreach (string id in new[] { "mara", "torren", "cael", "oris", "kesh", "haven" }) Assert.True(progression.CompleteObjective("objective." + id, "objective." + id).Success);
            Assert.True(progression.Craft(new("engrave", CraftingService.Engraving, owned.Id, PropertyId: "rune.guard")).Success);
        }
        Assert.Equal(progression.StateHash, ProgressionSession.Restore(content, progression.Capture()).StateHash);
        var properties = progression.Capture().Character.Items.SelectMany(i => new[] { content.Capture().Items.Single(d => d.Id == i.DefinitionId).Property, i.Engraving }).ToHashSet();
        return new(Discipline: discipline, Level: progression.Level, Offense: progression.View.Stats["passive.Offense"], Defense: progression.View.Stats["passive.Defense"], UltimateUnlocked: false, UnlockedMutations: [])
        {
            VirulentWake = properties.Contains(LegendaryEquipment.RotwakePower),
            RallyingChorus = properties.Contains(LegendaryEquipment.MourningPower),
            BarrierOnDodge = properties.Contains("rune.guard"),
            CinderCycle = properties.Contains(LegendaryEquipment.FurnacePower)
        };
    }

    private static CombatCommand[] Commands(CombatSession session, string archetype)
    {
        var view = session.View; var player = view.Actors[0];
        // The barrier comparison holds its attack position through ground warnings
        // so the ward is actually spent. Both variants use this same bounded policy.
        var commands = (archetype == "barrier" ? CombatProductionSmoke.Commands(view, session.Room)
            : CampaignCombatSmoke.Commands(view, session.Room)).ToList();
        var target = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0).OrderBy(a => Position.DistanceSquared(player.Position, a.Position)).ThenBy(a => a.Id).FirstOrDefault();
        if (target is null) return commands.ToArray();
        var available = view.Skills.Where(s => s.Available && s.RemainingTicks == 0 && (s.ResourceMode == "Heat" ? view.Resource + s.Cost <= 100 : s.Cost <= view.Resource)).ToArray();
        if (archetype == "barrier" && view.DodgeCooldownTicks == 0 && view.Tick % 60 == 0)
            commands.Add(new(CombatCommandKind.Dodge, Z: player.Position.Z >= 0 ? -1 : 1));
        if (view.Tick % 3 != 0) return commands.ToArray();
        string? skill = null;
        if (archetype == "poison")
        {
            commands.RemoveAll(c => c.Kind == CombatCommandKind.Cast);
            if (target.Health > 40 || !target.Statuses.Any(s => s.Id == "Poisoned")) skill = available.FirstOrDefault(s => s.Id == "skill.venom_knife")?.Id;
        }
        else if (archetype == "summon")
        {
            commands.RemoveAll(c => c.Kind == CombatCommandKind.Cast);
            skill = !view.Actors.Any(a => a.Faction == CombatFaction.Ally && a.Health > 0)
                ? available.FirstOrDefault(s => s.Id == "skill.feral_companion")?.Id : null;
            skill ??= available.FirstOrDefault(s => s.Id == "skill.thorn_shot")?.Id;
        }
        else if (archetype == "resource" && view.Discipline == "Arcanist")
        {
            commands.RemoveAll(c => c.Kind == CombatCommandKind.Cast);
            skill = view.Resource >= 52 ? available.FirstOrDefault(s => s.Id == "skill.vent")?.Id
                : available.FirstOrDefault(s => s.Id == "skill.frost_nova")?.Id;
        }
        else if (archetype == "resource" && view.Discipline == "Gravecaller")
        {
            var corpse = view.Actors.FirstOrDefault(a => a.Faction == CombatFaction.Enemy && a.Health == 0 && !a.CorpseConsumed && Position.DistanceSquared(a.Position, player.Position) <= 6000L * 6000);
            if (corpse is not null && view.Legendary is { CinderRemainingTicks: > 0 }) commands.Add(new(CombatCommandKind.ConsumeCorpse, TargetId: corpse.Id));
        }
        if (skill is not null) { commands.RemoveAll(c => c.Kind == CombatCommandKind.Cast); commands.Add(new(CombatCommandKind.Cast, SkillId: skill, TargetId: target.Id)); }
        return commands.ToArray();
    }
}
