using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

/// <summary>Navigation landmarks for the Hollow Night. Encounter Room definitions own collision.</summary>
public static class HollowCampaignLayout
{
    public const int InteractionRange = 1800;
    public static Position BackExit => new(-9500, 0);
    public static Position ForwardExit => new(9500, 0);
    public static Position VaultEntrance => new(6500, -6000);
    public static Position BranchReturn => new(-8500, 0);
    public static Position VaultTreasure => new(6500, 4000);

    public static bool Contains(string encounterId) => encounterId is "campaign.repeating_rooms" or
        "campaign.identity_memory" or "campaign.breach_heart" or "exploration.unremembered_vault";

    public static Position[] Route(string encounterId) => encounterId switch
    {
        "campaign.repeating_rooms" => [new(-4500, 0), new(-2000, 0), new(1800, 0), new(6000, 0), ForwardExit],
        "campaign.identity_memory" => [new(-4500, 0), new(-2000, 0), new(1800, 0), new(6000, 0), ForwardExit],
        "campaign.breach_heart" => [new(-4500, 0), new(0, 0), new(5000, 0), ForwardExit],
        "exploration.unremembered_vault" => [new(-4500, 0), new(0, 0), new(3400, 0), new(4600, 1800), VaultTreasure],
        _ => []
    };
}
