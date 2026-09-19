using Godot;

namespace Ashenwake.Client;

public partial class CraftingWorkbench
{
    public event Action? CloseRequested;

    private static readonly string[] CraftMenuActions = ["aw_inventory", "aw_character", "aw_journey", "aw_endgame", "aw_experiment", "aw_save", "aw_load"];
    private static readonly string[] CraftCloseBeforeMenuActions = ["aw_journey", "aw_endgame", "aw_experiment"];
    private static readonly string[] CraftNavigationActions = ["ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev"];

    public override void _Input(InputEvent input)
    {
        if (!HandlesCraftInput()) return;
        if (input.IsActionPressed("ui_cancel"))
        {
            CloseRequested?.Invoke(); GetViewport().SetInputAsHandled(); return;
        }
        // Search owns its text shortcuts, including letters that also open game menus.
        // Any key the field declines is gated again below, after GUI dispatch.
        if (input is InputEventKey && GetViewport().GuiGetFocusOwner() is LineEdit) return;
        if (CraftCloseBeforeMenuActions.Any(action => InputMap.HasAction(action) && input.IsActionPressed(action)))
        {
            CloseRequested?.Invoke(); return;
        }
        if (input is InputEventKey or InputEventJoypadButton &&
            !CraftMenuActions.Concat(CraftNavigationActions).Any(action => InputMap.HasAction(action) && input.IsAction(action)))
            GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!HandlesCraftInput()) return;
        if (input.IsActionPressed("ui_cancel"))
        {
            CloseRequested?.Invoke(); GetViewport().SetInputAsHandled(); return;
        }
        if (CraftCloseBeforeMenuActions.Any(action => InputMap.HasAction(action) && input.IsActionPressed(action)))
        {
            CloseRequested?.Invoke(); return;
        }
        // Navigation that no GUI control accepted must not become a skill, interaction or single step.
        if (input is InputEventKey or InputEventJoypadButton &&
            !CraftMenuActions.Any(action => InputMap.HasAction(action) && input.IsAction(action)))
            GetViewport().SetInputAsHandled();
    }

    private bool HandlesCraftInput() => IsInsideTree() && IsVisibleInTree() &&
        _confirmation is not { Visible: true } && !GetViewport().GuiIsDragging();
}
