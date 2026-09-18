using System.Diagnostics;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Diagnostics;

public static class ProjectileCeilingAudit
{
    public static object Run(string combatJson)
    {
        var session = CombatSession.CreateEncounter(combatJson, 42, "hub");
        var state = session.Capture();
        state.NextActionId = 2; state.NextObjectId = 2000;
        for (int i = 0; i < CombatSession.MaxProjectiles; i++)
            state.Projectiles.Add(new(1000 + i, 1, 1, new(-6000, -4500), new(6000, -4500), 0, "skill.fire_lance", 10, DamageFamily.Fire, 90, 1, 0));
        var times = new List<double>(); long allocated = 0; string? expectedHash = null;
        for (int i = 0; i < 330; i++)
        {
            var current = CombatSession.Restore(combatJson, state);
            long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            current.Step();
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            string hash = current.StateHash;
            expectedHash ??= hash;
            if (hash != expectedHash || current.View.Projectiles.Count != CombatSession.MaxProjectiles)
                throw new InvalidDataException("Projectile ceiling sample did not preserve the identical live population.");
            if (i >= 30) { times.Add(elapsed); allocated += bytes; }
        }
        times.Sort();
        return new
        {
            kind = "ProjectileCeilingAuditPassed",
            projectiles = CombatSession.MaxProjectiles,
            samples = times.Count,
            simulationP50Ms = times[149],
            simulationP95Ms = times[284],
            simulationP99Ms = times[296],
            maxMs = times[^1],
            bytesAllocatedPerStep = allocated / times.Count,
            stateHash = expectedHash,
            scope = "Synthetic validated maximum live projectile snapshot; one simulation step per restore,30warmups,300samples. Measures update/movement without impacts, chained hits, rendering or restore/hash costs. No player progression is modified."
        };
    }
}
