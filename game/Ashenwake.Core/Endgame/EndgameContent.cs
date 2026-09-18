using System.Diagnostics.CodeAnalysis;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed record FractureModifier(string Id, string Name, string Rule, string Counterplay, string[] Excludes, int MinimumTier);
public sealed record GodHuntDefinition(string Id, string Name, string Family, int RequiredTier, bool Secret,
    string[] Phases, string[] Counterplay, string EvolutionMaterial);
public sealed record EndgameDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Version { get; init; } = "endgame.1";
    public string[] Regions { get; init; } = [];
    public string[] BossFamilies { get; init; } = [];
    public string[] RewardTendencies { get; init; } = [];
    public FractureModifier[] Modifiers { get; init; } = [];
    public GodHuntDefinition[] Hunts { get; init; } = [];
}
public sealed record FractureSigil(long Id, ulong Seed, string Region, int Tier, string[] Modifiers, string BossFamily, string RewardTendency, bool Consumed = false);
public sealed class EndgameContent
{
    private readonly EndgameDefinition definition;
    internal EndgameDefinition Data => definition;
    public string Hash { get; }
    private EndgameContent(EndgameDefinition value) { Validate(value); definition = JsonData.Copy(value); Hash = JsonData.Hash(definition); }
    public EndgameDefinition Capture() => JsonData.Copy(definition);
    public static EndgameContent Create(EndgameDefinition value) => new(value);
    public static EndgameContent Parse(string json) => new(JsonData.Read<EndgameDefinition>(json));
    public static EndgameContent Default() => new(new()
    {
        Regions = ["act.grey_march", "act.verdant_maw", "act.cinder_reach", "act.shattered_spine", "act.hollow_night"],
        BossFamilies = ["Vael", "Serath", "Orrun", "Ilyra", "Nhal"],
        RewardTendencies = ["Materials", "Mastery", "Godwrought"],
        Modifiers =
        [
            new("fracture.burning_haste", "Fevered Cinders", "burning_enemies_move_25_percent_faster", "Burning remains useful; the haste is announced by a visible step pulse.", [], 1),
            new("fracture.healing_echoes", "Borrowed Relief", "healing_spawns_delayed_hostile_echoes", "Move out of the announced echo ring after healing; echoes cannot trigger further echoes.", ["fracture.elite_hazards"], 2),
            new("fracture.elite_hazards", "Scars of the Mighty", "dead_elites_leave_bounded_persistent_hazards", "Kill elites near arena edges and leave a safe approach to each exit.", ["fracture.healing_echoes"], 2),
            new("fracture.resistance_inversion", "Unequal Shelter", "highest_resistance_adds_damage_lowest_loses_15_percent", "Inspect the displayed extremes and use positioning against the newly exposed family.", [], 3),
            new("fracture.fragment_overcharge", "Divine Surges", "fragments_gain_50_percent_for_30_of_each_120_ticks", "An announced surge window strengthens fragments without suppressing the baseline build.", [], 3),
            new("fracture.inherited_boss", "Accumulated Memory", "boss_inherits_up_to_two_compatible_prior_elite_modifiers", "Three prior rooms nominate traits in order; the boss keeps at most two distinct compatible traits. Inspect skipped candidates before commitment.", [], 4)
        ],
        Hunts =
        [
            new("hunt.false_vael", "The False Vael", "Vael", 3, false,
                ["forge_heart_vents", "sever_rebuilding_limbs", "cool_the_solar_core"],
                ["Stand in inactive vents; each cycle retains a melee approach.", "Interrupt one rebuilding limb while the other visibly charges.", "Carry cooling gaps between marked solar waves."], "material.vael_rib"),
            new("hunt.ilyra_teeth", "Ilyra Reborn in Teeth", "Ilyra", 4, false,
                ["moving_jaw_roots", "split_brood_channels", "expose_the_seed"],
                ["Read the root trail before each jaw surfaces.", "Close one brood channel to preserve a safe flank.", "Break seed guards during the announced feeding pause."], "material.ilyra_seed"),
            new("hunt.thousand_memories", "The Thousand Memories of Serath", "Serath", 5, false,
                ["three_memory_processions", "break_repeating_echoes", "ring_the_silent_bell"],
                ["Use procession gaps; the real body always carries a solid sigil.", "Interrupt only the marked repeating echo to avoid replenishing it.", "Cross the silent lane after each visible bell pulse."], "material.serath_memory"),
            new("hunt.orrun_without_oath", "Orrun Without an Oath", "Orrun", 6, false,
                ["unbound_faults", "carry_the_broken_terms", "shatter_the_empty_contract"],
                ["Move along numbered faults as stone settles behind each strike.", "Deposit marked terms on separate safe plinths.", "Exploit the recovery after the final oathless slam."], "material.orrun_oath"),
            new("hunt.nhal_reconstruction", "The Shape That Remembers Nhal", "Nhal", 8, true,
                ["observe_the_absent_form", "deny_the_perfect_copy", "close_the_unremembered_door"],
                ["Anchor glyphs distinguish real safe space from decorative absence.", "Interrupt a marked copy channel while preserving another exit.", "Follow the stable rhythm beneath the reversed sound cues."], "material.nhal_absence")
        ]
    });
    public static void Validate(EndgameDefinition d)
    {
        static void Check([DoesNotReturnIf(false)] bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
        Check(d is not null && d.SchemaVersion == 1 && !string.IsNullOrWhiteSpace(d.Version) && d.Regions is { Length: > 0 and <= 32 } && d.BossFamilies is { Length: > 0 and <= 16 } && d.RewardTendencies is { Length: > 0 and <= 8 } && d.Modifiers is { Length: > 0 and <= 32 } && d.Hunts is { Length: > 0 and <= 16 }, "Invalid endgame content.");
        Check(d.Regions.Concat(d.BossFamilies).Concat(d.RewardTendencies).All(x => !string.IsNullOrWhiteSpace(x)) && d.Modifiers.All(m => m is not null) && d.Hunts.All(h => h is not null), "Null/empty endgame definition.");
        Check(d.Regions.Distinct().Count() == d.Regions.Length && d.BossFamilies.Distinct().Count() == d.BossFamilies.Length && d.RewardTendencies.Distinct().Count() == d.RewardTendencies.Length, "Duplicate endgame region/family/tendency.");
        var ids = new HashSet<string>(); void Id(string id) => Check(!string.IsNullOrWhiteSpace(id) && id.Length <= 100 && ids.Add(id), "Duplicate/invalid endgame ID.");
        foreach (var modifier in d.Modifiers)
        {
            Id(modifier.Id); Check(!string.IsNullOrWhiteSpace(modifier.Name) && !string.IsNullOrWhiteSpace(modifier.Rule) && !string.IsNullOrWhiteSpace(modifier.Counterplay) && modifier.MinimumTier is >= 1 and <= 10 && modifier.Excludes is not null && modifier.Excludes.Distinct().Count() == modifier.Excludes.Length && modifier.Excludes.All(id => id != modifier.Id && d.Modifiers.Any(m => m.Id == id)), "Invalid fracture modifier.");
        }
        foreach (var hunt in d.Hunts)
        {
            Id(hunt.Id); Check(!string.IsNullOrWhiteSpace(hunt.Name) && d.BossFamilies.Contains(hunt.Family) && hunt.RequiredTier is >= 1 and <= 10 && hunt.Phases is { Length: 3 } && hunt.Phases.All(p => !string.IsNullOrWhiteSpace(p)) && hunt.Phases.Distinct().Count() == hunt.Phases.Length && hunt.Counterplay is not null && hunt.Counterplay.Length == hunt.Phases.Length && hunt.Counterplay.All(p => !string.IsNullOrWhiteSpace(p)) && Ashenwake.Core.Progression.EndgameProgression.CatalystIds.Contains(hunt.EvolutionMaterial), "Invalid hunt phases/counterplay/reward.");
        }
        Check(d.Hunts.Count(h => !h.Secret) >= 4, "Endgame requires the four named primary hunt candidates.");
    }
    public static void ValidateSigil(EndgameContent content, FractureSigil s)
    {
        if (s is null || s.Id <= 0 || s.Tier is < 1 or > 10 || s.Modifiers is null || s.Modifiers.Length is < 1 or > 3 || s.Modifiers.Distinct().Count() != s.Modifiers.Length || !content.Data.Regions.Contains(s.Region) || !content.Data.BossFamilies.Contains(s.BossFamily) || !content.Data.RewardTendencies.Contains(s.RewardTendency)) throw new InvalidDataException("Invalid Fracture Sigil.");
        foreach (var id in s.Modifiers)
        {
            var modifier = content.Data.Modifiers.FirstOrDefault(m => m.Id == id);
            if (modifier is null || modifier.MinimumTier > s.Tier || modifier.Excludes.Any(s.Modifiers.Contains)) throw new InvalidDataException("Sigil contains an unknown, premature, or incompatible modifier.");
        }
    }
    public static FractureSigil GenerateSigil(EndgameContent content, long id, ulong seed, int tier)
    {
        if (id <= 0 || tier is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(tier));
        var data = content.Data; ulong rng = seed;
        var region = data.Regions[SeededRandom.Range(ref rng, data.Regions.Length)];
        var family = data.BossFamilies[SeededRandom.Range(ref rng, data.BossFamilies.Length)];
        var tendency = data.RewardTendencies[SeededRandom.Range(ref rng, data.RewardTendencies.Length)];
        var candidates = data.Modifiers.Where(m => m.MinimumTier <= tier).ToList();
        for (int i = candidates.Count - 1; i > 0; i--) { int j = SeededRandom.Range(ref rng, i + 1); (candidates[i], candidates[j]) = (candidates[j], candidates[i]); }
        var selected = new List<FractureModifier>(); int desired = Math.Min(3, 1 + tier / 4);
        foreach (var candidate in candidates)
        {
            if (selected.Count >= desired) break;
            if (selected.All(m => !m.Excludes.Contains(candidate.Id) && !candidate.Excludes.Contains(m.Id))) selected.Add(candidate);
        }
        if (selected.Count == 0) throw new InvalidDataException("No eligible fracture modifier for this tier.");
        var result = new FractureSigil(id, seed, region, tier, selected.Select(m => m.Id).ToArray(), family, tendency);
        ValidateSigil(content, result); return result;
    }
}
