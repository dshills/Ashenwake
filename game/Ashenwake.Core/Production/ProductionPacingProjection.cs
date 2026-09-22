using Ashenwake.Core.Adventure;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Production;

public sealed partial class ProductionSession
{
    /// <summary>Reprojects an already-authenticated campaign after its XP receipts were
    /// migrated. Ordinary Restore continues to reject mismatched permanent builds.</summary>
    internal static ProductionSession RestorePacingProjection(string combatJson, AdventureContent adventure,
        ProgressionContent policy, ProductionSnapshot snapshot)
    {
        var content = ProductionContent.Resolve(combatJson, policy, adventure);
        var world = ProductionContent.ResolveAdventure(combatJson, adventure);
        var progression = ProgressionSession.Restore(content, snapshot.Progression);
        var expedition = ExpeditionSession.Restore(combatJson, world, snapshot.Expedition);
        var session = new ProductionSession(combatJson, world, content, expedition, progression) { operationSequence = snapshot.OperationSequence };
        // Only XP-derived combat fields changed. Preserve the authenticated runtime
        // inventory order/subset, world state and object sequence exactly.
        session.Combat.ApplyProgressionBuild(session.DeriveBuild());
        session.ValidateProjection();
        session.initial = session.Capture();
        return session;
    }
}
