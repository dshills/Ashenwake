using System.Diagnostics.CodeAnalysis;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Progression;

public enum EquipmentSlot { Head, Shoulders, Chest, Gloves, Belt, Legs, Boots, Amulet, Ring1, Ring2, MainHand, OffHand }
public enum ItemRarity { Common, Tempered, Rare, Relic, Legendary, Godwrought }
public enum CraftingService { Tempering, Rebinding, Engraving, Extraction, DivineGrafting, Purification }
public sealed record DisciplineDefinition(string Id, string Resource, string StartingSkill, string UltimateSkill);
public sealed record SkillMasteryDefinition(string Id, string Discipline, string[] Mutations);
public sealed record AffixDefinition(string Id, EquipmentSlot[] Slots, int Minimum, int Maximum, string[] Excludes, bool Advanced, int Weight = 100);
public sealed record ProductionItemDefinition(string Id, EquipmentSlot[] Slots, int Hands, string[] Disciplines, string Property, int BaseDamage = 0, int BaseArmor = 0, int BaseCriticalBasisPoints = 0);
public sealed record SpecialPropertyDefinition(string Id, EquipmentSlot[] Slots, string Behavior);
public sealed record ObjectiveDefinition(string Id, string[] Requires, string Service, string JournalKey);
public sealed record ProgressionDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Version { get; init; } = "production.1";
    public int LevelCap { get; init; } = 50;
    public int ExperiencePerLevel { get; init; } = 100;
    public int RespecCost { get; init; } = 5;
    public DisciplineDefinition[] Disciplines { get; init; } = [];
    public SkillMasteryDefinition[] Skills { get; init; } = [];
    public AffixDefinition[] Affixes { get; init; } = [];
    public ProductionItemDefinition[] Items { get; init; } = [];
    public SpecialPropertyDefinition[] Properties { get; init; } = [];
    public ObjectiveDefinition[] Objectives { get; init; } = [];
    public SortedDictionary<string, string> Strings { get; init; } = [];
    public SortedDictionary<CraftingService, int> CraftingCosts { get; init; } = [];
    public string[] ProfileUnlocks { get; init; } = [];
    public string[] DiscoveryIds { get; init; } = ["discovery.greyhaven", "discovery.false_history", "discovery.ritual", "discovery.bell_saint"];
    public string[] FragmentIds { get; init; } = ["fragment.eye_vael", "fragment.heart_serath", "fragment.nerve_ilyra", "fragment.orrun_bone"];
}
public sealed class ProgressionContent
{
    private readonly ProgressionDefinition definition;
    internal ProgressionDefinition Data => definition;
    public string Hash { get; }
    private ProgressionContent(ProgressionDefinition value) { Validate(value); definition = JsonData.Copy(value); Hash = JsonData.Hash(definition); }
    public ProgressionDefinition Capture() => JsonData.Copy(definition);
    public static ProgressionContent Create(ProgressionDefinition value) => new(value);
    public static ProgressionContent Parse(string json) => new(JsonData.Read<ProgressionDefinition>(json));
    public static ProgressionContent Default()
    {
        var all = Enum.GetValues<EquipmentSlot>();
        EquipmentSlot[] weapons = [EquipmentSlot.MainHand, EquipmentSlot.OffHand];
        EquipmentSlot[] jewelry = [EquipmentSlot.Amulet, EquipmentSlot.Ring1, EquipmentSlot.Ring2];
        var disciplines = new[]
        {
            new DisciplineDefinition("Vanguard", "Momentum", "skill.cleave", "skill.cataclysm"),
            new DisciplineDefinition("Veilwalker", "Exposure", "skill.venom_knife", "skill.shadow_execution"),
            new DisciplineDefinition("Arcanist", "Instability", "skill.fire_lance", "skill.starfall"),
            new DisciplineDefinition("Gravecaller", "Remains", "skill.grave_bolt", "skill.procession"),
            new DisciplineDefinition("Warden", "Adaptation", "skill.thorn_shot", "skill.primal_awakening")
        };
        return new(new()
        {
            Disciplines = disciplines,
            Skills = new Dictionary<string, string[]>
            {
                ["Vanguard"] = ["cleave", "shield_breaker", "seismic_wave", "charge", "iron_guard", "cataclysm"],
                ["Veilwalker"] = ["venom_knife", "shadow_step", "dusk_fan", "shroud", "terror", "shadow_execution"],
                ["Arcanist"] = ["fire_lance", "frost_nova", "storm_arc", "vent", "ember_stride", "starfall"],
                ["Gravecaller"] = ["grave_bolt", "bone_lance", "raise_ancestor", "soul_siphon", "grave_command", "procession"],
                ["Warden"] = ["thorn_shot", "entangle", "feral_companion", "barkskin", "adaptive_strike", "primal_awakening"]
            }.SelectMany(pair => pair.Value.Select(id => new SkillMasteryDefinition("skill." + id, pair.Key, []))).ToArray(),
            Affixes =
            [
                new("affix.damage", weapons, 1, 30, [], false),
                new("affix.armor", all.Where(s => !jewelry.Contains(s)).ToArray(), 1, 500, [], false),
                new("affix.critical", jewelry, 10, 1500, [], false),
                new("affix.resource", all, 1, 20, [], false),
                new("affix.fork", weapons, 1, 2, ["affix.chain"], true, 15),
                new("affix.chain", weapons, 1, 3, ["affix.fork"], true, 15)
            ],
            Items = [.. all.Select(s => new ProductionItemDefinition("item.starter_" + s.ToString().ToLowerInvariant(), [s], s is EquipmentSlot.MainHand or EquipmentSlot.OffHand ? 1 : 0, [], "")),
                new("item.ashcleaver", [EquipmentSlot.MainHand], 1, [], "property.burning_stacks"),
                new("item.echo_ring", [EquipmentSlot.Ring1, EquipmentSlot.Ring2], 0, [], "property.summon_burst"),
                new("item.greatstaff", [EquipmentSlot.MainHand], 2, ["Arcanist", "Gravecaller", "Warden"], "")],
            Properties =
            [
                new("property.burning_stacks", [EquipmentSlot.MainHand], "burning_kill_attack_speed"),
                new("property.summon_burst", jewelry, "summons_explode_on_expiry"),
                new("rune.guard", all, "barrier_on_dodge"),
                new("evolution.serath", [EquipmentSlot.MainHand], "flaming_revenants"),
                new("evolution.orrun", [EquipmentSlot.MainHand], "molten_seismic_wave")
            ],
            Objectives =
            [
                new("objective.mara", [], "DivineGrafting", "journal.mara"),
                new("objective.torren", ["objective.mara"], "Tempering", "journal.torren"),
                new("objective.cael", ["objective.mara"], "Purification", "journal.cael"),
                new("objective.oris", ["objective.mara"], "Rebinding", "journal.oris"),
                new("objective.kesh", ["objective.torren"], "Extraction", "journal.kesh"),
                new("objective.haven", ["objective.cael", "objective.kesh", "objective.oris"], "Engraving", "journal.haven")
            ],
            Strings = new() { ["journal.mara"] = "Mara has reopened the anatomy workshop.", ["journal.torren"] = "Torren can improve promising equipment.", ["journal.cael"] = "Sister Cael offers measured relief from divine complications.", ["journal.oris"] = "Oris can rebind an unwanted affix.", ["journal.kesh"] = "Kesh salvages exceptional properties.", ["journal.haven"] = "Greyhaven's workshops are restored." },
            CraftingCosts = new() { [CraftingService.Tempering] = 5, [CraftingService.Rebinding] = 7, [CraftingService.Engraving] = 10, [CraftingService.Extraction] = 10, [CraftingService.DivineGrafting] = 20, [CraftingService.Purification] = 15 },
            ProfileUnlocks = ["profile.fractures", "profile.god_hunts", "profile.secret_hunt", "profile.memory_cartography"]
        });
    }
    public static int AffixLimit(ItemRarity rarity) => rarity switch { ItemRarity.Common => 0, ItemRarity.Tempered => 1, ItemRarity.Rare => 2, ItemRarity.Relic => 3, ItemRarity.Legendary => 3, ItemRarity.Godwrought => 3, _ => throw new InvalidDataException("Unknown rarity.") };
    public static void Validate(ProgressionDefinition d)
    {
        static void Check([DoesNotReturnIf(false)] bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
        Check(d is not null && d.SchemaVersion == 1 && !string.IsNullOrWhiteSpace(d.Version), "Invalid production schema/version.");
        Check(d.LevelCap is >= 2 and <= 100 && d.ExperiencePerLevel is > 0 and <= 10000 && d.RespecCost is >= 0 and <= 100, "Invalid level/respec policy.");
        Check(d.Disciplines is not null && d.Skills is not null && d.Affixes is not null && d.Items is not null && d.Properties is not null && d.Objectives is not null && d.Strings is not null && d.CraftingCosts is not null && d.ProfileUnlocks is not null && d.FragmentIds is not null && d.DiscoveryIds is not null, "Null production content collection.");
        Check(d.Disciplines.All(x => x is not null) && d.Skills.All(x => x is not null) && d.Affixes.All(x => x is not null) && d.Items.All(x => x is not null) && d.Properties.All(x => x is not null) && d.Objectives.All(x => x is not null), "Null production definition.");
        var ids = new HashSet<string>();
        void Id(string id) => Check(!string.IsNullOrWhiteSpace(id) && id.Length <= 100 && ids.Add(id), "Duplicate/invalid production ID: " + id);
        Check(d.Affixes.Length <= 256 && d.Disciplines.Length == 5 && d.Disciplines.Select(x => x.Id).Order().SequenceEqual(new[] { "Arcanist", "Gravecaller", "Vanguard", "Veilwalker", "Warden" }), "All five disciplines are required.");
        foreach (var discipline in d.Disciplines) { Id(discipline.Id); Check(!string.IsNullOrWhiteSpace(discipline.Resource) && discipline.StartingSkill.StartsWith("skill.", StringComparison.Ordinal) && discipline.UltimateSkill.StartsWith("skill.", StringComparison.Ordinal), "Invalid discipline resource/skill."); }
        foreach (var skill in d.Skills)
        {
            Id(skill.Id); Check(d.Disciplines.Any(x => x.Id == skill.Discipline) && skill.Mutations is not null && skill.Mutations.All(m => !string.IsNullOrWhiteSpace(m)) && skill.Mutations.Distinct().Count() == skill.Mutations.Length, "Invalid mastery skill/discipline/mutations.");
        }
        Check(d.Disciplines.All(discipline => d.Skills.Any(s => s.Id == discipline.StartingSkill && s.Discipline == discipline.Id) && d.Skills.Any(s => s.Id == discipline.UltimateSkill && s.Discipline == discipline.Id)), "Discipline entry/ultimate skills are missing from mastery content.");
        foreach (var property in d.Properties) { Id(property.Id); Check(property.Slots is { Length: > 0 } && property.Slots.All(Enum.IsDefined) && property.Slots.Distinct().Count() == property.Slots.Length && !string.IsNullOrWhiteSpace(property.Behavior), "Invalid special property."); }
        foreach (var affix in d.Affixes)
        {
            Id(affix.Id); Check(affix.Slots is { Length: > 0 } && affix.Slots.All(Enum.IsDefined) && affix.Slots.Distinct().Count() == affix.Slots.Length && affix.Weight is >= 1 and <= 10000 && affix.Minimum >= 0 && affix.Maximum >= affix.Minimum && affix.Maximum <= 10000 && affix.Excludes is not null, "Invalid affix range/slots.");
            Check(affix.Excludes.All(id => id != affix.Id && d.Affixes.Any(a => a.Id == id)) && affix.Excludes.Distinct().Count() == affix.Excludes.Length, "Unknown/duplicate affix exclusion.");
        }
        foreach (var item in d.Items)
        {
            Id(item.Id); Check(item.Slots is { Length: > 0 } && item.Slots.All(Enum.IsDefined) && item.Slots.Distinct().Count() == item.Slots.Length && item.Hands is >= 0 and <= 2 && item.Disciplines is not null && item.Disciplines.All(id => d.Disciplines.Any(x => x.Id == id)), "Invalid item slots/hands/discipline.");
            Check(item.BaseDamage is >= 0 and <= 400 && item.BaseArmor is >= 0 and <= 7500 && item.BaseCriticalBasisPoints is >= 0 and <= 7500, "Invalid item base stats.");
            Check(item.Hands == 0 || item.Slots.All(s => s is EquipmentSlot.MainHand or EquipmentSlot.OffHand), "Only weapons occupy hands.");
            Check(item.Hands != 2 || item.Slots.SequenceEqual(new[] { EquipmentSlot.MainHand }), "Two-handed items require MainHand.");
            Check(item.Property == "" || d.Properties.Any(p => p.Id == item.Property && item.Slots.All(p.Slots.Contains)), "Unknown/ineligible item property.");
        }
        foreach (var objective in d.Objectives)
        {
            Id(objective.Id); Check(objective.Requires is not null && objective.Requires.All(id => d.Objectives.Any(o => o.Id == id)) && objective.Requires.Distinct().Count() == objective.Requires.Length && Enum.TryParse<CraftingService>(objective.Service, out var service) && Enum.IsDefined(service), "Invalid objective dependency/service.");
            Check(d.Strings.TryGetValue(objective.JournalKey, out var value) && !string.IsNullOrWhiteSpace(value), "Missing objective localization key.");
        }
        var reached = new HashSet<string>();
        for (int i = 0; i < d.Objectives.Length; i++) foreach (var objective in d.Objectives.Where(o => o.Requires.All(reached.Contains))) reached.Add(objective.Id);
        Check(reached.Count == d.Objectives.Length, "Quest objective dependency cycle.");
        Check(d.CraftingCosts.Count == 6 && d.CraftingCosts.All(p => Enum.IsDefined(p.Key) && p.Value is >= 0 and <= 1000), "All six crafting costs are required.");
        foreach (var unlock in d.ProfileUnlocks) Id(unlock);
        foreach (var fragment in d.FragmentIds) Id(fragment);
        foreach (var discovery in d.DiscoveryIds) { Id(discovery); Check(discovery.StartsWith("discovery.", StringComparison.Ordinal), "Invalid discovery ID."); }
    }
}
