using System.Diagnostics;
using System.Runtime.InteropServices;
using Ashenwake.Core.Authoring;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Tooling;

internal static class SandboxCommands
{
    public static int Run(string[] args)
    {
        var content = File.ReadAllText("content/combat.json");
        if (args[1] is "validate" or "compile")
        {
            var session = CombatSession.Create(content);
            if (args[1] == "compile")
            {
                string names = File.ReadAllText("content/text.en.json");
                string policy = File.ReadAllText("content/progression.json");
                var text = TextCatalog.Parse(names);
                text.RequireKeys(CombatContent.Parse(content).Items.SelectMany(item => new[] { "equipment." + item.Id[5..], "lore." + item.Id[5..] }));
                text.RequireKeys(["source.pyrebound_treads", "source.oathkeeper_reprisal", "source.widows_last_echo"]);
                text.RequireKeys(ProgressionContent.Parse(policy).Capture().Properties.SelectMany(p => new[] { "power." + p.Id + ".name", "power." + p.Id + ".description" }));
                AtomicFile.Write("game/Ashenwake.Client/combat.json", content);
                AtomicFile.Write("game/Ashenwake.Client/text.en.json", names);
                AtomicFile.Write("game/Ashenwake.Client/progression.json", policy);
            }
            Console.WriteLine(JsonData.Write(new { kind = "SandboxContentValidated", session.View.ContentVersion, session.Capture().ContentHash }));
            return 0;
        }
        if (args[1] == "replay" && args.Length == 3)
        {
            var result = CombatReplayRunner.Run(content, JsonData.Read<CombatReplay>(File.ReadAllText(args[2])));
            Console.WriteLine(JsonData.Write(result));
            return result.Success ? 0 : 1;
        }
        if (args[1] == "demo")
        {
            var output = args.Length > 2 ? args[2] : "artifacts/combat";
            var session = CombatSession.Create(content, 42, "standard");
            var recorder = new CombatRecorder(session);
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (var tick = 0; tick < 900; tick++)
                foreach (var item in recorder.Step(session, Commands(session.View)))
                    counts[item.Kind] = counts.GetValueOrDefault(item.Kind) + 1;
            AtomicFile.Write(Path.Combine(output, "session.awc"), JsonData.Write(recorder.Capture()));
            var savePath = Path.Combine(output, "save." + session.ContentHash[..12] + ".json");
            CombatSaveStore.Write(savePath, content, session.Capture());
            var restored = CombatSession.Restore(content, CombatSaveStore.Load(savePath, content).State);
            var replay = CombatReplayRunner.Run(content, recorder.Capture());
            if (!replay.Success || restored.StateHash != session.StateHash) throw new InvalidDataException("Combat save/replay mismatch.");
            var report = new { kind = "CombatDemoPassed", session.Tick, session.StateHash, counts, replay };
            AtomicFile.Write(Path.Combine(output, "report.json"), JsonData.Write(report));
            Console.WriteLine(JsonData.Write(report));
            return 0;
        }
        if (args[1] == "builds")
        {
            var reports = new List<object>();
            foreach (var mutation in new[] { "mutation.avalanche", "mutation.no_ground_given" })
            {
                var session = CombatSession.Create(content, 42);
                var commands = new List<CombatCommand> { new(CombatCommandKind.SetMutation, ContentId: mutation) };
                // Prove the defensive counter build does not require the fragment chain.
                if (mutation == "mutation.no_ground_given")
                    commands.AddRange(session.View.Fragments.Select(f => new CombatCommand(CombatCommandKind.UnequipFragment, ContentId: f.Id)));
                session.Step(commands);
                var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                while (session.Tick < 3600 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0) && session.View.Actors.Single(a => a.Id == 1).Health > 0)
                    foreach (var e in session.Step(Commands(session.View))) counts[e.Kind] = counts.GetValueOrDefault(e.Kind) + 1;
                var view = session.View;
                bool won = !view.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0) && view.Actors.Single(a => a.Id == 1).Health > 0;
                reports.Add(new { mutation, won, ticks = session.Tick, health = view.Actors.Single(a => a.Id == 1).Health, resonance = view.Resonance, counts });
                if (!won) throw new InvalidDataException($"Reference loadout failed: {mutation}.");
            }
            var report = new { kind = "ReferenceBuildsPassed", seed = 42, preset = "standard", reports };
            AtomicFile.Write(args.Length > 2 ? args[2] : "artifacts/combat/builds.json", JsonData.Write(report));
            Console.WriteLine(JsonData.Write(report));
            return 0;
        }
        if (args[1] == "benchmark")
        {
            var reports = new List<object>();
            foreach (var preset in new[] { "standard", "dense", "projectiles", "summons", "chain" })
            {
                var samples = new List<double>();
                long bytes = 0;
                var maxActors = 0;
                var maxProjectiles = 0;
                var maxAreas = 0;
                var maxEffects = 0;
                for (var run = -1; run < 5; run++)
                {
                    var session = CombatSession.Create(content, (ulong)(run + 43), preset);
                    for (var tick = 0; tick < 900; tick++)
                    {
                        var commands = Commands(session.View);
                        var before = GC.GetAllocatedBytesForCurrentThread();
                        var start = Stopwatch.GetTimestamp();
                        session.Step(commands);
                        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        if (run < 0) continue;
                        bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                        samples.Add(elapsed);
                        var view = session.View;
                        maxActors = Math.Max(maxActors, view.Actors.Count);
                        maxProjectiles = Math.Max(maxProjectiles, view.Projectiles.Count);
                        maxAreas = Math.Max(maxAreas, view.Areas.Count);
                        maxEffects = Math.Max(maxEffects, view.PeakEffects);
                    }
                }
                samples.Sort();
                reports.Add(new { preset, samples = samples.Count, p50TickMs = samples[samples.Count / 2], p95TickMs = samples[(int)(samples.Count * .95)], p99TickMs = samples[(int)(samples.Count * .99)], maxTickMs = samples[^1], bytesAllocatedPerTick = bytes / samples.Count, maxActors, maxProjectiles, maxAreas, maxEffects });
                if (samples[(int)(samples.Count * .99)] >= 25) throw new InvalidDataException($"{preset} exceeds the 25 ms simulation p99 budget.");
            }
            var report = new { kind = "CombatBenchmarkPassed", runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), logicalProcessors = Environment.ProcessorCount, scope = "Simulation Step only, excluding input policy, view snapshots, replay hashing and GPU. 5x900 ticks per preset, one warmup run.", reports };
            AtomicFile.Write(args.Length > 2 ? args[2] : "artifacts/combat/benchmark.json", JsonData.Write(report));
            Console.WriteLine(JsonData.Write(report));
            return 0;
        }
        Console.Error.WriteLine("aw sandbox validate|compile|demo [directory]|replay <file>|builds [file]|benchmark [file]");
        return 2;
    }

    private static CombatCommand[] Commands(CombatView view)
    {
        var player = view.Actors.Single(a => a.Id == 1);
        if (player.Health <= 0) return [];
        var target = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0)
            .OrderBy(a => Distance(player, a)).ThenBy(a => a.Id).FirstOrDefault();
        if (target is null)
        {
            var loot = view.Loot.OrderBy(l => (long)(l.Position.X - player.Position.X) * (l.Position.X - player.Position.X) + (long)(l.Position.Z - player.Position.Z) * (l.Position.Z - player.Position.Z)).FirstOrDefault();
            return loot is null ? [new(CombatCommandKind.Stop)] :
                [new(CombatCommandKind.Move, X: Math.Sign(loot.Position.X - player.Position.X), Z: Math.Sign(loot.Position.Z - player.Position.Z)), new(CombatCommandKind.Pickup, ItemId: loot.Id)];
        }
        var commands = new List<CombatCommand>();
        if (player.Health < player.MaxHealth / 2) commands.Add(new(CombatCommandKind.Potion));
        var dx = target.Position.X - player.Position.X;
        var dz = target.Position.Z - player.Position.Z;
        commands.Add(new(CombatCommandKind.Move, X: Math.Sign(dx), Z: Math.Sign(dz)));
        if (view.Tick % 16 == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: target.Id));
        if (view.Tick % 61 == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.shield_breaker", TargetId: target.Id));
        if (view.Tick % 91 == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.seismic_wave", TargetId: target.Id));
        if (view.Tick % 131 == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.charge", TargetId: target.Id));
        if (view.Tick % 157 == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.iron_guard", TargetId: target.Id));
        if (view.Tick % 223 == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.cataclysm", TargetId: target.Id));
        if (view.Tick % 73 == 0 && target.TelegraphTicks > 0) commands.Add(new(CombatCommandKind.Dodge, X: -Math.Sign(dz), Z: Math.Sign(dx)));
        return commands.ToArray();
    }
    private static long Distance(CombatActorView a, CombatActorView b) => (long)(a.Position.X - b.Position.X) * (a.Position.X - b.Position.X) + (long)(a.Position.Z - b.Position.Z) * (a.Position.Z - b.Position.Z);
}
