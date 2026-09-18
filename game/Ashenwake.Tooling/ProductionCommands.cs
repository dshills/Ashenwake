using Ashenwake.Core.Adventure;
using Ashenwake.Core.Authoring;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using System.Diagnostics;

namespace Ashenwake.Tooling;

internal static class ProductionCommands
{
    public static int Run(string[] args)
    {
        string combat = File.ReadAllText("content/combat.json");
        var adventure = AdventureContent.Parse(File.ReadAllText("content/adventure.json"));
        var policy = ProgressionContent.Parse(File.ReadAllText("content/progression.json"));
        var progression = ProductionContent.Resolve(combat, policy, adventure);
        var text = TextCatalog.Parse(File.ReadAllText("content/text.en.json"));
        if (args[1] is "validate" or "compile")
        {
            ExpeditionSession.ValidateContent(combat, adventure);
            if (args[1] == "compile")
                foreach (string file in new[] { "combat", "adventure", "progression", "text.en" })
                    AtomicFile.Write("game/Ashenwake.Client/" + file + ".json", File.ReadAllText("content/" + file + ".json"));
            Console.WriteLine(JsonData.Write(new { kind = "ProductionContentValidated", combatHash = JsonData.Hash(CombatContent.Parse(combat)), adventureHash = adventure.Hash, progressionHash = progression.Hash, textHash = text.Hash }));
            return 0;
        }
        if (args[1] == "replay" && args.Length == 3)
        {
            var result = ProductionReplayRunner.Run(combat, adventure, policy, JsonData.Read<ProductionReplay>(File.ReadAllText(args[2])));
            Console.WriteLine(JsonData.Write(result)); return result.Success ? 0 : 1;
        }
        if (args[1] == "migrate-phase2" && args.Length == 4)
        {
            if (File.Exists(args[3]) || Directory.Exists(args[3])) throw new IOException("Migration destination must be a new file; the original is never overwritten.");
            var session = PhaseTwoMigration.Read(File.ReadAllText(args[2]), combat, adventure, policy);
            ProductionSaveStore.Write(args[3], combat, adventure, policy, session.Capture());
            Console.WriteLine(JsonData.Write(new { kind = "PhaseTwoMigrationPassed", session.StateHash, sourcePreserved = args[2], destination = args[3] })); return 0;
        }
        if (args[1] is "demo" or "benchmark")
        {
            string key = JsonData.Hash(new { combat = CombatContent.Parse(combat).Identity, adventure = adventure.Hash, progression = progression.Hash })[..12];
            string output = Path.Combine(args.Length > 2 ? args[2] : "artifacts/production", key);
            var reports = new List<object>(); var samples = new List<double>(); long allocation = 0;
            var disciplines = CombatSession.Disciplines.ToArray();
            for (int index = args[1] == "benchmark" ? -1 : 0; index < disciplines.Length; index++)
            {
                string discipline = disciplines[Math.Max(0, index)]; var session = ProductionSession.Create(combat, adventure, policy, 42, discipline);
                int operations = 0; var events = new SortedDictionary<string, int>(StringComparer.Ordinal);
                while (!ProductionSmoke.Complete(session) && operations < ProductionSmoke.MaximumCommands)
                {
                    var command = ProductionSmoke.Next(session); long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                    var result = session.Execute(command);
                    double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    if (index >= 0) { allocation += GC.GetAllocatedBytesForCurrentThread() - before; samples.Add(ms); }
                    if (!result.Success) throw new InvalidDataException("Production public action rejected: " + result.Reason);
                    foreach (var e in result.CombatEvents) events[e.Kind] = events.GetValueOrDefault(e.Kind) + 1;
                    operations++;
                }
                if (!ProductionSmoke.Complete(session)) throw new InvalidDataException("Production route did not finish: " + discipline + "/" + session.View.RoomId);
                if (index < 0) continue;
                var replay = session.CaptureReplay(); var verified = ProductionReplayRunner.Run(combat, adventure, policy, replay);
                if (!verified.Success) throw new InvalidDataException("Production replay diverged: " + discipline);
                string directory = Path.Combine(output, discipline.ToLowerInvariant());
                string replayPath = Path.Combine(directory, "production.awp"), savePath = Path.Combine(directory, "production.save.json");
                AtomicFile.Write(replayPath, JsonData.Write(replay));
                ProductionSaveStore.Write(savePath, combat, adventure, policy, session.Capture());
                if (ProductionSaveStore.Load(savePath, combat, adventure, policy).Session.StateHash != session.StateHash) throw new InvalidDataException("Production save changed state: " + discipline);
                reports.Add(new { discipline, operations, tick = session.Combat.Tick, session.StateHash, level = session.ProgressionView.Level, deaths = session.Capture().Expedition.Adventure.Deaths, events, replayPath, savePath, replayOperations = replay.Frames.Length });
            }
            samples.Sort(); double Percentile(double p) => samples[(int)Math.Ceiling(samples.Count * p) - 1];
            var report = new
            {
                kind = args[1] == "demo" ? "ProductionDemoPassed" : "ProductionBenchmarkPassed",
                key,
                reports,
                operations = samples.Count,
                p50Ms = Percentile(.5),
                p95Ms = Percentile(.95),
                p99Ms = Percentile(.99),
                maxMs = samples[^1],
                bytesAllocatedPerOperation = allocation / samples.Count,
                scope = "Five fresh-character full dungeon routes. Execute includes permanent synchronization, snapshot/event hashes and bounded replay. Input policy, presentation and disk IO are outside timing. Benchmark includes a separate warmup route.",
                replayScope = "Each archive retains at most 1800 operations with its own checkpoint origin; long runs verify the retained tail."
            };
            AtomicFile.Write(Path.Combine(output, args[1] + ".json"), JsonData.Write(report)); Console.WriteLine(JsonData.Write(report));
            if (args[1] == "benchmark" && Percentile(.99) >= 25) throw new InvalidDataException("Full production operation p99 exceeds 25 ms budget.");
            return 0;
        }
        return 2;
    }
}
