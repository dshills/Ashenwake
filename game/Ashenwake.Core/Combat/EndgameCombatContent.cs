using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

/// <summary>A separately versioned overlay. Campaign-only catalog and snapshot identities remain unchanged.</summary>
public sealed class EndgameCombatContent
{
    // Ordered pool is part of endgame-combat.1's seeded manifest contract.
    // New authored campaign traits must not change existing Fracture replays.
    private static readonly string[] InheritedElitePool = ["Mirrorborn", "Gravewake", "Stormbound", "Devourer", "Null", "Hunter", "Martyr", "Riftborn"];
    private readonly CombatContent content;
    public string CombatJson { get; }
    public string Hash => content.Identity;
    public string PolicyHash => EndgameContent.Create(content.Endgame!.Policy!).Hash;
    private EndgameCombatContent(string json) { CombatJson = json; content = CombatContent.Parse(json); }
    public static EndgameCombatContent Parse(string campaignCombatJson, string endgameCombatJson, EndgameContent? policy = null)
    {
        var baseline = CombatContent.Parse(campaignCombatJson);
        EndgameCombatDefinition definition;
        try { definition = JsonData.Read<EndgameCombatDefinition>(endgameCombatJson); }
        catch (System.Text.Json.JsonException ex) { throw new InvalidDataException("Invalid endgame combat JSON.", ex); }
        if (definition?.Enemies is null || baseline.Endgame is not null) throw new InvalidDataException("Missing or duplicate endgame overlay.");
        definition = definition with { Policy = (policy ?? EndgameContent.Default()).Capture() };
        var composed = baseline with { ContentVersion = baseline.ContentVersion + "+" + definition.Version, Enemies = [.. baseline.Enemies, .. definition.Enemies], Endgame = definition };
        return new(JsonData.Write(composed));
    }
    public static EndgameCombatContent FromComposed(string combatJson)
    {
        var result = new EndgameCombatContent(combatJson);
        if (result.content.Endgame is null) throw new InvalidDataException("Endgame combat overlay is required.");
        return result;
    }
    public EndgameCombatManifest CreateFractureManifest(FractureSigil sigil, long runId) => GenerateFracture(content, sigil, runId);
    public EndgameCombatManifest CreateHuntManifest(string huntId, ulong seed, long runId) => GenerateHunt(content, huntId, seed, runId);
    public CombatSession CreateEncounter(EndgameCombatManifest manifest, int encounterIndex, int attempt,
        CombatSnapshot? previous = null, bool restoreAtAnchor = false)
        => CombatSession.CreateEndgameEncounter(CombatJson, manifest, encounterIndex, attempt, previous, restoreAtAnchor);
    public RoomDefinition RoomFor(EndgameCombatManifest manifest, int encounterIndex)
    {
        ValidateManifest(content, manifest);
        if (encounterIndex < 0 || encounterIndex >= manifest.Rooms.Length) throw new ArgumentOutOfRangeException(nameof(encounterIndex));
        return JsonData.Copy(content.Endgame!.Arenas.Single(a => a.Id == manifest.Rooms[encounterIndex].RoomId).Room);
    }
    public void ValidateManifest(EndgameCombatManifest manifest) => ValidateManifest(content, manifest);

    internal static EndgameCombatManifest GenerateFracture(CombatContent content, FractureSigil sigil, long runId)
    {
        EndgameContent.ValidateSigil(EndgameContent.Create(content.Endgame?.Policy ?? throw new InvalidDataException("Missing endgame policy.")), sigil);
        if (content.Endgame is null || runId <= 0) throw new InvalidDataException("A valid run and endgame registry are required.");
        ulong rng = sigil.Seed;
        var packs = content.Endgame.Packs.Where(p => p.Region == sigil.Region).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        for (int i = packs.Count - 1; i > 0; i--) { int j = SeededRandom.Range(ref rng, i + 1); (packs[i], packs[j]) = (packs[j], packs[i]); }
        var rooms = new List<EndgameManifestRoom>(); var inherited = new List<string>(); var sources = new List<EndgameInheritanceSource>();
        for (int index = 0; index < 3; index++)
        {
            var pack = packs[index]; string candidate = InheritedElitePool[SeededRandom.Range(ref rng, InheritedElitePool.Length)];
            int count = Math.Min(pack.Positions.Length, 3 + (sigil.Tier + 1) / 2);
            var spawns = new List<EndgameManifestSpawn>();
            for (int i = 0; i < count; i++)
                spawns.Add(new(pack.EnemyIds[i % pack.EnemyIds.Length], pack.Positions[i], i == count - 1 ? [candidate] : []));
            ulong roomSeed = SeededRandom.Next(ref rng);
            rooms.Add(new(index, "endgame.fracture." + pack.Id, pack.ArenaId, content.Endgame.Arenas.Single(a => a.Id == pack.ArenaId).Name,
                roomSeed, [candidate], false, "Pack", spawns.ToArray()));
            if (!sigil.Modifiers.Contains("fracture.inherited_boss")) continue;
            string reason = inherited.Contains(candidate) ? "duplicate" : inherited.Count >= 2 ? "cap" : "selected";
            if (reason == "selected")
            {
                try { CombatSession.ValidateEliteModifiers([.. inherited, candidate]); }
                catch (InvalidDataException) { reason = "incompatible"; }
            }
            if (reason == "selected") inherited.Add(candidate);
            sources.Add(new(index, candidate, reason == "selected", reason));
        }
        string family = sigil.BossFamily.ToLowerInvariant(); string bossId = "boss.fracture_" + family;
        var arena = content.Endgame.Arenas.Single(a => a.Id == "arena.hunt");
        rooms.Add(new(3, "endgame.fracture.boss_" + family, arena.Id, sigil.BossFamily + " Fracture", SeededRandom.Next(ref rng),
            inherited.ToArray(), true, "Fracture" + sigil.BossFamily,
            [new(bossId, new(2500, 0), inherited.ToArray()), new("enemy.ash_ghoul", new(1000, -3300), []), new("enemy.cinder_acolyte", new(4800, 3300), [])]));
        return new(1, "endgame-combat.1", content.Identity, runId, "Fracture", sigil.BossFamily, sigil.Id, sigil.Seed,
            sigil.Tier, sigil.Region, sigil.RewardTendency, sigil.Modifiers.ToArray(), rooms.ToArray(), sources.ToArray());
    }
    internal static EndgameCombatManifest GenerateHunt(CombatContent content, string huntId, ulong seed, long runId)
    {
        var hunt = content.Endgame?.Policy?.Hunts.FirstOrDefault(h => h.Id == huntId);
        if (hunt is null || content.Endgame is null || runId <= 0) throw new InvalidDataException("Unknown hunt/run.");
        ulong rng = seed;
        var phases = content.Endgame.HuntPhases.Where(p => p.HuntId == huntId).OrderBy(p => p.Index).ToArray();
        if (phases.Length != 3) throw new InvalidDataException("Hunt requires three actual phases.");
        return new(1, "endgame-combat.1", content.Identity, runId, "GodHunt", huntId, 0, seed, hunt.RequiredTier, "", "Godwrought", [],
            phases.Select(p => new EndgameManifestRoom(p.Index, "endgame." + huntId + "." + (p.Index + 1), p.ArenaId, p.Name,
                SeededRandom.Next(ref rng), [], true, p.Pattern, [new(p.BossId, new(2500, 0), [])])).ToArray(), []);
    }
    internal static void ValidateManifest(CombatContent content, EndgameCombatManifest manifest)
    {
        if (manifest is null || manifest.SchemaVersion != 1 || manifest.RulesVersion != "endgame-combat.1" || manifest.ContentHash != content.Identity || manifest.Rooms is null || manifest.RuleIds is null || manifest.Inheritance is null)
            throw new InvalidDataException("Invalid endgame manifest identity.");
        EndgameCombatManifest expected = manifest.Kind switch
        {
            "Fracture" => GenerateFracture(content, new(manifest.SigilId, manifest.Seed, manifest.Region, manifest.Tier, manifest.RuleIds, manifest.ContentId, manifest.RewardTendency), manifest.RunId),
            "GodHunt" => GenerateHunt(content, manifest.ContentId, manifest.Seed, manifest.RunId),
            _ => throw new InvalidDataException("Unknown endgame manifest kind.")
        };
        if (JsonData.Hash(expected) != JsonData.Hash(manifest)) throw new InvalidDataException("Endgame manifest does not match its deterministic source.");
    }
}

public sealed partial class CombatSession
{
    internal static void ValidateEndgameContent(CombatContent content)
    {
        var d = content.Endgame; if (d is null) return;
        if (d.Policy is null) throw new InvalidDataException("Endgame combat requires its exact policy.");
        EndgameContent.Validate(d.Policy);
        var reference = EndgameContent.Default().Capture();
        if (d.Policy.Modifiers.Length != 6 || d.Policy.Modifiers.Any(m => !reference.Modifiers.Any(r => r.Id == m.Id && r.Rule == m.Rule)) || d.Policy.Hunts.Length != 5 || d.Policy.Hunts.Any(h => !reference.Hunts.Any(r => r.Id == h.Id && r.Family == h.Family) || h.Phases.Length != 3)) throw new InvalidDataException("Unsupported endgame rule/hunt behavior policy.");
        if (d.SchemaVersion != 1 || string.IsNullOrWhiteSpace(d.Version) || d.Enemies is null || d.Arenas is not { Length: > 0 and <= 32 } || d.Packs is not { Length: >= 15 and <= 64 } || d.HuntPhases is not { Length: 15 }) throw new InvalidDataException("Invalid endgame combat registry.");
        var ids = new HashSet<string>();
        foreach (var arena in d.Arenas)
        {
            if (arena is null || !ids.Add(arena.Id) || string.IsNullOrWhiteSpace(arena.Name) || arena.Room is not { HalfWidth: >= 10000 and <= 16000, HalfDepth: >= 9000 and <= 16000, Obstacles: not null } room || room.Obstacles.Length > 12 || room.Obstacles.Any(b => b.MinX >= b.MaxX || b.MinZ >= b.MaxZ || b.MinX < -room.HalfWidth || b.MaxX > room.HalfWidth || b.MinZ < -room.HalfDepth || b.MaxZ > room.HalfDepth) || !new SpatialWorld(room).CanOccupy(room.PlayerSpawn, ActorRadius)) throw new InvalidDataException("Invalid endgame arena.");
        }
        ids.Clear();
        foreach (var pack in d.Packs)
        {
            var arena = pack is null ? null : d.Arenas.FirstOrDefault(a => a.Id == pack.ArenaId);
            if (pack is null || !ids.Add(pack.Id) || !d.Policy.Regions.Contains(pack.Region) || arena is null || pack.EnemyIds is not { Length: > 0 } || pack.EnemyIds.Any(id => !content.Enemies.Any(e => e.Id == id && e.Role is "Melee" or "Ranged" or "Armored" or "Support" or "Rusher")) || pack.Positions is not { Length: >= 8 and <= 16 }) throw new InvalidDataException("Invalid Fracture pack.");
            var space = new SpatialWorld(arena.Room); var used = new List<Position> { arena.Room.PlayerSpawn };
            foreach (var at in pack.Positions) { if (!space.CanOccupy(at, ActorRadius) || used.Any(p => Position.DistanceSquared(at, p) < 4L * ActorRadius * ActorRadius)) throw new InvalidDataException("Invalid Fracture spawn."); used.Add(at); }
        }
        if (d.Policy.Regions.Any(region => d.Packs.Count(p => p.Region == region) < 3)) throw new InvalidDataException("Each region requires three authored Fracture pack variants.");
        foreach (var hunt in d.Policy.Hunts)
        {
            var phases = d.HuntPhases.Where(p => p?.HuntId == hunt.Id).OrderBy(p => p.Index).ToArray();
            if (phases.Length != 3 || !phases.Select(p => p.Index).SequenceEqual(new[] { 0, 1, 2 }) || phases.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.Pattern != hunt.Family + (p.Index + 1) || string.IsNullOrWhiteSpace(p.Counterplay) || !d.Arenas.Any(a => a.Id == p.ArenaId) || !content.Enemies.Any(e => e.Id == p.BossId && e.Role == "Beast"))) throw new InvalidDataException("Invalid authored hunt phases.");
        }
        foreach (var family in d.Policy.BossFamilies)
            if (!content.Enemies.Any(e => e.Id == "boss.fracture_" + family.ToLowerInvariant())) throw new InvalidDataException("Missing Fracture boss family.");
    }
}
