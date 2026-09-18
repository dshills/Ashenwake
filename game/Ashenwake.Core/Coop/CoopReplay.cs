using Ashenwake.Core.Content;

namespace Ashenwake.Core.Coop;

public sealed record CoopReplayOperation(string Kind, int PlayerId, CoopInput? Input, bool Connected, string StateHash, string ResultHash);
public sealed record CoopReplay(int SchemaVersion, string RulesVersion, CoopSnapshot InitialState, CoopReplayOperation[] Operations);
public sealed record CoopReplayResult(bool Success, int OperationIndex, string Reason, string StateHash);
/// <summary>Records server admissions and connection changes as well as fixed ticks.</summary>
public sealed class CoopRecorder(CoopCombatSession session)
{
    private readonly CoopSnapshot _initial = session.Capture();
    private readonly List<CoopReplayOperation> _operations = [];
    public CoopInputResult Submit(CoopCombatSession current, int playerId, CoopInput input)
    {
        Capacity(); var result = current.Submit(playerId, input);
        _operations.Add(new("Submit", playerId, input, false, current.StateHash, JsonData.Hash(result))); return result;
    }
    public void SetConnected(CoopCombatSession current, int playerId, bool connected)
    {
        Capacity(); current.SetConnected(playerId, connected); _operations.Add(new("Connection", playerId, null, connected, current.StateHash, ""));
    }
    public IReadOnlyList<CoopEvent> Step(CoopCombatSession current)
    {
        Capacity(); var events = current.Step(); _operations.Add(new("Step", 0, null, false, current.StateHash, JsonData.Hash(events))); return events;
    }
    private void Capacity() { if (_operations.Count >= 100000) throw new InvalidOperationException("Co-op replay operation budget exhausted."); }
    public CoopReplay Capture() => new(1, CoopCombatSession.RulesVersion, JsonData.Copy(_initial), _operations.ToArray());
}
public static class CoopReplayRunner
{
    public static CoopReplayResult Run(string combatJson, CoopReplay replay)
    {
        if (replay is null || replay.SchemaVersion != 1 || replay.RulesVersion != CoopCombatSession.RulesVersion || replay.InitialState is null || replay.Operations is null || replay.Operations.Length > 100000) throw new InvalidDataException("Unsupported or invalid co-op replay.");
        var session = CoopCombatSession.Restore(combatJson, replay.InitialState);
        for (int i = 0; i < replay.Operations.Length; i++)
        {
            var operation = replay.Operations[i]; string result;
            if (operation is null) return new(false, i, "Missing operation.", session.StateHash);
            switch (operation.Kind)
            {
                case "Submit" when operation.Input is not null: result = JsonData.Hash(session.Submit(operation.PlayerId, operation.Input)); break;
                case "Connection" when operation.PlayerId is 1 or 2 && operation.Input is null: session.SetConnected(operation.PlayerId, operation.Connected); result = ""; break;
                case "Step" when operation.PlayerId == 0 && operation.Input is null: result = JsonData.Hash(session.Step()); break;
                default: return new(false, i, "Invalid server operation.", session.StateHash);
            }
            if (operation.StateHash != session.StateHash || operation.ResultHash != result) return new(false, i, "Server state or result diverged.", session.StateHash);
        }
        return new(true, replay.Operations.Length, "", session.StateHash);
    }
}
