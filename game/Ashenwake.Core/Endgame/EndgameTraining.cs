using Ashenwake.Core.Training;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed partial class EndgameRuntimeSession
{
    /// <summary>Copies the current earned build. No training outcome is ever reconciled into this journey.</summary>
    public TrainingSession CreateTrainingSession(TrainingTargetMode mode = TrainingTargetMode.Single)
    {
        if (!InHub || HasUnresolvedRegionalHunt) throw new InvalidOperationException("Training is available only in Greyhaven.");
        var player = Combat.View.Actors.Single(a => a.Id == 1);
        if (player.Health <= 0 || Position.DistanceSquared(player.Position, TrainingSession.EntryPosition) >
            (long)TrainingSession.InteractionRange * TrainingSession.InteractionRange)
            throw new InvalidOperationException("Approach the training ground to begin.");
        return new(combatJson, Production.ProjectCampaignCombat(Combat.Capture()).Capture(), mode);
    }
}
