using System.Diagnostics;
using System.Globalization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Tooling;

internal static class CampaignBalanceCommands
{
    public static int Run(string[] args)
    {
        if (args.Length is < 3 or > 6) return 2;
        bool mainPath = args.Contains("--main-path"), managed = args.Contains("--managed-build");
        var options = args.Skip(3).Where(a => a is not ("--main-path" or "--managed-build")).ToArray();
        int seeds = 1;
        if (options.Length > 1 || options.Length == 1 && !int.TryParse(options[0], NumberStyles.None, CultureInfo.InvariantCulture, out seeds) ||
            seeds is < 1 or > 3 || args.Count(a => a == "--main-path") > 1 || args.Count(a => a == "--managed-build") > 1)
            throw new ArgumentException("Use balance campaign <fresh-output-directory> [seed-count 1..3] [--main-path] [--managed-build].");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Campaign balance requires a fresh output directory; previous measurements are never overwritten.");
        string combat = CampaignCombatContent.Parse(File.ReadAllText("content/combat.json"), File.ReadAllText("content/campaign-combat.json")).CombatJson;
        var adventure = AdventureContent.Parse(File.ReadAllText("content/adventure.json"));
        var progression = ProgressionContent.Parse(File.ReadAllText("content/progression.json"));
        var campaign = CampaignContent.Parse(File.ReadAllText("content/campaign.json"));
        var policy = new CampaignBalancePolicy(mainPath, managed);
        var identities = new { combat = CombatContent.Parse(combat).Identity, adventure = adventure.Hash, progression = progression.Hash, campaign = campaign.Hash };
        Directory.CreateDirectory(output);
        var declaration = new
        {
            kind = "CampaignBalanceStarted",
            policy = policy.Name,
            policyVersion = CampaignBalancePolicy.Version,
            policyHash = policy.Hash,
            policy.Description,
            mainPath,
            seeds,
            seedStart = 42,
            identities,
            units = "Simulation ticks are 1/30 second. Combat ticks have living enemies at command entry; loot ticks use pickup commands with no living enemies; remaining ticks are travel. World/menu commands have no simulated duration. Wall seconds include measurement, save, and replay verification.",
            resourceMetrics = "ZeroResourceCombatTicks is a raw counter, not starvation: Arcanist Instability accumulates heat. HeatSaturatedCombatTicks counts full Arcanist heat. ResourceLimitedCombatTicks counts combat ticks with at least one available, cooldown-ready skill unaffordable using its real Spend/Heat rule; ResourceLimitedSkills identifies those skills. Other skills may remain usable.",
            limits = "Scripted runs do not measure human encounter difficulty, reading/menu time, rendering performance or fun. No damage-dealt/DPS metric is inferred from ambiguous source ownership. Managed gear scores ignore behavior powers and are reproducible heuristics, not optimal builds. No crafting/material spending policy is simulated."
        };
        AtomicFile.Write(Path.Combine(output, "policy.json"), JsonData.Write(declaration));
        Console.WriteLine(JsonData.Write(declaration));
        var runs = new List<object>(); int failed = 0;
        foreach (string discipline in CombatSession.Disciplines)
            for (int index = 0; index < seeds; index++)
            {
                ulong seed = (ulong)(42 + index);
                string directory = Path.Combine(output, discipline.ToLowerInvariant() + "-" + seed); Directory.CreateDirectory(directory);
                var session = CampaignRuntimeSession.Create(combat, adventure, progression, campaign, seed, discipline);
                var measured = new CampaignBalanceMeasurement(session);
                var segments = new List<object>(); var timer = Stopwatch.StartNew();
                int segmentCommands = 0, segmentStart = 0; string failure = ""; CampaignBalanceRoom? last = null;
                void VerifySegment()
                {
                    if (segmentCommands == 0) return;
                    var replay = session.CaptureReplay(); var verified = CampaignRuntimeReplayRunner.Run(combat, adventure, progression, campaign, replay);
                    if (!verified.Success || verified.FinalHash != session.StateHash) throw new InvalidDataException("Campaign balance replay diverged: " + discipline + "/" + measured.Commands);
                    string name = "segment-" + segments.Count.ToString("D4", CultureInfo.InvariantCulture) + ".awcampaign";
                    AtomicFile.Write(Path.Combine(directory, name), JsonData.Write(replay));
                    segments.Add(new { fromCommand = segmentStart, throughCommand = measured.Commands, replay = name, verified.FinalHash });
                    session = CampaignRuntimeSession.Restore(combat, adventure, progression, campaign, session.Capture());
                    if (session.StateHash != verified.FinalHash) throw new InvalidDataException("Campaign balance checkpoint changed on restore.");
                    segmentStart = (int)measured.Commands; segmentCommands = 0;
                }
                while (!policy.Complete(session) && measured.Commands < CampaignRuntimeSmoke.MaximumCommands)
                {
                    var before = CampaignBalanceObservation.Capture(session);
                    var command = policy.Next(session); var result = session.Execute(command);
                    last = measured.Observe(before, command, result, session); segmentCommands++;
                    var after = CampaignBalanceObservation.Capture(session);
                    bool changedRoom = before.Act != after.Act || before.RoomId != after.RoomId;
                    if (changedRoom)
                        Console.WriteLine(JsonData.Write(new
                        {
                            kind = "CampaignBalanceRoom",
                            discipline,
                            seed,
                            commands = measured.Commands,
                            last.Act,
                            last.RoomId,
                            last.Completed,
                            last.Attempts,
                            last.Deaths,
                            last.CombatTicks,
                            last.TravelTicks,
                            last.LootTicks,
                            last.EntryLevel,
                            last.ExitLevel,
                            last.MaterialsEarned,
                            last.MaterialsSpent,
                            nextRoom = after.RoomId
                        }));
                    if (changedRoom || segmentCommands >= 900) VerifySegment();
                    if (!result.Success) { failure = command.Action + ": " + result.Reason; break; }
                }
                bool complete = policy.Complete(session);
                if (!complete && failure == "") failure = "Command bound exceeded in " + session.ActiveEncounterId;
                VerifySegment();
                string savePath = Path.Combine(directory, "final.save.json");
                CampaignRuntimeSaveStore.Write(savePath, combat, adventure, progression, campaign, session.Capture());
                var restored = CampaignRuntimeSaveStore.Load(savePath, combat, adventure, progression, campaign).Session;
                if (restored.StateHash != session.StateHash) throw new InvalidDataException("Campaign balance final save changed on load.");
                var final = session.Capture();
                var report = new
                {
                    kind = "CampaignBalanceRun",
                    discipline,
                    seed,
                    complete,
                    failure,
                    policy = policy.Name,
                    policyHash = policy.Hash,
                    measured.Commands,
                    simulationTicks = final.Tick,
                    wallSeconds = timer.Elapsed.TotalSeconds,
                    session.StateHash,
                    encountersCompleted = final.Campaign.CompletedEncounters.Count,
                    explorationCompleted = final.Campaign.CompletedExploration.Count,
                    deaths = final.Campaign.Deaths,
                    ending = session.View.Ending,
                    profileUnlocks = final.Production.Progression.Profile.Unlocks,
                    finalProgression = session.Production.ProgressionView,
                    finalEquipment = final.Production.Progression.Character.Equipment,
                    finalPassives = final.Production.Progression.Character.Passives,
                    rooms = measured.Rooms,
                    acts = measured.ActSummaries(),
                    measured.GearHistory,
                    measured.AbilityUnlocks,
                    measured.BuildChanges,
                    segments,
                    verification = "Every public command belongs to a saved, replay-verified segment of at most 900 commands. Each segment boundary restores exactly. Final save/profile is loaded and hash-compared."
                };
                AtomicFile.Write(Path.Combine(directory, "report.json"), JsonData.Write(report));
                runs.Add(new
                {
                    discipline,
                    seed,
                    complete,
                    failure,
                    report = Path.GetRelativePath(output, Path.Combine(directory, "report.json")),
                    measured.Commands,
                    final.Tick,
                    deaths = final.Campaign.Deaths,
                    wallSeconds = timer.Elapsed.TotalSeconds,
                    session.StateHash
                });
                if (!complete) failed++;
                Console.WriteLine(JsonData.Write(new
                {
                    kind = "CampaignBalanceDiscipline",
                    discipline,
                    seed,
                    complete,
                    failure,
                    measured.Commands,
                    deaths = final.Campaign.Deaths,
                    rooms = measured.Rooms.Count(r => r.Completed),
                    level = session.Production.ProgressionView.Level,
                    wallSeconds = timer.Elapsed.TotalSeconds,
                    verifiedSegments = segments.Count
                }));
            }
        AtomicFile.Write(Path.Combine(output, "summary.json"), JsonData.Write(new
        {
            kind = "CampaignBalanceMeasured",
            identities,
            policy = policy.Name,
            policyHash = policy.Hash,
            mainPath,
            seeds,
            runs,
            failures = failed
        }));
        return failed == 0 ? 0 : 1;
    }
}
