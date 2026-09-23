using Ashenwake.Core.Combat;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Simulation;
using Ashenwake.Core.Training;

namespace Ashenwake.Core.Progression;

public static partial class OpeningGuidance
{
    /// <summary>Read-only presentation of earned equipment and services; never reveals optional discoveries.</summary>
    public static OpeningGuidanceView Project(EndgameRuntimeSession session, OpeningGuidanceMemory memory)
    {
        Validate(memory);
        var production = session.Production.Capture();
        var character = production.Progression.Character;
        if (character.CharacterId != memory.CharacterId) throw new InvalidDataException("Guidance belongs to another character.");
        var story = session.Campaign.Capture().Campaign;
        var combat = session.Combat.View;
        bool Done(string id) => memory.Completed.Contains(id, StringComparer.Ordinal);
        OpeningGuidanceCard Card(string id, string title, string text, string action, string target, string category, bool complete = false)
            => new(id, title, text, action, target, category, complete);
        OpeningGuidanceCard[] basics =
        [
            Card("hint.move", "Find your footing", "Click open ground to walk, or use {move}. Clicking a destination guides you around nearby obstacles.", "", "", "Basics", Done("hint.move")),
            Card("hint.interact", "Meet the world", "Click a resident or highlighted interaction to approach it. Use {interact} when nearby to interact.", "", "", "Basics", Done("hint.interact")),
            Card("hint.dodge", "Leave the warning", "Move clear of marked attacks. Use {dodge} toward open ground to dodge; scenery can block your escape.", "", "", "Basics", Done("hint.dodge")),
            Card("hint.interrupt", "Stop a dangerous cast", "Use {interrupt} on a nearby enemy before its interruptible cast finishes. If you cannot reach it, leave the marked attack area.", "", "", "Basics", Done("hint.interrupt")),
            Card("hint.loot", "Collect what you earned", "Click a dropped item to approach and collect it, or use {loot} nearby. Inspect your equipment with {inventory}.", "", "", "Basics", Done("hint.loot"))
        ];
        bool pyreOwned = character.Items.Any(i => i.DefinitionId == LegendaryEquipment.Pyre);
        bool pyreEquipped = character.Items.Any(i => i.DefinitionId == LegendaryEquipment.Pyre && character.Equipment.Values.Contains(i.Id));
        bool pyreDropped = combat.Loot.Any(l => l.Item.DefinitionId == LegendaryEquipment.Pyre);
        bool heartOwned = character.OwnedFragments.Contains("fragment.heart_serath");
        bool heartInstalled = production.Expedition.Adventure.Anatomy.GetValueOrDefault("Heart") == "fragment.heart_serath";
        var steps = new List<OpeningGuidanceCard>
        {
            Card("build.pyre", "1 · A trail of fire", pyreEquipped
                ? "Pyrebound Treads are equipped. Dodge into open ground, then draw enemies through the burning trail."
                : pyreOwned ? "Inspect Pyrebound Treads, then visit Torren in Greyhaven. Drag the boots onto the Boots slot; retrieve them from the stash first if stored."
                : pyreDropped ? "Pyrebound Treads are still on the ground. Collect the boots, then inspect their dodge power."
                : "Follow the opening journey and inspect the equipment you find. Your first optional build lesson appears when you collect its reward.",
                pyreOwned ? "inspect_gear" : "journey", pyreOwned ? LegendaryEquipment.Pyre : "", "Build", pyreEquipped),
            Card("build.heart", "2 · Shape your anatomy", heartInstalled
                ? "Heart of Serath is installed. Inspect its power and resonance alongside your equipment before changing your build."
                : heartOwned ? "You own Heart of Serath. Visit Mara in Greyhaven, inspect its power and resonance, then choose whether to install it in Heart."
                : "Complete the Grey March, then inspect the fragment you earn with Mara. This optional upgrade follows your first equipment lesson.",
                heartOwned ? "inspect_anatomy" : "journey", heartOwned ? "fragment.heart_serath" : "", "Build", heartInstalled),
            Card("build.practice", "3 · Try the combination", "Visit the training ground in Greyhaven to practice your earned build. Try a clear dodge, watch the burning trail, and compare your equipped powers. Training does not spend materials or change campaign progress.",
                "training", TrainingSession.InteractionId, "Build", Done("build.practice"))
        };
        var services = new List<OpeningGuidanceCard>();
        foreach (var service in character.Services)
        {
            var (title, description, target) = service switch
            {
                CraftingService.Tempering => ("Torren · tempering", "Improve equipment you want to keep. Inspect the material cost before confirming.", "service.torren"),
                CraftingService.Rebinding => ("Oris · rebinding", "Replace an unwanted affix. Inspect the replacement and material cost before confirming.", "npc.oris"),
                CraftingService.Engraving => ("Engraving workshop", "Apply a learned property to compatible equipment. Inspect the cost and replacement before confirming.", "hub.workshops"),
                CraftingService.Extraction => ("Kesh · extraction", "Learn an item's property. Extraction consumes the source item; review the confirmation carefully.", "npc.kesh"),
                CraftingService.Purification => ("Sister Cael · purification", "Inspect purification options for owned fragments and their costs before committing.", "npc.cael"),
                _ => ("Mara · divine grafting", "Inspect your anatomy and available grafting options. Review every cost and permanent choice before confirming.", "service.mara")
            };
            services.Add(Card("unlock." + service, title, description + " Visit this specialist in Greyhaven.", "service", target, "Services"));
        }
        services.Add(Card("service.training", "Training ground", "Practice your earned build against targets in Greyhaven. Return to your journey with its progress preserved.", "training", TrainingSession.InteractionId, "Services"));
        if (character.CompletedObjectives.Contains("objective.torren"))
            services.Add(Card("service.stash", "Personal stash", "Torren's rescue opened storage beside his workshop in Greyhaven. Store spare equipment; retrieve it before equipping or crafting with it.", "stash", PersonalStashCatalog.InteractionId, "Services"));
        if (session.RegionalHunts.Contracts.Any(c => c.Unlocked))
            services.Add(Card("service.hunts", "Regional hunt board", "A regional hunt is available after your completed act. Inspect its counterplay and reward at Greyhaven's board before accepting a contract.", "hunts", RegionalHuntCatalog.BoardInteraction, "Services"));

        OpeningGuidanceCard? hint = null;
        bool Offered(OpeningGuidanceCard card) => !card.Completed && !memory.Dismissed.Contains(card.Id, StringComparer.Ordinal);
        var player = combat.Actors.FirstOrDefault(a => a.Id == 1);
        bool ordinaryJourney = !session.HasUnresolvedRegionalHunt && !session.InRegionalHunt && !session.InSecretChamber && !session.InRoamingChampion && session.RunView?.Status != "Active";
        if (memory.Enabled && ordinaryJourney && player is { Health: > 0 })
        {
            if (story.HighestActVisited <= 1 && !story.CompletedActs.Contains(1))
            {
                // Urgent contextual lessons take precedence over general controls.
                bool Enemy(CombatActorView actor) => actor.Faction == CombatFaction.Enemy && actor.Health > 0;
                bool supportWarning = combat.CampaignHazards?.Any(h => h.RemainingTicks > 0 && CombatSession.IsSupportHazard(h.ContentId)) == true;
                bool damageWarning = combat.CampaignHazards?.Any(h => h.RemainingTicks > 0 && !CombatSession.IsSupportHazard(h.ContentId)) == true;
                string wanted = combat.Loot.Count > 0 && !combat.Actors.Any(Enemy) ? "hint.loot"
                    : supportWarning || combat.Actors.Any(a => Enemy(a) && a.TelegraphTicks > 0 && a.Role is "Support" or "Bell") ? "hint.interrupt"
                    : damageWarning || combat.Actors.Any(a => Enemy(a) && a.TelegraphTicks > 0) ? "hint.dodge"
                    : !Done("hint.move") ? "hint.move" : "hint.interact";
                hint = basics.FirstOrDefault(c => c.Id == wanted && Offered(c));
                if (hint?.Id == "hint.interact" && !session.Interactions.Any(i =>
                    Position.DistanceSquared(player.Position, i.Position) <= (long)i.Range * i.Range)) hint = null;
            }
            if (hint is null && session.InHub && services.FirstOrDefault(Offered) is { } available)
                hint = available with { Title = "Available · " + available.Title };
        }
        return new(hint, steps.ToArray(), services.ToArray(), basics);
    }
}
