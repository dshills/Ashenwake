using System.Diagnostics;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Tooling;

internal static class AdventureCommands
{
    public static int Run(string[] args)
    {
        string combat = File.ReadAllText("content/combat.json"), world = File.ReadAllText("content/adventure.json");
        var content = AdventureContent.Parse(world);
        if (args[1] is "validate" or "compile")
        {
            ExpeditionSession.Create(combat, content);
            if (args[1] == "compile") AtomicFile.Write("game/Ashenwake.Client/adventure.json", world);
            Console.WriteLine(JsonData.Write(new { kind = "AdventureContentValidated", content.Hash, content.Capture().Version }));
            return 0;
        }
        if (args[1] == "replay" && args.Length == 3)
        {
            var result = ExpeditionReplayRunner.Run(combat, content, JsonData.Read<ExpeditionReplay>(File.ReadAllText(args[2])));
            Console.WriteLine(JsonData.Write(result)); return result.Success ? 0 : 1;
        }
        if (args[1] == "benchmark")
        {
            var samples = new List<double>(); long bytes = 0;
            for (int run = -1; run < 5; run++)
            {
                var session = ExpeditionSession.Create(combat, content, (ulong)(42 + run));
                for (int i = 0; i < ExpeditionSmoke.MaximumCommands && !ExpeditionSmoke.Complete(session); i++)
                {
                    var input = ExpeditionSmoke.Next(session);
                    var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                    var result = session.Execute(input);
                    var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    if (!result.Success) throw new InvalidDataException(result.Reason);
                    if (run >= 0) { samples.Add(elapsed); bytes += GC.GetAllocatedBytesForCurrentThread() - allocated; }
                }
                if (!ExpeditionSmoke.Complete(session)) throw new InvalidDataException("Benchmark encounter did not terminate.");
            }
            samples.Sort();
            var report = new
            {
                kind = "ExpeditionBenchmarkPassed",
                scope = "Full world/combat command execution, snapshot synchronization, state/event hashing and bounded replay recording. Five full dungeon routes plus one warmup. Excludes input policy, client, GPU and disk writes.",
                samples = samples.Count,
                p50Ms = samples[samples.Count / 2],
                p95Ms = samples[(int)(samples.Count * .95)],
                p99Ms = samples[(int)(samples.Count * .99)],
                maxMs = samples[^1],
                bytesAllocatedPerOperation = bytes / samples.Count
            };
            AtomicFile.Write(args.Length > 2 ? args[2] : "artifacts/adventure/benchmark.json", JsonData.Write(report));
            Console.WriteLine(JsonData.Write(report));
            return report.p99Ms < 25 ? 0 : 1;
        }
        if (args[1] == "demo")
        {
            var output = args.Length > 2 ? args[2] : "artifacts/adventure";
            var session = ExpeditionSession.Create(combat, content);
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var messages = new List<string>();
            int operations = 0;
            while (!ExpeditionSmoke.Complete(session) && operations++ < ExpeditionSmoke.MaximumCommands)
            {
                var command = ExpeditionSmoke.Next(session);
                var result = session.Execute(command);
                if (!result.Success) throw new InvalidDataException($"Adventure input rejected: {command}: {result.Reason}");
                foreach (var e in result.CombatEvents) counts[e.Kind] = counts.GetValueOrDefault(e.Kind) + 1;
                messages.AddRange(result.WorldEvents);
            }
            if (!ExpeditionSmoke.Complete(session)) throw new InvalidDataException($"Slice demo timed out at {session.View.RoomId}/{session.View.EncounterId}, deaths={session.Capture().Adventure.Deaths}.");
            var replay = session.CaptureReplay();
            var verified = ExpeditionReplayRunner.Run(combat, content, replay);
            if (!verified.Success) throw new InvalidDataException(verified.Detail);
            var savePath = Path.Combine(output, $"character.{content.Hash[..8]}.{session.Combat.ContentHash[..8]}.json");
            ExpeditionSaveStore.Write(savePath, combat, content, session.Capture());
            var restored = ExpeditionSaveStore.Load(savePath, combat, content);
            if (restored.Session.StateHash != session.StateHash) throw new InvalidDataException("Expedition save/load mismatch.");
            AtomicFile.Write(Path.Combine(output, "session.awe"), JsonData.Write(replay));
            var report = new { kind = "AdventureDemoPassed", session.Tick, session.StateHash, operations, deaths = session.Capture().Adventure.Deaths, victories = session.View.Victories, counts, messages, verified };
            AtomicFile.Write(Path.Combine(output, "report.json"), JsonData.Write(report));
            Console.WriteLine(JsonData.Write(report)); return 0;
        }
        Console.Error.WriteLine("aw adventure validate|compile|demo [directory]|replay <file>|benchmark [file]"); return 2;
    }
}
