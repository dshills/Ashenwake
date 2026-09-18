using System.Diagnostics;
using System.Runtime.InteropServices;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Tooling;

/// <summary>A continuing character, including disk archives and continuous replay verification.</summary>
internal static class ReleaseEndgameSoak
{
    public static int Run(int extraFractures, string output)
    {
        if (extraFractures is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(extraFractures));
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Soak requires a new output directory to preserve previous evidence.");
        var catalog = EndgameCommands.Load();
        var session = EndgameRuntimeMigration.ImportPhaseFour(File.ReadAllText("fixtures/phase4-campaign-complete.json"),
            catalog.CampaignCombat, catalog.Combat, catalog.Adventure, catalog.Progression, catalog.Campaign, catalog.Endgame);
        int commands = 0, restores = 0, replayChecks = 0;
        int requiredFractures = 10 + extraFractures;
        string directory = Path.Combine(Path.GetFullPath(output), "character");
        Directory.CreateDirectory(directory);
        string archive = Path.Combine(directory, "endgame.save.json");
        var samples = new List<double>(); long allocatedBytes = 0;
        long before = GC.GetTotalMemory(true);
        bool Finished() => EndgameRuntimeSmoke.Complete(session, 10, true) && session.View.CompletedFractures >= requiredFractures;
        while (!Finished() && commands < EndgameRuntimeSmoke.MaximumCommands)
        {
            var command = EndgameRuntimeSmoke.Next(session, 10, true);
            long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            var result = session.Execute(command);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
            if (!result.Success) throw new InvalidDataException("Persistent endgame soak command rejected: " + result.Reason);
            commands++;
            if (commands % 600 == 0) Checkpoint();
        }
        Checkpoint();
        if (!Finished()) throw new InvalidDataException("Persistent endgame soak exceeded its bounded route; last archive retained.");
        long retained = GC.GetTotalMemory(true) - before;
        samples.Sort(); double Percentile(double p) => samples[(int)Math.Ceiling(samples.Count * p) - 1];
        var state = session.Capture();
        var report = new
        {
            kind = "PersistentEndgameSoakPassed",
            source = "Maintained actual Phase4 completed character",
            commands,
            extraFractures,
            restores,
            replayChecks,
            finalHash = session.StateHash,
            state.Tick,
            session.View.CompletedFractures,
            session.View.CompletedGodHunts,
            items = state.Campaign.Production.Progression.Character.Items.Length,
            permanentReceipts = state.Campaign.Production.Progression.Character.OperationReceipts.Count,
            roomReceipts = state.Endgame.CompletedRooms.Count,
            p50Ms = Percentile(.5),
            p95Ms = Percentile(.95),
            p99Ms = Percentile(.99),
            maxMs = samples[^1],
            bytesAllocatedPerCommand = allocatedBytes / commands,
            retainedManagedBytesDelta = retained,
            runtime = RuntimeInformation.FrameworkDescription,
            platform = RuntimeInformation.OSDescription,
            scope = "One continuing character through ten tiers, five hunts, and additional tier-ten Fractures; actual commands, disk/profile archive cycles, and complete replay segments.",
            timing = "Full public command only. Driver, disk, verification, rendering excluded. Managed retention includes report samples and JIT; it is not by itself a leak measurement."
        };
        AtomicFile.Write(Path.Combine(output, "report.json"), JsonData.Write(report));
        Console.WriteLine(JsonData.Write(report)); return 0;
        void Checkpoint()
        {
            var replay = session.CaptureReplay();
            var result = EndgameRuntimeReplayRunner.Run(catalog.Combat, catalog.Adventure, catalog.Progression, catalog.Campaign, catalog.Endgame, replay);
            if (!result.Success || result.FinalHash != session.StateHash) throw new InvalidDataException("Persistent endgame replay diverged.");
            replayChecks++;
            string hash = session.StateHash;
            EndgameRuntimeSaveStore.Write(archive, catalog.Combat, catalog.Adventure, catalog.Progression, catalog.Campaign, catalog.Endgame, session.Capture());
            session = EndgameRuntimeSaveStore.Load(archive, catalog.Combat, catalog.Adventure, catalog.Progression, catalog.Campaign, catalog.Endgame).Session;
            if (session.StateHash != hash) throw new InvalidDataException("Persistent archive/profile changed state.");
            restores++;
            Console.WriteLine(JsonData.Write(new
            {
                kind = "PersistentEndgameCheckpoint",
                commands,
                restores,
                tier = session.View.HighestClearedTier,
                fractures = session.View.CompletedFractures,
                hunts = session.View.CompletedGodHunts
            }));
        }
    }
}
