using Ashenwake.Core.Content;
using System.Diagnostics.CodeAnalysis;

namespace Ashenwake.Core.Adventure;

public sealed record GodwroughtProgress
{
    public const int AwakeningKills = 1000;
    public string InstanceId { get; init; } = "ashcleaver.1";
    public string DefinitionId { get; init; } = "item.ashcleaver";
    public int BurningKills { get; set; }
    public long LastKillSequence { get; set; } = -1;
    public int AttackSpeedStacks { get; set; }
    public int StackTicks { get; set; }
    public string Evolution { get; set; } = "";
    public int TemperLevel { get; set; }
    public static bool HasAwakened(int burningKills) => burningKills >= AwakeningKills;
    public bool Awakened => HasAwakened(BurningKills);
    public bool FlameWaveReady => Awakened && AttackSpeedStacks == 5;
}

public sealed record AdventureState
{
    public string CharacterId { get; init; } = "wanderer";
    public ulong Seed { get; init; } = 1;
    public int Expedition { get; set; } = 1;
    public string RoomId { get; set; } = "room.greyhaven";
    public string AnchorId { get; set; } = "room.greyhaven";
    public int BellPhase { get; set; }
    public int Deaths { get; set; }
    public int Materials { get; set; } = 25;
    public int BellVictories { get; set; }
    public int RewardedExpedition { get; set; }
    public bool QuestAccepted { get; set; }
    public bool ReturnedToMara { get; set; }
    public SortedSet<string> CompletedEncounters { get; set; } = [];
    public SortedSet<string> DestroyedAnchors { get; set; } = [];
    public SortedSet<string> Discoveries { get; set; } = [];
    public SortedSet<string> Journal { get; set; } = [];
    public SortedSet<string> OwnedFragments { get; set; } = ["fragment.eye_vael", "fragment.nerve_ilyra", "fragment.orrun_bone"];
    public SortedDictionary<string, string> Anatomy { get; set; } = new() { ["Eyes"] = "fragment.eye_vael" };
    public SortedDictionary<int, string> Manifestations { get; set; } = [];
    public SortedSet<string> Concordances { get; set; } = [];
    public GodwroughtProgress[] Godwrought { get; set; } = [new()];
}

public sealed record AdventureResult(bool Success, string Reason, string[] Events);
public sealed record AdventureView(string RoomId, string RoomName, string Objective, string[] AvailableExits,
    string? EncounterId, int BellPhase, int Resonance, string[] ActiveManifestations, string NpcReaction,
    int Materials, int Expedition, int Victories, string[] Discoveries);

/// <summary>Logical world state. All mutations validate a private candidate before replacing the live state.</summary>
public sealed partial class AdventureSession
{
    private readonly AdventureContent content;
    private AdventureState state;
    private AdventureDefinition Definitions => content.Data;
    private AdventureSession(AdventureContent content, AdventureState state)
    {
        ValidateState(content, state); this.content = content; this.state = JsonData.Copy(state);
    }
    public static AdventureSession Create(AdventureContent content, ulong seed = 1, string characterId = "wanderer")
    {
        var available = content.Data.Fragments.Select(f => f.Id).Where(id => id != content.Data.RewardFragment).ToArray();
        var first = content.Data.Fragments.FirstOrDefault(f => available.Contains(f.Id));
        var state = new AdventureState
        {
            CharacterId = characterId,
            Seed = seed,
            RoomId = content.Data.Hub,
            AnchorId = content.Data.Hub,
            OwnedFragments = new(available),
            Anatomy = first is null ? [] : new() { [first.Slot] = first.Id },
            Discoveries = [content.Data.Rooms.Single(r => r.Id == content.Data.Hub).Discovery]
        };
        return new(content, state);
    }
    public static AdventureSession Restore(AdventureContent content, AdventureState state) => new(content, state);
    public AdventureState Capture() => JsonData.Copy(state);
    public string StateHash => JsonData.Hash(state);
    public int Resonance => ResonanceOf(state);
    private int ResonanceOf(AdventureState value) => value.Anatomy.Values.Sum(id => Definitions.Fragments.Single(f => f.Id == id).Resonance);
    public AdventureView View
    {
        get
        {
            var room = Definitions.Rooms.Single(r => r.Id == state.RoomId);
            var encounter = room.Encounters.FirstOrDefault(e => !state.CompletedEncounters.Contains(e));
            var exits = room.Exits.Where(id => encounter is null || id == Definitions.Hub || id == state.AnchorId).ToArray();
            string objective = !state.QuestAccepted ? "Speak with Mara in Greyhaven." : state.BellVictories > 0 && !state.ReturnedToMara
                ? "Return to Mara with the Heart of Serath." : state.RoomId == Definitions.Hub
                ? "Enter the ossuary, rebuild, or replay the dungeon." : state.BellPhase == 2 && state.DestroyedAnchors.Count < 2
                ? "Break both ritual anchors, then defeat the resurrected dead." : encounter is null ? "Continue to the next room." : $"Defeat {encounter}.";
            return new(room.Id, room.Name, objective, exits, encounter, state.BellPhase, Resonance,
                state.Manifestations.Where(m => m.Key <= Resonance).Select(m => m.Value).ToArray(),
                Resonance >= 40 ? "Mara: The divine marks are becoming visible. Tell me if the voices change." : "Mara: You are still yourself. That matters.",
                state.Materials, state.Expedition, state.BellVictories, state.Discoveries.ToArray());
        }
    }

    private AdventureResult Change(Func<AdventureState, List<string>, string?> mutate)
    {
        var next = Capture(); var events = new List<string>();
        string? error = mutate(next, events);
        if (error is not null) return new(false, error, []);
        DiscoverConcordances(next, events);
        ValidateState(content, next);
        state = next;
        return new(true, "", events.ToArray());
    }
    private void DiscoverConcordances(AdventureState next, List<string> events)
    {
        var tags = next.Anatomy.Values.SelectMany(id => Definitions.Fragments.Single(f => f.Id == id).Tags).ToHashSet();
        foreach (var concordance in Definitions.Concordances.Where(c => c.Tags.All(tags.Contains)))
            if (next.Concordances.Add(concordance.Id)) events.Add("ConcordanceDiscovered:" + concordance.Id);
    }

    public AdventureResult EnterRoom(string roomId) => Change((next, events) =>
    {
        var current = Definitions.Rooms.Single(r => r.Id == next.RoomId);
        var target = Definitions.Rooms.FirstOrDefault(r => r.Id == roomId);
        if (target is null || !current.Exits.Contains(roomId)) return "Room is not connected to this exit.";
        if (!next.QuestAccepted && roomId != Definitions.Hub) return "Speak with Mara before entering the dungeon.";
        if (current.Encounters.Any(e => !next.CompletedEncounters.Contains(e)) && roomId != Definitions.Hub && roomId != next.AnchorId)
            return "Clear the current encounter before advancing.";
        if (next.RoomId == Definitions.BossRoom && next.BellPhase is > 0 and < 4) ResetBoss(next);
        next.RoomId = roomId;
        if (target.Anchor) next.AnchorId = roomId;
        if (next.Discoveries.Add(target.Discovery)) next.Journal.Add(target.Discovery);
        if (roomId == Definitions.BossRoom && next.BellPhase == 0) next.BellPhase = 1;
        events.Add("RoomEntered:" + roomId); return null;
    });

    public AdventureResult Interact(string actionId) => Change((next, events) =>
    {
        if (actionId is "ritual.anchor_left" or "ritual.anchor_right")
        {
            if (next.RoomId != Definitions.BossRoom || next.BellPhase != 2) return "No active ritual anchor is available.";
            if (!next.DestroyedAnchors.Add(actionId)) return "Ritual anchor already destroyed.";
            events.Add("RitualAnchorDestroyed:" + actionId); return null;
        }
        if (next.RoomId != Definitions.Hub) return "This service is available in Greyhaven.";
        if (actionId == "npc.mara")
        {
            next.QuestAccepted = true; next.Journal.Add("quest.bell_saint");
            if (next.BellVictories > 0) { next.ReturnedToMara = true; next.Journal.Add("quest.bell_saint.returned"); }
            events.Add("Dialogue:" + (next.ReturnedToMara ? "mara.reward_reaction" : "mara.false_history")); return null;
        }
        if (actionId == "dungeon.replay")
        {
            if (next.RewardedExpedition != next.Expedition) return "Finish this expedition before beginning another.";
            if (next.Expedition == int.MaxValue) return "Expedition counter is exhausted.";
            next.Expedition++; next.CompletedEncounters.Clear(); next.DestroyedAnchors.Clear(); next.BellPhase = 0;
            next.AnchorId = Definitions.Hub; events.Add("ExpeditionStarted:" + next.Expedition); return null;
        }
        return "Unknown interaction.";
    });

    public AdventureResult EncounterCompleted(string encounterId) => Change((next, events) =>
    {
        var room = Definitions.Rooms.Single(r => r.Id == next.RoomId);
        var expected = room.Encounters.FirstOrDefault(e => !next.CompletedEncounters.Contains(e));
        if (expected is null || expected != encounterId) return "Encounter is absent, out of order, or already completed.";
        if (next.RoomId == Definitions.BossRoom && next.BellPhase == 2 && next.DestroyedAnchors.Count != 2)
            return "The ritual anchors are sustaining the resurrected dead.";
        next.CompletedEncounters.Add(encounterId); events.Add("EncounterCompleted:" + encounterId);
        if (next.RoomId == Definitions.BossRoom)
        {
            next.BellPhase++;
            if (next.BellPhase == 4)
            {
                if (next.RewardedExpedition == next.Expedition) return "This expedition's reward was already awarded.";
                next.RewardedExpedition = next.Expedition; next.BellVictories++;
                next.Materials = (int)Math.Min(1_000_000L, (long)next.Materials + 15);
                if (next.OwnedFragments.Add(Definitions.RewardFragment)) events.Add("FragmentAwarded:" + Definitions.RewardFragment);
                // Reward uses only seed and expedition, so reset/death cannot reroll a boss reward.
                ulong reward = unchecked(next.Seed ^ (ulong)next.Expedition * 0x9E3779B97F4A7C15UL);
                reward ^= reward >> 30; reward = unchecked(reward * 0xBF58476D1CE4E5B9UL); reward ^= reward >> 27;
                if (reward % 100 < (ulong)Definitions.RelicChancePercent && next.Godwrought.Length < 10000)
                {
                    var id = "ashcleaver.reward." + next.Expedition;
                    next.Godwrought = [.. next.Godwrought, new() { InstanceId = id }]; events.Add("GodwroughtAwarded:" + id);
                }
                next.Journal.Add("quest.bell_saint.defeated"); events.Add("BossDefeated:bell_saint");
            }
            else events.Add("BellSaintPhase:" + next.BellPhase);
        }
        return null;
    });

    public AdventureResult PlayerDied() => Change((next, events) =>
    {
        if (next.RoomId == Definitions.Hub) return "Greyhaven is safe.";
        next.Deaths = checked(next.Deaths + 1);
        // The completed boss and reward survive death; only an unfinished encounter is reset.
        if (next.BellPhase < 4) ResetBoss(next);
        var anchor = Definitions.Rooms.Single(r => r.Id == next.AnchorId);
        foreach (var id in anchor.Encounters) next.CompletedEncounters.Remove(id);
        next.RoomId = next.AnchorId;
        foreach (var item in next.Godwrought) { item.AttackSpeedStacks = 0; item.StackTicks = 0; }
        events.Add("PlayerReturnedToAnchor:" + next.AnchorId); return null;
    });
    private void ResetBoss(AdventureState next)
    {
        foreach (var id in Definitions.Rooms.Single(r => r.Id == Definitions.BossRoom).Encounters) next.CompletedEncounters.Remove(id);
        next.BellPhase = 0; next.DestroyedAnchors.Clear();
    }

    internal AdventureResult ApplyBuildAnatomy(SortedDictionary<string, string> fragments, SortedDictionary<int, string> manifestations)
        => Change((next, events) =>
        {
            if (next.RoomId != Definitions.Hub) return "Change Divine Anatomy in Greyhaven.";
            next.Anatomy = new(fragments); next.Manifestations = new(manifestations);
            events.Add("BuildAnatomyApplied"); return null;
        });
    public AdventureResult InstallFragment(string slot, string? fragmentId) => Change((next, events) =>
        ApplyFragment(next, events, slot, fragmentId, requireHub: true));
    private string? ApplyFragment(AdventureState next, List<string> events, string slot, string? fragmentId, bool requireHub)
    {
        if (requireHub && next.RoomId != Definitions.Hub) return "Change Divine Anatomy in Greyhaven.";
        if (!new[] { "Mind", "Eyes", "Heart", "Spine", "Arms", "Legs" }.Contains(slot)) return "Unknown anatomy slot.";
        if (fragmentId is null) { next.Anatomy.Remove(slot); events.Add("FragmentRemoved:" + slot); return null; }
        var fragment = Definitions.Fragments.FirstOrDefault(f => f.Id == fragmentId);
        if (fragment is null || fragment.Slot != slot || !next.OwnedFragments.Contains(fragmentId)) return "Fragment is unowned or incompatible with this slot.";
        next.Anatomy[slot] = fragmentId; events.Add("FragmentInstalled:" + fragmentId); return null;
    }
    public AdventureResult SelectManifestation(string manifestationId) => Change((next, events) =>
        ApplyManifestation(next, events, manifestationId, requireHub: true));
    private string? ApplyManifestation(AdventureState next, List<string> events, string manifestationId, bool requireHub)
    {
        var definition = Definitions.Manifestations.FirstOrDefault(m => m.Id == manifestationId);
        if ((requireHub && next.RoomId != Definitions.Hub) || definition is null || ResonanceOf(next) < definition.Threshold) return "Manifestation is unavailable.";
        // One reversible choice per threshold. Removing anatomy suppresses it without forgetting the choice.
        next.Manifestations[definition.Threshold] = manifestationId; events.Add("ManifestationSelected:" + manifestationId); return null;
    }
    public AdventureResult RecordBurningKill(long killSequence, string itemInstanceId) => Change((next, events) =>
    {
        var item = next.Godwrought.FirstOrDefault(i => i.InstanceId == itemInstanceId);
        if (item is null || killSequence < 0 || killSequence <= item.LastKillSequence) return "Unknown item or duplicate/out-of-order kill event.";
        item.LastKillSequence = killSequence; item.BurningKills = Math.Min(1000, item.BurningKills + 1);
        item.AttackSpeedStacks = Math.Min(5, item.AttackSpeedStacks + 1); item.StackTicks = 150;
        events.Add(item.BurningKills == 1000 && state.Godwrought.Single(i => i.InstanceId == itemInstanceId).BurningKills < 1000 ? "GodwroughtAwakened:" + itemInstanceId : "BurningKillTracked:" + itemInstanceId);
        return null;
    });
    public AdventureResult ElapseCombatTicks(int ticks) => Change((next, events) =>
    {
        if (ticks < 0 || ticks > 1_000_000) return "Invalid elapsed combat ticks.";
        foreach (var item in next.Godwrought) { item.StackTicks = Math.Max(0, item.StackTicks - ticks); if (item.StackTicks == 0) item.AttackSpeedStacks = 0; }
        return null;
    });
    public AdventureResult Temper(string itemInstanceId) => Change((next, events) =>
    {
        var item = next.Godwrought.FirstOrDefault(i => i.InstanceId == itemInstanceId);
        if (next.RoomId != Definitions.Hub || item is null || item.TemperLevel >= 5) return "Item cannot be tempered here or is at its cap.";
        int cost = 5 * (item.TemperLevel + 1);
        if (next.Materials < cost) return "Insufficient tempering material.";
        next.Materials -= cost; item.TemperLevel++; events.Add("ItemTempered:" + itemInstanceId); return null;
    });
    public AdventureResult Graft(string itemInstanceId, string lineage, bool confirmPermanentChoice) => Change((next, events) =>
    {
        var item = next.Godwrought.FirstOrDefault(i => i.InstanceId == itemInstanceId);
        if (!confirmPermanentChoice) return "Confirm the permanent evolution before grafting.";
        if (next.RoomId != Definitions.Hub || item is null || !item.Awakened || item.Evolution != "" || lineage is not ("Serath" or "Orrun")) return "Item is not eligible for this evolution.";
        string fragment = lineage == "Serath" ? Definitions.RewardFragment : "fragment.orrun_bone";
        if (next.Materials < 20 || !next.OwnedFragments.Contains(fragment)) return "Evolution requires 20 materials and the lineage fragment.";
        next.Materials -= 20; item.Evolution = lineage; events.Add("GodwroughtEvolved:" + lineage); return null;
    });

    public static void ValidateState(AdventureContent content, AdventureState s)
    {
        static void Check([DoesNotReturnIf(false)] bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
        Check(s is not null, "Adventure state is null.");
        var d = content.Data;
        Check(!string.IsNullOrWhiteSpace(s!.CharacterId) && s.CharacterId.Length <= 80 && s.Expedition > 0 && s.Deaths >= 0 && s.Materials >= 0 && s.Materials <= 1_000_000,
            "Invalid identity, expedition, deaths, or materials.");
        Check(s.CompletedEncounters is not null && s.DestroyedAnchors is not null && s.Discoveries is not null && s.Journal is not null && s.OwnedFragments is not null && s.Anatomy is not null && s.Manifestations is not null && s.Concordances is not null && s.Godwrought is not null, "Null adventure state collection.");
        Check(d.Rooms.Any(r => r.Id == s.RoomId) && d.Rooms.Any(r => r.Id == s.AnchorId && r.Anchor), "Unknown room or anchor content; preserve the save for its matching build.");
        var encounterIds = d.Rooms.SelectMany(r => r.Encounters).ToHashSet();
        Check(s.CompletedEncounters!.All(encounterIds.Contains), "Unknown saved encounter.");
        foreach (var room in d.Rooms)
        {
            bool gap = false;
            foreach (var encounter in room.Encounters) { if (!s.CompletedEncounters.Contains(encounter)) gap = true; else Check(!gap, "Encounter phases saved out of order."); }
        }
        Check(s.BellPhase is >= 0 and <= 4 && s.BellVictories >= 0 && s.RewardedExpedition >= 0 && s.RewardedExpedition <= s.Expedition && s.BellVictories == s.RewardedExpedition,
            "Invalid boss/reward progression.");
        Check(s.RewardedExpedition >= s.Expedition - 1, "Expedition skipped its previous reward transaction.");
        Check((s.RoomId != d.BossRoom || s.BellPhase > 0) && (s.BellPhase is not (> 0 and < 4) || s.RoomId == d.BossRoom), "Active boss phase is outside its arena.");
        Check(s.QuestAccepted || (s.RoomId == d.Hub && s.CompletedEncounters.Count == 0 && s.BellVictories == 0), "Adventure progress exists before the opening quest.");
        int completedPhases = d.Rooms.Single(r => r.Id == d.BossRoom).Encounters.Count(s.CompletedEncounters.Contains);
        Check(completedPhases == Math.Max(0, s.BellPhase - 1), "Bell Saint phase does not match completed encounters.");
        Check((s.BellPhase == 4) == (s.RewardedExpedition == s.Expedition), "Boss reward must commit with encounter completion.");
        Check(s.DestroyedAnchors!.All(a => a is "ritual.anchor_left" or "ritual.anchor_right") && (s.BellPhase >= 2 || s.DestroyedAnchors.Count == 0) && (s.BellPhase < 3 || s.DestroyedAnchors.Count == 2), "Invalid ritual anchor state.");
        Check(s.Discoveries!.All(id => d.Rooms.Any(r => r.Discovery == id)) && s.Journal!.All(id => id is "quest.bell_saint" or "quest.bell_saint.defeated" or "quest.bell_saint.returned" || d.Rooms.Any(r => r.Discovery == id)), "Unknown discovery/journal content.");
        Check(s.OwnedFragments!.All(id => d.Fragments.Any(f => f.Id == id)), "Unknown owned fragment content.");
        Check(s.Anatomy!.Count <= 6 && s.Anatomy.All(pair => s.OwnedFragments.Contains(pair.Value) && d.Fragments.Any(f => f.Id == pair.Value && f.Slot == pair.Key)), "Invalid saved anatomy.");
        Check(s.Manifestations!.All(pair => d.Manifestations.Any(m => m.Id == pair.Value && m.Threshold == pair.Key)), "Unknown manifestation content.");
        Check(s.Concordances!.All(id => d.Concordances.Any(c => c.Id == id)), "Unknown Concordance content.");
        Check(s.Godwrought!.Length is > 0 and <= 10000 && s.Godwrought.All(i => i is not null), "Invalid Godwrought inventory.");
        Check(s.Godwrought.Select(i => i.InstanceId).Distinct().Count() == s.Godwrought.Length, "Duplicate Godwrought instance ID.");
        foreach (var item in s.Godwrought)
            Check(!string.IsNullOrWhiteSpace(item.InstanceId) && item.DefinitionId == "item.ashcleaver" && item.BurningKills is >= 0 and <= 1000 && item.LastKillSequence >= -1 && item.AttackSpeedStacks is >= 0 and <= 5 && item.StackTicks is >= 0 and <= 150 && ((item.AttackSpeedStacks == 0) == (item.StackTicks == 0)) && item.TemperLevel is >= 0 and <= 5 && item.Evolution is "" or "Serath" or "Orrun" && (item.Evolution == "" || item.Awakened), "Invalid Godwrought progression.");
        Check(s.BellVictories == 0 || s.OwnedFragments.Contains(d.RewardFragment), "Boss victory is missing its permanent fragment.");
        Check(!s.ReturnedToMara || s.BellVictories > 0, "Cannot return a reward before earning it.");
    }
}
