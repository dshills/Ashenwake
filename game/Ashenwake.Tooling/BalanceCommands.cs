using System.Diagnostics;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Tooling;

internal static class BalanceCommands
{
    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 4 || args[1] is not ("run" or "loot")) return 2;
        int seeds = args[1] == "loot" ? 20 : 3;
        if (args.Length > 3 && !int.TryParse(args[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out seeds))
            throw new ArgumentException("Seed count must be an integer from 1 to 100.");
        if (seeds is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(seeds), "Use 1..100 fixed seeds.");
        string json = File.ReadAllText("content/combat.json"); var content = CombatContent.Parse(json);
        var rows = new List<object>(); var rarities = new SortedDictionary<string, int>(StringComparer.Ordinal); var definitions = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int totalDrops = 0, wins = 0, attempts = 0;
        var disciplines = args[1] == "loot" ? new[] { "Vanguard" } : CombatSession.Disciplines.ToArray();
        var encounters = args[1] == "loot" ? new[] { "encounter.ossuary" } : new[] { "encounter.ossuary", "encounter.cloister", "bell_saint.1", "bell_saint.2", "bell_saint.3" };
        foreach (string discipline in disciplines)
            for (int seedIndex = 0; seedIndex < seeds; seedIndex++)
                foreach (string encounter in encounters)
                {
                    ulong seed = (ulong)(42 + seedIndex);
                    // This is a declared reference build, not a fresh-character progression test.
                    var hub = CombatSession.CreateEncounter(json, seed, "hub");
                    hub.ApplyProgressionBuild(new(discipline, Level: 10, Offense: 3, Defense: 3));
                    var session = CombatSession.CreateEncounter(json, seed, encounter, hub.Capture());
                    var recorder = new CombatRecorder(session);
                    var damageSources = new SortedDictionary<string, long>(StringComparer.Ordinal);
                    var skills = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    var statusTicks = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    var rejects = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    var ticks = new List<double>(); long damageTaken = 0, damageDealt = 0, resourceSum = 0; int emptyResourceTicks = 0, peakSummons = 0, procs = 0;
                    var seenLoot = new HashSet<long>();
                    for (int tick = 0; tick < 3600; tick++)
                    {
                        var before = session.View;
                        if (before.Actors.Single(a => a.Id == 1).Health == 0 || !before.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)) break;
                        var commands = CombatProductionSmoke.Commands(before, content.Room);
                        long started = Stopwatch.GetTimestamp(); var events = recorder.Step(session, commands); ticks.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        foreach (var e in events)
                        {
                            if (e.Kind == "DamageApplied")
                            {
                                if (e.TargetId == 1) damageTaken += e.Amount;
                                else { damageDealt += e.Amount; damageSources[e.ContentId] = damageSources.GetValueOrDefault(e.ContentId) + e.Amount; }
                            }
                            if (e.Kind == "AbilityStarted" && e.ActorId == 1) skills[e.ContentId] = skills.GetValueOrDefault(e.ContentId) + 1;
                            if (e.Kind == "CommandRejected") rejects[e.ContentId] = rejects.GetValueOrDefault(e.ContentId) + 1;
                            if (e.Kind == "FragmentTriggered") procs++;
                        }
                        var view = session.View; resourceSum += view.Momentum; if (view.Momentum == 0) emptyResourceTicks++;
                        peakSummons = Math.Max(peakSummons, view.Actors.Count(a => a.Faction == CombatFaction.Ally && a.Health > 0));
                        foreach (string status in view.Actors.SelectMany(a => a.Statuses).Select(s => s.Id).Distinct()) statusTicks[status] = statusTicks.GetValueOrDefault(status) + 1;
                        foreach (var loot in view.Loot.Where(l => seenLoot.Add(l.Id)))
                        {
                            totalDrops++; rarities[loot.Item.Rarity] = rarities.GetValueOrDefault(loot.Item.Rarity) + 1;
                            definitions[loot.Item.DefinitionId] = definitions.GetValueOrDefault(loot.Item.DefinitionId) + 1;
                        }
                    }
                    var final = session.View; var player = final.Actors.Single(a => a.Id == 1);
                    bool won = player.Health > 0 && !final.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
                    attempts++; if (won) wins++;
                    var replay = CombatReplayRunner.Run(json, recorder.Capture());
                    if (!replay.Success) throw new InvalidDataException("Balance replay diverged: " + discipline + "/" + encounter);
                    ticks.Sort();
                    rows.Add(new
                    {
                        discipline,
                        encounter,
                        seed,
                        won,
                        ticks = session.Tick,
                        secondsToOutcome = session.Tick / 30.0,
                        health = player.Health,
                        survivors = final.Actors.Where(a => a.Health > 0).Select(a => new { a.Id, a.DefinitionId, a.Position, a.Health, a.State }),
                        damageTaken,
                        damageDealt,
                        damageSources,
                        skills,
                        statusTicks,
                        fragmentProcs = procs,
                        peakSummons,
                        emptyResourceTicks,
                        meanResource = ticks.Count == 0 ? 0 : (double)resourceSum / ticks.Count,
                        rejectedCommands = rejects,
                        p99StepAndReplayMs = ticks.Count == 0 ? 0 : ticks[(int)Math.Ceiling(ticks.Count * .99) - 1],
                        session.StateHash
                    });
                }
        var report = new
        {
            kind = "BalanceMeasured",
            content.ContentVersion,
            contentHash = JsonData.Hash(content),
            seedStart = 42,
            seeds,
            attempts,
            wins,
            referenceBuild = "Level 10; offense/defense 3 each; starter equipment and anatomy; ultimates and mutation access enabled; no mid-encounter state injection.",
            policy = "Scripted public commands; successful deterministic simulations do not establish human balance or fun. Damage-source totals include all non-player victims and are diagnostic rather than player-only DPS.",
            totalDrops,
            rarities,
            definitions,
            rows
        };
        AtomicFile.Write(args.Length > 2 ? args[2] : "artifacts/balance/" + args[1] + ".json", JsonData.Write(report));
        Console.WriteLine(JsonData.Write(new { report.kind, report.contentHash, attempts, wins, totalDrops }));
        return wins == attempts ? 0 : 1;
    }
}
