using Godot;

namespace Ashenwake.Client;

/// <summary>Routes explicit diagnostic entry points inside exported templates that disable command-line scene overrides.</summary>
public partial class Launch : Node
{
    public override void _Ready()
    {
        var arguments = OS.GetCmdlineUserArgs();
        string scene = arguments.Contains("--coop") || arguments.Contains("--coop-smoke") ? "res://Coop.tscn" :
            arguments.Contains("--mouse-movement-smoke") ? "res://MouseMovementSmoke.tscn" :
            arguments.Contains("--spine-smoke") ? "res://SpineSmoke.tscn" :
            arguments.Contains("--cinder-smoke") ? "res://CinderSmoke.tscn" :
            arguments.Contains("--verdant-smoke") ? "res://VerdantSmoke.tscn" :
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
