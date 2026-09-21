using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

/// <summary>Authored navigation landmarks shared by campaign interactions and presentation.
/// Collision geometry remains authoritative in each campaign-combat encounter's Room definition.</summary>
public static class OpeningCampaignLayout
{
    public static Position BackExit => new(-9500, 0);
    public static Position ForwardExit => new(9500, 0);
    public static Position CryptEntrance => new(6500, -6000);
    public static Position CryptReturn => new(-8500, 0);
    public static Position CryptTreasure => new(6500, 4000);

    public static bool Contains(string encounterId)
        => encounterId is "campaign.road" or "campaign.monastery" or "campaign.bell_saint" or "exploration.widow_crypt";

    public static Position[] Route(string encounterId) => encounterId switch
    {
        "campaign.road" => [new(-4500, 0), new(-2000, 1800), new(1800, 1800), new(4000, 0), ForwardExit],
        "campaign.monastery" => [new(-4500, 0), new(0, 0), new(3400, 0), new(4500, 1900), new(7500, 1900), ForwardExit],
        "campaign.bell_saint" => [new(-4500, 0), new(0, 0), ForwardExit],
        "exploration.widow_crypt" => [new(-4500, 0), new(2000, 0), new(4500, 2000), CryptTreasure],
        _ => []
    };
}
