using Godot;

namespace Ashenwake.Client;

/// <summary>Read-only layout assertions for the native death-recap diagnostic.</summary>
public static class DeathRecapClientChecks
{
    public static void Validate(DeathRecapHud hud, Action<string, bool> check)
    {
        check("death.recap.visible", hud.IsOpen);
        if (!hud.IsOpen) return;
        var panel = hud.FindChild("DeathRecapPanel", true, false) as Control;
        check("death.recap.viewport", panel is not null && Inside(panel.GetGlobalRect(), hud.GetViewportRect()));
        if (panel is null) return;
        foreach (string name in new[] { "DeathRecapTitle", "DeathRecapScroll", "DeathRecapPrimary", "DeathRecapClose" })
        {
            var control = hud.FindChild(name, true, false) as Control;
            check("death.recap.control." + name, control?.IsVisibleInTree() == true && Inside(control.GetGlobalRect(), panel.GetGlobalRect()));
        }
        var hub = hud.FindChild("DeathRecapHub", true, false) as Button;
        check("death.recap.hub.bounds", hub is not null && (!hub.Visible || Inside(hub.GetGlobalRect(), panel.GetGlobalRect())));
        var scroll = hud.FindChild("DeathRecapScroll", true, false) as ScrollContainer;
        check("death.recap.details.scroll", scroll is { HorizontalScrollMode: ScrollContainer.ScrollMode.Disabled } && scroll.Size.Y >= 160);
        foreach (string name in new[] { "DeathRecapKillingBlow", "DeathRecapRecentDamage", "DeathRecapConditions", "DeathRecapCounterplay", "DeathRecapRecovery" })
        {
            var label = hud.FindChild(name, true, false) as Label;
            check("death.recap.section." + name, label is not null && !string.IsNullOrWhiteSpace(label.Text) && label.AutowrapMode == TextServer.AutowrapMode.WordSmart);
        }
        var actions = new[] { "DeathRecapPrimary", "DeathRecapHub", "DeathRecapClose" }
            .Select(name => hud.FindChild(name, true, false)).OfType<Control>().Where(control => control.IsVisibleInTree()).ToArray();
        check("death.recap.actions.distinct", actions.Length >= 2 && actions.SelectMany((a, index) => actions.Skip(index + 1)
            .Select(b => !a.GetGlobalRect().Intersects(b.GetGlobalRect()))).All(value => value));
    }

    private static bool Inside(Rect2 inner, Rect2 outer) => inner.Size.X > 0 && inner.Size.Y > 0 &&
        inner.Position.X >= outer.Position.X - 1 && inner.Position.Y >= outer.Position.Y - 1 &&
        inner.End.X <= outer.End.X + 1 && inner.End.Y <= outer.End.Y + 1;
}
