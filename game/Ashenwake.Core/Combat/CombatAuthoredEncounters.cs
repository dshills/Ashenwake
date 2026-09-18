using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed record CombatSpawn(string EnemyId, Position Position, bool Elite = false, bool Hidden = false);
public sealed record CombatEncounterDefinition(string Id, string Name, CombatSpawn[] Spawns);

public sealed partial class CombatSession
{
    private bool KnownEncounter(string id) => (_state.Endgame is { } e && e.Manifest.Rooms.Any(r => r.EncounterId == id)) || EncounterIds.Contains(id) || _content.Encounters.Any(e => e.Id == id) || _content.Campaign?.Encounters.Any(e => e.Id == id) == true;
    internal static void ValidateAuthoredEncounters(CombatContent content)
    {
        if (content.Encounters is null || content.Encounters.Length > 256) throw new InvalidDataException("Authored encounter registry is missing or too large.");
        var ids = new HashSet<string>(StringComparer.Ordinal); var space = new SpatialWorld(content.Room);
        foreach (var encounter in content.Encounters)
        {
            if (encounter is null || string.IsNullOrWhiteSpace(encounter.Id) || !encounter.Id.StartsWith("encounter.", StringComparison.Ordinal) ||
                encounter.Id.Length > 100 || !encounter.Id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_') ||
                EncounterIds.Contains(encounter.Id) || !ids.Add(encounter.Id) || string.IsNullOrWhiteSpace(encounter.Name) || encounter.Name.Length > 160 ||
                encounter.Spawns is not { Length: > 0 and <= 64 }) throw new InvalidDataException("Invalid/duplicate authored encounter.");
            var positions = new List<Position> { content.Room.PlayerSpawn };
            foreach (var spawn in encounter.Spawns)
            {
                if (spawn is null || !content.Enemies.Any(e => e.Id == spawn.EnemyId && e.Role is "Melee" or "Ranged" or "Armored" or "Support" or "Rusher") ||
                    !space.CanOccupy(spawn.Position, ActorRadius) || positions.Any(p => Position.DistanceSquared(p, spawn.Position) < 4L * ActorRadius * ActorRadius))
                    throw new InvalidDataException("Authored packs require ordinary enemy roles and unobstructed, separated spawns.");
                positions.Add(spawn.Position);
            }
        }
    }
    private void PopulateAuthoredEncounter(string id)
    {
        var encounter = _content.Encounters.FirstOrDefault(e => e.Id == id);
        if (encounter is null) return;
        foreach (var spawn in encounter.Spawns)
        {
            var actor = AddEncounterActor(spawn.EnemyId, spawn.Position);
            _state.Actors[_state.Actors.IndexOf(actor)] = actor with { Elite = spawn.Elite, Hidden = spawn.Hidden };
        }
    }
}
