using Godot;

namespace Ashenwake.Client;

/// <summary>Routes explicit diagnostic entry points inside exported templates that disable command-line scene overrides.</summary>
public partial class Launch : Node
{
    public override void _Ready()
    {
        string scene = OS.GetCmdlineUserArgs().Contains("--release-smoke") ? "res://ReleaseSmoke.tscn" : "res://Endgame.tscn";
        Callable.From(() =>
        {
            if (GetTree().ChangeSceneToFile(scene) != Error.Ok)
            { GD.PushError("The requested application entry scene could not be loaded."); GetTree().Quit(1); }
        }).CallDeferred();
    }
}
