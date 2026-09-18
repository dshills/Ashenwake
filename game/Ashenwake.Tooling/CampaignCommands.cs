using System.Diagnostics;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Tooling;

internal static class CampaignCommands
{
    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 4 || args[1] is not ("validate" or "compile" or "demo" or "benchmark" or "replay" or "migrate-phase3")) return 2;
        string baseline = File.ReadAllText("content/combat.json");
        string combat = CampaignCombatContent.Parse(baseline, File.ReadAllText("content/campaign-combat.json")).CombatJson;
        var adventure = AdventureContent.Parse(File.ReadAllText("content/adventure.json"));
        var policy = ProgressionContent.Parse(File.ReadAllText("content/progression.json"));
        var campaign = CampaignContent.Parse(File.ReadAllText("content/campaign.json"));
        string key = JsonData.Hash(new { combat = CombatContent.Parse(combat).Identity, adventure = adventure.Hash, policy = policy.Hash, campaign = campaign.Hash })[..12];
        if (args[1] == "migrate-phase3")
        {
            if (args.Length != 4) return 2;
            if (File.Exists(args[3]) || Directory.Exists(args[3])) throw new IOException("Campaign migration requires a new destination; source files are never overwritten.");
            var imported = CampaignRuntimeMigration.ImportPhaseThree(File.ReadAllText(args[2]), baseline, combat, adventure, policy, campaign);
            CampaignRuntimeSaveStore.Write(args[3], combat, adventure, policy, campaign, imported.Capture());
            Console.WriteLine(JsonData.Write(new { kind = "PhaseThreeMigrationPassed", imported.StateHash, sourcePreserved = args[2], destination = args[3] })); return 0;
        }
        if (args.Length > 3) return 2;
        if (args[1] is "validate" or "compile")
        {
            CampaignRuntimeSession.Create(combat, adventure, policy, campaign);
            if (args[1] == "compile")
            {
                ProductionCommands.Run(["production", "compile"]);
                foreach (string name in new[] { "campaign", "campaign-combat" })
                    AtomicFile.Write("game/Ashenwake.Client/" + name + ".json", File.ReadAllText("content/" + name + ".json"));
            }
            Console.WriteLine(JsonData.Write(new { kind = "CampaignContentValidated", key, campaignHash = campaign.Hash, combatHash = CombatContent.Parse(combat).Identity })); return 0;
        }
        if (args[1] == "replay")
        {
            if (args.Length != 3) return 2;
            var result = CampaignRuntimeReplayRunner.Run(combat, adventure, policy, campaign, JsonData.Read<CampaignRuntimeReplay>(File.ReadAllText(args[2])));
            Console.WriteLine(JsonData.Write(result)); return result.Success ? 0 : 1;
        }
        string output = Path.Combine(args.Length > 2 ? args[2] : "artifacts/campaign", key);
        var rows = new List<object>(); var samples = new List<double>(); long allocations = 0;
        string[] disciplines = CombatSession.Disciplines.ToArray();
        for (int index = args[1] == "benchmark" ? -1 : 0; index < disciplines.Length; index++)
        {
            string discipline = disciplines[Math.Max(index, 0)];
            var session = CampaignRuntimeSession.Create(combat, adventure, policy, campaign, 42, discipline);
            int commands = 0, verifiedSegments = 0, restoredBoundaries = 0; string boundary = "";
            var events = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void VerifyReplay()
            {
                var verified = CampaignRuntimeReplayRunner.Run(combat, adventure, policy, campaign, session.CaptureReplay());
                if (!verified.Success || verified.FinalHash != session.StateHash) throw new InvalidDataException("Campaign replay diverged: " + discipline + "/" + commands);
                verifiedSegments++;
            }
            while (!CampaignRuntimeSmoke.Complete(session) && commands < CampaignRuntimeSmoke.MaximumCommands)
            {
                var command = CampaignRuntimeSmoke.Next(session);
                long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                var result = session.Execute(command);
                if (index >= 0) { samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); allocations += GC.GetAllocatedBytesForCurrentThread() - allocated; }
                if (!result.Success) throw new InvalidDataException("Campaign smoke action rejected: " + discipline + "/" + command.Action + "/" + result.Reason);
                foreach (var e in result.CombatEvents) events[e.Kind] = events.GetValueOrDefault(e.Kind) + 1;
                commands++;
                if (commands % 600 == 0) VerifyReplay();
                string current = session.View.Act + ":" + session.ActiveEncounterId + ":" + session.EncounterCleared + ":" + session.InHub;
                if (current != boundary)
                {
                    var restored = CampaignRuntimeSession.Restore(combat, adventure, policy, campaign, session.Capture());
                    if (restored.StateHash != session.StateHash) throw new InvalidDataException("Campaign boundary restore changed state.");
                    restoredBoundaries++; boundary = current;
                }
            }
            if (!CampaignRuntimeSmoke.Complete(session))
            {
                AtomicFile.Write(Path.Combine(output, discipline.ToLowerInvariant(), "failed-checkpoint.json"), JsonData.Write(session.Capture()));
                throw new InvalidDataException("Campaign route exceeded its bound: " + discipline + "/" + session.ActiveEncounterId);
            }
            VerifyReplay();
            if (index < 0) continue;
            string directory = Path.Combine(output, discipline.ToLowerInvariant());
            string replayPath = Path.Combine(directory, "campaign.awcampaign"), savePath = Path.Combine(directory, "campaign.save.json");
            AtomicFile.Write(replayPath, JsonData.Write(session.CaptureReplay()));
            CampaignRuntimeSaveStore.Write(savePath, combat, adventure, policy, campaign, session.Capture());
            var loaded = CampaignRuntimeSaveStore.Load(savePath, combat, adventure, policy, campaign).Session;
            if (loaded.StateHash != session.StateHash) throw new InvalidDataException("Campaign save/profile round trip changed state: " + discipline);
            var state = session.Capture();
            rows.Add(new
            {
                discipline,
                commands,
                state.Tick,
                session.StateHash,
                encounters = state.Campaign.CompletedEncounters.Count,
                exploration = state.Campaign.CompletedExploration.Count,
                choices = state.Campaign.Choices,
                deaths = state.Campaign.Deaths,
                level = session.Production.ProgressionView.Level,
                ending = session.View.Ending,
                verifiedSegments,
                restoredBoundaries,
                events,
                replayPath,
                savePath
            });
            Console.WriteLine(JsonData.Write(new { kind = "CampaignDisciplinePassed", discipline, commands, verifiedSegments, restoredBoundaries, session.StateHash }));
        }
        samples.Sort(); double Percentile(double p) => samples[(int)Math.Ceiling(samples.Count * p) - 1];
        var report = new
        {
            kind = args[1] == "benchmark" ? "CampaignBenchmarkPassed" : "CampaignDemoPassed",
            key,
            seed = 42,
            rows,
            operations = samples.Count,
            p50Ms = Percentile(.5),
            p95Ms = Percentile(.95),
            p99Ms = Percentile(.99),
            maxMs = samples[^1],
            bytesAllocatedPerOperation = allocations / samples.Count,
            scope = "Five fresh disciplines through the authored campaign and optional exploration. Full public command execution, synchronization and replay hashing; excludes driver, archive verification, disk IO and rendering.",
            replayScope = "Verify rolling checkpoints every 600 commands and at completion; overlapping segments cover the entire run, including before the retained replay tail.",
            limitations = "Greybox engineering route. Scripted success is not human balance, narrative comprehension, production art or platform certification."
        };
        AtomicFile.Write(Path.Combine(output, args[1] + ".json"), JsonData.Write(report)); Console.WriteLine(JsonData.Write(report));
        if (args[1] == "benchmark" && Percentile(.99) >= 25) throw new InvalidDataException("Campaign command p99 exceeds 25 ms budget.");
        return 0;
    }
}
