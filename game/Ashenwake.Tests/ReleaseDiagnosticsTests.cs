using System.Security.Cryptography;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Diagnostics;
using Xunit;

namespace Ashenwake.Tests;

public sealed class ReleaseDiagnosticsTests
{
    private static string Temp() { string path = Path.Combine(Path.GetTempPath(), "ashenwake-release-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    [Fact]
    public void ManifestIsCanonicalAndDetectsFileMetadataContentAndPathTampering()
    {
        string root = Temp();
        try
        {
            File.WriteAllText(Path.Combine(root, "content.json"), "{\"version\":1}"); File.WriteAllText(Path.Combine(root, "asset.txt"), "authored geometry");
            ReleaseInput[] inputs = [new("content.json", "content"), new("asset.txt", "asset")];
            var first = ReleaseManifests.Create(root, "abc1234", "combat.1", inputs);
            var second = ReleaseManifests.Create(root, "abc1234", "combat.1", inputs.Reverse());
            Assert.Equal(first.ManifestHash, second.ManifestHash); Assert.True(ReleaseManifests.Verify(root, first).Success);
            File.WriteAllText(Path.Combine(root, "unexpected.dll"), "not declared");
            Assert.Contains("Unexpected package file: unexpected.dll", ReleaseManifests.Verify(root, first).Problems);
            File.Delete(Path.Combine(root, "unexpected.dll"));
            Assert.False(ReleaseManifests.Verify(root, first with { BuildId = "other" }).Success);
            File.AppendAllText(Path.Combine(root, "asset.txt"), " changed");
            Assert.Contains("File differs: asset.txt", ReleaseManifests.Verify(root, first).Problems);
            Assert.Throws<InvalidDataException>(() => ReleaseManifests.Create(root, "abc1234", "combat.1", [new("../secret", "source")]));
            Assert.Throws<InvalidDataException>(() => ReleaseManifests.Create(root, "abc1234", "combat.1", [new("asset.txt", "asset"), new("ASSET.TXT", "asset")]));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void ManifestNeverFollowsPackageSymlinksOutsideTheRoot()
    {
        string root = Temp(), outside = Temp();
        try
        {
            string secret = Path.Combine(outside, "private.txt"); File.WriteAllText(secret, "private");
            File.CreateSymbolicLink(Path.Combine(root, "linked.txt"), secret);
            Assert.Throws<InvalidDataException>(() => ReleaseManifests.Create(root, "abc1234", "combat.1", [new("linked.txt", "asset")]));
        }
        finally { Directory.Delete(root, true); Directory.Delete(outside, true); }
    }
    [Fact]
    public void DiagnosticsBoundEventsRedactFreeTextAndRequireReplayOptIn()
    {
        var buffer = new DiagnosticBuffer();
        for (int tick = 0; tick < 300; tick++) buffer.Record(new(tick, "DamageApplied", ContentId: "skill.cleave"));
        buffer.Record(new(300, "Authorization: Bearer secret", ContentId: "/Users/alice/private/token.txt"));
        var bundle = buffer.Capture("abcdef123", new string('A', 64), "Simulation", new IOException("password=secret /Users/alice/save.json"));
        string json = JsonData.Write(bundle);
        Assert.Equal(256, bundle.RecentEvents.Length); Assert.Equal(45, bundle.DroppedEvents);
        Assert.DoesNotContain("alice", json); Assert.DoesNotContain("secret", json); Assert.DoesNotContain("password", json); Assert.Null(bundle.OptInReplay);
        Assert.Equal("Redacted", bundle.RecentEvents[^1].Kind); Assert.Equal("redacted", bundle.RecentEvents[^1].ContentId);
        var session = CombatSession.Create(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json")));
        var recorder = new Ashenwake.Core.Serialization.CombatRecorder(session); recorder.Step(session);
        Assert.Null(buffer.Capture("abcdef123", session.ContentHash, replay: recorder.Capture()).OptInReplay);
        Assert.NotNull(buffer.Capture("abcdef123", session.ContentHash, replay: recorder.Capture(), includeReplay: true).OptInReplay);
        var oversized = recorder.Capture() with { Frames = Enumerable.Repeat(recorder.Capture().Frames[0], 301).ToArray() };
        Assert.Throws<InvalidDataException>(() => buffer.Capture("abcdef123", session.ContentHash, replay: oversized, includeReplay: true));
    }
    [Fact]
    public void MaintainedLegacyFixtureMigratesWithoutRewritingOriginalAndDetectsChanges()
    {
        string root = Temp();
        try
        {
            var content = AdventureContent.Default(); var state = new AdventureLegacyState(17, 12, true);
            string json = JsonData.Write(new AdventureLegacySave(1, content.Hash, JsonData.Hash(state), state)); string path = Path.Combine(root, "v1.json"); File.WriteAllText(path, json);
            var inventory = new SaveFixtureInventory(1, [new("v1", "v1.json", "Adventure", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))]);
            var combat = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
            var phaseZero = ContentCompiler.Compile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "phase0.json")));
            Assert.True(SaveFixtureAudit.Run(root, inventory, content, combat, phaseZero).Success); Assert.Equal(json, File.ReadAllText(path));
            File.WriteAllText(path, json + " "); Assert.False(SaveFixtureAudit.Run(root, inventory, content, combat, phaseZero).Success);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void EveryMaintainedApplicationBuildUpgradesToCurrentWithoutChangingItsOriginal()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "fixtures/save-fixtures.json"))) directory = directory.Parent;
        Assert.NotNull(directory); string root = directory.FullName;
        var inventory = JsonData.Read<SaveFixtureInventory>(File.ReadAllText(Path.Combine(root, "fixtures/save-fixtures.json")));
        var before = inventory.Fixtures.ToDictionary(f => f.Path, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, f.Path)))));
        var result = SaveFixtureAudit.Run(root, inventory, AdventureContent.Parse(File.ReadAllText(Path.Combine(root, "content/adventure.json"))),
            File.ReadAllText(Path.Combine(root, "content/combat.json")), ContentCompiler.Compile(File.ReadAllText(Path.Combine(root, "content/phase0.json"))));
        Assert.True(result.Success, JsonData.Write(result));
        foreach (var pair in before) Assert.Equal(pair.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, pair.Key)))));
        Assert.Equal(3, inventory.Fixtures.Count(f => f.Kind is "PhaseTwo" or "PhaseThree" or "PhaseFour"));
        Assert.Contains(inventory.Fixtures, f => f.Id == "pre-pets-client" && f.Kind == "Endgame");
        Assert.Contains(inventory.Fixtures, f => f.Id == "pet-companion-client" && f.Kind == "Endgame");
        foreach (var fixture in inventory.Fixtures.Where(f => f.Kind == "FrozenDocument"))
            Assert.Contains("no executable restore claimed", result.Fixtures.Single(f => f.Id == fixture.Id).Detail);
    }
    [Fact]
    public void AssetAuditSeparatesMissingSourcesFromUndeclaredDistributionRights()
    {
        string root = Temp();
        try
        {
            File.WriteAllText(Path.Combine(root, "procedural.cs"), "// authored");
            var inventory = new AssetInventory(1, [new("asset.1", "Procedural mesh", ["procedural.cs"], "Project-authored", "Owner declaration pending", false, "Project contributors")], []);
            var audit = AssetCredits.Audit(root, inventory); Assert.True(audit.FilesPresent); Assert.False(audit.RightsApproved);
            File.Delete(Path.Combine(root, "procedural.cs")); Assert.False(AssetCredits.Audit(root, inventory).FilesPresent);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void ActualPhaseFivePackageArchivePreservesAllPermanentProgress()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Ashenwake.sln"))) root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException();
        string path = Path.Combine(root, "fixtures/phase5-endgame-complete.json");
        string original = File.ReadAllText(path);
        ReleaseUpgradeAudit.Run(root, "PhaseFive", original);
        Assert.Equal(original, File.ReadAllText(path));
    }
    [Fact]
    public void BoundedSoakVerifiesCheckpointsAndReplaysWithActualPopulationCaps()
    {
        string content = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
        var report = CombatSoak.Run(content, 2, 180);
        Assert.True(report.Success); Assert.Equal(360, report.Ticks); Assert.Equal(2, report.CheckpointRestores); Assert.Equal(2, report.ReplaysVerified);
        Assert.InRange(report.PeakActors, 1, CombatSession.MaxActors); Assert.InRange(report.PeakProjectiles, 0, CombatSession.MaxProjectiles);
        Assert.True(report.SimulationP99Milliseconds >= report.SimulationP50Milliseconds); Assert.Equal(2, report.FinalHashes.Length);
    }
}
