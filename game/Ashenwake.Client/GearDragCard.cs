using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>A native drag/drop button. The owner supplies inspection, validation and transaction behavior.</summary>
public partial class GearDragCard : Button
{
    public Func<Variant>? DragDataRequested { get; set; }
    public Func<Variant, bool>? CanReceive { get; set; }
    public Action<Variant>? Receive { get; set; }
    public Action<Variant>? DragHover { get; set; }
    public string DragLabel { get; set; } = "";
    private Color _normalModulate;
    private bool _dragHighlighted;
    private GearItemIcon? _icon;
    private string _visualKey = "";

    public void SetItemVisual(string definitionId, EquipmentSlot slot, string discipline, ItemRarity? rarity)
    {
        string key = $"{definitionId}/{slot}/{discipline}/{rarity}";
        if (_visualKey == key) return;
        _visualKey = key;
        if (_icon is null)
        {
            _icon = new GearItemIcon { Name = "GearItemIcon", MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_icon); _icon.AnchorTop = .5f; _icon.AnchorBottom = .5f;
            _icon.OffsetLeft = 5; _icon.OffsetRight = 37; _icon.OffsetTop = -16; _icon.OffsetBottom = 16;
        }
        _icon.Configure(definitionId, slot, discipline, rarity);
        Color color = rarity is null ? new("607580") : LootVisual.RarityColor(rarity.ToString()!);
        AddThemeColorOverride("font_color", rarity is null ? new("92a4ad") : color.Lightened(.15f));
        var normal = new StyleBoxFlat
        {
            BgColor = new Color("14232c").Lerp(color, .06f),
            BorderColor = color.Darkened(.28f),
            BorderWidthLeft = 2,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 41,
            ContentMarginRight = 5,
            ContentMarginTop = 4,
            ContentMarginBottom = 4
        };
        AddThemeStyleboxOverride("normal", normal);
        var hover = (StyleBoxFlat)normal.Duplicate(); hover.BorderColor = color.Lightened(.35f); hover.BgColor = normal.BgColor.Lightened(.08f);
        AddThemeStyleboxOverride("hover", hover);
        var pressed = (StyleBoxFlat)normal.Duplicate(); pressed.BgColor = normal.BgColor.Lightened(.14f);
        AddThemeStyleboxOverride("pressed", pressed);
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!CanUseCard() || DragDataRequested is null) return default;
        Variant data = DragDataRequested();
        if (data.VariantType == Variant.Type.Nil || !CanUseCard()) return default;

        var preview = new PanelContainer
        {
            CustomMinimumSize = new(204, 0),
            Size = new(204, 48),
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None
        };
        preview.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("10232bf5"),
            BorderColor = new("83cdb5"),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        });
        var label = new Label
        {
            Text = DragLabel.Length > 0 ? DragLabel : Text,
            CustomMinimumSize = new(184, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MaxLinesVisible = 4,
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", new Color("e8f1e8"));
        preview.AddChild(label);
        // Godot 4.6 requires an unattached preview and owns its lifetime after this call.
        // Keep no reference: the engine deletes the preview when the drag ends.
        SetDragPreview(preview);
        return data;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!CanUseCard() || data.VariantType == Variant.Type.Nil) return false;
        DragHover?.Invoke(data);
        return CanReceive?.Invoke(data) == true;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        // Validate again at release; ownership or service eligibility may have changed during the drag.
        if (_CanDropData(atPosition, data) && CanUseCard()) Receive?.Invoke(data);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd) RestoreDragColor();
        else if (what == NotificationDragBegin && CanUseCard())
        {
            RestoreDragColor();
            Variant data = GetViewport().GuiGetDragData();
            if (CanReceive?.Invoke(data) != true || !CanUseCard()) return;
            _normalModulate = SelfModulate;
            _dragHighlighted = true;
            SelfModulate = _normalModulate.Lerp(new Color("a6efcd"), .65f);
        }
    }

    public override void _ExitTree() => RestoreDragColor();

    private bool CanUseCard() => GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion() &&
        IsInsideTree() && IsVisibleInTree() && !Disabled;

    private void RestoreDragColor()
    {
        if (!_dragHighlighted) return;
        _dragHighlighted = false;
        if (GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion()) SelfModulate = _normalModulate;
    }
}
