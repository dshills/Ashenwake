using System.Diagnostics;
using System.Runtime.InteropServices;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;

return Run(args);

static int Run(string[] args)
{
    try
    {
        if (args.Length < 2) return Usage();
        if (args[0] == "sandbox") return Ashenwake.Tooling.SandboxCommands.Run(args);
        if (args[0] == "adventure") return Ashenwake.Tooling.AdventureCommands.Run(args);
        if (args[0] == "authoring") return Ashenwake.Tooling.AuthoringCommands.Run(args);
        if (args[0] == "balance") return Ashenwake.Tooling.BalanceCommands.Run(args);
        if (args[0] == "production") return Ashenwake.Tooling.ProductionCommands.Run(args);
        if (args[0] == "campaign") return Ashenwake.Tooling.CampaignCommands.Run(args);
        if (args[0] == "endgame") return Ashenwake.Tooling.EndgameCommands.Run(args);
        if (args[0] == "release") return Ashenwake.Tooling.ReleaseCommands.Run(args);
        if (args[0] == "item") return Ashenwake.Tooling.InspectionCommands.Item(args);
        if (args[0] == "content" && args[1] == "refs") return Ashenwake.Tooling.InspectionCommands.References(args);
        if (args[0] == "content" && args[1] is "validate" or "compile")
        {
            var source = args.Length > 2 ? args[2] : "content/phase0.json";
            var bundle = ContentCompiler.Compile(File.ReadAllText(source));
            if (args[1] == "compile")
            {
                var destination = args.Length > 3 ? args[3] : "game/Ashenwake.Client/content.bundle.json";
                Write(destination, JsonData.Write(bundle));
            }
            Console.WriteLine(JsonData.Write(new { kind = "ContentValidated", bundle.Content.ContentVersion, bundle.Hash }));
            return 0;
        }
        var content = ContentCompiler.Compile(File.ReadAllText("content/phase0.json"));
        if (args[0] == "replay" && args[1] == "run" && args.Length == 3)
        {
            var result = ReplayRunner.Run(content, JsonData.Read<ReplayDocument>(File.ReadAllText(args[2])));
            Console.WriteLine(JsonData.Write(result));
            return result.Success ? 0 : 1;
        }
        if (args[0] == "demo" && args[1] == "run")
        {
            var output = args.Length > 2 ? args[2] : "artifacts/phase0";
            var world = new SimulationWorld(content);
            var recorder = new ReplayRecorder(world);
            var events = new List<SimulationEvent>();
            for (int tick = 0; tick < 90; tick++)
            {
                var command = tick switch
                {
                    0 => new GameCommand(tick, 1, 0, CommandKind.Attack, TargetId: 2),
                    40 => new GameCommand(tick, 1, 0, CommandKind.Move, X: 1),
                    43 => new GameCommand(tick, 1, 0, CommandKind.Move),
                    44 => new GameCommand(tick, 1, 0, CommandKind.Pickup),
                    _ => null
                };
                events.AddRange(recorder.Step(world, command is null ? [] : [command]));
            }
            Write(Path.Combine(output, "demo.awr"), JsonData.Write(recorder.Capture()));
            SaveStore.Write(Path.Combine(output, "demo.save.json"), content, world.Capture());
            var loaded = SaveStore.Load(Path.Combine(output, "demo.save.json"), content);
            var result = ReplayRunner.Run(content, recorder.Capture());
            if (!result.Success || JsonData.Hash(loaded.State) != world.StateHash || world.Capture().Inventory.Count != 2)
                throw new InvalidDataException("Demo replay/save/loot verification failed.");
            Write(Path.Combine(output, "events.jsonl"), string.Join('\n', events.Select(e => JsonData.Write(new
            {
                sessionId = "phase0-demo",
                encounterId = "room.phase0",
                buildId = BuildIdentity.RulesVersion,
                contentVersion = content.Content.ContentVersion,
                contentHash = content.Hash,
                simulationEvent = e
            }))) + "\n");
            Console.WriteLine(JsonData.Write(new { kind = "DemoPassed", world.Tick, world.StateHash, result, output }));
            return 0;
        }
        if (args[0] == "benchmark" && args[1] == "run")
        {
            var output = args.Length > 2 ? args[2] : "artifacts/phase0/benchmark.json";
            var samples = new List<double>();
            long allocation = 0;
            // Warm JIT separately. Measure simulation only; replay hashing has its own client cost.
            for (int run = -10; run < 100; run++)
            {
                var world = new SimulationWorld(content, (ulong)(run + 10));
                for (int tick = 0; tick < 90; tick++)
                {
                    GameCommand[] commands = tick == 0 ? [new(0, 1, 0, CommandKind.Attack, TargetId: 2)] : [];
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    world.Step(commands);
                    double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    if (run >= 0) { samples.Add(ms); allocation += GC.GetAllocatedBytesForCurrentThread() - before; }
                }
            }
            samples.Sort();
            var report = new
            {
                buildId = BuildIdentity.RulesVersion,
                contentHash = content.Hash,
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                logicalProcessors = Environment.ProcessorCount,
                scene = "phase0: 2 actors, 1 status, 1 loot drop; 100 encounters of 90 ticks, 10 warmup encounters",
                samples = samples.Count,
                p50TickMs = samples[samples.Count / 2],
                p95TickMs = samples[(int)(samples.Count * .95)],
                p99TickMs = samples[(int)(samples.Count * .99)],
                maxTickMs = samples[^1],
                bytesAllocatedPerTick = allocation / samples.Count,
                limit = "Architecture baseline only; not a campaign population or GPU benchmark."
            };
            Write(output, JsonData.Write(report)); Console.WriteLine(JsonData.Write(report)); return 0;
        }
        return Usage();
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException or OverflowException)
    {
        Console.Error.WriteLine(JsonData.Write(new { kind = "Error", message = ex.Message }));
        return 1;
    }
}

static void Write(string path, string contents)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    File.WriteAllText(path, contents);
}
static int Usage()
{
    Console.Error.WriteLine("aw content validate|compile|refs | item show <id> | demo run | replay run <file> | benchmark run | sandbox validate|compile|demo|builds|replay|benchmark | adventure validate|compile|demo|replay|benchmark | production validate|compile|demo|replay|benchmark|migrate-phase2 | campaign validate|compile|demo|benchmark|replay|migrate-phase3 | release manifest|verify|audit|fixtures|soak|endgame-soak | endgame validate|compile|demo|benchmark|builds|exhaustive|replay|migrate-phase4 | authoring validate|templates|pseudo | balance run|loot");
    return 2;
}
