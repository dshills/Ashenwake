using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

// Additive encounter identities leave published campaign bundles untouched.
internal static class RoamingChampionCombat
{
    internal static readonly RoomDefinition Arena = new(10500, 8500, new(-7000, 0), new(4000, 0), []);
    internal static CampaignCombatEncounter? Find(string id) => id switch
    {
        "championarena.foyer" => new(id, "A champion's trail", "", 0, [], Arena),
        "championarena.pilgrim" => new(id, "The Bell-Torn Pilgrim", "", 0,
            [new("enemy.funeral_guard", new(1700, 0), [])], Arena),
        "championarena.rootwidow" => new(id, "Widow of the Root", "", 0,
            [new("enemy.bloom_carrier", new(1600, 0), []), new("enemy.feeding_root", new(-1800, -2900), []),
             new("enemy.feeding_root", new(2300, 3100), []), new("enemy.feeding_root", new(4800, -2300), [])], Arena),
        "championarena.tithekeeper" => new(id, "The Cinder Tithekeeper", "", 0,
            [new("enemy.forge_sentinel", new(1700, 0), [])], Arena),
        _ => null
    };
}

public sealed partial class CombatSession
{
    private const string ChampionChain = "campaign.champion_chain";
    private const int ChampionChainPullDistance = 1400;
    private string? RoamingChampionName(CombatActor actor) => !IsRoamingChampionArena || actor.Faction != CombatFaction.Enemy ? null
        : actor.DefinitionId == "enemy.feeding_root" ? "Poisonous Brood Nest" : RoamingChampionCombat.Find(_state.EncounterId)!.Name;

    private bool IsRoamingChampionArena => _state.EncounterId is "championarena.foyer" or "championarena.pilgrim" or "championarena.rootwidow" or "championarena.tithekeeper";
    private bool IsRoamingChampion(CombatActor actor) => IsRoamingChampionArena && actor.Faction == CombatFaction.Enemy && actor.DefinitionId != "enemy.feeding_root";

    private void PopulateRoamingChampion()
    {
        if (!IsRoamingChampionArena) return;
        int nest = 0;
        foreach (var actor in _state.Actors.Where(a => a.DefinitionId == "enemy.feeding_root")) actor.RecoveryUntil = Tick + nest++ * 15;
        foreach (var actor in _state.Actors.Where(IsRoamingChampion).ToArray())
        {
            int health = _state.EncounterId switch { "championarena.pilgrim" => 420, "championarena.rootwidow" => 540, _ => 720 };
            _state.Actors[_state.Actors.IndexOf(actor)] = actor with { Health = health, MaxHealth = health, Elite = true };
        }
    }

    private bool ThinkRoamingChampion(CombatActor actor)
    {
        if (!IsRoamingChampionArena) return false;
        if (actor.Pending is not null || actor.RecoveryUntil > Tick || Player.Health <= 0)
        {
            actor.State = _state.Campaign!.Hazards.Any(h => h.SourceId == actor.Id) ? "Windup" : "Recover";
            return true;
        }
        if (actor.DefinitionId == "enemy.feeding_root")
        {
            // A living nest repeatedly marks the player's current position. Killing
            // that actor removes its outstanding warning and all future blooms.
            Warn(actor, "campaign.champion_poison", Player.Position, 1750, 36, 12, DamageFamily.Venom, "Poisoned");
            actor.RecoveryUntil = Tick + 105; actor.SpecialCycle++; actor.State = "Windup";
            return true;
        }
        if (Position.DistanceSquared(actor.Position, Player.Position) > 4000L * 4000)
        {
            actor.State = "Approach";
            MoveTowardTarget(actor, Player.Position, 100);
            return true;
        }
        int cycle = actor.SpecialCycle++;
        actor.State = "Windup";
        switch (_state.EncounterId)
        {
            case "championarena.pilgrim":
                if (cycle % 2 == 0)
                    Warn(actor, ChampionChain, actor.Position, 650, 33, 17, DamageFamily.PhysicalCrush, "Rooted", Player.Position);
                else
                    Warn(actor, "campaign.champion_bell", actor.Position, 3000, 48, 32, DamageFamily.Storm, "Shocked");
                actor.RecoveryUntil = Tick + (cycle % 2 == 0 ? 72 : 108);
                break;
            case "championarena.rootwidow":
                Warn(actor, "campaign.champion_thorns", actor.Position, 700, 33, 20, DamageFamily.PhysicalPierce, "", Player.Position);
                actor.RecoveryUntil = Tick + 81;
                break;
            case "championarena.tithekeeper":
                _state.Campaign!.Actors[actor.Id].GuardedUntil = Tick + 45;
                Warn(actor, "campaign.champion_vent", actor.Position, 1000, 45, 28, DamageFamily.Fire, "Burning", Player.Position);
                Warn(actor, "campaign.champion_slag", Player.Position, 1400, 30, 18, DamageFamily.Fire);
                actor.RecoveryUntil = Tick + 105;
                Emit("ChampionVentCycleStarted", actor.Id, amount: 60, content: "champion.tithekeeper");
                break;
        }
        return true;
    }

    private bool TithekeeperExposed(CombatActor actor) => _state.EncounterId == "championarena.tithekeeper" && IsRoamingChampion(actor) &&
        actor.SpecialCycle > 0 && actor.RecoveryUntil > Tick && _state.Campaign!.Actors[actor.Id].GuardedUntil <= Tick;

    private int RoamingChampionDefense(CombatActor actor) => _state.EncounterId == "championarena.tithekeeper" && IsRoamingChampion(actor)
        ? TithekeeperExposed(actor) ? 0 : 6000 : 0;

    private void RoamingChampionHit(Hit hit, CombatActor? source, CombatActor target, int healthDamage)
    {
        if (hit.ContentId != ChampionChain || _state.EncounterId != "championarena.pilgrim" ||
            source is null || target.Id != 1 || target.Health <= 0 || healthDamage <= 0) return;
        MoveActor(target, Toward(target.Position, source.Position, ChampionChainPullDistance));
        Emit("ChampionChainPulled", source.Id, target.Id, ChampionChainPullDistance, hit.ContentId, hit.ActionId);
    }

    private void ValidateRoamingChampionHazards()
    {
        foreach (var hazard in _state.Campaign!.Hazards.Where(h => h.ContentId.StartsWith("campaign.champion_", StringComparison.Ordinal)))
        {
            var source = _state.Actors.Single(a => a.Id == hazard.SourceId);
            (string encounter, string enemy, string kind, int radius, int damage, DamageFamily family, string status, int windup) expected = hazard.ContentId switch
            {
                ChampionChain => ("pilgrim", "enemy.funeral_guard", "Line", 650, 17, DamageFamily.PhysicalCrush, "Rooted", 33),
                "campaign.champion_bell" => ("pilgrim", "enemy.funeral_guard", "Circle", 3000, 32, DamageFamily.Storm, "Shocked", 48),
                "campaign.champion_poison" => ("rootwidow", "enemy.feeding_root", "Circle", 1750, 12, DamageFamily.Venom, "Poisoned", 36),
                "campaign.champion_thorns" => ("rootwidow", "enemy.bloom_carrier", "Line", 700, 20, DamageFamily.PhysicalPierce, "", 33),
                "campaign.champion_vent" => ("tithekeeper", "enemy.forge_sentinel", "Line", 1000, 28, DamageFamily.Fire, "Burning", 45),
                "campaign.champion_slag" => ("tithekeeper", "enemy.forge_sentinel", "Circle", 1400, 18, DamageFamily.Fire, "", 30),
                _ => throw new InvalidDataException("Unknown champion hazard.")
            };
            if (_state.EncounterId != "championarena." + expected.encounter || source.DefinitionId != expected.enemy || source.Health <= 0 ||
                hazard.Kind != expected.kind || hazard.Radius != expected.radius || hazard.Damage != expected.damage || hazard.Family != expected.family ||
                hazard.Status != expected.status || hazard.ResolveTick > Tick + expected.windup ||
                hazard.Kind == "Circle" && hazard.Position != hazard.End || _state.Campaign.Actors[source.Id].IsEcho)
                throw new InvalidDataException("Invalid roaming champion hazard ownership or geometry.");
        }
    }

    internal void SealRoamingChampionVictory()
    {
        if (!IsRoamingChampionArena || Player.Health <= 0 || _state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
            throw new InvalidOperationException("Only an observed roaming champion victory can seal its arena.");
        _state.Campaign!.Hazards.Clear(); _state.Campaign.SuppressedFragmentId = ""; _state.Campaign.SuppressedUntil = Tick;
        _state.Projectiles.Clear(); _state.Areas.Clear();
        foreach (var actor in _state.Actors) { actor.Pending = null; actor.Statuses.Clear(); }
    }
}
