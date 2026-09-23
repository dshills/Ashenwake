using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class ProductionHud
{
    public event Action<ProductionAction, string, string>? BuildLoadoutRequested;
    private BuildLoadoutsPanel _buildLoadouts = null!;
    private IReadOnlyList<BuildLoadoutView> _savedBuildLoadouts = [];
    private Func<string, BuildLoadoutPreview>? _previewBuildLoadout;
    private IReadOnlyDictionary<string, string> _loadoutFragments = new Dictionary<string, string>();
    private IReadOnlyDictionary<int, string> _loadoutManifestations = new Dictionary<int, string>();
    private object? _loadoutSession;
    private long _loadoutRevision = -1;
    private long _configuredLoadoutRevision = -1;
    private bool _loadoutCanManage;

    public void ConfigureBuildLoadoutNames(CombatContent content) => _buildLoadouts.SetContentNames(
        content.Skills.Select(skill => (skill.Id, skill.Name)).Concat(content.Mutations.Select(mutation => (mutation.Id, mutation.Name)))
            .Concat(content.Fragments.Select(fragment => (fragment.Id, fragment.Name))).ToDictionary(pair => pair.Id, pair => pair.Name));

    public void ConfigureBuildLoadouts(IReadOnlyList<BuildLoadoutView> loadouts, Func<string, BuildLoadoutPreview> preview,
        IReadOnlyDictionary<string, string> fragments, IReadOnlyDictionary<int, string> manifestations)
    {
        _savedBuildLoadouts = loadouts; _previewBuildLoadout = preview;
        _loadoutFragments = fragments; _loadoutManifestations = manifestations;
        _configuredLoadoutRevision = _revision;
        RefreshBuildLoadouts();
    }

    public void ShowBuildLoadouts()
    {
        _gearLoadout.CancelDrag(); _gearInspecting = false;
        _tab = "Loadouts"; _panel.Visible = true; Rebuild(true);
    }

    public void ReportBuildLoadoutResult(bool success, string reason) => _buildLoadouts.ReportResult(success, reason);

    private void InitializeBuildLoadouts(VBoxContainer column)
    {
        _buildLoadouts = new BuildLoadoutsPanel { Visible = false }; column.AddChild(_buildLoadouts);
        _buildLoadouts.Requested += (action, id, value) => BuildLoadoutRequested?.Invoke(action, id, value);
        _buildLoadouts.CloseRequested += () => { _tab = "Gear"; Rebuild(true); _tabs["Gear"].GrabFocus(); };
        _buildLoadouts.TrainingRequested += () => TrainingRequested?.Invoke();
        _buildLoadouts.MinimumSizeChanged += QueuePanelLayout;
    }

    private void RefreshBuildLoadouts()
    {
        if (_buildLoadouts is null || _state is null || _tab != "Loadouts" || !_panel.Visible || _configuredLoadoutRevision != _revision) return;
        bool canManage = At("service.mara") && _combat.Actors.Any(a => a.Id == 1 && a.Health > 0);
        // Detailed hashing and comparisons are only needed after an authoritative change or service move.
        if (ReferenceEquals(_loadoutSession, _previewBuildLoadout?.Target) && _loadoutRevision == _revision && _loadoutCanManage == canManage) return;
        _loadoutSession = _previewBuildLoadout?.Target; _loadoutRevision = _revision; _loadoutCanManage = canManage;
        _buildLoadouts.SetCurrentAnatomy(_loadoutFragments, _loadoutManifestations);
        _buildLoadouts.SetView(_state, _content, _savedBuildLoadouts, _previewBuildLoadout, canManage, _revision);
    }

    private void AddBuildLoadoutControl()
    {
        if (_previewBuildLoadout is null) return;
        var button = Button("Build loadouts · equipment, anatomy & skills", ShowBuildLoadouts);
        button.Name = "OpenBuildLoadouts";
        button.TooltipText = "Inspect eight complete builds. Visit Mara in Greyhaven to save or switch; previews explain costs and requirements.";
    }
}
