using System.Globalization;
using Ashenwake.Core.Authoring;
using Ashenwake.Core.Combat;

namespace Ashenwake.Client;

/// <summary>Shared display text. Stable item IDs and serialized names retain save/replay compatibility.</summary>
internal static class EquipmentNames
{
    private static readonly Dictionary<string, string> Names = Load();

    public static string For(CombatItem item) => For(item.DefinitionId, item.Name);

    public static string For(string definitionId, string fallback = "")
    {
        if (Names.TryGetValue(definitionId, out string? name)) return name;
        if (!string.IsNullOrWhiteSpace(fallback)) return fallback;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(definitionId.Split('.').Last().Replace('_', ' '));
    }

    private static Dictionary<string, string> Load()
    {
        // Required packaged asset, also shipped by the standalone sandbox compiler.
        // English/pseudo-locale is selected at launch; there is no runtime language switch.
        var catalog = TextCatalog.Parse(Godot.FileAccess.GetFileAsString("res://text.en.json"));
        if (Godot.OS.GetCmdlineUserArgs().Contains("--pseudo-locale")) catalog = catalog.PseudoLocalize();
        return catalog.Capture().Messages.Keys.Where(key => key.StartsWith("equipment.", StringComparison.Ordinal))
            .ToDictionary(key => "item." + key[10..], key => catalog.Format(key), StringComparer.Ordinal);
    }
}
