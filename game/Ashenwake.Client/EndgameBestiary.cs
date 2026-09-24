using System.Security.Cryptography;
using System.Text;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private BestiaryPanel _bestiary = null!;
    private BestiaryCatalog _bestiaryCatalog = null!;
    private BestiaryMemory? _bestiaryMemory;
    private CombatView? _bestiaryBefore;
    private string _bestiaryBeforeEncounter = "", _bestiaryPath = "", _bestiaryNotice = "";
    private bool _bestiaryCanWrite, _bestiaryDirty;
    private readonly HashSet<string> _bestiaryKnown = new(StringComparer.Ordinal);

    private void InitializeBestiary()
    {
        _bestiaryCatalog = BestiaryCatalog.Build(_combat, _campaignDefinition, _endgameDefinition);
        _bestiary = new BestiaryPanel(); _sandbox.AddOverlay(_bestiary);
        _campaignHud.BestiaryRequested += OpenBestiary; _board.BestiaryRequested += OpenBestiary;
        _bestiary.SourceRequested += (itemId, kind) =>
        {
            if (_bestiaryMemory is null) return;
            bool earnedKnowledge = Bestiary.Project(_bestiaryMemory, _bestiaryCatalog).Any(e => e.Defeated && e.RewardItemIds.Contains(itemId));
            if (!earnedKnowledge || !KnownBestiarySources(itemId).Any(s => s.Kind == kind)) return;
            _bestiary.SetOpen(false); OpenCollectionSource(itemId, kind);
        };
        _bestiary.MenuRequested += action =>
        {
            _bestiary.SetOpen(false);
            if (action == "aw_inventory") _character.ToggleInventory();
            else if (action == "aw_character") _character.Toggle();
            else if (action == "aw_experiment") ShowExperimentPanel();
            else if (_session.InWorldEncounter) OpenWorldEncounters();
            else if (_session.InRoamingChampion) OpenRoamingChampions();
            else if (_session.InSecretChamber) OpenSecretChambers();
            else if (_session.HasUnresolvedRegionalHunt) OpenRegionalHunts();
            else if (action == "aw_endgame" || _session.Combat.View.Endgame is not null) _board.ShowRun();
            else { _campaignHud.Visible = true; _campaignHud.SetOpen(true); }
        };
    }

    private void OpenBestiary()
    {
        if (!_hasActiveCharacter || _training is not null || _frontMenu.IsOpen || _deathRecapHud?.IsOpen == true) return;
        RefreshBestiary(); PersistBestiary();
        _collection.SetOpen(false); _wardrobe.SetOpen(false); _huntBoard.SetOpen(false); _secretPanel.SetOpen(false);
        _stashPanel.SetOpen(false); _championPanel.SetOpen(false); _worldEncounterPanel.SetOpen(false); _openingGuide.SetOpen(false);
        _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); CloseExperimentPanel();
        PresentBestiary(); _bestiary.SetOpen(true);
    }

    private void BeginBestiaryObservation()
    {
        _bestiaryBefore = _bestiaryMemory is not null && _hasActiveCharacter && _training is null && !_session.InHub ? _session.Combat.View : null;
        _bestiaryBeforeEncounter = _bestiaryBefore is null ? "" : _session.Combat.EncounterId;
    }

    private void ObserveBestiary(EndgameRuntimeResult result)
    {
        var before = _bestiaryBefore; string encounter = _bestiaryBeforeEncounter; _bestiaryBefore = null;
        if (!result.Success || _bestiaryMemory is null || !_hasActiveCharacter || _training is not null) return;
        var deaths = result.CombatEvents.Where(e => e.Kind == "EntityKilled").ToArray();
        if (deaths.Length == 0) return;
        var after = _session.Combat.View;
        var records = new List<BestiaryDefeat>(); string hash = _session.StateHash;
        foreach (var death in deaths)
        {
            var actor = before?.Actors.FirstOrDefault(a => a.Id == death.TargetId) ?? after.Actors.FirstOrDefault(a => a.Id == death.TargetId);
            if (actor is null || actor.Faction != CombatFaction.Enemy) continue;
            string sourceEncounter = before?.Actors.Any(a => a.Id == actor.Id) == true ? encounter : _session.Combat.EncounterId;
            var entry = _bestiaryCatalog.Resolve(actor.DefinitionId, sourceEncounter);
            if (entry is null) continue;
            string token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash + ":" + actor.Id)));
            records.Add(new(token, entry.Id, actor.EliteModifiers?.Count > 0 || actor.Role.Contains("elite", StringComparison.OrdinalIgnoreCase)));
        }
        if (records.Count > 0) UpdateBestiary([], records);
    }

    private void RefreshBestiary()
    {
        if (_bestiary is null || !_hasActiveCharacter || _training is not null) return;
        string path = Path.Combine(_output, ActiveCharacterFilename);
        if (_bestiaryMemory is null || path != _bestiaryPath)
        {
            var loaded = BestiaryStore.Load(path, _session.Production.Capture().Progression.Character.CharacterId, _bestiaryCatalog);
            _bestiaryMemory = loaded.Memory; _bestiaryPath = path; _bestiaryCanWrite = loaded.CanWrite; _bestiaryNotice = loaded.Notice; _bestiaryDirty = false;
            _bestiaryKnown.Clear(); _bestiaryKnown.UnionWith(_bestiaryMemory.DiscoveredEntries);
        }
        if (!_session.InHub)
        {
            var seen = _session.Combat.View.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Visible && a.Health > 0)
                .Select(a => _bestiaryCatalog.Resolve(a.DefinitionId, _session.Combat.EncounterId)?.Id)
                .Where(id => id is not null && !_bestiaryKnown.Contains(id)).Select(id => id!).Distinct(StringComparer.Ordinal).ToArray();
            if (seen.Length > 0) UpdateBestiary(seen, []);
        }
    }

    private void UpdateBestiary(IEnumerable<string> seen, IEnumerable<BestiaryDefeat> defeats)
    {
        if (_bestiaryMemory is null) return;
        var observed = Bestiary.Observe(_bestiaryMemory, _bestiaryCatalog, seen, defeats);
        bool changed = !observed.DiscoveredEntries.SequenceEqual(_bestiaryMemory.DiscoveredEntries) ||
            observed.Defeats.Length != _bestiaryMemory.Defeats.Length || observed.RecordLimitReached != _bestiaryMemory.RecordLimitReached;
        _bestiaryMemory = observed; _bestiaryKnown.UnionWith(observed.DiscoveredEntries);
        if (changed) { _bestiaryDirty = true; PersistBestiary(); }
    }

    private void PersistBestiary()
    {
        if (!_bestiaryDirty || !_bestiaryCanWrite || _bestiaryMemory is null) return;
        try
        {
            _bestiaryMemory = BestiaryStore.Write(_bestiaryPath, _bestiaryMemory, _bestiaryCatalog);
            _bestiaryKnown.UnionWith(_bestiaryMemory.DiscoveredEntries); _bestiaryDirty = false; _bestiaryNotice = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { _bestiaryCanWrite = false; _bestiaryNotice = "Discoveries remain available this session. Existing bestiary files were preserved: " + ex.Message; }
    }

    private LegendaryCollectionSourceView[] KnownBestiarySources(string itemId) => LegendaryCollectionSources.For(_session, itemId).Where(s => s.Kind switch
    {
        LegendaryCollectionSourceKind.Campaign => s.EncounterId.Length > 0,
        LegendaryCollectionSourceKind.SecretChamber => s.ChamberId.Length > 0,
        LegendaryCollectionSourceKind.RoamingChampion => s.ChampionId.Length > 0,
        LegendaryCollectionSourceKind.GodHunt => s.HuntId.Length > 0,
        LegendaryCollectionSourceKind.Fracture => _session.View.Unlocked && s.RegionId.Length > 0,
        _ => false
    }).ToArray();

    private void PresentBestiary()
    {
        if (_bestiaryMemory is null) return;
        var entries = Bestiary.Project(_bestiaryMemory, _bestiaryCatalog).Select(e => new BestiaryEntryDisplay(e.Id, e.Name, e.Kind.ToString(),
            string.Join(" · ", e.Regions), e.Lore, e.Discovered ? e.Kind == BestiaryKind.Champion ? e.Id : e.EnemyId : "", e.Role,
            e.Discovered, (int)e.Defeats, (int)e.EliteDefeats, e.Defeated ? e.CombatNotes.Concat(new[]
            { $"Base physical damage reduction: {e.ArmorBasisPoints / 100m:0.##}%. Base nonphysical resistance: {e.ResistanceBasisPoints / 100m:0.##}%." }).ToArray() : [],
            e.Defeated ? e.RewardItemIds.SelectMany(id => KnownBestiarySources(id).Select(source => new BestiaryRewardDisplay(id,
                EquipmentNames.For(id), source.Label + " · " + source.Requirement, source.Kind))).ToArray() : [])).ToArray();
        string notice = _bestiaryNotice;
        if (_bestiaryMemory.RecordLimitReached) notice += " Defeat record capacity reached; new creature discoveries are still recorded.";
        _bestiary.SetView(new(entries, notice));
    }

    private void ResetBestiary()
    {
        _bestiary?.SetOpen(false); _bestiary?.ResetSelection(); _bestiaryMemory = null; _bestiaryBefore = null;
        _bestiaryPath = _bestiaryNotice = ""; _bestiaryKnown.Clear(); _bestiaryDirty = false;
    }
}
