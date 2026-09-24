using Ashenwake.Core.Endgame;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private readonly WorldRewardCues _worldRewardCues = new();
    internal int SecretTreasureAudioCount { get; private set; }
    internal int MaterialRewardAudioCount { get; private set; }

    private void PresentWorldRewardAudio(EndgameRuntimeResult result)
    {
        string cue = _worldRewardCues.Observe(result, _session.Tick);
        if (cue.Length == 0) return;
        RewardAudio.Play(this, cue);
        if (cue == "secret_treasure") SecretTreasureAudioCount++;
        if (cue == "collect_currency") MaterialRewardAudioCount++;
    }
}

/// <summary>Runtime commands advance Tick on success. Restored receipts establish a quiet baseline.</summary>
internal sealed class WorldRewardCues
{
    private long _throughTick = -1;
    public void Reset(long tick) => _throughTick = tick;
    public string Observe(EndgameRuntimeResult result, long tick)
    {
        if (!result.Success || tick <= _throughTick) return "";
        _throughTick = tick;
        bool Has(string prefix) => result.WorldEvents.Any(e => e.StartsWith(prefix, StringComparison.Ordinal));
        // A treasure claim may also report its item grant: let the treasure cue carry that moment.
        if (Has("SecretTreasureClaimed:")) return "secret_treasure";
        if (Has("RoamingChampionRewardClaimed:")) return "collect_equipment";
        // These events acknowledge committed materials, not hypothetical currency on the ground.
        if (Has("RegionalHuntRewardClaimed:") || Has("EndgameRewardCommitted:") ||
            result.WorldEvents.Any(e => e.StartsWith("SalvageMaterials:", StringComparison.Ordinal) &&
                int.TryParse(e["SalvageMaterials:".Length..], out int amount) && amount > 0)) return "collect_currency";
        return "";
    }
}
