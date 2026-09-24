using System.Globalization;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Endgame;

namespace Ashenwake.Core.Progression;

public enum BestiaryKind { Family, Boss, Champion }
public sealed record BestiaryEntry(string Id, string EnemyId, string Role, string Name, BestiaryKind Kind,
    string Lore, string[] Regions, string[] CombatNotes, int ArmorBasisPoints, int ResistanceBasisPoints,
    string[] RewardItemIds, string[] HuntIds);

/// <summary>Read-only presentation catalog; never serialized into authoritative combat content.</summary>
public sealed class BestiaryCatalog
{
    public IReadOnlyList<BestiaryEntry> Entries { get; }
    private readonly Dictionary<string, BestiaryEntry> byId;
    private BestiaryCatalog(IEnumerable<BestiaryEntry> entries)
    {
        Entries = Array.AsReadOnly(entries.OrderBy(e => e.Kind).ThenBy(e => e.Name, StringComparer.Ordinal).ToArray());
        byId = Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
    }
    public BestiaryEntry? Find(string id) => byId.GetValueOrDefault(id);
    public BestiaryEntry? Resolve(string enemyId, string encounterId = "")
    {
        var champion = RoamingChampionCatalog.Definitions.FirstOrDefault(c => c.PrimaryEnemyId == enemyId && c.EncounterId == encounterId);
        return Find(champion?.Id ?? enemyId);
    }
    public static BestiaryCatalog Build(CombatContent combat, CampaignDefinition campaign, EndgameDefinition endgame)
    {
        ArgumentNullException.ThrowIfNull(combat); ArgumentNullException.ThrowIfNull(campaign); ArgumentNullException.ThrowIfNull(endgame);
        var entries = new List<BestiaryEntry>();
        // These two legacy Adventure/coop slice forms are not spawned by the solo campaign or endgame.
        // The campaign's boss.bell_saint is its own encounter with internal phase transitions.
        foreach (var enemy in combat.Enemies.Where(e => e.Role is not ("Anchor" or "Bell") &&
                     e.Id is not ("enemy.bell_saint" or "enemy.bell_beast")))
        {
            var campaignEncounters = campaign.Acts.SelectMany(a => a.Encounters).Where(e => e.EnemyIds.Contains(enemy.Id) ||
                combat.Campaign?.Encounters.FirstOrDefault(c => c.Id == e.Id)?.Spawns.Any(s => s.EnemyId == enemy.Id) == true).ToArray();
            var regions = campaign.Acts.Where(a => a.Encounters.Any(e => campaignEncounters.Any(c => c.Id == e.Id))).Select(a => a.Name).ToList();
            foreach (var pack in combat.Endgame?.Packs.Where(p => p.EnemyIds.Contains(enemy.Id)) ?? [])
                regions.Add(campaign.Acts.FirstOrDefault(a => a.Id == pack.Region)?.Name ?? "Fractures");
            var phases = combat.Endgame?.HuntPhases.Where(p => p.BossId == enemy.Id).OrderBy(p => p.Index).ToArray() ?? [];
            string[] hunts = phases.Select(p => p.HuntId).Distinct(StringComparer.Ordinal).ToArray();
            if (hunts.Length > 0) regions.Add("God Hunts");
            if (enemy.Id.StartsWith("boss.fracture_", StringComparison.Ordinal)) regions.Add("Fractures");
            if (regions.Count == 0) regions.Add(enemy.Id is "enemy.hunt_limb" or "enemy.brood_root" or "enemy.memory_copy" or "enemy.contract_seal" or "enemy.absence_anchor" ? "God Hunts" : "The Grey March");
            string pattern = combat.Campaign?.Behaviors.FirstOrDefault(b => b.EnemyId == enemy.Id)?.Pattern ?? "";
            bool boss = enemy.Id.StartsWith("boss.", StringComparison.Ordinal) || enemy.Role is "BellSaint" or "Beast";
            var notes = new List<string> { Counterplay(enemy, pattern) };
            if (boss) notes.AddRange(campaignEncounters.Select(e => e.Counterplay));
            else if (pattern is not ("SporeMend" or "ForgeBellows" or "OathWard" or "SupportFire") && enemy.Role != "Support")
                notes.Add(FormattableString.Invariant($"Standard attack: {enemy.Windup / 30.0:0.##} seconds of windup, then {enemy.Recovery / 30.0:0.##} seconds of recovery. Special attacks can use a different rhythm."));
            // The hunt's authored text is phase-specific. Prefix each phase so it cannot be mistaken for a universal move.
            notes.AddRange(phases.Select(p => p.Name + ": " + p.Counterplay));
            if (enemy.Id.StartsWith("boss.fracture_", StringComparison.Ordinal))
            {
                notes.Add(FractureCounterplay(enemy.Id));
                notes.Add("A Fracture sigil chooses its region and boss family independently. Final-room treasure follows the region, so this boss can guard different rewards in different regions.");
            }
            notes.Add("Defenses shown are base values. Encounter shields, elite traits, and temporary effects can change them.");
            var rewards = LegendaryCollectionCatalog.Entries.Where(r =>
                r.CampaignEncounterId.Length > 0 && campaignEncounters.Any(e => e.Id == r.CampaignEncounterId) ||
                r.HuntId.Length > 0 && hunts.Contains(r.HuntId) ||
                enemy.Id.StartsWith("boss.fracture_", StringComparison.Ordinal) && r.FractureRegionId.Length > 0 && r.FractureEncounterIndex < 0)
                .Select(r => r.ItemId).Distinct(StringComparer.Ordinal).ToArray();
            if (rewards.Length > 0) notes.Add("Known treasure belongs to the listed encounter or reward source; an individual creature does not guarantee a drop.");
            string name = hunts.Length > 0 ? endgame.Hunts.First(h => h.Id == hunts[0]).Name : Readable(enemy.Id);
            entries.Add(new(enemy.Id, enemy.Id, enemy.Role, name, boss ? BestiaryKind.Boss : BestiaryKind.Family,
                Lore(enemy.Id), regions.Distinct(StringComparer.Ordinal).ToArray(), notes.Distinct(StringComparer.Ordinal).ToArray(),
                enemy.Armor, 0, rewards, hunts));
        }
        foreach (var champion in RoamingChampionCatalog.Definitions)
        {
            var enemy = combat.Enemies.FirstOrDefault(e => e.Id == champion.PrimaryEnemyId);
            if (enemy is null) continue;
            entries.Add(new(champion.Id, enemy.Id, enemy.Role, champion.Name, BestiaryKind.Champion, champion.Description,
                [campaign.Acts.First(a => a.Number == champion.Act).Name],
                [champion.Counterplay, "Defenses shown are base values; champion effects can change them.", "Signature treasure is collected after victory, once per character."],
                enemy.Armor, 0, LegendaryCollectionCatalog.Entries.Where(e => e.RoamingChampionId == champion.Id).Select(e => e.ItemId).ToArray(), []));
        }
        return new(entries);
    }
    private static string Readable(string id) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id[(id.IndexOf('.') + 1)..].Replace('_', ' '));
    private static string Counterplay(CombatEnemy enemy, string pattern) => pattern switch
    {
        "SonicLane" or "MemoryArrow" or "ForgeSweep" or "Fault" => "Sidestep the announced line before it resolves; attack during the recovery that follows.",
        "VenomPod" or "Swarm" or "PoisonBurst" => "Leave the announced poison area before it blooms. Keep a clear escape route.",
        "SporeMend" => "Interrupt its announced healing support, then move away from the poison burst.",
        "ForgeBellows" => "Interrupt its announced bellows to deny nearby allies the furnace boost. Avoid its heat warnings.",
        "OathWard" => "Interrupt its announced ward to deny protection to nearby allies. Leave the oath mark before it resolves.",
        "HeatVent" => "Move out of the announced heat area and return during recovery.",
        "OathMark" => "Leave the announced mark before it resolves and roots you.",
        "ShadowDouble" or "CausalEcho" => "Move away from the announced impact. Leave room to evade the next echo.",
        "Root" => "Destroy this stationary support to weaken the encounter mechanic it sustains.",
        "Bell" or "Rootheart" or "Furnace" or "Covenant" or "Breach" or "Antler" => "Read the boss's floor warnings and use the recovery between attacks. Encounter notes describe its special mechanics.",
        _ => enemy.Role switch
        {
            "Support" => "Interrupt its support cast before returning to the surrounding pack.",
            "Ranged" => "Keep moving across its aim and close the distance during recovery.",
            "Armored" => "Evade its windup, then punish recovery. Physical attacks face its base armor.",
            "Rusher" => "Retreat from its announced detonation. Keep your distance when it dies, as its death can also trigger an explosion.",
            "Anchor" => "This stationary support can be destroyed. Its exact role depends on the encounter.",
            "Bell" => "Watch the bell's pulse warning and move clear before it rings.",
            "BellSaint" => "Watch the chains and ritual transitions. Destroy anchors when they sustain the ritual.",
            "Beast" => "Leave the announced strike and attack during recovery.",
            _ => "Let it commit to its windup, evade the strike, and counter during recovery."
        }
    };
    private static string FractureCounterplay(string id) => id switch
    {
        "boss.fracture_vael" => "One vent lane remains inactive each cycle; move into its gap before the furnace lanes ignite.",
        "boss.fracture_ilyra" => "Leave the marked root trail and the later surfacing-jaw circle.",
        "boss.fracture_serath" => "Use the open lane between announced memory processions.",
        "boss.fracture_orrun" => "Read the numbered faults and move before each staggered strike.",
        _ => "Find the lane without an absence warning before the marked lanes resolve."
    };
    private static string Lore(string id) => id switch
    {
        "enemy.ash_ghoul" => "Ash cakes the hands of these restless scavengers. They still reach for warmth among the ruins.",
        "enemy.cinder_acolyte" => "An old furnace prayer survives as a spark on the acolyte's tongue.",
        "enemy.furnace_brute" => "Layers of slag have become armor around a body that has forgotten the forge's silence.",
        "enemy.cinder_priest" => "The priest tends a congregation of embers, keeping its followers close to the fire.",
        "enemy.emberling" => "A hungry coal given legs, forever searching for something else to burn.",
        "enemy.bell_saint" or "boss.bell_saint" => "The monastery's saint carries its worship as iron chains. Something beneath the vestments answers every bell.",
        "enemy.ritual_anchor" => "Bound offerings hold a failing rite in place. The chains lead deeper than stone.",
        "enemy.bell_beast" => "Released from its ceremonial shell, the thing behind the bells no longer pretends to be holy.",
        "enemy.broken_bell" => "Its cracked bronze remembers a service that ended long ago.",
        "enemy.funeral_guard" => "The procession's guards keep their posts after every mourner has gone.",
        "enemy.memory_archer" => "An arrow leaves the bow with the certainty of a remembered wound.",
        "enemy.carnivorous_vine" => "The Maw's roots have learned that travelers are richer nourishment than rain.",
        "enemy.needle_swarm" => "A thousand small hungers move as though they share one thought.",
        "enemy.bloom_carrier" => "A walking garden bears its brood through streets that once knew other names.",
        "enemy.forge_sentinel" => "The workshops' watchmen remain on duty, their orders fused into furnace plate.",
        "enemy.heat_tender" => "Even abandoned furnaces have attendants who refuse to let the coals die.",
        "enemy.oath_giant" => "Old promises have settled into the giant's limbs like veins of stone.",
        "enemy.contract_keeper" => "The keeper enforces terms whose signatories have long since turned to dust.",
        "enemy.bone_sentinel" => "A fortress remembers its defenders in bone and borrowed iron.",
        "enemy.doubled_shadow" => "A shadow took one step too many and found it could walk without its owner.",
        "enemy.breach_echo" => "Across the breach, an unfinished moment keeps trying to happen again.",
        "enemy.feeding_root" => "Every pulse carries stolen life toward a larger heart.",
        "enemy.seal_channel" => "The channel binds a wounded boundary with the weight of an old covenant.",
        "boss.rootheart" => "The living ruins gather their hunger into one restless core.",
        "boss.furnace_spindle" => "A city's appetite for heat turns endlessly around the spindle.",
        "boss.covenant_warden" => "The last custodian of a broken agreement still demands that its terms be honored.",
        "boss.breach_heart" => "At the wound's center, absence has learned to beat like a heart.",
        "boss.antler" => "Something beneath the old paths wears a crown of branching bone.",
        "enemy.hunt_limb" => "The forge's severed limbs remember how to rebuild themselves.",
        "enemy.brood_root" => "A buried channel nourishes the teeth gathering above it.",
        "enemy.memory_copy" => "Some memories repeat because they cannot bear to end.",
        "enemy.contract_seal" => "A promise made visible can also be broken by hand.",
        "enemy.absence_anchor" => "A small certainty holds the shape of the surrounding void.",
        _ when id.Contains("vael", StringComparison.Ordinal) => "A likeness of the forge's power continues to demand fuel long after its makers have vanished.",
        _ when id.Contains("serath", StringComparison.Ordinal) => "Memory gathers into a procession, each echo certain it is the original.",
        _ when id.Contains("orrun", StringComparison.Ordinal) => "Stone carries the burden of an oath whose words are lost.",
        _ when id.Contains("ilyra", StringComparison.Ordinal) => "The scattered hunger of the living world gathers teeth around a hidden seed.",
        _ when id.Contains("nhal", StringComparison.Ordinal) => "A shape at the edge of remembrance makes the empty spaces feel watched.",
        _ => "A survivor of the Ashenwake, shaped by the ruin it inhabits."
    };
}
