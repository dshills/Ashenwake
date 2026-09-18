using System.Text.Json;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Authoring;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;

namespace Ashenwake.Tooling;

internal static class AuthoringCommands
{
    public static int Run(string[] args)
    {
        var combat = CombatContent.Parse(File.ReadAllText("content/combat.json"));
        var adventure = AdventureContent.Parse(File.ReadAllText("content/adventure.json"));
        var production = ProgressionContent.Parse(File.ReadAllText("content/progression.json"));
        var text = TextCatalog.Parse(File.ReadAllText("content/text.en.json"));
        var progression = production.Capture();
        foreach (var discipline in progression.Disciplines)
            if (!combat.Skills.Any(s => s.Id == discipline.StartingSkill) || !combat.Skills.Any(s => s.Id == discipline.UltimateSkill))
                throw new InvalidDataException("Discipline references an unauthored combat skill: " + discipline.Id);
        text.RequireKeys(["production.title", "production.level", "production.materials", "production.discipline", "production.equipment", "production.mastery", "production.crafting", "production.profile"]);
        switch (args[1])
        {
            case "validate":
                Console.WriteLine(JsonData.Write(new
                {
                    kind = "AuthoringValidated",
                    combat.ContentVersion,
                    adventureHash = adventure.Hash,
                    progressionHash = production.Hash,
                    text.Hash,
                    inventory = new { disciplines = progression.Disciplines.Length, skills = combat.Skills.Length, enemies = combat.Enemies.Length, mutations = combat.Mutations.Length, fragments = combat.Fragments.Length, items = combat.Items.Length, affixes = progression.Affixes.Length, rooms = adventure.Capture().Rooms.Length, objectives = progression.Objectives.Length, localizedMessages = text.Capture().Messages.Count },
                    acceptance = "Counts refer to authored definitions, not independently tested art, balance, language coverage, or production throughput."
                }));
                return 0;
            case "pseudo":
                Write(args.Length > 2 ? args[2] : "artifacts/authoring/text.qps-ploc.json", JsonData.Write(text.PseudoLocalize().Capture()));
                return 0;
            case "templates":
                string output = args.Length > 2 ? args[2] : "artifacts/authoring/templates";
                // Copy validated examples with their real cross-references. Change the ID and adjust
                // intent, then insert into the owning document and validate the complete bundle.
                var templates = new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["skill"] = combat.Skills[0],
                    ["mutation"] = combat.Mutations[0],
                    ["fragment"] = combat.Fragments[0],
                    ["enemy"] = combat.Enemies[0],
                    ["boss"] = combat.Enemies.Single(e => e.Id == "enemy.bell_saint"),
                    ["item"] = combat.Items[0],
                    ["loadout"] = combat.Loadouts[0],
                    ["room"] = adventure.Capture().Rooms[1],
                    ["affix"] = progression.Affixes[0],
                    ["objective"] = progression.Objectives[0],
                    ["message"] = new TextEntry("{count} fragments found", "{count} fragment found"),
                    ["crafting_costs"] = progression.CraftingCosts
                };
                templates["encounter"] = new CombatEncounterDefinition("encounter.new_pack", "New pack",
                    [new("enemy.ash_ghoul", new(-1000, -1500)), new("enemy.cinder_acolyte", new(4300, 2000)), new("enemy.cinder_priest", new(5000, -1500))]);
                foreach (var (kind, value) in templates) Write(Path.Combine(output, kind + ".json"), JsonSerializer.Serialize(value, value.GetType(), JsonData.Options));
                Console.WriteLine(JsonData.Write(new { kind = "AuthoringTemplatesWritten", output, templates = templates.Keys, combat.ContentVersion }));
                return 0;
            default: return 2;
        }
    }
    private static void Write(string path, string json)
    {
        string full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        // Authoring commands may generate artifacts, but cannot overwrite a designer's source.
        using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream); writer.Write(json);
    }
}
