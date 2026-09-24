using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private AppearanceWardrobePanel _wardrobe = null!;
    private AppearanceWardrobeMemory? _wardrobeMemory, _wardrobeDraft;
    private string _wardrobePath = "", _wardrobeNotice = "";
    private bool _wardrobeCanWrite;
    private long _wardrobeRevision = -1;

    private void InitializeWardrobe()
    {
        _wardrobe = new AppearanceWardrobePanel(); _sandbox.AddOverlay(_wardrobe);
        _character.WardrobeRequested += OpenWardrobe;
        _wardrobe.VisibilityChangedByPlayer += open => { if (!open) _wardrobeDraft = null; };
        _wardrobe.SelectRequested += (slot, id) => EditWardrobe(d => AppearanceWardrobe.Select(d, slot, id, _productionDefinition));
        _wardrobe.HelmetHiddenRequested += hidden => EditWardrobe(d => AppearanceWardrobe.SetHelmetHidden(d, hidden));
        _wardrobe.ResetRequested += () => EditWardrobe(AppearanceWardrobe.Reset);
        _wardrobe.SaveLookRequested += name => EditWardrobe(d => AppearanceWardrobe.SaveLook(d, name, _productionDefinition));
        _wardrobe.ApplyLookRequested += name => EditWardrobe(d => AppearanceWardrobe.ApplyLook(d, name, _productionDefinition));
        _wardrobe.DeleteLookRequested += name => EditWardrobe(d => AppearanceWardrobe.DeleteLook(d, name));
        _wardrobe.ApplyRequested += ApplyWardrobe;
        _wardrobe.MenuRequested += action =>
        {
            _wardrobe.SetOpen(false);
            if (action == "aw_inventory") _character.ToggleInventory();
            else if (action == "aw_character") _character.Toggle();
            else if (action == "aw_experiment") ShowExperimentPanel();
            else if (action is "aw_endgame" or "aw_journey")
            {
                if (_session.InWorldEncounter) OpenWorldEncounters();
                else if (_session.InRoamingChampion) OpenRoamingChampions();
                else if (_session.InSecretChamber) OpenSecretChambers();
                else if (_session.HasUnresolvedRegionalHunt) OpenRegionalHunts();
                else if (action == "aw_endgame" || _session.Combat.View.Endgame is not null) _board.ShowRun();
                else { _campaignHud.Visible = true; _campaignHud.SetOpen(true); }
            }
        };
    }

    private void OpenWardrobe()
    {
        if (!_hasActiveCharacter || _training is not null || _frontMenu.IsOpen || _deathRecapHud?.IsOpen == true) return;
        if (_session.Combat.View.Actors.All(a => a.Id != 1 || a.Health <= 0)) return;
        _wardrobeRevision = -1; RefreshWardrobe(_session.Capture());
        if (_wardrobeMemory is null) return;
        if (_wardrobeCanWrite)
        {
            var loaded = AppearanceWardrobeStore.Load(_wardrobePath, _wardrobeMemory.CharacterId, _productionDefinition);
            var retained = loaded.Memory.Unlocks.Concat(_wardrobeMemory.Unlocks).GroupBy(u => u.ItemId, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(u => u.Rarity).First()).OrderBy(u => u.ItemId, StringComparer.Ordinal).ToArray();
            _wardrobeMemory = loaded.Memory with { Unlocks = retained }; _wardrobeCanWrite = loaded.CanWrite; _wardrobeNotice = loaded.Notice;
            _sandbox.SetWardrobeAppearance(_wardrobeMemory);
        }
        _bestiary?.SetOpen(false); _collection.SetOpen(false); _huntBoard.SetOpen(false); _secretPanel.SetOpen(false); _stashPanel.SetOpen(false);
        _championPanel.SetOpen(false); _worldEncounterPanel.SetOpen(false); _openingGuide.SetOpen(false);
        _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); CloseExperimentPanel();
        _wardrobeDraft = JsonData.Copy(_wardrobeMemory);
        PresentWardrobe(); _wardrobe.SetOpen(true);
    }

    private void RefreshWardrobe(EndgameRuntimeSnapshot snapshot)
    {
        if (_wardrobe is null || !_hasActiveCharacter) return;
        var character = snapshot.Campaign.Production.Progression.Character;
        string path = Path.Combine(_output, ActiveCharacterFilename);
        if (_wardrobeMemory is null || _wardrobePath != path || _wardrobeMemory.CharacterId != character.CharacterId)
        {
            _wardrobe.SetOpen(false);
            var loaded = AppearanceWardrobeStore.Load(path, character.CharacterId, _productionDefinition);
            _wardrobePath = path; _wardrobeMemory = loaded.Memory; _wardrobeCanWrite = loaded.CanWrite;
            _wardrobeNotice = loaded.Notice; _wardrobeRevision = -1;
        }
        if (_wardrobeRevision != _revision)
        {
            var observed = AppearanceWardrobe.Observe(_wardrobeMemory, character, _productionDefinition);
            bool changed = !observed.Unlocks.SequenceEqual(_wardrobeMemory.Unlocks);
            _wardrobeMemory = observed;
            if (changed && _wardrobeCanWrite)
            {
                try { _wardrobeMemory = AppearanceWardrobeStore.Write(_wardrobePath, observed, _productionDefinition); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                { _wardrobeNotice = "New appearances remain available this session. Existing wardrobe files were preserved: " + ex.Message; }
            }
            _wardrobeRevision = _revision;
        }
        _sandbox.SetWardrobeAppearance(_wardrobeMemory);
        if (_wardrobe.IsOpen) PresentWardrobe();
    }

    private void EditWardrobe(Func<AppearanceWardrobeMemory, AppearanceWardrobeMemory> edit)
    {
        if (_wardrobeDraft is null) return;
        try { _wardrobeDraft = edit(_wardrobeDraft); _wardrobeNotice = "Preview only. Apply look to save these changes, including named looks."; }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
        { _wardrobeNotice = ex.Message; }
        PresentWardrobe();
    }

    private void ApplyWardrobe()
    {
        if (_wardrobeDraft is null) return;
        try
        {
            AppearanceWardrobe.Validate(_wardrobeDraft, _productionDefinition);
            var applied = _wardrobeCanWrite ? AppearanceWardrobeStore.Write(_wardrobePath, _wardrobeDraft, _productionDefinition) : JsonData.Copy(_wardrobeDraft);
            _wardrobeMemory = applied; _wardrobeDraft = JsonData.Copy(applied);
            _wardrobeNotice = _wardrobeCanWrite ? "Look applied and saved. Equipped items, stats and set bonuses are unchanged."
                : "Look applied for this session only. Existing wardrobe files are protected; this look will not survive reloading.";
            _sandbox.SetWardrobeAppearance(applied); _collectionRevision = -1;
            Refresh(); PresentWardrobe();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _wardrobeNotice = "Look was not applied. Close and reopen the wardrobe to refresh its saved choices, then try again. " + ex.Message;
            // Keep the current applied appearance and the draft intact; never publish an unsuccessful write.
            PresentWardrobe();
        }
    }

    private void PresentWardrobe()
    {
        var memory = _wardrobeDraft ?? _wardrobeMemory;
        if (memory is null) return;
        var snapshot = _session.Capture().Campaign.Production;
        var equipped = CharacterAppearance.FromProgression(snapshot.Progression, _session.Production.View.ActiveManifestations,
            snapshot.Expedition.Adventure.Anatomy.Values);
        _wardrobe.SetView(new(memory, _productionDefinition, equipped, _wardrobeNotice));
    }

    private void ResetWardrobe()
    {
        _wardrobe?.SetOpen(false); _bestiary?.SetOpen(false); _wardrobeMemory = _wardrobeDraft = null; _wardrobePath = "";
        _wardrobeNotice = ""; _wardrobeRevision = -1; _sandbox.SetWardrobeAppearance(null);
    }
}
