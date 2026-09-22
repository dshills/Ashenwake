using Ashenwake.Core.Campaign;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Endgame;

/// <summary>Fresh campaign-to-Fracture inputs, with no snapshot mutation or synthetic progression.</summary>
public sealed class EndgameBalancePolicy
{
    public const string Version = "endgame-balance-policy.1";
    public const int TargetTier = 3;
    public CampaignBalancePolicy Campaign { get; } = new(managedBuild: true);
    public string Description => Campaign.Description + " After all campaign encounters, retain that character and manage the owned build with exactly the same passive/gear rules before each Fracture and after tier three. Claim the ordinary recovery sigil, then use the lowest-ID earned sigil for the next uncleared tier, in order 1–3. Use EndgameRuntimeSmoke combat, mechanism, pickup and retry inputs; preserve a failed run as a failed measurement instead of farming replacement runs. No God Hunts, sigil attunement, crafting, mutations or shared profile.";
    public string Hash => JsonData.Hash(new { Version, TargetTier, campaignPolicyHash = Campaign.Hash, Description });

    public bool Complete(EndgameRuntimeSession session) => Campaign.Complete(session.Campaign) &&
        EndgameRuntimeSmoke.Complete(session, TargetTier) && CampaignBalancePolicy.ManagedCommand(session.Campaign) is null;

    public EndgameRuntimeCommand Next(EndgameRuntimeSession session)
    {
        if (!Campaign.Complete(session.Campaign))
            return new(EndgameRuntimeAction.Campaign, Campaign: Campaign.Next(session.Campaign));
        if (!session.InHub) return EndgameRuntimeSmoke.Next(session, TargetTier);
        if (CampaignBalancePolicy.ManagedCommand(session.Campaign) is { } build)
            return new(EndgameRuntimeAction.Campaign, Campaign: build);
        var view = session.View;
        int tier = view.HighestClearedTier + 1;
        var sigil = view.AvailableSigils.Where(s => s.Tier == tier).OrderBy(s => s.Id).FirstOrDefault();
        return EndgameRuntimeSmoke.AtGate(session, sigil is null
            ? new(EndgameRuntimeAction.ClaimRecoverySigil)
            : new(EndgameRuntimeAction.StartFracture, sigil.Id));
    }
}
