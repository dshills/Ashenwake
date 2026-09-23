using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Campaign;

public sealed partial class CampaignRuntimeSession
{
    public BuildLoadoutPreview PreviewBuildLoadout(string id)
    {
        var preview = Production.PreviewBuildLoadout(id);
        string blocked = !InHub ? "Manage complete builds at Mara's workshop in Greyhaven." : "";
        var build = Production.BuildLoadouts.FirstOrDefault(b => b.Id == id);
        if (blocked.Length == 0 && build is not null && !build.Equipment.OrderBy(p => p.Key).SequenceEqual(Production.ProgressionView.Equipment.OrderBy(p => p.Key)) &&
            !Interactions.Any(i => i.ActionId == "service.torren")) blocked = "Rescue Torren before applying equipment changes through Mara's build workshop.";
        return blocked.Length == 0 ? preview : preview with { Success = false, Reason = blocked, Requirements = Array.AsReadOnly(new[] { blocked }.Concat(preview.Requirements).ToArray()) };
    }
}
