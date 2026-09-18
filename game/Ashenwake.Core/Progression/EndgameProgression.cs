using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Progression;

/// <summary>Optional permanent endgame inventory. Absence retains the previous character format exactly.</summary>
public sealed record EndgamePermanentState
{
    public int SchemaVersion { get; init; } = 1;
    public SortedDictionary<string, int> Catalysts { get; set; } = [];
    public SortedDictionary<string, int> SpentCatalysts { get; set; } = [];
}

public static class EndgameProgression
{
    public static IReadOnlyList<string> CatalystIds { get; } = Array.AsReadOnly(new[]
    {
        "material.divine_catalyst", "material.vael_rib", "material.ilyra_seed",
        "material.serath_memory", "material.orrun_oath", "material.nhal_absence"
    });

    public static string ResistanceAffix(DamageFamily family) => "affix.resistance_" + family.ToString().ToLowerInvariant();
    public static ProgressionContent Resolve(ProgressionContent content)
    {
        var data = content.Capture();
        var armorSlots = Enum.GetValues<EquipmentSlot>().Where(s => s is not (EquipmentSlot.MainHand or EquipmentSlot.OffHand)).ToArray();
        var additions = Enum.GetValues<DamageFamily>().Where(f => !data.Affixes.Any(a => a.Id == ResistanceAffix(f)))
            .Select(f => new AffixDefinition(ResistanceAffix(f), armorSlots, 100, 1500, [], false, 35));
        return ProgressionContent.Create(data with { Affixes = [.. data.Affixes, .. additions] });
    }

    public static void Validate(EndgamePermanentState? value)
    {
        if (value is null) return;
        if (value.SchemaVersion != 1 || value.Catalysts is null || value.SpentCatalysts is null || value.Catalysts.Count > CatalystIds.Count || value.SpentCatalysts.Count > CatalystIds.Count ||
            value.Catalysts.Concat(value.SpentCatalysts).Any(p => !CatalystIds.Contains(p.Key) || p.Value is < 1 or > 1000000))
            throw new InvalidDataException("Invalid permanent endgame catalyst inventory.");
    }

    public static SortedDictionary<DamageFamily, int> Resistances(IReadOnlyDictionary<string, int> stats)
        => new(Enum.GetValues<DamageFamily>().ToDictionary(f => f, f => Math.Min(7500, stats.GetValueOrDefault(ResistanceAffix(f)))));
}
