using System.Diagnostics.CodeAnalysis;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Campaign;

public sealed record CampaignEncounter(string Id, string[] EnemyIds, string Mechanic, string Counterplay, string[] EliteModifiers, int Experience, int Materials, string RescuedResident);
public sealed record CampaignAct(int Number, string Id, string Name, string Revelation, string Anchor, CampaignEncounter[] Encounters, string RequiredChoice);
public sealed record ChoiceOutcome(string Id, string Text, string[] ImmediateFlags, int DueAct, string[] DelayedFlags, string[] Allies);
public sealed record CampaignChoice(string Id, int Act, string Prompt, string RequiredEncounter, ChoiceOutcome[] Outcomes);
public sealed record ExplorationDefinition(string Id, int Act, string Kind, string Name, string EncounterId, int DurationTicks, string[] Rules, string[] Clues, string Discovery, int Materials);
public sealed record CampaignDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Version { get; init; } = "campaign.greybox.1";
    public CampaignAct[] Acts { get; init; } = [];
    public CampaignChoice[] Choices { get; init; } = [];
    public ExplorationDefinition[] Exploration { get; init; } = [];
    public string[] Factions { get; init; } = [];
    public string[] Residents { get; init; } = [];
}
public sealed class CampaignContent
{
    private readonly CampaignDefinition definition;
    internal CampaignDefinition Data => definition;
    public string Hash { get; }
    private CampaignContent(CampaignDefinition value) { Validate(value); definition = JsonData.Copy(value); Hash = JsonData.Hash(definition); }
    public CampaignDefinition Capture() => JsonData.Copy(definition);
    public static CampaignContent Create(CampaignDefinition value) => new(value);
    public static CampaignContent Parse(string json) => new(JsonData.Read<CampaignDefinition>(json));
    public static CampaignContent Default() => new(new()
    {
        Version = "campaign.hollow.6",
        Factions = ["Reliquary Church", "Anatomists", "Cinder Compact", "Children of Ilyra", "Oathbound", "Quiet"],
        Residents = ["Mara Vey", "Torren Bale", "Sister Cael", "Oris Fen", "Kesh", "Pale Child"],
        Acts =
        [
            new(1, "act.grey_march", "The Grey March", "Mara saves the protagonist through implantation. Monastery records contradict the official Ashenwake history.", "anchor.greyhaven",
            [new("campaign.road", ["enemy.ash_ghoul", "enemy.cinder_priest"], "escort_telegraphed_ambush", "Interrupt the priest before pursuing the ghouls; the rescue route remains open.", [], 50, 5, "Torren Bale"),
             new("campaign.monastery", ["enemy.funeral_guard", "enemy.memory_archer"], "alternating_sonic_lanes", "Cross a silent lane between bell pulses and recover the false-history ledger.", ["Mirrorborn"], 75, 5, "Sister Cael"),
             new("campaign.bell_saint", ["boss.bell_saint"], "chains_ritual_anchors_released_creature", "Evade chains, destroy ritual anchors during resurrection, then use quiet gaps between independent bells.", [], 150, 15, "")], "choice.fragment"),
            new(2, "act.verdant_maw", "The Verdant Maw", "Ilyra's distributed ecosystem is assembling a body; transformed communities remain people with choices.", "anchor.living_ruins",
            [new("campaign.living_ruins", ["enemy.carnivorous_vine", "enemy.needle_swarm"], "rooting_pods_and_poison_lanes", "Destroy visible rooting pods and preserve clean lanes between poison blooms.", ["Devourer"], 150, 8, "Oris Fen"),
             new("campaign.plague_village", ["enemy.bloom_carrier", "enemy.needle_swarm"], "protect_quarantine_channels", "Rotate between announced breaches; choose the village's treatment after securing the channels.", [], 175, 8, ""),
             new("campaign.rootheart", ["boss.rootheart"], "three_feeding_roots_and_mobile_core", "Sever one feeding root to expose the moving core; alternate paths remain viable for melee and ranged builds.", ["Gravewake"], 250, 20, "")], "choice.transformation"),
            new(3, "act.cinder_reach", "The Cinder Reach", "Extraction warms cities and produces medicine while thinning a seal beneath the industrial works.", "anchor.cinder_station",
            [new("campaign.cinder_pack", ["enemy.ash_ghoul", "enemy.cinder_priest", "enemy.furnace_brute", "enemy.emberling"], "support_armored_pressure_and_death_bursts", "Separate the priest from the brute and step outside the Emberlings' marked burst rings.", ["Stormbound"], 250, 10, "Kesh"),
             new("campaign.extraction_floor", ["enemy.forge_sentinel", "enemy.heat_tender"], "alternating_conveyor_heat", "Use cooled platforms during the machine's announced vent cycle.", [], 275, 10, ""),
             new("campaign.furnace_spindle", ["boss.furnace_spindle"], "rotating_vents_slag_and_core_windows", "Rotate into inactive vents and attack the exposed core after the slag discharge.", ["Martyr"], 350, 25, "")], "choice.extraction"),
            new(4, "act.shattered_spine", "The Shattered Spine", "Orrun's bones are seals. Oathbound contracts sustain cities while binding people to cruel obligations.", "anchor.bone_causeway",
            [new("campaign.bone_causeway", ["enemy.oath_giant", "enemy.contract_keeper"], "sequential_seismic_faults", "Read the numbered fault sequence and move to unbroken stone.", ["Hunter"], 350, 12, ""),
             new("campaign.contract_hall", ["enemy.contract_keeper", "enemy.bone_sentinel"], "visible_oath_zones", "Break the keeper's channel from an unmarked zone; no damage family is mandatory.", [], 375, 12, ""),
             new("campaign.covenant_warden", ["boss.covenant_warden"], "oath_marks_faults_and_stagger_windows", "Spread oath marks, cross the announced fault, then exploit the recovery window.", ["Null"], 450, 30, "")], "choice.oath"),
            new(5, "act.hollow_night", "The Hollow Night", "Nhal never fell: Nhal remained beyond the breach to hold it closed. The protagonist carries an identity fragment, and the old seals cannot simply be restored.", "anchor.last_memory",
            [new("campaign.repeating_rooms", ["enemy.doubled_shadow", "enemy.memory_archer"], "marked_real_exit_and_shadow_decoys", "Real exits keep their anchor glyph; decoys never cover required telegraphs.", ["Riftborn"], 450, 15, "Pale Child"),
             new("campaign.identity_memory", ["enemy.breach_echo", "enemy.doubled_shadow"], "causal_echo_warnings", "Future attack outlines appear before their source; follow the timing marks rather than decorative perspective.", [], 1200, 15, ""),
             new("campaign.breach_heart", ["boss.breach_heart"], "three_seal_channels_and_returning_echoes", "Rotate through visible seal channels, interrupt exposed echoes, and stabilize the breach during each recovery.", ["Mirrorborn"], 600, 40, "")], "choice.future")
        ],
        Choices =
        [
            new("choice.fragment", 1, "Who should hold the dangerous monastery fragment?", "campaign.monastery",
            [new("reliquary", "Entrust it to Sister Cael and the Reliquary, under public witness.", ["fragment.reliquary"], 4, ["reliquary.witnesses_survive"], ["Reliquary Church"]),
             new("mara", "Give Mara access to study it and publish her methods.", ["fragment.anatomists"], 3, ["anatomists.seal_evidence"], ["Anatomists"]),
             new("community", "Let Greyhaven keep it with a shared watch.", ["fragment.community"], 4, ["greyhaven.shared_watch"], []),
             new("keep", "Carry the fragment and accept personal responsibility.", ["fragment.protagonist"], 5, ["identity.fragment_recognizes_carrier"], [])]),
            new("choice.transformation", 2, "Permit controlled Ilyran transformation to treat the plague village?", "campaign.plague_village",
            [new("permit", "Let volunteers choose the treatment under local oversight.", ["village.transformed", "village.survivors"], 4, ["ilyra.envoys_arrive", "village.growth_needs_care"], ["Children of Ilyra"]),
             new("refuse", "Keep the quarantine and fund conventional care.", ["village.quarantined", "medicine.committed"], 4, ["village.slow_recovery", "cael.clinic_needed"], ["Reliquary Church"])]),
            new("choice.extraction", 3, "Continue extraction that heats the city but damages a divine seal?", "campaign.extraction_floor",
            [new("continue", "Keep essential furnaces running while limiting new shafts.", ["city.heated", "seal.extraction_continues"], 5, ["seal.cinder_weakened", "compact.supplies_arrive"], ["Cinder Compact"]),
             new("stop", "Close the shafts and organize replacement heat and medicine.", ["seal.cinder_preserved", "city.rationing"], 5, ["city.hard_winter", "seal.cinder_stable"], ["Anatomists"])]),
            new("choice.oath", 4, "Enforce the cruel contract that powers the causeway, or break it?", "campaign.contract_hall",
            [new("enforce", "Preserve the binding while negotiating relief for its debtors.", ["causeway.stable", "debtors.bound"], 5, ["oathbound.guard_arrives", "debtors.petition_pending"], ["Oathbound"]),
             new("break", "Free the debtors and rebuild the causeway without their bondage.", ["debtors.freed", "causeway.damaged"], 5, ["greyhaven.refugees_arrive", "causeway.rebuild_needed"], ["Anatomists"])]),
            new("choice.future", 5, "After stabilizing the breach, how should Edrath face the truth?", "campaign.identity_memory",
            [new("share", "Publish the evidence and form a coalition to outgrow divine extraction.", ["future.open_coalition"], 5, ["ending.shared_stewardship"], ["Anatomists"]),
             new("guard", "Work with the Quiet to protect the truth while preparing the cities.", ["future.guarded_truth"], 5, ["ending.guarded_transition"], ["Quiet"])])
        ],
        Exploration =
        [
            new("event.widow_crypt", 1, "Memory", "The Widow's Crypt", "exploration.widow_crypt", 0,
                ["context:crypt", "reward:widows_testament", "return:recorded_road"], [], "discovery.widow_crypt", 25),
            new("event.resonance_storm", 3, "Storm", "The Burning Rain", "exploration.burning_rain", 900,
                ["population:stormbound", "hazard:announced_fire_lanes", "fragments:overcharge_windows", "reward:forge_material"], [], "discovery.storm", 10),
            new("event.divine_memory", 4, "Memory", "The Promise Before Stone", "exploration.first_oath", 0,
                ["context:memory", "rule:reversed_fault_order", "return:recorded_anchor"], [], "discovery.divine_seals", 15),
            new("event.wake_hunt", 2, "Hunt", "The Antler That Walks", "exploration.antler_hunt", 0,
                ["behavior:burrow_then_root_charge", "reward:unique_growth_material"], ["clue.shed_bark", "clue.reversed_tracks", "clue.heartwood_nest"], "discovery.antler", 20),
            new("event.briar_shrine", 2, "Memory", "Briarheart Shrine", "exploration.briar_shrine", 0,
                ["context:briar_shrine", "reward:oathseal", "return:living_ruins"], [], "discovery.briar_shrine", 30),
            new("event.sealed_foundry", 3, "Memory", "The Sealed Foundry", "exploration.sealed_foundry", 0,
                ["context:sealed_foundry", "reward:cinderwake_saber", "return:cinder_fields"], [], "discovery.sealed_foundry", 35),
            new("event.oathkeeper_archive", 4, "Memory", "The Oathkeeper’s Archive", "exploration.oathkeeper_archive", 0,
                ["context:oathkeeper_archive", "lore:the_first_witnesses_bound_themselves_to_protect_those_without_a_voice", "reward:archive_testament"], [], "discovery.oathkeeper_archive", 40),
            new("event.unremembered_vault", 5, "Memory", "The Unremembered Vault", "exploration.unremembered_vault", 0,
                ["context:unremembered_vault", "lore:those_erased_from_history_kept_watch_beside_nhal", "reward:vault_testament"], [], "discovery.unremembered_vault", 45)
        ]
    });

    public static void Validate(CampaignDefinition d)
    {
        static void Check([DoesNotReturnIf(false)] bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
        Check(d is not null && d.SchemaVersion == 1 && !string.IsNullOrWhiteSpace(d.Version) && d.Acts is not null && d.Choices is not null && d.Exploration is not null && d.Factions is not null && d.Residents is not null, "Invalid/null campaign content.");
        Check(d.Acts.Length == 5 && d.Acts.All(a => a is not null) && d.Choices.All(c => c is not null) && d.Exploration.All(e => e is not null), "Campaign requires five acts and nonnull definitions.");
        Check(d.Acts.Select(a => a.Number).SequenceEqual(new[] { 1, 2, 3, 4, 5 }), "Campaign acts must be ordered 1..5.");
        Check(d.Factions.Length == 6 && d.Factions.All(f => !string.IsNullOrWhiteSpace(f)) && d.Factions.Distinct().Count() == 6 && d.Residents.All(r => !string.IsNullOrWhiteSpace(r)) && d.Residents.Distinct().Count() == d.Residents.Length, "Invalid faction/resident inventory.");
        var ids = new HashSet<string>(); void Id(string id) => Check(!string.IsNullOrWhiteSpace(id) && id.Length <= 100 && ids.Add(id), "Duplicate/invalid campaign ID: " + id);
        foreach (var act in d.Acts)
        {
            Id(act.Id); Id(act.Anchor);
            Check(!string.IsNullOrWhiteSpace(act.Name) && !string.IsNullOrWhiteSpace(act.Revelation) && act.Encounters is { Length: >= 2 } && act.Encounters.All(e => e is not null), "An act requires story and encounters.");
            foreach (var encounter in act.Encounters)
            {
                Id(encounter.Id); Check(encounter.EnemyIds is { Length: > 0 } && encounter.EnemyIds.All(id => !string.IsNullOrWhiteSpace(id)) && !string.IsNullOrWhiteSpace(encounter.Mechanic) && !string.IsNullOrWhiteSpace(encounter.Counterplay) && encounter.EliteModifiers is not null && encounter.Experience is > 0 and <= 10000 && encounter.Materials is >= 0 and <= 1000, "Invalid campaign encounter/reward/counterplay.");
                Check(encounter.EliteModifiers.All(m => new[] { "Mirrorborn", "Gravewake", "Stormbound", "Devourer", "Null", "Hunter", "Martyr", "Riftborn", "Dirgebound" }.Contains(m)), "Unknown campaign elite modifier.");
                Check(encounter.RescuedResident == "" || d.Residents.Contains(encounter.RescuedResident), "Unknown rescued resident.");
            }
            Check(d.Choices.Any(c => c.Id == act.RequiredChoice && c.Act == act.Number), "Act is missing its required choice.");
        }
        foreach (var choice in d.Choices)
        {
            Id(choice.Id); Check(choice.Act is >= 1 and <= 5 && !string.IsNullOrWhiteSpace(choice.Prompt) && choice.Outcomes is { Length: >= 2 } && choice.Outcomes.All(o => o is not null), "Invalid campaign choice.");
            var act = d.Acts[choice.Act - 1]; Check(act.Encounters.Take(act.Encounters.Length - 1).Any(e => e.Id == choice.RequiredEncounter), "Choice must be reachable before its act boss.");
            Check(choice.Outcomes.Select(o => o.Id).Distinct().Count() == choice.Outcomes.Length, "Duplicate choice outcome.");
            foreach (var outcome in choice.Outcomes)
                Check(!string.IsNullOrWhiteSpace(outcome.Id) && !string.IsNullOrWhiteSpace(outcome.Text) && outcome.ImmediateFlags is not null && outcome.DelayedFlags is not null && outcome.Allies is not null && outcome.DueAct >= choice.Act && outcome.DueAct <= 5 && outcome.ImmediateFlags.Concat(outcome.DelayedFlags).All(f => !string.IsNullOrWhiteSpace(f)) && outcome.Allies.All(d.Factions.Contains), "Invalid choice consequences or faction references.");
        }
        foreach (var exploration in d.Exploration)
        {
            Id(exploration.Id); Id(exploration.EncounterId);
            Check(exploration.Act is >= 1 and <= 5 && exploration.Kind is "Storm" or "Memory" or "Hunt" && !string.IsNullOrWhiteSpace(exploration.Name) && exploration.Rules is { Length: > 0 } && exploration.Rules.All(r => !string.IsNullOrWhiteSpace(r)) && exploration.Clues is not null && exploration.Clues.Distinct().Count() == exploration.Clues.Length && !string.IsNullOrWhiteSpace(exploration.Discovery) && exploration.Materials is >= 0 and <= 1000, "Invalid exploration event.");
            Check(exploration.Kind == "Storm" ? exploration.DurationTicks is >= 30 and <= 18000 : exploration.DurationTicks == 0, "Storm lifetime must be bounded; other events expire on leaving/death/completion.");
            Check(exploration.Kind == "Hunt" ? exploration.Clues.Length >= 2 : exploration.Clues.Length == 0, "Only hunts have ordered tracking clues.");
        }
    }
}
