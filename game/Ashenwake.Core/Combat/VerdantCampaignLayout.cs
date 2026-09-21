using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

/// <summary>Navigation landmarks for the Verdant Maw. Encounter Room definitions own collision.</summary>
public static class VerdantCampaignLayout
{
    public static Position BackExit => new(-9500, 0);
    public static Position ForwardExit => new(9500, 0);
    public static Position ShrineEntrance => new(6500, -6000);
    public static Position HuntEntrance => new(6500, 6000);
    public static Position BranchReturn => new(-8500, 0);
    public static Position ShrineTreasure => new(6500, 4000);
    public static Position[] HuntClues => [new(-4200, -2500), new(0, 4500), new(4800, -1800)];

    public static bool Contains(string encounterId) => encounterId is "campaign.living_ruins" or
        "campaign.plague_village" or "campaign.rootheart" or "exploration.briar_shrine" or "exploration.antler_hunt";

    public static Position[] Route(string encounterId) => encounterId switch
    {
        "campaign.living_ruins" => [new(-4500, 0), new(-2000, 0), new(1800, 0), new(6000, 0), ForwardExit],
        "campaign.plague_village" => [new(-4500, 0), new(-2000, 0), new(1800, 0), new(6000, 0), ForwardExit],
        "campaign.rootheart" => [new(-4500, 0), new(0, 0), new(5000, 0), ForwardExit],
        "exploration.briar_shrine" => [new(-4500, 0), new(0, 0), new(3200, 0), new(4600, 1800), ShrineTreasure],
        "exploration.antler_hunt" => [new(-4500, 0), HuntClues[0], new(-2600, 2500), HuntClues[1], new(3000, 2800), HuntClues[2]],
        _ => []
    };
}
