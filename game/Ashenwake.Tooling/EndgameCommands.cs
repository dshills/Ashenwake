using System.Diagnostics;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Tooling;

internal static class EndgameCommands
{
    internal sealed record Catalog(string CampaignCombat, string Combat, AdventureContent Adventure,
        ProgressionContent Progression, CampaignContent Campaign, EndgameContent Endgame)
    {
        public string Key => JsonData.Hash(new { combat = CombatContent.Parse(Combat).Identity, Adventure.Hash, progression = Progression.Hash, campaign = Campaign.Hash, endgame = Endgame.Hash })[..12];
        public EndgameRuntimeSession Create(string discipline = "Vanguard") => EndgameRuntimeSession.Create(Combat, Adventure, Progression, Campaign, Endgame, 42, discipline);
        public EndgameRuntimeSession Restore(EndgameRuntimeSnapshot state) => EndgameRuntimeSession.Restore(Combat, Adventure, Progression, Campaign, Endgame, state);
    }
    internal static Catalog Load()
    {
        string campaignCombat = CampaignCombatContent.Parse(File.ReadAllText("content/combat.json"), File.ReadAllText("content/campaign-combat.json")).CombatJson;
        var endgame = EndgameContent.Parse(File.ReadAllText("content/endgame.json"));
        string combat = EndgameCombatContent.Parse(campaignCombat, File.ReadAllText("content/endgame-combat.json"), endgame).CombatJson;
        return new(campaignCombat, combat, AdventureContent.Parse(File.ReadAllText("content/adventure.json")),
            ProgressionContent.Parse(File.ReadAllText("content/progression.json")), CampaignContent.Parse(File.ReadAllText("content/campaign.json")), endgame);
    }
    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 4 || args[1] is not ("validate" or "compile" or "demo" or "benchmark" or "exhaustive" or "builds" or "replay" or "migrate-phase4")) return 2;
        var c = Load();
        if (args[1] == "migrate-phase4")
        {
            if (args.Length != 4) return 2;
            if (File.Exists(args[3]) || Directory.Exists(args[3])) throw new IOException("Endgame migration requires a new destination; source files are never overwritten.");
            var imported = EndgameRuntimeMigration.ImportPhaseFour(File.ReadAllText(args[2]), c.CampaignCombat, c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame);
            EndgameRuntimeSaveStore.Write(args[3], c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, imported.Capture());
            Console.WriteLine(JsonData.Write(new { kind = "PhaseFourMigrationPassed", imported.StateHash, sourcePreserved = args[2], destination = args[3] })); return 0;
        }
        if (args.Length > 3) return 2;
        if (args[1] == "builds")
        {
            var matrix = EndgameCombatDiagnostics.Run(EndgameCombatContent.FromComposed(c.Combat));
            bool covered = matrix.Cases.All(row => row.RestoreVerified) && matrix.Cases
                .GroupBy(row => new { row.Discipline, row.Resonance, row.Kind, row.ContentId }).All(group => group.Any(row => row.Success));
            var matrixReport = new
            {
                kind = "EndgameBuildMatrix",
                routeCoveragePassed = covered,
                matrix,
                limitation = "Authored isolated loadouts. Failed reference cases remain visible; each discipline/resonance/route requires a successful reference or same-budget counter-build."
            };
            AtomicFile.Write(args.Length == 3 ? args[2] : "artifacts/endgame/build-matrix.json", JsonData.Write(matrixReport));
            Console.WriteLine(JsonData.Write(matrixReport)); return covered ? 0 : 1;
        }
        if (args[1] is "validate" or "compile")
        {
            if (args.Length != 2) return 2;
            c.Create();
            if (args[1] == "compile")
            {
                CampaignCommands.Run(["campaign", "compile"]);
                foreach (string name in new[] { "endgame", "endgame-combat" })
                    AtomicFile.Write("game/Ashenwake.Client/" + name + ".json", File.ReadAllText("content/" + name + ".json"));
                foreach (string name in new[] { "phase4-campaign-complete", "combat-phase4", "campaign-combat-phase4", "adventure-phase4", "progression-phase4", "campaign-phase4", "phase4-migration-manifest" })
                    AtomicFile.Write("game/Ashenwake.Client/" + name + ".json", File.ReadAllText("fixtures/" + name + ".json"));
            }
            Console.WriteLine(JsonData.Write(new { kind = "EndgameContentValidated", key = c.Key, endgameHash = c.Endgame.Hash, combatHash = CombatContent.Parse(c.Combat).Identity })); return 0;
        }
        if (args[1] == "replay")
        {
            if (args.Length != 3) return 2;
            var replay = EndgameRuntimeReplayRunner.Read(c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, File.ReadAllText(args[2]));
            var result = EndgameRuntimeReplayRunner.Run(c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, replay);
            Console.WriteLine(JsonData.Write(result)); return result.Success ? 0 : 1;
        }
        bool exhaustive = args[1] == "exhaustive";
        int targetTier = exhaustive ? 8 : 1;
        string output = Path.Combine(args.Length > 2 ? args[2] : "artifacts/endgame", c.Key, args[1]);
        var rows = new List<object>(); var samples = new List<double>(); long allocations = 0;
        string[] disciplines = exhaustive ? ["Vanguard"] : CombatSession.Disciplines.ToArray();
        for (int index = args[1] == "benchmark" ? -1 : 0; index < disciplines.Length; index++)
        {
            string discipline = disciplines[Math.Max(index, 0)]; var session = c.Create(discipline);
            int commands = 0, verifiedSegments = 0, restoredBoundaries = 0; string boundary = "";
            var events = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void VerifyReplay()
            {
                var result = EndgameRuntimeReplayRunner.Run(c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, session.CaptureReplay());
                if (!result.Success || result.FinalHash != session.StateHash) throw new InvalidDataException("Endgame replay diverged: " + discipline + "/" + commands);
                verifiedSegments++;
            }
            while (!EndgameRuntimeSmoke.Complete(session, targetTier, exhaustive) && commands < EndgameRuntimeSmoke.MaximumCommands)
            {
                var command = EndgameRuntimeSmoke.Next(session, targetTier, exhaustive);
                long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                var result = session.Execute(command);
                if (index >= 0) { samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); allocations += GC.GetAllocatedBytesForCurrentThread() - allocated; }
                if (!result.Success)
                {
                    AtomicFile.Write(Path.Combine(output, discipline.ToLowerInvariant(), "failed-checkpoint.json"), JsonData.Write(session.Capture()));
                    throw new InvalidDataException("Endgame smoke rejected: " + discipline + "/" + command.Action + "/" + result.Reason);
                }
                foreach (var e in result.CombatEvents) events[e.Kind] = events.GetValueOrDefault(e.Kind) + 1;
                commands++;
                if (commands % 600 == 0)
                {
                    VerifyReplay();
                    Console.WriteLine(JsonData.Write(new
                    {
                        kind = "EndgameProgress",
                        discipline,
                        commands,
                        encounter = session.Campaign.ActiveEncounterId,
                        run = session.View.Run,
                        tier = session.View.HighestClearedTier
                    }));
                    if (!session.InHub && session.View.Run is not null)
                        AtomicFile.Write(Path.Combine(output, discipline.ToLowerInvariant(), "latest-checkpoint.json"), JsonData.Write(session.Capture()));
                }
                var view = session.View;
                string current = session.Campaign.ActiveEncounterId + ":" + view.InHub + ":" + view.Run?.Id + ":" + view.Run?.EncounterIndex + ":" + view.Run?.AttemptsRemaining + ":" + view.Run?.EncounterCleared + ":" + view.Run?.Status;
                if (current != boundary)
                {
                    if (c.Restore(session.Capture()).StateHash != session.StateHash) throw new InvalidDataException("Endgame boundary restore changed state.");
                    restoredBoundaries++; boundary = current;
                }
            }
            if (!EndgameRuntimeSmoke.Complete(session, targetTier, exhaustive))
            {
                AtomicFile.Write(Path.Combine(output, discipline.ToLowerInvariant(), "failed-checkpoint.json"), JsonData.Write(session.Capture()));
                throw new InvalidDataException("Endgame route exceeded its bound: " + discipline);
            }
            VerifyReplay(); if (index < 0) continue;
            string directory = Path.Combine(output, discipline.ToLowerInvariant());
            string replayPath = Path.Combine(directory, "endgame.awendgame"), savePath = Path.Combine(directory, "endgame.save.json");
            AtomicFile.Write(replayPath, JsonData.Write(session.CaptureReplay()));
            EndgameRuntimeSaveStore.Write(savePath, c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, session.Capture());
            var loaded = EndgameRuntimeSaveStore.Load(savePath, c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame).Session;
            if (loaded.StateHash != session.StateHash) throw new InvalidDataException("Endgame save/profile changed state.");
            rows.Add(new { discipline, commands, session.Tick, session.StateHash, view = session.View, level = session.Production.ProgressionView.Level, verifiedSegments, restoredBoundaries, events, replayPath, savePath });
            Console.WriteLine(JsonData.Write(new { kind = "EndgameDisciplinePassed", discipline, commands, verifiedSegments, restoredBoundaries, session.StateHash }));
        }
        samples.Sort(); double Percentile(double p) => samples[(int)Math.Ceiling(samples.Count * p) - 1];
        var report = new
        {
            kind = "Endgame" + (args[1] == "benchmark" ? "Benchmark" : exhaustive ? "Exhaustive" : "Demo") + "Passed",
            key = c.Key,
            seed = 42,
            targetTier,
            exhaustiveHunts = exhaustive,
            rows,
            operations = samples.Count,
            p50Ms = Percentile(.5),
            p95Ms = Percentile(.95),
            p99Ms = Percentile(.99),
            maxMs = samples[^1],
            bytesAllocatedPerOperation = allocations / samples.Count,
            scope = "Actual fresh campaign-to-endgame inputs, full public command synchronization and replay hashing; driver, archive validation, disk and rendering excluded.",
            replayScope = "Overlapping verification every 600 commands plus completion covers the entire run despite a bounded retained tail.",
            limitations = "Automated engineering routes, not human balance, final art, hardware/controller certification or public release acceptance."
        };
        AtomicFile.Write(Path.Combine(output, "report.json"), JsonData.Write(report)); Console.WriteLine(JsonData.Write(report));
        if (args[1] == "benchmark" && Percentile(.99) >= 25) throw new InvalidDataException("Endgame command p99 exceeds 25 ms budget.");
        return 0;
    }
}
