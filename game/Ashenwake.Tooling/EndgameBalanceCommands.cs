using System.Diagnostics;
using System.Globalization;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Tooling;

internal static class EndgameBalanceCommands
{
    public static int Run(string[] args)
    {
        const string usage = "Use balance endgame <fresh-output-directory> [seed-count 1..3] [--discipline Vanguard|Veilwalker|Arcanist|Gravecaller|Warden].";
        int seeds = 1; string? selected = null; var options = args.Skip(3).ToList();
        int selector = options.IndexOf("--discipline");
        if (selector >= 0)
        {
            if (selector + 1 >= options.Count) throw new ArgumentException(usage);
            selected = options[selector + 1]; options.RemoveRange(selector, 2);
        }
        if (args.Length is < 3 or > 6 || options.Count > 1 || options.Count == 1 &&
            !int.TryParse(options[0], NumberStyles.None, CultureInfo.InvariantCulture, out seeds) || seeds is < 1 or > 3 ||
            selected is not null && !CombatSession.Disciplines.Contains(selected)) throw new ArgumentException(usage);
        string[] disciplines = selected is null ? CombatSession.Disciplines.ToArray() : [selected];
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Endgame balance requires a fresh output directory; prior measurements are never overwritten.");
        var c = EndgameCommands.Load(); var policy = new EndgameBalancePolicy();
        var fresh = c.Create();
        var identities = new
        {
            combat = CombatContent.Parse(c.Combat).Identity,
            campaignCombat = CombatContent.Parse(c.CampaignCombat).Identity,
            adventure = c.Adventure.Hash,
            sourceProgression = c.Progression.Hash,
            effectiveProgression = fresh.Production.Content.Hash,
            campaign = c.Campaign.Hash,
            endgame = c.Endgame.Hash
        };
        Directory.CreateDirectory(output);
        var declaration = new
        {
            kind = "EndgameBalanceStarted",
            policyVersion = EndgameBalancePolicy.Version,
            policyHash = policy.Hash,
            campaignPolicyHash = policy.Campaign.Hash,
            policy.Description,
            identities,
            seeds,
            seedStart = 42,
            disciplines,
            targetTier = EndgameBalancePolicy.TargetTier,
            units = "Combat, loot and travel ticks count public simulation Tick inputs only, at 30 ticks/second. World/menu commands are counted separately; the outer runtime Tick also includes those commands and is not elapsed combat time. Damage is incoming player DamageApplied only. Wall time includes save/replay measurement.",
            limitations = "Scripted public-input evidence, not human difficulty, optimal builds, graphics performance or fun. Raw-stat gear scoring ignores behavior powers. No crafting, mutation selection or material-spending strategy. A failed expedition is recorded immediately; ordinary finite encounter retries remain visible."
        };
        AtomicFile.Write(Path.Combine(output, "policy.json"), JsonData.Write(declaration));
        Console.WriteLine(JsonData.Write(declaration));
        var runs = new List<object>(); int failures = 0;
        foreach (string discipline in disciplines)
            for (int seedIndex = 0; seedIndex < seeds; seedIndex++)
            {
                ulong seed = (ulong)(42 + seedIndex);
                string directory = Path.Combine(output, discipline.ToLowerInvariant() + "-" + seed); Directory.CreateDirectory(directory);
                var session = EndgameRuntimeSession.Create(c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, seed, discipline);
                var campaign = new CampaignBalanceMeasurement(session.Campaign);
                EndgameBalanceMeasurement? endgame = null;
                var segments = new List<object>(); var entries = new List<object>(); var timer = Stopwatch.StartNew();
                int commands = 0, segmentStart = 0; string failure = ""; object? handoff = null;
                void VerifySegment()
                {
                    if (commands == segmentStart) return;
                    var replay = session.CaptureReplay();
                    if (replay.Frames.Length != commands - segmentStart) throw new InvalidDataException("Endgame measurement lost retained replay commands.");
                    var result = EndgameRuntimeReplayRunner.Run(c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, replay);
                    if (!result.Success || result.FinalHash != session.StateHash) throw new InvalidDataException("Endgame measurement replay diverged: " + discipline + "/" + commands);
                    string name = "segment-" + segments.Count.ToString("D4", CultureInfo.InvariantCulture) + ".awendgame";
                    AtomicFile.Write(Path.Combine(directory, name), JsonData.Write(replay));
                    segments.Add(new { fromCommand = segmentStart, throughCommand = commands, replay = name, result.FinalHash });
                    session = c.Restore(session.Capture());
                    if (session.StateHash != result.FinalHash) throw new InvalidDataException("Endgame measurement checkpoint changed on restore.");
                    segmentStart = commands;
                }
                void SaveAndLoad(string name)
                {
                    string path = Path.Combine(directory, name);
                    EndgameRuntimeSaveStore.Write(path, c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame, session.Capture());
                    var restored = EndgameRuntimeSaveStore.Load(path, c.Combat, c.Adventure, c.Progression, c.Campaign, c.Endgame).Session;
                    if (restored.StateHash != session.StateHash) throw new InvalidDataException("Endgame measurement save changed on load.");
                }
                while (!policy.Complete(session) && commands < EndgameRuntimeSmoke.MaximumCommands)
                {
                    if (endgame is null && policy.Campaign.Complete(session.Campaign))
                    {
                        VerifySegment(); SaveAndLoad("campaign-earned.save.json");
                        var state = session.Capture();
                        handoff = new
                        {
                            commands,
                            session.StateHash,
                            level = session.Production.ProgressionView.Level,
                            experience = session.Production.ProgressionView.Experience,
                            materials = session.Production.ProgressionView.Materials,
                            state.Campaign.Production.Progression.Character,
                            campaignEncounters = state.Campaign.Campaign.CompletedEncounters.Count,
                            optionalEncounters = state.Campaign.Campaign.CompletedExploration.Count,
                            deaths = state.Campaign.Campaign.Deaths
                        };
                        endgame = new(session);
                    }
                    var campaignBefore = endgame is null ? CampaignBalanceObservation.Capture(session.Campaign) : null;
                    var before = endgame is not null ? EndgameBalanceObservation.Capture(session) : null;
                    var command = policy.Next(session); var result = session.Execute(command); commands++;
                    bool changed;
                    if (endgame is null)
                    {
                        if (command.Action != EndgameRuntimeAction.Campaign || command.Campaign is null) throw new InvalidDataException("Campaign policy produced a non-campaign input before handoff.");
                        campaign.Observe(campaignBefore!, command.Campaign, new(result.Success, result.Reason, result.CombatEvents, result.WorldEvents), session.Campaign);
                        var after = CampaignBalanceObservation.Capture(session.Campaign);
                        changed = campaignBefore!.Act != after.Act || campaignBefore.RoomId != after.RoomId;
                    }
                    else
                    {
                        endgame.Observe(before!, command, result, session);
                        var after = EndgameBalanceObservation.Capture(session);
                        changed = before!.Key != after.Key || before.Deaths != after.Deaths;
                        if (result.Success && command.Action == EndgameRuntimeAction.StartFracture)
                        {
                            var state = session.Capture();
                            entries.Add(new { commands, session.StateHash, session.Production.ProgressionView, state.Campaign.Production.Progression.Character, state.Manifest });
                            Console.WriteLine(JsonData.Write(new { kind = "EndgameBalanceTierStarted", discipline, seed, commands, after.Tier, after.Level }));
                        }
                    }
                    if (changed || commands - segmentStart >= 900) VerifySegment();
                    if (!result.Success) { failure = command.Action + ": " + result.Reason; break; }
                    if (result.WorldEvents.Contains("ExpeditionFailed")) { failure = "Expedition exhausted its earned attempts at tier " + session.RunView!.Tier; break; }
                }
                bool complete = policy.Complete(session);
                if (!complete && failure == "") failure = "Public command bound exceeded.";
                VerifySegment(); SaveAndLoad("final.save.json");
                var final = session.Capture();
                bool tiersVerified = Enumerable.Range(1, EndgameBalancePolicy.TargetTier).All(tier => final.Endgame.Rewards.Values.Any(r => r.Kind == "Fracture" && r.Tier == tier));
                if (complete && !tiersVerified) throw new InvalidDataException("Tier completion lacks an authoritative reward receipt.");
                var report = new
                {
                    kind = "EndgameBalanceRun",
                    discipline,
                    seed,
                    complete,
                    failure,
                    commands,
                    policyHash = policy.Hash,
                    identities,
                    wallSeconds = timer.Elapsed.TotalSeconds,
                    session.StateHash,
                    handoff,
                    campaign = new { campaign.Commands, campaign.Rooms, acts = campaign.ActSummaries(), campaign.GearHistory, campaign.BuildChanges },
                    endgame = new { commands = endgame?.Commands ?? 0, rooms = endgame?.Rooms, gearGrants = endgame?.GearGrants, buildChanges = endgame?.BuildChanges, entries, rewards = final.Endgame.Rewards.Values, completedRooms = final.Endgame.CompletedRooms.Values },
                    finalProgression = session.Production.ProgressionView,
                    finalCharacter = final.Campaign.Production.Progression.Character,
                    campaignDeaths = final.Campaign.Campaign.Deaths,
                    endgameDeaths = endgame?.Rooms.Sum(r => r.Deaths) ?? 0,
                    segments,
                    tiersVerified,
                    verification = "Every public command is retained in a persisted, nonoverlapping replay segment of at most 900 commands. Replay end hash and a restore match at every segment. Campaign handoff and final saves/profile round-trip exactly. Tier completion requires immutable reward receipts."
                };
                AtomicFile.Write(Path.Combine(directory, "report.json"), JsonData.Write(report));
                if (!complete) failures++;
                var summary = new { discipline, seed, complete, failure, commands, report.campaignDeaths, report.endgameDeaths, report.wallSeconds, report.StateHash, verifiedSegments = segments.Count, report = Path.GetRelativePath(output, Path.Combine(directory, "report.json")) };
                runs.Add(summary); Console.WriteLine(JsonData.Write(new { kind = "EndgameBalanceDiscipline", summary }));
            }
        AtomicFile.Write(Path.Combine(output, "summary.json"), JsonData.Write(new { kind = "EndgameBalanceMeasured", policyHash = policy.Hash, identities, seeds, disciplines, runs, failures }));
        return failures == 0 ? 0 : 1;
    }
}
