using Ashenwake.Core.Progression;

namespace Ashenwake.Client;

/// <summary>Cosmetic catalog text for actual equipped powers; never predicts a craft or grants an effect.</summary>
internal static class EquipmentDetails
{
    private static readonly Lazy<Dictionary<string, string>> InnateProperties = new(() => ProgressionContent.Parse(
        Godot.FileAccess.GetFileAsString("res://progression.json")).Capture().Items.ToDictionary(i => i.Id, i => i.Property, StringComparer.Ordinal));
    public static void Preload() => _ = InnateProperties.Value;
    public static string InnateProperty(string id) => InnateProperties.Value.GetValueOrDefault(id, "");
    public static string Lore(string id) => id.StartsWith("item.", StringComparison.Ordinal) ? EquipmentNames.Message("lore." + id[5..]) : "";
    public static string PowerName(string? id) => string.IsNullOrEmpty(id) ? "none" : EquipmentNames.Message("power." + id + ".name") is { Length: > 0 } name ? name : id;
    public static string Power(string? id) => string.IsNullOrEmpty(id) ? "none" : PowerName(id) +
        (EquipmentNames.Message("power." + id + ".description") is { Length: > 0 } text ? "\n" + text : "");
    public static string Evolution(string? evolution) => string.IsNullOrEmpty(evolution) ? "none" : Power("evolution." + evolution.ToLowerInvariant());
    public static string Awakening(PermanentItem item) => Power(item.Awakened ? "awakening.awakened" : "awakening.dormant");

    public static string Inspect(PermanentItem item, ProgressionDefinition content)
    {
        var lines = new List<string> { Lore(item.DefinitionId) };
        string property = content.Items.Single(d => d.Id == item.DefinitionId).Property;
        if (property.Length > 0) lines.Add("Property: " + Power(property));
        if (item.Engraving.Length > 0) lines.Add("Engraving: " + Power(item.Engraving));
        if (item.DefinitionId == "item.ashcleaver" && item.Rarity == ItemRarity.Godwrought)
        {
            lines.Add(Awakening(item));
            if (item.Evolution.Length > 0) lines.Add("Evolution: " + Evolution(item.Evolution));
        }
        return string.Join("\n\n", lines.Where(s => s.Length > 0));
    }
}
