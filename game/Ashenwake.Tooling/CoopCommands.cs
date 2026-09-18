using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Tooling;

internal static class CoopCommands
{
    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 3) return 2;
        string content = File.ReadAllText("content/combat.json");
        if (args[1] == "validate" && args.Length == 2)
        {
            foreach (string id in CoopCombatSession.EncounterIds) CoopCombatSession.Create(content, encounterId: id);
            Console.WriteLine(JsonData.Write(new { kind = "CoopContentValidated", protocolVersion = "coop-net.1", rulesVersion = CoopCombatSession.RulesVersion, contentHash = CombatContent.Parse(content).Identity })); return 0;
        }
        if (args[1] == "demo" && args.Length == 2)
        {
            var report = CoopDiagnostics.Run(content, 42, 43, 44);
            AtomicFile.Write("artifacts/coop/diagnostics.json", JsonData.Write(report));
            Console.WriteLine(JsonData.Write(report));
            return report.Cases.All(c => c.Completed && c.ReplayVerified && c.RestoreVerified && c.Rewards == 10) ? 0 : 1;
        }
        if (args[1] == "replay" && args.Length == 3)
        {
            if (new FileInfo(args[2]).Length > 64 * 1024 * 1024) throw new InvalidDataException("Co-op replay exceeds the bounded file size.");
            var result = CoopReplayRunner.Run(content, JsonData.Read<CoopReplay>(File.ReadAllText(args[2])));
            Console.WriteLine(JsonData.Write(result)); return result.Success ? 0 : 1;
        }
        Console.Error.WriteLine("coop validate | coop demo | coop replay <server-segment.json>"); return 2;
    }
}
