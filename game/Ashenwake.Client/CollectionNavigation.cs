using Godot;

namespace Ashenwake.Client;

public partial class CampaignHud
{
    public event Action? CollectionRequested;
    private Button _collectionTracking = null!;
    public void InspectCollectionRegion(int act) { SelectJourneyRegion(act); OpenTab("Map"); }
    public void SetCollectionTracking(string title, string hint)
    { _collectionTracking.Text = title; _collectionTracking.TooltipText = hint; _collectionTracking.Visible = title.Length > 0; }
    private void BuildCollectionTracking(VBoxContainer column)
    {
        _collectionTracking = new Button { Name = "JourneyTrackedRelic", Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _collectionTracking.AddThemeFontSizeOverride("font_size", 12); _collectionTracking.Pressed += () => CollectionRequested?.Invoke(); column.AddChild(_collectionTracking);
    }
}

public partial class EndgameHud
{
    public event Action? CollectionRequested;
    private Button _collectionTracking = null!;
    public void SetCollectionTracking(string title, string hint)
    { _collectionTracking.Text = title; _collectionTracking.TooltipText = hint; _collectionTracking.Visible = title.Length > 0; }
    private void BuildCollectionTracking(VBoxContainer column)
    {
        _collectionTracking = new Button { Name = "ExpeditionTrackedRelic", Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _collectionTracking.AddThemeFontSizeOverride("font_size", 12); _collectionTracking.Pressed += () => CollectionRequested?.Invoke(); column.AddChild(_collectionTracking);
    }
}
