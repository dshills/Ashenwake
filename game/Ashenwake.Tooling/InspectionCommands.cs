using System.Text.Json;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Authoring;
using Ashenwake.Core.Progression;

namespace Ashenwake.Tooling;

internal static class InspectionCommands
{
    public static int Item(string[] args)
    {
        if (args.Length != 3 || args[1] != "show") return 2;
        var content = CombatContent.Parse(File.ReadAllText("content/combat.json"));
        var item = content.Items.FirstOrDefault(i => i.Id == args[2]);
        if (item is null) throw new InvalidDataException("Unknown item ID: " + args[2]);
        Console.WriteLine(JsonData.Write(new { content.ContentVersion, item, source = "content/combat.json", loadouts = content.Loadouts.Where(l => l.ItemIds.Contains(item.Id)).Select(l => l.Id) }));
        return 0;
    }
    public static int References(string[] args)
    {
        if (args.Length > 3) return 2;
        string combatSource = File.ReadAllText("content/combat.json"), adventureSource = File.ReadAllText("content/adventure.json");
        var combat = CombatContent.Parse(combatSource);
        var adventure = AdventureContent.Parse(adventureSource);
        string progressionSource = File.ReadAllText("content/progression.json"), textSource = File.ReadAllText("content/text.en.json");
        var progression = ProgressionContent.Parse(progressionSource);
        TextCatalog.Parse(textSource);
        var target = args.Length > 2 ? args[2] : null;
        var references = new List<object>();
        static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
        void Walk(JsonElement value, string file, string pointer, string? owner = null, bool definition = false)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) owner = id.GetString();
                foreach (var property in value.EnumerateObject()) Walk(property.Value, file, pointer + "/" + Escape(property.Name), owner, property.Name == "id");
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var item in value.EnumerateArray()) Walk(item, file, pointer + "/" + index++, owner);
            }
            else if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text &&
                (target is not null ? text == target : text.Contains('.') && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_')))
                references.Add(new { file, pointer, value = text, owner, definition });
        }
        using var c = JsonDocument.Parse(combatSource);
        using var a = JsonDocument.Parse(adventureSource);
        using var p = JsonDocument.Parse(progressionSource);
        using var t = JsonDocument.Parse(textSource);
        Walk(c.RootElement, "content/combat.json", ""); Walk(a.RootElement, "content/adventure.json", "");
        Walk(p.RootElement, "content/progression.json", ""); Walk(t.RootElement, "content/text.en.json", "");
        foreach (string name in new[] { "campaign", "campaign-combat" })
        {
            string file = "content/" + name + ".json";
            using var campaign = JsonDocument.Parse(File.ReadAllText(file));
            Walk(campaign.RootElement, file, "");
        }
        Console.WriteLine(JsonData.Write(new { combat.ContentVersion, adventureVersion = adventure.Capture().Version, progressionHash = progression.Hash, target, references }));
        return 0;
    }
}
