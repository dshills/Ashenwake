using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

internal static class SecretChamberCombat
{
    internal static readonly RoomDefinition Arena = new(10500, 8500, new(-7000, 0), new(4000, 0), []);
    private static string Primary(string encounter) => SecretChamberCatalog.Definitions.Single(d => d.EncounterId == encounter).PrimaryEnemyId;
    internal static CampaignCombatEncounter? Find(string id) => id switch
    {
        "secretarena.foyer" => new(id, "A hidden sanctuary", "", 0, [], Arena),
        "secretarena.belfry" => new(id, "Last Tollkeeper", "SonicLanes", 0,
            [new(Primary(id), new(1700, 0), ["Gravewake"]), new("enemy.memory_archer", new(4000, -2400), []), new("enemy.memory_archer", new(4000, 2400), [])], Arena),
        "secretarena.nest" => new(id, "Mother Without Eyes", "PoisonLanes", 0,
            [new(Primary(id), new(3300, 0), ["Devourer"]), new("enemy.needle_swarm", new(1800, -2000), []), new("enemy.needle_swarm", new(1800, 2000), [])], Arena),
        "secretarena.furnace" => new(id, "Ash-Eater", "Storm", 900,
            [new(Primary(id), new(2000, 0), ["Stormbound"]), new("enemy.heat_tender", new(4200, -2000), []), new("enemy.emberling", new(4200, 2300), [])], Arena),
        _ => null
    };
}
public sealed partial class CombatSession
{
    internal void SealSecretChamberVictory()
    {
        if (SecretChamberCombat.Find(_state.EncounterId) is null || Player.Health <= 0 || _state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
            throw new InvalidOperationException("Only an observed secret guardian victory can seal its chamber.");
        _state.Campaign!.Hazards.Clear(); _state.Campaign.SuppressedFragmentId = ""; _state.Campaign.SuppressedUntil = Tick;
        _state.Projectiles.Clear(); _state.Areas.Clear();
        foreach (var actor in _state.Actors) { actor.Pending = null; actor.Statuses.Clear(); }
    }
}
