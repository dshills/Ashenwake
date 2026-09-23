using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Endgame;

public sealed partial class EndgameRuntimeSession
{
    public BuildLoadoutPreview PreviewBuildLoadout(string id)
    {
        var preview = Campaign.PreviewBuildLoadout(id);
        const string blocked = "Finish or abandon the endgame run before changing the permanent build.";
        return arena is null ? preview : preview with { Success = false, Reason = blocked, Requirements = Array.AsReadOnly(new[] { blocked }.Concat(preview.Requirements).ToArray()) };
    }
}
