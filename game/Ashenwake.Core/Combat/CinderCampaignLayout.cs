using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

/// <summary>Navigation landmarks for the Cinder Reach. Encounter Room definitions own collision.</summary>
public static class CinderCampaignLayout
{
    public const int InteractionRange = 1800;
    public static Position BackExit => new(-9500, 0);
    public static Position ForwardExit => new(9500, 0);
    public static Position FoundryEntrance => new(6500, -6000);
    public static Position StormEntrance => new(6500, 6000);
    public static Position BranchReturn => new(-8500, 0);
    public static Position FoundryTreasure => new(6500, 4000);

    public static bool Contains(string encounterId) => encounterId is "campaign.cinder_pack" or
        "campaign.extraction_floor" or "campaign.furnace_spindle" or "exploration.sealed_foundry" or "exploration.burning_rain";

    public static Position[] Route(string encounterId) => encounterId switch
    {
        "campaign.cinder_pack" => [new(-4500, 0), new(-2000, 0), new(1800, 0), new(6000, 0), ForwardExit],
        "campaign.extraction_floor" => [new(-4500, 0), new(-2000, 0), new(1800, 0), new(6000, 0), ForwardExit],
        "campaign.furnace_spindle" => [new(-4500, 0), new(0, 0), new(5000, 0), ForwardExit],
        "exploration.sealed_foundry" => [new(-4500, 0), new(0, 0), new(3400, 0), new(4600, 1800), FoundryTreasure],
        "exploration.burning_rain" => [new(-4500, 0), new(-1800, 0), new(1800, 0), new(5500, 0), new(6500, 4500)],
        _ => []
    };
}
