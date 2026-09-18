using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Tooling;

internal static class ExperimentCommands
{
    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 4) return 2;
        var c = EndgameCommands.Load();
        string experimentJson = File.ReadAllText("content/experiments.json");
        var content = ExperimentContent.Parse(experimentJson);
        string key = JsonData.Hash(Directory.EnumerateFiles("content", "*.json").Order(StringComparer.Ordinal)
            .Select(path => new { path, hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) }).ToArray())[..12];
        if (args[1] is "validate" or "compile" && args.Length == 2)
        {
            ExperimentRuntimeSession.Create(c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, content);
            if (args[1] == "compile" && EndgameCommands.Run(["endgame", "compile"]) != 0) return 1;
            Console.WriteLine(JsonData.Write(new { kind = "ExperimentContentValidated", key, content.Hash, content.Admission, combatHash = CombatContent.Parse(c.Combat).Identity, definition = content.Capture() })); return 0;
        }
        if (args[1] == "replay" && args.Length == 3)
        {
            var replay = ExperimentReplayRunner.Read(c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, content, File.ReadAllText(args[2]));
            var result = ExperimentReplayRunner.Run(c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, content, replay);
            Console.WriteLine(JsonData.Write(result)); return result.Success ? 0 : 1;
        }
        if (args[1] == "import-endgame" && args.Length == 4)
        {
            if (File.Exists(args[3]) || Directory.Exists(args[3])) throw new IOException("Experiment import requires a new destination.");
            var session = ExperimentSaveStore.ImportEndgame(c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, content, File.ReadAllText(args[2]));
            ExperimentSaveStore.Write(args[3], c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, content, session.Capture());
            Console.WriteLine(JsonData.Write(new { kind = "ExperimentOptInImported", session.StateHash, sourcePreserved = args[2], destination = args[3] })); return 0;
        }
        if (args[1] == "demo" && args.Length is 2 or 3)
        {
            string directory = args.Length == 3 ? args[2] : "artifacts/experiment/" + key;
            var original = EndgameRuntimeMigration.ImportPhaseFour(File.ReadAllText("fixtures/phase4-campaign-complete.json"), c.CampaignCombat, c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame);
            var session = ExperimentRuntimeSession.FromEndgame(c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, content, original.Capture());
            int commands = 0, replayChecks = 0, restored = 0; var events = new SortedDictionary<string, int>(StringComparer.Ordinal);
            while (!ExperimentRuntimeSmoke.Complete(session) && commands < ExperimentRuntimeSmoke.MaximumCommands)
            {
                var result = session.Execute(ExperimentRuntimeSmoke.Next(session));
                if (!result.Success) throw new InvalidDataException("Experiment ordinary command rejected: " + result.Reason);
                foreach (var item in result.CombatEvents) events[item.Kind] = events.GetValueOrDefault(item.Kind) + 1;
                foreach (string item in result.WorldEvents) events[item] = events.GetValueOrDefault(item) + 1;
                commands++;
                if (commands % 600 == 0) Verify();
            }
            Verify();
            if (!ExperimentRuntimeSmoke.Complete(session)) throw new InvalidDataException("Experiment route exceeded its bounded command budget.");
            AtomicFile.Write(Path.Combine(directory, "echoes.awexperiment"), JsonData.Write(session.CaptureReplay()));
            string savePath = Path.Combine(directory, "echoes.save.json");
            ExperimentSaveStore.Write(savePath, c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, content, session.Capture());
            if (ExperimentSaveStore.Load(savePath, c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, content).Session.StateHash != session.StateHash)
                throw new InvalidDataException("Experiment archive/profile changed state.");
            var report = new
            {
                kind = "ExperimentDemoPassed",
                key,
                commands,
                replayChecks,
                restored,
                stateHash = session.StateHash,
                cosmetics = session.View.Cosmetics,
                entries = session.View.Entries,
                events,
                scope = "Maintained completed campaign, actual Sigil consumption and Fracture, near-corpse binding, temporary Echo cast, visible Storm counterplay, actual victory and once-only cosmetic receipt; ordinary permanent rewards stay in Endgame."
            };
            AtomicFile.Write(Path.Combine(directory, "report.json"), JsonData.Write(report)); Console.WriteLine(JsonData.Write(report)); return 0;
            void Verify()
            {
                var replay = ExperimentReplayRunner.Run(c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, content, session.CaptureReplay());
                if (!replay.Success || replay.FinalHash != session.StateHash) throw new InvalidDataException("Experiment replay diverged.");
                replayChecks++;
                if (ExperimentRuntimeSession.Restore(c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, content, session.Capture()).StateHash != session.StateHash)
                    throw new InvalidDataException("Experiment snapshot changed on restore.");
                restored++;
            }
        }
        Console.Error.WriteLine("experiment validate|compile|demo [output] | experiment replay <path> | experiment import-endgame <source> <new-destination>"); return 2;
    }
}
