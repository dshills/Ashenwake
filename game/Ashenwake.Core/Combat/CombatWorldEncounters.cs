using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

// These additive identities do not change published campaign content or save hashes.
internal static class WorldEncounterCombat
{
    internal static readonly RoomDefinition Arena = new(10500, 8500, new(-7000, 0), new(4000, 0), []);
    private static string Name(string id) => WorldEncounterCatalog.Definitions.Single(d => d.EncounterId == id).Name;
    internal static CampaignCombatEncounter? Find(string id) => id switch
    {
        "worldarena.foyer" => new(id, "An unexpected encounter", "", 0, [], Arena),
        "worldarena.lantern" => new(id, Name(id), "", 0,
            [new("enemy.funeral_guard", new(2300, 0), []),
             new("enemy.ash_ghoul", new(900, -2100), []), new("enemy.ash_ghoul", new(900, 2100), [])], Arena),
        "worldarena.caravan" => new(id, "The Mourning Caravan", "", 0, [], Arena),
        "worldarena.caravan_challenge" => new(id, Name(id), "SonicLanes", 0,
            [new("enemy.funeral_guard", new(1600, 0), ["Dirgebound"]),
             new("enemy.memory_archer", new(4200, -1900), []), new("enemy.memory_archer", new(4200, 1900), [])], Arena),
        "worldarena.shrine" => new(id, Name(id), "", 0,
            [new("enemy.funeral_guard", new(1600, 0), ["Hunter"]), new("enemy.memory_archer", new(4200, 1900), [])], Arena),
        "worldarena.storm_grey_march" => new(id, Name(id), "SonicLanes", 0,
            [new("enemy.memory_archer", new(4100, -2100), ["Stormbound"]),
             new("enemy.memory_archer", new(4100, 2100), []), new("enemy.funeral_guard", new(1400, 0), [])], Arena),
        "worldarena.storm_verdant" => new(id, Name(id), "PoisonLanes", 0,
            [new("enemy.carnivorous_vine", new(3300, 0), ["Devourer"]),
             new("enemy.needle_swarm", new(900, -2200), ["Hunter"]), new("enemy.needle_swarm", new(900, 2200), [])], Arena),
        "worldarena.storm_cinder" => new(id, Name(id), "Conveyor", 0,
            [new("enemy.forge_sentinel", new(1400, 0), []), new("enemy.heat_tender", new(3900, 0), ["Stormbound"]),
             new("enemy.emberling", new(2000, -2500), []), new("enemy.emberling", new(2000, 2500), [])], Arena),
        "worldarena.storm_spine" => new(id, Name(id), "Faults", 0,
            [new("enemy.contract_keeper", new(3800, 0), ["Dirgebound"]),
             new("enemy.bone_sentinel", new(1600, -1700), []), new("enemy.oath_giant", new(1600, 1700), [])], Arena),
        "worldarena.storm_hollow" => new(id, Name(id), "CausalEchoes", 0,
            [new("enemy.doubled_shadow", new(1500, -1800), ["Riftborn"]),
             new("enemy.doubled_shadow", new(1500, 1800), []), new("enemy.breach_echo", new(4300, 0), ["Stormbound"])], Arena),
        _ => null
    };
}

public sealed partial class CombatSession
{
    private bool IsWorldEncounterArena => WorldEncounterCombat.Find(_state.EncounterId) is not null;
    private bool WorldEncounterBattleActive => IsWorldEncounterArena && Player.Health > 0 &&
        _state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
    private bool ResonanceStormActive => _state.EncounterId.StartsWith("worldarena.storm_", StringComparison.Ordinal) && WorldEncounterBattleActive;
    private bool ShrineDamagePenalty => _state.EncounterId == "worldarena.shrine" && WorldEncounterBattleActive;

    private int WorldEncounterDamagePower(Hit hit, CombatActor? source, CombatActor target)
    {
        if (ShrineDamagePenalty && target.Id == 1) return 12500;
        // The charge powers implanted fragment effects and their summoned attacks,
        // not ordinary skills, equipment procs, or unowned environmental damage.
        return ResonanceStormActive && hit.OwnerId == 1 &&
            (hit.FragmentId != "" || source?.FragmentId is { Length: > 0 } ||
             hit.ContentId is "effect.seismic_release" or "effect.overheated" or "skill.echo_storm") ? 12500 : 10000;
    }

    internal void SealWorldEncounterVictory()
    {
        if (!IsWorldEncounterArena || _state.EncounterId is "worldarena.foyer" or "worldarena.caravan" ||
            Player.Health <= 0 || _state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
            throw new InvalidOperationException("Only an observed world encounter victory can seal its arena.");
        _state.Campaign!.Hazards.Clear();
        _state.Campaign.SuppressedFragmentId = ""; _state.Campaign.SuppressedUntil = Tick;
        _state.Projectiles.Clear(); _state.Areas.Clear();
        foreach (var actor in _state.Actors) { actor.Pending = null; actor.Statuses.Clear(); }
    }
}
