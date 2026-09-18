using Ashenwake.Core.Combat;
using Ashenwake.Core.Endgame;

namespace Ashenwake.Core.Experiments;

public enum ExperimentAction { Tick, StartContract, BindMemory, ReleaseMemory, Endgame }
public enum ExperimentChoice { BorrowMind, KeepMind }
public sealed record ExperimentCommand(ExperimentAction Action, long SigilId = 0, int SourceActorId = 0,
    ExperimentChoice Choice = ExperimentChoice.BorrowMind, CombatCommand[]? Commands = null, EndgameRuntimeCommand? Endgame = null);
public sealed record ExperimentResult(bool Success, string Reason, CombatEvent[] CombatEvents, string[] WorldEvents);
public sealed record ExperimentRun
{
    public long RunId { get; init; }
    public long SigilId { get; init; }
    public string ContentHash { get; init; } = "";
    public long StartedTick { get; init; }
    public string Status { get; set; } = "Active";
    public int SourceActorId { get; set; }
    public int SourceRoom { get; set; }
    public long BoundTick { get; set; }
    public long UsedTick { get; set; }
    public long EchoActionId { get; set; }
}
public sealed record ExperimentCosmeticReceipt(string Id, string ContentHash, long RunId, int SourceActorId, long EchoActionId, long CompletedTick);
public sealed record ExperimentEntry(long RunId, long SigilId, ExperimentChoice Choice, string ContentHash, long StartedTick, string Outcome);
public sealed record ExperimentSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public string RulesVersion { get; init; } = "experiment-runtime.1";
    public string ContentHash { get; init; } = "";
    public long OperationSequence { get; init; }
    public EndgameRuntimeSnapshot Endgame { get; init; } = null!;
    public ExperimentRun? Run { get; init; }
    public SortedDictionary<string, ExperimentCosmeticReceipt> Cosmetics { get; init; } = [];
    public SortedDictionary<long, ExperimentEntry> Entries { get; init; } = [];
}
public sealed record ExperimentFrame(ExperimentCommand Command, string StateHash, string EventHash);
public sealed record ExperimentReplay(int SchemaVersion, ExperimentSnapshot Initial, ExperimentFrame[] Frames, string Admission = "Available");
public sealed record ExperimentView(string Name, string Admission, bool CanStart, string Tradeoff, string Counterplay,
    ExperimentRun? Run, BorrowedMemoryView? Memory, IReadOnlyList<string> Cosmetics, IReadOnlyList<ExperimentEntry> Entries);
