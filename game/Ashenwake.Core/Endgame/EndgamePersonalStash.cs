using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Endgame;

public sealed partial class EndgameRuntimeSession
{
    public PersonalStashView Stash
    {
        get
        {
            var view = Production.Stash;
            return InHub && !HasUnresolvedRegionalHunt && !InSecretChamber ? view :
                view with { CanUse = false, Requirement = "Resolve your active journey, then visit your personal stash in Greyhaven." };
        }
    }
}
