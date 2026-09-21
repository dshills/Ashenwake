using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

/// <summary>Navigation landmarks for the Shattered Spine. Encounter Room definitions own collision.</summary>
public static class SpineCampaignLayout
{
    public const int InteractionRange = 1800;
    public static Position BackExit => new(-9500, 0);
    public static Position ForwardExit => new(9500, 0);
    public static Position ArchiveEntrance => new(6500, -6000);
    public static Position MemoryEntrance => new(6500, 6000);
    public static Position BranchReturn => new(-8500, 0);
    public static Position ArchiveTreasure => new(6500, 4000);

    public static bool Contains(string encounterId) => encounterId is "campaign.bone_causeway" or
        "campaign.contract_hall" or "campaign.covenant_warden" or "exploration.oathkeeper_archive" or "exploration.first_oath";

    public static Position[] Route(string encounterId) => encounterId switch
    {
        "campaign.bone_causeway" => [new(-4500, 0), new(-2000, 0), new(1800, 0), new(6000, 0), ForwardExit],
        "campaign.contract_hall" => [new(-4500, 0), new(-2000, 0), new(1800, 0), new(6000, 0), ForwardExit],
        "campaign.covenant_warden" => [new(-4500, 0), new(0, 0), new(5000, 0), ForwardExit],
        "exploration.oathkeeper_archive" => [new(-4500, 0), new(0, 0), new(3400, 0), new(4600, 1800), ArchiveTreasure],
        "exploration.first_oath" => [new(-4500, 0), new(-1800, 0), new(1800, 0), new(5500, 0), new(6500, 4500)],
        _ => []
    };
}
