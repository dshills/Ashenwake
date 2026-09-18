using System.Diagnostics;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Coop;

public sealed record CoopDiagnosticCase(ulong Seed, bool Completed, long Ticks, int Wipes, int Rewards,
    int[] PlayerHealth, string FinalHash, bool ReplayVerified, bool RestoreVerified, CoopCounters Counters,
    double MedianStepMicroseconds, double P95StepMicroseconds, double P99StepMicroseconds);
public sealed record CoopDiagnosticReport(string RulesVersion, string ContentHash, CoopDiagnosticCase[] Cases);
public static class CoopDiagnostics
{
    public static CoopDiagnosticReport Run(string combatJson, params ulong[] seeds)
    {
        if (seeds is null || seeds.Length > 64) throw new ArgumentException("Co-op diagnostics support at most 64 seeds.", nameof(seeds));
        if (seeds.Length == 0) seeds = [42, 43, 44];
        var cases = new List<CoopDiagnosticCase>();
        foreach (ulong seed in seeds)
        {
            var session = CoopCombatSession.Create(combatJson, seed, matchId: "diagnostic-" + seed); var recorder = new CoopRecorder(session);
            var durations = new List<double>(); int wipes = 0; bool restored = true;
            for (int tick = 0; tick < CoopSmoke.MaximumTicks && !session.View.Completed; tick++)
            {
                var view = session.View;
                foreach (var player in view.Players)
                    if (!recorder.Submit(session, player.Id, CoopSmoke.Input(view, player.Id, player.AcceptedSequence + 1)).Accepted) throw new InvalidOperationException("Diagnostic intent was rejected.");
                long started = Stopwatch.GetTimestamp(); var events = recorder.Step(session);
                durations.Add(Stopwatch.GetElapsedTime(started).TotalMicroseconds);
                wipes += events.Count(e => e.Kind == "PartyWiped");
                if (tick % 137 == 0) restored &= session.StateHash == CoopCombatSession.Restore(combatJson, session.Capture()).StateHash;
            }
            durations.Sort(); double Percentile(double percentile) => durations[Math.Clamp((int)Math.Ceiling(durations.Count * percentile) - 1, 0, durations.Count - 1)];
            var final = session.View;
            cases.Add(new(seed, final.Completed, session.Tick, wipes, final.Rewards.Length, final.Actors.Where(a => a.PlayerId > 0).Select(a => a.Health).ToArray(), session.StateHash,
                CoopReplayRunner.Run(combatJson, recorder.Capture()).Success, restored, final.Counters, Percentile(.5), Percentile(.95), Percentile(.99)));
        }
        return new(CoopCombatSession.RulesVersion, Ashenwake.Core.Combat.CombatContent.Parse(combatJson).Identity, cases.ToArray());
    }
}
