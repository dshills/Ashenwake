using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;

namespace Ashenwake.Core.Combat;

public sealed record EndgameCombatDiagnosticCase(string Discipline, int Resonance, string BuildVariant, string Kind, string ContentId, ulong Seed,
    bool Success, int ClearedRooms, long Ticks, int HealthRemaining, int DamageDealt, int DamageTaken,
    int PeakActors, int PeakSummons, int PeakProjectiles, int PeakHazards, int PeakEffects, int SummonsCreated,
    string[] UsedSkills, string[] DamageFamilies, bool RestoreVerified, string StateHash);
public sealed record EndgameCombatDiagnosticReport(string ContentHash, string BuildPolicy, EndgameCombatDiagnosticCase[] Cases);

/// <summary>Authored isolated reference loadouts; this is separate evidence from earned campaign-to-endgame playthroughs.</summary>
public static class EndgameCombatDiagnostics
{
    public static EndgameCombatDiagnosticReport Run(EndgameCombatContent catalog, ulong seed = 42)
    {
        var content = CombatContent.Parse(catalog.CombatJson); var policy = EndgameContent.Create(content.Endgame!.Policy!);
        var cases = new List<EndgameCombatDiagnosticCase>();
        foreach (string discipline in CombatSession.Disciplines)
            foreach (bool high in new[] { false, true })
            {
                foreach (string region in policy.Capture().Regions)
                {
                    var sigil = new FractureSigil(1, seed, region, 4,
                        high ? ["fracture.inherited_boss", "fracture.fragment_overcharge", "fracture.resistance_inversion"] : ["fracture.burning_haste", "fracture.healing_echoes"],
                        policy.Capture().BossFamilies[cases.Count % policy.Capture().BossFamilies.Length], "Materials");
                    cases.Add(RunCase(catalog, catalog.CreateFractureManifest(sigil, 1), discipline, high));
                }
                foreach (var hunt in policy.Capture().Hunts) cases.Add(RunCase(catalog, catalog.CreateHuntManifest(hunt.Id, seed, 1), discipline, high));
            }
        foreach (string discipline in new[] { "Vanguard", "Veilwalker" })
        {
            var sigil = new FractureSigil(1, seed, "act.verdant_maw", 4, ["fracture.burning_haste", "fracture.healing_echoes"], "Serath", "Materials");
            cases.Add(RunCase(catalog, catalog.CreateFractureManifest(sigil, 1), discipline, false, poisonPreparation: true));
        }
        return new(catalog.Hash, "Isolated level20, Offense10/Defense8, affix damage12/armor800/crit600, explicit12/64 Resonance; all casts/movement ordinary commands, health/potions carry between rooms.", cases.ToArray());
    }
    private static EndgameCombatDiagnosticCase RunCase(EndgameCombatContent catalog, EndgameCombatManifest manifest, string discipline, bool high, bool poisonPreparation = false)
    {
        var definitions = CombatContent.Parse(catalog.CombatJson);
        var session = CombatSession.CreateEncounter(catalog.CombatJson, manifest.Seed, "hub");
        session.ApplyProgressionBuild(new(discipline, Level: 20, Offense: 10, Defense: 8, FlatDamage: 12, Armor: 800, CriticalBasisPoints: 600)
        { Resistances = new() { [poisonPreparation ? DamageFamily.Venom : DamageFamily.Fire] = 2200, [DamageFamily.Frost] = 900, [DamageFamily.PhysicalCrush] = 1000 } });
        session.ApplyAnatomy(high ? new Dictionary<string, string> { ["Eyes"] = "fragment.eye_vael", ["Heart"] = "fragment.heart_serath", ["Spine"] = "fragment.nerve_ilyra", ["Arms"] = "fragment.orrun_bone" } : new Dictionary<string, string> { ["Eyes"] = "fragment.eye_vael" });
        if (high) session.ApplyAdventureBuild(new("manifestation.burning_blood", SecondaryManifestation: "manifestation.stone_memory"));
        if (discipline == "Vanguard" && high)
            session.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.reaping_arc"), new(CombatCommandKind.SetMutation, ContentId: "mutation.furnace")]);
        if (discipline == "Arcanist") session.Step([new(CombatCommandKind.SetMutation, ContentId: high ? "mutation.living_flame" : "mutation.forking_flame")]);
        int resonance = session.View.Resonance, cleared = 0, peakActors = 0, peakSummons = 0, peakProjectiles = 0, peakHazards = 0, peakEffects = 0, summons = 0, dealt = 0, taken = 0;
        long ticks = 0; bool restored = true; var skills = new SortedSet<string>(); var families = new SortedSet<string>();
        var carry = session.Capture();
        for (int roomIndex = 0; roomIndex < manifest.Rooms.Length; roomIndex++)
        {
            session = catalog.CreateEncounter(manifest, roomIndex, 0, carry);
            for (int step = 0; step < 9000 && session.View.Actors[0].Health > 0 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); step++)
            {
                var view = session.View; var commands = EndgameCombatSmoke.Commands(view, session.Room);
                // The conversion reference explicitly uses its unlocked ultimate after naturally building Momentum.
                if (discipline == "Vanguard" && high && view.Skills.FirstOrDefault(s => s.Id == "skill.cataclysm" && s.Available && s.RemainingTicks == 0) is { } ultimate)
                {
                    if (view.Resource >= ultimate.Cost) commands = [.. commands.Where(c => c.Kind != CombatCommandKind.Cast), new(CombatCommandKind.Cast, SkillId: ultimate.Id)];
                    else if (commands.FirstOrDefault(c => c.Kind == CombatCommandKind.Cast) is { } cast && view.Skills.FirstOrDefault(s => s.Generate > 0 && s.RemainingTicks == 0 && s.Cost <= view.Resource) is { } generator)
                        commands = [.. commands.Where(c => c.Kind != CombatCommandKind.Cast), new(CombatCommandKind.Cast, SkillId: generator.Id, TargetId: cast.TargetId)];
                }
                if (step == 100)
                {
                    var copy = CombatSession.Restore(catalog.CombatJson, session.Capture());
                    var left = session.Step(commands); var right = copy.Step(commands);
                    restored &= session.StateHash == copy.StateHash && JsonData.Hash(left) == JsonData.Hash(right); session = copy;
                    Observe(right);
                }
                else Observe(session.Step(commands));
                ticks++; view = session.View;
                peakActors = Math.Max(peakActors, view.Actors.Count); peakSummons = Math.Max(peakSummons, view.Actors.Count(a => a.Faction == CombatFaction.Ally && a.Health > 0));
                peakProjectiles = Math.Max(peakProjectiles, view.Projectiles.Count); peakHazards = Math.Max(peakHazards, (view.CampaignHazards?.Count ?? 0) + (view.Endgame?.Hazards.Length ?? 0)); peakEffects = Math.Max(peakEffects, view.PeakEffects);
            }
            if (session.View.Actors[0].Health <= 0 || session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)) break;
            cleared++; carry = session.Capture();
        }
        return new(discipline, resonance, poisonPreparation ? "LowResonanceVenomPreparation" : high ? "HighResonance" : "LowResonance", manifest.Kind, manifest.Kind == "Fracture" ? manifest.Region + "." + manifest.ContentId : manifest.ContentId,
            manifest.Seed, cleared == manifest.Rooms.Length, cleared, ticks, session.View.Actors[0].Health, dealt, taken,
            peakActors, peakSummons, peakProjectiles, peakHazards, peakEffects, summons, skills.ToArray(), families.ToArray(), restored, session.StateHash);
        void Observe(IReadOnlyList<CombatEvent> events)
        {
            foreach (var ev in events)
            {
                if (ev.Kind == "SummonSpawned") summons++;
                if (ev.Kind == "AbilityStarted" && ev.ActorId == 1) skills.Add(ev.ContentId);
                if (ev.Kind != "DamageApplied" || ev.Amount <= 0) continue;
                if (ev.TargetId == 1) { taken += ev.Amount; continue; }
                if (!session.View.Actors.Any(a => a.Id == ev.TargetId && a.Faction == CombatFaction.Enemy)) continue;
                dealt += ev.Amount;
                var skill = definitions.Skills.FirstOrDefault(s => s.Id == ev.ContentId);
                if (skill is not null) families.Add(skill.Id == "skill.cataclysm" && high && discipline == "Vanguard" ? "Fire" : skill.Family.ToString());
                else if (ev.ContentId is "Burning" or "effect.burning_blood") families.Add("Fire");
                else if (ev.ContentId == "Poisoned") families.Add("Venom");
                else if (ev.ContentId == "Bleeding") families.Add("PhysicalSlash");
            }
        }
    }
}
