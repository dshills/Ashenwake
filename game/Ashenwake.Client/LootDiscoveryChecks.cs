using Ashenwake.Core.Combat;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Godot;

namespace Ashenwake.Client;

public partial class CombatFeedbackSmoke
{
    private int _legendaryCommands;
    private string _legendaryHash = "";

    private async Task EarnedLegendaryDiscovery()
    {
        string Read(string name) => Godot.FileAccess.GetFileAsString("res://" + name + ".json");
        string combatJson = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
        var hero = ProductionSession.Create(combatJson, AdventureContent.Parse(Read("adventure")), ProgressionContent.Parse(Read("progression")), seed: 20);
        var combat = CombatSession.CreateEncounter(combatJson, 20, "campaign.road", hero.Combat.Capture());
        var recorder = new CombatRecorder(combat);
        var sandbox = new Sandbox { ContentJsonOverride = combatJson, AutomaticStep = false };
        AddChild(sandbox); sandbox.EnableCampaign(); sandbox.SetSession(combat); sandbox.SetPaused(true);
        sandbox.PresentAuthoredRoom(combat.Room, "legendary-discovery", "default");
        sandbox.SetWorldSubtitle("THE ROAD · EARNED LEGENDARY DISCOVERY");
        IReadOnlyList<CombatEvent> last = [];
        for (int tick = 0; tick < 900 && !combat.View.Loot.Any(l => l.Item.DefinitionId == LegendaryEquipment.Pyre); tick++)
        {
            last = recorder.Step(combat, CampaignCombatSmoke.Commands(combat.View, combat.Room));
            sandbox.PresentCombatEvents(last, combat); sandbox.AdoptSession(combat);
            if (tick % 60 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        int discoveries = sandbox.SpecialLootDropCount;
        Check("real_legendary_drop_reaches_shipping_feedback", discoveries >= 1 && sandbox.LastSpecialLootCue == "loot_legendary" &&
            last.Any(e => e.Kind == "LootDropped" && e.ContentId == LegendaryEquipment.Pyre && combat.View.Loot.Any(l => l.Id == e.Amount && l.Item.DefinitionId == e.ContentId && l.Item.Rarity == "Legendary")));
        _legendaryHash = combat.StateHash; _legendaryCommands = recorder.FrameCount;
        sandbox.PresentCombatEvents(last, combat);
        Check("real_duplicate_drop_does_not_replay_discovery", sandbox.SpecialLootDropCount == discoveries && combat.StateHash == _legendaryHash);
        await Capture("earned-legendary-discovery.png");
        var restored = CombatSession.Restore(combatJson, combat.Capture());
        sandbox.SetSession(restored); sandbox.PresentCombatEvents(last, restored);
        Check("real_restored_ground_loot_stays_quiet", sandbox.SpecialLootDropCount == discoveries && restored.StateHash == _legendaryHash);
        sandbox.SetSession(combat);
        var earnedDrop = combat.View.Loot.Single(l => l.Item.DefinitionId == LegendaryEquipment.Pyre);
        int collectedBefore = sandbox.EquipmentCollectionCount;
        for (int tick = 0; tick < 1200 && !combat.View.Inventory.Any(i => i.Id == earnedDrop.Item.Id); tick++)
        {
            CombatCommand[] commands;
            if (combat.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)) commands = CampaignCombatSmoke.Commands(combat.View, combat.Room);
            else
            {
                var player = combat.View.Actors.Single(a => a.Id == 1);
                var direction = CombatProductionSmoke.MovementDirection(player.Position, earnedDrop.Position, combat.Room);
                commands = [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z), new(CombatCommandKind.Pickup, ItemId: earnedDrop.Id)];
            }
            last = recorder.Step(combat, commands); sandbox.PresentCombatEvents(last, combat);
            if (tick % 60 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _legendaryHash = combat.StateHash; _legendaryCommands = recorder.FrameCount;
        Check("earned_equipment_pickup_plays_once", combat.View.Inventory.Any(i => i.Id == earnedDrop.Item.Id) && sandbox.EquipmentCollectionCount == collectedBefore + 1);
        sandbox.PresentCombatEvents(last, combat);
        Check("duplicate_equipment_pickup_stays_quiet", sandbox.EquipmentCollectionCount == collectedBefore + 1 && combat.StateHash == _legendaryHash);
        restored = CombatSession.Restore(combatJson, combat.Capture()); sandbox.SetSession(restored); sandbox.PresentCombatEvents(last, restored);
        Check("restored_inventory_and_old_pickup_stay_quiet", sandbox.EquipmentCollectionCount == collectedBefore + 1 && restored.StateHash == _legendaryHash);
        var replay = recorder.Capture(); var result = CombatReplayRunner.Run(combatJson, replay);
        Check("legendary_discovery_combat_replays_exactly", result.Success && result.FinalHash == _legendaryHash);
        System.IO.File.WriteAllText(Path.Combine(_output, "legendary-drop.replay.json"), JsonData.Write(replay));
    }

    private void CheckLootDiscovery()
    {
        // These detached presentation fixtures do not create gameplay rewards or alter drop tables.
        CombatLoot Drop(long id, string rarity) => new(id, new(0, 0), new(id, rarity == "Godwrought" ? "item.ashcleaver" : "item.echo_ring", "", "MainHand", rarity, 0, 0, 0));
        CombatEvent Event(CombatLoot drop) => new(12, "LootDropped", 1, 2, (int)drop.Id, drop.Item.DefinitionId, 9);
        var legendary = Drop(50, "Legendary"); var godwrought = Drop(51, "Godwrought");
        CombatLoot[] drops = [legendary, godwrought];
        var cues = new LootDropCues();
        Check("new_legendary_and_godwrought_events_have_distinct_cues", cues.Observe(Event(legendary), drops) == legendary &&
            cues.Observe(Event(godwrought), drops) == godwrought && LootDropCues.Cue("Legendary") != LootDropCues.Cue("Godwrought"));
        Check("duplicate_drop_events_are_quiet", cues.Observe(Event(legendary), drops) is null && cues.Observe(Event(godwrought), drops) is null);
        cues.Reset(drops);
        Check("restored_ground_loot_is_a_quiet_baseline", cues.Observe(Event(legendary), drops) is null && cues.Observe(Event(godwrought), drops) is null);
        cues.Reset([]);
        Check("unknown_ids_and_mismatched_definitions_cannot_announce", cues.Observe(Event(legendary) with { Amount = 90 }, drops) is null &&
            cues.Observe(Event(legendary) with { ContentId = "item.ash_axe" }, drops) is null && cues.Observe(Event(legendary) with { Kind = "LootPickedUp" }, drops) is null);
        Check("invalid_event_does_not_consume_real_drop", cues.Observe(Event(legendary), drops) == legendary);
        for (int i = 0; i < 1200; i++)
        {
            var drop = Drop(100 + i, "Legendary"); CombatLoot[] live = [drop];
            cues.Trim(live); cues.Observe(Event(drop), live);
        }
        Check("loot_discovery_receipts_stay_bounded_to_live_drops", cues.RetainedCount == 1);
        cues.Trim([]); Check("collected_drops_leave_no_discovery_receipt", cues.RetainedCount == 0);
        var rare = Drop(2000, "Rare"); var relic = Drop(2001, "Relic"); var common = Drop(2002, "Common"); var tempered = Drop(2003, "Tempered");
        CombatLoot[] tiers = [rare, relic, common, tempered]; cues.Reset([]);
        Check("rare_and_relic_drop_cues_are_distinct", cues.Observe(Event(rare), tiers) == rare && cues.Observe(Event(relic), tiers) == relic &&
            LootDropCues.Cue("Rare") == "drop_rare" && LootDropCues.Cue("Relic") == "drop_relic");
        Check("ordinary_drops_do_not_add_discovery_noise", cues.Observe(Event(common), tiers) is null && cues.Observe(Event(tempered), tiers) is null && LootDropCues.Cue("Common") == "");
        var collection = new LootCollectionCues(); collection.Reset([], 10);
        var pickup = Event(rare) with { Kind = "LootPickedUp" };
        CombatItem[] inventory = [rare.Item];
        Check("pickup_requires_matching_owned_item", collection.Observe(pickup, []) is null && collection.Observe(pickup with { ContentId = "item.unknown" }, inventory) is null && collection.Observe(pickup with { ActorId = 2 }, inventory) is null);
        Check("matching_pickup_acknowledged_once", collection.Observe(pickup, inventory) == rare.Item && collection.Observe(pickup, inventory) is null);
        collection.Reset(inventory, 13);
        Check("loaded_inventory_and_stale_pickup_are_quiet", collection.Observe(pickup, inventory) is null && collection.Observe(pickup with { Tick = 14 }, inventory) is null);
        for (int i = 0; i < 1200; i++)
        {
            var item = Drop(3000 + i, "Rare").Item; collection.Trim([item]);
            collection.Observe(new(20 + i, "LootPickedUp", 1, Amount: (int)item.Id, ContentId: item.DefinitionId), [item]);
        }
        Check("collection_receipts_bounded_to_inventory", collection.RetainedCount == 1);
        var world = new WorldRewardCues();
        EndgameRuntimeResult Result(params string[] events) => new(true, "", [], events);
        Check("secret_cue_waits_for_earned_claim", world.Observe(Result("SecretClueResolved:fixture"), 1) == "" && world.Observe(Result("SecretEntranceRevealed:fixture"), 2) == "" && world.Observe(Result("SecretChamber:Victory"), 3) == "");
        var claim = Result("SecretTreasureClaimed:fixture", "ItemGranted:fixture");
        Check("secret_claim_has_one_distinct_acknowledgement", world.Observe(claim, 4) == "secret_treasure" && world.Observe(claim, 4) == "");
        world.Reset(4);
        Check("loaded_and_rejected_world_rewards_are_quiet", world.Observe(claim, 4) == "" && world.Observe(claim with { Success = false }, 5) == "");
        Check("materials_acknowledge_only_actual_positive_awards", world.Observe(Result("SalvageMaterials:0"), 5) == "" && world.Observe(Result("SalvageMaterials:12"), 6) == "collect_currency" && world.Observe(Result("RegionalHuntRewardClaimed:fixture"), 7) == "collect_currency" && world.Observe(Result("EndgameRewardCommitted:2"), 8) == "collect_currency");
        Check("distinct_successful_claims_each_acknowledged", world.Observe(Result("RoamingChampionRewardClaimed:fixture"), 9) == "collect_equipment" && world.Observe(claim, 10) == "secret_treasure");
        var effects = new CombatEffects(); AddChild(effects);
        try
        {
            foreach (string rarity in new[] { "Legendary", "Godwrought" })
            {
                effects.Emit(LootDropCues.Cue(rarity), Vector3.Zero, Vector3.Forward, LootVisual.RarityColor(rarity), reducedEffects: true);
                Check(rarity + "_discovery_respects_reduced_effects", effects.Count == 0);
            }
        }
        finally { effects.Free(); }
    }

    private async Task LootDiscoveryGallery()
    {
        _heading.Text = "A VOICE IN THE ASH";
        _caption.Text = "Legendary: rising golden sparks · Godwrought: a brief ember crown\nPresentation fixtures show both discovery cues; the road encounter separately verifies an earned Pyrebound drop.";
        var effects = new CombatEffects(); _gallery.AddChild(effects);
        for (int i = 0; i < 2; i++)
        {
            string rarity = i == 0 ? "Legendary" : "Godwrought";
            var item = new CombatItem(i + 1, i == 0 ? "item.echo_ring" : "item.ashcleaver", "", i == 0 ? "Ring1" : "MainHand", rarity, 0, 0, 0);
            var loot = LootVisual.Create(item); loot.Position = new(i == 0 ? 2.5f : -2.5f, 0, 0); _gallery.AddChild(loot);
            effects.Emit(LootDropCues.Cue(rarity), loot.Position, Vector3.Forward, LootVisual.RarityColor(rarity));
        }
        for (int tick = 0; tick < 10; tick++) effects.Advance(1.0 / 60, false, false);
        var cameraPosition = _camera.Position; var cameraSize = _camera.Size;
        _camera.Position = new(0, 5, -7); _camera.Size = 8; _camera.LookAt(new(0, .25f, 0));
        await Capture("special-loot-discovery.png");
        foreach (var child in _gallery.GetChildren()) child.Free();
        _camera.Position = cameraPosition; _camera.Size = cameraSize; _camera.LookAt(new(0, 1.5f, 0));
    }
}
