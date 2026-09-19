using System.Collections.ObjectModel;

namespace Ashenwake.Core.Adventure;

/// <summary>A remembered choice can be suppressed when its Resonance threshold is no longer met.</summary>
public sealed record ManifestationThresholdPreview(int Threshold, string? SelectedId, bool Available, bool Active);

/// <summary>Presentation snapshot. Qualifying combinations and historical discoveries are deliberately separate.</summary>
public sealed record AnatomyLoadoutPreview(int Resonance, IReadOnlyDictionary<string, string> Anatomy,
    string[] QualifyingConcordances, string[] DiscoveredConcordances, ManifestationThresholdPreview[] Manifestations);

/// <summary>CanApply describes only the Adventure hub gate; callers must still use their normal service transaction.</summary>
public sealed record AnatomyPreviewResult(bool Success, string Reason, bool CanApply,
    AnatomyLoadoutPreview Before, AnatomyLoadoutPreview After);

public sealed partial class AdventureSession
{
    /// <summary>Forecasts a replacement or removal without changing this session, including while away from Greyhaven.</summary>
    public AnatomyPreviewResult PreviewFragment(string slot, string? fragmentId) => PreviewAnatomy(candidate =>
        candidate.Change((next, events) => candidate.ApplyFragment(next, events, slot, fragmentId, requireHub: false)));

    /// <summary>Forecasts the remembered choice and its active state without committing a service transaction.</summary>
    public AnatomyPreviewResult PreviewManifestation(string manifestationId) => PreviewAnatomy(candidate =>
        candidate.Change((next, events) => candidate.ApplyManifestation(next, events, manifestationId, requireHub: false)));

    private AnatomyPreviewResult PreviewAnatomy(Func<AdventureSession, AdventureResult> operation)
    {
        var before = DescribeAnatomy(state);
        // Run the same mutation, validation, and discovery transaction against a fully isolated candidate.
        // Bypassing the location check on that candidate permits planning away from the service, not applying it.
        var candidate = new AdventureSession(content, state);
        var result = operation(candidate);
        return new(result.Success, result.Reason, result.Success && state.RoomId == Definitions.Hub,
            before, result.Success ? DescribeAnatomy(candidate.state) : before);
    }

    private AnatomyLoadoutPreview DescribeAnatomy(AdventureState value)
    {
        int resonance = ResonanceOf(value);
        var tags = value.Anatomy.Values.SelectMany(id => Definitions.Fragments.Single(f => f.Id == id).Tags).ToHashSet(StringComparer.Ordinal);
        var qualifying = Definitions.Concordances.Where(c => c.Tags.All(tags.Contains)).Select(c => c.Id).Order(StringComparer.Ordinal).ToArray();
        var manifestations = Definitions.Manifestations.Select(m => m.Threshold).Distinct().Order().Select(threshold =>
        {
            string? selected = value.Manifestations.GetValueOrDefault(threshold);
            bool available = resonance >= threshold;
            return new ManifestationThresholdPreview(threshold, selected, available, available && selected is not null);
        }).ToArray();
        return new(resonance, new ReadOnlyDictionary<string, string>(new SortedDictionary<string, string>(value.Anatomy)),
            qualifying, value.Concordances.ToArray(), manifestations);
    }
}
