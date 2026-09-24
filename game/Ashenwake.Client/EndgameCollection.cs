using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private LegendaryCollectionPanel _collection = null!;
    private LegendaryCollectionMemory? _collectionMemory;
    private string _collectionPath = "", _collectionNotice = "";
    private bool _collectionCanWrite, _collectionDirty;
    private long _collectionRevision = -1;

    private void InitializeCollection()
    {
        _collection = new LegendaryCollectionPanel(); _sandbox.AddOverlay(_collection);
        _campaignHud.CollectionRequested += OpenCollection; _board.CollectionRequested += OpenCollection;
        _collection.VisibilityChangedByPlayer += open =>
        {
            if (open) { _wardrobe?.SetOpen(false); _bestiary?.SetOpen(false); _pets?.SetOpen(false); _huntBoard?.SetOpen(false); _secretPanel?.SetOpen(false); _stashPanel?.SetOpen(false); _championPanel?.SetOpen(false); _worldEncounterPanel?.SetOpen(false); _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); CloseExperimentPanel(); }
        };
        _collection.TrackRequested += id =>
        {
            if (_collectionMemory is null) return;
            _collectionMemory = LegendaryCollection.Track(_collectionMemory, id); _collectionDirty = true;
            PersistCollection(); _collectionRevision = -1; RefreshCollection(_session.Capture());
        };
        _collection.SourceRequested += OpenCollectionSource;
        _collection.MenuRequested += action =>
        {
            // Explicit transfer prevents H from becoming the in-world Bind Memory action after unpausing.
            if (action == "aw_experiment") { ShowExperimentPanel(); return; }
            _collection.SetOpen(false); _wardrobe?.SetOpen(false); _bestiary?.SetOpen(false); _pets?.SetOpen(false);
            if (action == "aw_inventory") _character.ToggleInventory();
            else if (action == "aw_character") _character.Toggle();
            else if (_session.InRoamingChampion) OpenRoamingChampions();
            else if (action == "aw_endgame" || _session.Combat.View.Endgame is not null) _board.ShowRun();
            else { _campaignHud.Visible = true; _campaignHud.SetOpen(true); }
        };
    }

    private void OpenCollection()
    {
        if (!_hasActiveCharacter || _training is not null || _frontMenu.IsOpen || _deathRecapHud?.IsOpen == true) return;
        _collectionRevision = -1; RefreshCollection(_session.Capture()); _collection.SetOpen(true);
    }

    private void RefreshCollection(EndgameRuntimeSnapshot snapshot, bool persist = false)
    {
        if (_collection is null || !_hasActiveCharacter) return;
        string path = Path.Combine(_output, ActiveCharacterFilename);
        var character = snapshot.Campaign.Production.Progression.Character;
        if (_collectionMemory is null || _collectionPath != path || _collectionMemory.CharacterId != character.CharacterId)
        {
            var loaded = LegendaryCollectionStore.Load(path, character.CharacterId);
            _collectionPath = path; _collectionMemory = loaded.Memory; _collectionCanWrite = loaded.CanWrite;
            _collectionNotice = loaded.Notice; _collectionDirty = false; _collectionRevision = -1;
        }
        if (_collectionRevision == _revision && !persist) return;
        var observed = LegendaryCollection.Observe(_collectionMemory, character);
        if (!observed.DiscoveredItems.SequenceEqual(_collectionMemory.DiscoveredItems)) { _collectionMemory = observed; _collectionDirty = true; }
        if (persist) PersistCollection();
        _collectionRevision = _revision;
        var campaign = snapshot.Campaign.Campaign; var campaignView = _session.Campaign.View; var endgame = _session.View;
        var cards = LegendaryCollectionCatalog.Entries.Where(entry => _collectionMemory.DiscoveredItems.Contains(entry.ItemId) ||
            (entry.SecretChamberId.Length == 0 || _session.SecretChambers.Entries.Any(e => e.Id == entry.SecretChamberId && e.Revealed)) &&
            (entry.RoamingChampionId.Length == 0 || _session.RoamingChampions.Entries.Any(e => e.Id == entry.RoamingChampionId && e.Discovered))).Select(entry => new LegendaryCollectionCard(entry,
            _collectionMemory.DiscoveredItems.Contains(entry.ItemId), character.Items.Count(i => i.DefinitionId == entry.ItemId),
            entry.PowerId.Length > 0 && character.PropertyLibrary.Contains(entry.PowerId), LegendaryCollectionSources.Project(_campaignDefinition, campaign, campaignView,
                _session.Campaign.ActiveEncounterId, _endgameDefinition, endgame, entry.ItemId, _session.CurrentRunContentId, _session.SecretChambers, _session.RoamingChampions))).ToArray();
        var tracked = cards.FirstOrDefault(c => c.Entry.ItemId == _collectionMemory.TrackedItem);
        string title = tracked is null ? "" : "TRACKED · " + EquipmentNames.For(tracked.Entry.ItemId);
        var best = tracked?.Sources.FirstOrDefault(s => s.State is LegendaryCollectionSourceState.Active or LegendaryCollectionSourceState.Available)
            ?? tracked?.Sources.FirstOrDefault(s => s.State != LegendaryCollectionSourceState.Completed);
        string hint = best is null ? "" : best.Label + " · " + best.Requirement;
        string target = title + (best is null ? "" : "\n" + best.Label);
        _campaignHud.SetCollectionTracking(target, hint); _board.SetCollectionTracking(target, hint);
        _collection.SetView(new(cards, _collectionMemory.TrackedItem,
            WardrobeAppearance.Project(CharacterAppearance.FromProgression(snapshot.Campaign.Production.Progression, _session.Production.View.ActiveManifestations,
                snapshot.Campaign.Production.Expedition.Adventure.Anatomy.Values), _wardrobeMemory), _collectionNotice,
            EquipmentSets.Catalog.ToDictionary(set => set.Id, set => EquipmentSets.CountEquipped(set.Id, character))));
    }

    private void PersistCollection()
    {
        if (!_collectionDirty || _collectionMemory is null || !_collectionCanWrite) return;
        try
        {
            _collectionMemory = LegendaryCollectionStore.Write(_collectionPath, _collectionMemory);
            _collectionDirty = false; _collectionNotice = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { _collectionCanWrite = false; _collectionNotice = "Collection changes remain available this session. Existing journal files are preserved: " + ex.Message; }
    }

    private void ResetCollection()
    {
        _collection?.ResetSelection(); _collectionMemory = null; _collectionPath = ""; _collectionRevision = -1; _collectionDirty = false;
    }

    private void OpenCollectionSource(string itemId, LegendaryCollectionSourceKind kind)
    {
        // Resolve again from live state. Inspection never issues a travel, spend or hunt command.
        var source = LegendaryCollectionSources.For(_session, itemId).FirstOrDefault(s => s.Kind == kind);
        if (source is null || kind == LegendaryCollectionSourceKind.GodHunt && source.HuntId.Length == 0) return;
        _collection.SetOpen(false); _wardrobe?.SetOpen(false); _bestiary?.SetOpen(false); _pets?.SetOpen(false);
        if (kind == LegendaryCollectionSourceKind.RoamingChampion) { if (source.ChampionId.Length > 0) OpenRoamingChampions(source.ChampionId); }
        else if (kind == LegendaryCollectionSourceKind.SecretChamber) { if (source.ChamberId.Length > 0) OpenSecretChambers(source.ChamberId); }
        else if (kind == LegendaryCollectionSourceKind.Campaign)
        {
            if (_session.Combat.View.Endgame is not null)
            { _board.ShowRun(); _board.Notice("Finish or leave this expedition to return to the campaign route."); return; }
            _board.SetOpen(false); _campaignHud.Visible = true; _campaignHud.InspectCollectionRegion(source.Act);
        }
        else if (kind == LegendaryCollectionSourceKind.Fracture)
        {
            if (_session.View.Run?.Status == "Active") _board.ShowRun();
            else { _board.ShowTab("Sigils"); if (source.SigilId > 0) _board.SelectSigil(source.SigilId); }
            _board.Notice(source.Requirement);
        }
        else { _board.ShowTab("Hunts"); _board.SelectHunt(source.HuntId); _board.Notice(source.Requirement); }
    }
}
