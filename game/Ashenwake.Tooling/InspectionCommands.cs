using System.Text.Json;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;

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
        string combatSource = File.ReadAllText("content/combat.json"), adventureSource = File.ReadAllText("content/adventure.json");
        var combat = CombatContent.Parse(combatSource);
        var adventure = AdventureContent.Parse(adventureSource);
        var target = args.Length > 2 ? args[2] : null;
        var references = new List<object>();
        void Walk(JsonElement value, string file, string pointer)
        {
            if (value.ValueKind == JsonValueKind.Object)
                foreach (var property in value.EnumerateObject()) Walk(property.Value, file, pointer + "/" + property.Name);
            else if (value.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var item in value.EnumerateArray()) Walk(item, file, pointer + "/" + index++);
            }
            else if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text &&
                (target is not null ? text == target : text.Contains('.')))
                references.Add(new { file, pointer, value = text });
        }
        using var c = JsonDocument.Parse(combatSource);
        using var a = JsonDocument.Parse(adventureSource);
        Walk(c.RootElement, "content/combat.json", ""); Walk(a.RootElement, "content/adventure.json", "");
        Console.WriteLine(JsonData.Write(new { combat.ContentVersion, adventureVersion = adventure.Capture().Version, target, references }));
        return 0;
    }
}
