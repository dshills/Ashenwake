using System.Diagnostics;
using System.Runtime.InteropServices;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Diagnostics;

public sealed record SaveFixture(string Id, string Path, string Kind, string Sha256);
public sealed record SaveFixtureInventory(int SchemaVersion, SaveFixture[] Fixtures);
public sealed record FixtureResult(string Id, bool Success, string Detail);
public sealed record FixtureAuditResult(bool Success, FixtureResult[] Fixtures);
public static class SaveFixtureAudit
{
    public static FixtureAuditResult Run(string root, SaveFixtureInventory inventory, AdventureContent adventure, string combatJson, ContentBundle phaseZero)
    {
        if (inventory is null || inventory.SchemaVersion != 1 || inventory.Fixtures is not { Length: > 0 } || inventory.Fixtures.Any(f => f is null) || inventory.Fixtures.Select(f => f.Id).Distinct().Count() != inventory.Fixtures.Length)
            throw new InvalidDataException("Invalid maintained save-fixture inventory.");
        var results = new List<FixtureResult>();
        foreach (var fixture in inventory.Fixtures)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fixture.Id) || fixture.Kind is not ("Adventure" or "Combat" or "PhaseZero" or "FrozenDocument" or "PhaseTwo" or "PhaseThree" or "PhaseFour" or "PhaseFive" or "Endgame") || fixture.Sha256 is not { Length: 64 } || !fixture.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid fixture identity, kind, or expected hash.");
                string path = ReleaseManifests.Resolve(root, fixture.Path); var bytes = File.ReadAllBytes(path);
                if (bytes.Length > 16 * 1024 * 1024 || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) != fixture.Sha256) throw new InvalidDataException("Maintained fixture bytes changed; review and version their inventory explicitly.");
                string json = System.Text.Encoding.UTF8.GetString(bytes);
                switch (fixture.Kind)
                {
                    case "FrozenDocument":
                        using (System.Text.Json.JsonDocument.Parse(json)) { }
                        break;
                    case "PhaseTwo":
                    case "PhaseThree":
                    case "PhaseFour":
                    case "PhaseFive":
                    case "Endgame":
                        ReleaseUpgradeAudit.Run(root, fixture.Kind, json);
                        break;
                    case "Adventure":
                        var session = AdventureSaveStore.Deserialize(adventure, json);
                        string upgraded = AdventureSaveStore.Serialize(adventure, session);
                        if (AdventureSaveStore.Deserialize(adventure, upgraded).StateHash != session.StateHash) throw new InvalidDataException("Migrated adventure state changed on its current-schema round trip.");
                        break;
                    case "Combat":
                        var combat = CombatSaveStore.Read(json, combatJson);
                        if (CombatSession.Restore(combatJson, combat).StateHash != JsonData.Hash(combat)) throw new InvalidDataException("Combat fixture restore changed state.");
                        break;
                    case "PhaseZero":
                        var state = SaveStore.Read(json, phaseZero);
                        if (SaveStore.Read(JsonData.Write(SaveStore.Create(phaseZero, state)), phaseZero).Tick != state.Tick) throw new InvalidDataException("Phase zero fixture round trip failed.");
                        break;
                }
                results.Add(new(fixture.Id, true, fixture.Kind == "FrozenDocument" ? "Original bytes and JSON syntax verified; no executable restore claimed." : "Original bytes verified; supported migration/restore and round trip passed."));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
            { results.Add(new(fixture.Id, false, ex is SaveCompatibilityException ? "Fixture requires an explicit schema/content migration; its original bytes were preserved." : "Fixture is missing, changed, malformed, or failed logical validation.")); }
        }
        return new(results.All(r => r.Success), results.ToArray());
    }
}

public sealed record SoakReport(bool Success, string ContentHash, ulong Seed, int Sessions, int TicksPerSession,
    long Ticks, int CheckpointRestores, int ReplaysVerified, int PeakActors, int PeakProjectiles, int PeakAreas,
    double SimulationP50Milliseconds, double SimulationP95Milliseconds, double SimulationP99Milliseconds,
    double AllocatedBytesPerTick, long RetainedManagedBytesDelta, string Runtime, string Platform, string[] FinalHashes, string BuildId);
public static class CombatSoak
{
    public static SoakReport Run(string contentJson, int sessions = 10, int ticksPerSession = 900, ulong seed = 42)
    {
        if (sessions is < 1 or > 100 || ticksPerSession is < 1 or > 3600) throw new ArgumentOutOfRangeException(nameof(sessions), "Soaks support 1..100 sessions of 1..3600 ticks.");
        var content = CombatContent.Parse(contentJson); var samples = new List<double>(sessions * ticksPerSession);
        var hashes = new List<string>(); int restores = 0, peakActors = 0, peakProjectiles = 0, peakAreas = 0;
        long allocations = 0, memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        for (int index = 0; index < sessions; index++)
        {
            var session = CombatSession.Create(contentJson, unchecked(seed + (ulong)index), index % 2 == 0 ? "standard" : "chain");
            var initial = session.Capture(); var frames = new List<CombatFrame>(ticksPerSession); CombatSession? restored = null;
            for (int tick = 0; tick < ticksPerSession; tick++)
            {
                var commands = Commands(session.View); long allocatedBefore = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                var events = session.Step(commands);
                samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); allocations += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                string hash = session.StateHash; frames.Add(new(tick, commands, hash, JsonData.Hash(events)));
                if (restored is not null)
                {
                    var restoredEvents = restored.Step(commands);
                    if (restored.StateHash != hash || JsonData.Hash(restoredEvents) != JsonData.Hash(events)) throw new InvalidDataException("Soak checkpoint continuation diverged.");
                }
                if ((tick + 1) % 120 == 0)
                {
                    var snapshot = session.Capture(); string saved = JsonData.Write(new CombatSave(1, JsonData.Hash(snapshot), snapshot));
                    restored = CombatSession.Restore(contentJson, CombatSaveStore.Read(saved, contentJson)); restores++;
                }
                var view = session.View; peakActors = Math.Max(peakActors, view.Actors.Count); peakProjectiles = Math.Max(peakProjectiles, view.Projectiles.Count); peakAreas = Math.Max(peakAreas, view.Areas.Count);
                if (peakActors > CombatSession.MaxActors || peakProjectiles > CombatSession.MaxProjectiles || peakAreas > CombatSession.MaxAreas) throw new InvalidDataException("Soak population budget exceeded.");
            }
            var replay = CombatReplayRunner.Run(contentJson, new(1, initial, frames.ToArray()));
            if (!replay.Success || replay.FinalHash != session.StateHash) throw new InvalidDataException("Soak replay diverged.");
            hashes.Add(session.StateHash);
        }
        samples.Sort();
        double Percentile(double quantile) => samples[(int)Math.Ceiling(quantile * samples.Count) - 1];
        long retained = GC.GetTotalMemory(forceFullCollection: true) - memoryBefore;
        return new(true, JsonData.Hash(content), seed, sessions, ticksPerSession, (long)sessions * ticksPerSession, restores, sessions,
            peakActors, peakProjectiles, peakAreas, Percentile(.5), Percentile(.95), Percentile(.99),
            (double)allocations / samples.Count, retained, RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription + " " + RuntimeInformation.ProcessArchitecture, hashes.ToArray(), typeof(CombatSoak).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
    }
    private static CombatCommand[] Commands(CombatView view)
    {
        var player = view.Actors.Single(a => a.Id == 1); if (player.Health <= 0) return [];
        var enemy = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0)
            .OrderBy(a => (long)(a.Position.X - player.Position.X) * (a.Position.X - player.Position.X) + (long)(a.Position.Z - player.Position.Z) * (a.Position.Z - player.Position.Z)).ThenBy(a => a.Id).FirstOrDefault();
        if (enemy is null)
        {
            var loot = view.Loot.OrderBy(l => l.Id).FirstOrDefault();
            return loot is null ? [new(CombatCommandKind.Stop)] : [new(CombatCommandKind.Move, X: Math.Sign(loot.Position.X - player.Position.X), Z: Math.Sign(loot.Position.Z - player.Position.Z)), new(CombatCommandKind.Pickup, ItemId: loot.Id)];
        }
        var commands = new List<CombatCommand> { new(CombatCommandKind.Move, X: Math.Sign(enemy.Position.X - player.Position.X), Z: Math.Sign(enemy.Position.Z - player.Position.Z)) };
        if (player.Health < player.MaxHealth / 2) commands.Add(new(CombatCommandKind.Potion));
        for (int i = 0; i < view.Skills.Count; i++) if (view.Tick % (16 + i * 37) == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: view.Skills[i].Id, TargetId: enemy.Id));
        if (view.Tick % 73 == 0 && enemy.TelegraphTicks > 0) commands.Add(new(CombatCommandKind.Dodge, X: Math.Sign(player.Position.Z - enemy.Position.Z), Z: Math.Sign(enemy.Position.X - player.Position.X)));
        return commands.ToArray();
    }
}
