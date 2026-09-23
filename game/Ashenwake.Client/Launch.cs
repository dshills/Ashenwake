using Godot;

namespace Ashenwake.Client;

/// <summary>Routes explicit diagnostic entry points inside exported templates that disable command-line scene overrides.</summary>
public partial class Launch : Node
{
    public override void _Ready()
    {
        var arguments = OS.GetCmdlineUserArgs();
        string scene = arguments.Contains("--coop") || arguments.Contains("--coop-smoke") ? "res://Coop.tscn" :
            arguments.Contains("--secret-chambers-smoke") ? "res://SecretChambersSmoke.tscn" :
            arguments.Contains("--regional-hunts-smoke") ? "res://RegionalHuntsSmoke.tscn" :
            arguments.Contains("--build-loadouts-smoke") ? "res://BuildLoadoutsSmoke.tscn" :
            arguments.Contains("--legendary-collection-smoke") ? "res://LegendaryCollectionSmoke.tscn" :
            arguments.Contains("--death-recap-smoke") ? "res://DeathRecapSmoke.tscn" :
            arguments.Contains("--loot-management-smoke") ? "res://LootManagementSmoke.tscn" :
            arguments.Contains("--defensive-training-smoke") ? "res://DefensiveTrainingSmoke.tscn" :
            arguments.Contains("--training-smoke") ? "res://TrainingSmoke.tscn" :
            arguments.Contains("--endgame-polish-smoke") ? "res://EndgameCombatPolishSmoke.tscn" :
            arguments.Contains("--late-campaign-combat-smoke") ? "res://LateCampaignCombatSmoke.tscn" :
            arguments.Contains("--midgame-combat-smoke") ? "res://MidgameCombatSmoke.tscn" :
            arguments.Contains("--opening-combat-smoke") ? "res://OpeningCombatSmoke.tscn" :
            arguments.Contains("--opening-audio-smoke") ? "res://OpeningAudioSmoke.tscn" :
            arguments.Contains("--hollow-exploration-smoke") ? "res://HollowExplorationSmoke.tscn" :
            arguments.Contains("--spine-exploration-smoke") ? "res://SpineExplorationSmoke.tscn" :
            arguments.Contains("--cinder-exploration-smoke") ? "res://CinderExplorationSmoke.tscn" :
            arguments.Contains("--verdant-exploration-smoke") ? "res://VerdantExplorationSmoke.tscn" :
            arguments.Contains("--local-map-smoke") ? "res://LocalMapSmoke.tscn" :
            arguments.Contains("--settings-smoke") ? "res://SettingsSmoke.tscn" :
            arguments.Contains("--front-menu-smoke") ? "res://FrontMenuSmoke.tscn" :
            arguments.Contains("--anatomy-smoke") ? "res://AnatomySmoke.tscn" :
            arguments.Contains("--mouse-actions-smoke") ? "res://MouseActionsSmoke.tscn" :
            arguments.Contains("--mouse-movement-smoke") ? "res://MouseMovementSmoke.tscn" :
            arguments.Contains("--hollow-smoke") ? "res://HollowSmoke.tscn" :
            arguments.Contains("--spine-smoke") ? "res://SpineSmoke.tscn" :
            arguments.Contains("--cinder-smoke") ? "res://CinderSmoke.tscn" :
            arguments.Contains("--verdant-smoke") ? "res://VerdantSmoke.tscn" :
            arguments.Contains("--skills-smoke") ? "res://SkillsSmoke.tscn" :
            arguments.Contains("--expedition-smoke") ? "res://ExpeditionSmoke.tscn" :
            arguments.Contains("--hud-smoke") ? "res://HudSmoke.tscn" :
            arguments.Contains("--echoes-screen-smoke") ? "res://EchoesScreenSmoke.tscn" :
            arguments.Contains("--crafting-smoke") ? "res://CraftingSmoke.tscn" :
            arguments.Contains("--appearance-smoke") ? "res://AppearanceSmoke.tscn" :
            arguments.Contains("--combat-feedback-smoke") ? "res://CombatFeedbackSmoke.tscn" :
            arguments.Contains("--visual-smoke") ? "res://VisualSmoke.tscn" :
            arguments.Contains("--release-smoke") ? "res://ReleaseSmoke.tscn" :
            arguments.Contains("--journey-smoke") ? "res://JourneySmoke.tscn" :
            arguments.Contains("--service-interaction-smoke") ? "res://ServiceInteractionSmoke.tscn" :
            arguments.Contains("--interaction-smoke") ? "res://InteractionSmoke.tscn" : "res://Endgame.tscn";
        Callable.From(() =>
        {
            if (GetTree().ChangeSceneToFile(scene) != Error.Ok)
            { GD.PushError("The requested application entry scene could not be loaded."); GetTree().Quit(1); }
        }).CallDeferred();
    }
}
