using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Endgame;

public sealed partial class EndgameRuntimeSession
{
    public BuildLoadoutPreview PreviewBuildLoadout(string id)
    {
        var preview = Campaign.PreviewBuildLoadout(id);
        const string blocked = "Finish or abandon the active hunt or expedition before changing the permanent build.";
        return arena is null && !InWorldEncounter && !InRoamingChampion && !InSecretChamber && !HasUnresolvedRegionalHunt ? preview : preview with { Success = false, Reason = blocked, Requirements = Array.AsReadOnly(new[] { blocked }.Concat(preview.Requirements).ToArray()) };
    }
}
