using Ashenwake.Core.Combat;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private string? RoamingChampionCombatLabel(CombatActorView actor, CombatHazardView? hazard)
    {
        if (actor.Health <= 0 || actor.Faction != CombatFaction.Enemy || !_session.EncounterId.StartsWith("championarena.", StringComparison.Ordinal)) return null;
        if (_session.EncounterId == "championarena.tithekeeper" && actor.DefinitionId == "enemy.forge_sentinel")
        {
            if (actor.BossRecoveryTicks > 0) return "VENTS OPEN · ATTACK " + CombatSeconds(actor.BossRecoveryTicks);
            return actor.BossGuardedTicks > 0 ? "FURNACE GUARDED · " + CombatSeconds(actor.BossGuardedTicks) : "FURNACE ARMORED";
        }
        if (hazard?.ContentId == "campaign.champion_bell") return "BELL TOLL · INTERRUPT " + CombatSeconds(hazard.RemainingTicks);
        if (hazard?.ContentId == "campaign.champion_chain") return "DRAGGING CHAIN · SIDESTEP";
        // Nest counterplay lives in the objective card; repeating it above all
        // three nests obscures the Widow and her incoming poison warnings.
        return null;
    }
}
