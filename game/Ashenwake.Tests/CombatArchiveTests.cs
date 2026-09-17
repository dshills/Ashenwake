using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CombatArchiveTests
{
    private static string Content => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));

    [Fact]
    public void RecordingRestoresFromMidEncounterAndReportsFirstDivergentTick()
    {
        var session = CombatSession.Create(Content);
        for (var i = 0; i < 35; i++) session.Step([]);
        var recorder = new CombatRecorder(session);
        for (var i = 0; i < 60; i++) recorder.Step(session, new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: 2));
        var replay = JsonData.Read<CombatReplay>(JsonData.Write(recorder.Capture()));
        Assert.True(CombatReplayRunner.Run(Content, replay).Success);
        replay.Frames[12] = replay.Frames[12] with { StateHash = "invalid" };
        var failure = CombatReplayRunner.Run(Content, replay);
        Assert.False(failure.Success);
        Assert.Equal(47, failure.DivergentTick);
    }

    [Fact]
    public void AtomicSaveRecoversValidBackupAndProtectsIncompatiblePrimary()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ashenwake-combat-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "character.json");
        try
        {
            var session = CombatSession.Create(Content);
            var initial = session.StateHash;
            CombatSaveStore.Write(path, Content, session.Capture());
            session.Step([]);
            CombatSaveStore.Write(path, Content, session.Capture());
            File.WriteAllText(path, "{truncated");
            var recovered = CombatSaveStore.Load(path, Content);
            Assert.True(recovered.RecoveredBackup);
            Assert.Equal(initial, CombatSession.Restore(Content, recovered.State).StateHash);
            CombatSaveStore.Write(path, Content, session.Capture());
            Assert.Equal(initial, CombatSession.Restore(Content, CombatSaveStore.Read(File.ReadAllText(path + ".bak"), Content)).StateHash);
            var newer = JsonData.Read<CombatSave>(File.ReadAllText(path)) with { SchemaVersion = 99 };
            var future = JsonData.Write(newer);
            File.WriteAllText(path, future);
            Assert.Throws<SaveCompatibilityException>(() => CombatSaveStore.Load(path, Content));
            Assert.Throws<SaveCompatibilityException>(() => CombatSaveStore.Write(path, Content, session.Capture()));
            Assert.Equal(future, File.ReadAllText(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ReplaysRejectDiscontinuousFramesAndRecorderDetectsReset()
    {
        var session = CombatSession.Create(Content);
        var recorder = new CombatRecorder(session);
        recorder.Step(session);
        Assert.Throws<InvalidOperationException>(() => recorder.Step(CombatSession.Create(Content)));
        var replay = recorder.Capture();
        replay.Frames[0] = replay.Frames[0] with { Tick = 10 };
        Assert.Throws<InvalidDataException>(() => CombatReplayRunner.Run(Content, replay));
    }

    [Fact]
    public void FragmentIdentityCannotBeAuthoredOrRestoredInTwoSlots()
    {
        var definitions = CombatContent.Parse(Content);
        var eye = definitions.Fragments.Single(f => f.Id == "fragment.eye_vael");
        var duplicate = definitions with { Fragments = [.. definitions.Fragments, eye with { Slot = AnatomySlot.Mind }] };
        Assert.Throws<InvalidDataException>(() => CombatSession.Create(JsonData.Write(duplicate)));
        var snapshot = CombatSession.Create(Content).Capture();
        snapshot.Fragments["Mind"] = eye.Id;
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content, snapshot));
        var session = CombatSession.Create(Content);
        session.Step([new(CombatCommandKind.EquipFragment, ContentId: eye.Id), new(CombatCommandKind.EquipFragment, ContentId: eye.Id)]);
        Assert.Single(session.Capture().Fragments, f => f.Value == eye.Id);
    }
}
