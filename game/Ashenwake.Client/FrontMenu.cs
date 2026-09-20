using Godot;
using System.Globalization;

namespace Ashenwake.Client;

/// <summary>Presentation-only front door. The director owns character creation, saves and loading.</summary>
public partial class FrontMenu : Control
{
    public event Action<string>? CreateRequested, LoadRequested;
    public event Action? SettingsRequested, QuitRequested, ResumeRequested, ImportRequested;
    public event Action<bool>? OpenChanged;
    public PanelContainer Panel { get; private set; } = null!;
    public bool IsOpen => Panel is { Visible: true };
    public string Page => _page;
    public bool CatalogLoading { get; private set; }

    private static readonly Color Bone = new("eee1c8"), Mint = new("add7ca"), Gold = new("e8c286"), Blue = new("a8c5df");
    private FrontDiscipline[] _disciplines = [];
    private CharacterSlot[] _slots = [];
    private string _continueSlot = "", _activeSlot = "", _selectedSlot = "", _selectedDiscipline = "", _page = "Main", _viewKey = "";
    private bool _canResume;
    private int _oldSibling = -1;
    private ColorRect _backdrop = null!;
    private Label _subtitle = null!, _notice = null!, _catalogStatus = null!;
    private VBoxContainer _body = null!;
    private HBoxContainer _footer = null!;
    private ScrollContainer _scroll = null!;
    private CharacterPreview? _preview;
    private Button? _initialFocus;
    private Vector2 _viewport = new(-1, -1);

    public override void _Ready()
    {
        Name = "FrontMenu";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _backdrop = new ColorRect
        {
            Name = "FrontBackdrop",
            Color = new("060e17ee"),
            MouseFilter = MouseFilterEnum.Stop,
            MouseForcePassScrollEvents = false
        };
        AddChild(_backdrop); _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Panel = Card("FrontMenuPanel", new("61717d"), 22);
        Panel.MouseForcePassScrollEvents = false;
        AddChild(Panel);
        var column = Stack(12); Panel.AddChild(column);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 16); column.AddChild(header);
        var seal = new EchoesSeal { Kind = "mind", CustomMinimumSize = new(58, 58), MouseFilter = MouseFilterEnum.Ignore };
        header.AddChild(seal);
        var headings = Stack(2); headings.SizeFlagsHorizontal = SizeFlags.ExpandFill; header.AddChild(headings);
        var title = Text("ASHENWAKE", 38, Bone); title.Name = "FrontTitle"; headings.AddChild(title);
        _subtitle = Text("CHOOSE YOUR NEXT JOURNEY", 12, Gold); _subtitle.Name = "FrontSubtitle"; headings.AddChild(_subtitle);
        var rule = new HSeparator(); column.AddChild(rule);
        _catalogStatus = Text("Checking saved characters…", 12, Blue);
        _catalogStatus.Name = "FrontCatalogStatus"; _catalogStatus.Visible = false; column.AddChild(_catalogStatus);
        _scroll = new ScrollContainer
        {
            Name = "FrontScroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FollowFocus = true,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseForcePassScrollEvents = false
        };
        column.AddChild(_scroll);
        _body = Stack(12); _body.SizeFlagsHorizontal = SizeFlags.ExpandFill; _scroll.AddChild(_body);
        _notice = Text("", 13, Gold); _notice.Name = "FrontNotice"; _notice.MaxLinesVisible = 3;
        _notice.Visible = false; column.AddChild(_notice);
        _footer = new HBoxContainer(); _footer.AddThemeConstantOverride("separation", 10); column.AddChild(_footer);
        Panel.Visible = _backdrop.Visible = false;
        Rebuild(); UpdateLayout();
    }

    public override void _Process(double delta) => UpdateLayout();

    public void SetView(FrontDiscipline[] disciplines, CharacterSlot[] slots, string continueSlot, bool canResume, string activeSlot, bool catalogLoading = false)
    {
        CatalogLoading = catalogLoading;
        _catalogStatus.Visible = catalogLoading;
        _disciplines = disciplines; _slots = slots; _continueSlot = continueSlot;
        _canResume = canResume; _activeSlot = activeSlot;
        if (!disciplines.Any(d => d.Id == _selectedDiscipline)) _selectedDiscipline = disciplines.FirstOrDefault()?.Id ?? "";
        if (!slots.Any(s => s.Filename == _selectedSlot))
            _selectedSlot = slots.FirstOrDefault(s => s.Filename == continueSlot)?.Filename ?? slots.FirstOrDefault()?.Filename ?? "";
        string key = string.Join("\n", disciplines.Select(d => $"{d.Id}|{d.Resource}|{d.Playstyle}|{d.ResourceHint}|{d.Appearance.Key}|" +
            string.Join(";", d.Abilities.Select(a => $"{a.Id}|{a.Name}|{a.Description}|{a.ResourceCost}")))) + "\n" +
            string.Join("\n", slots.Select(s => $"{s.Filename}|{s.Discipline}|{s.Level}|{s.Location}|{s.IsEchoes}|{s.Available}|{s.RecoveredBackup}|{s.Notice}|{s.Appearance?.Key}|{s.SavedUtc.Ticks}")) +
            $"\n{continueSlot}|{canResume}|{activeSlot}|{catalogLoading}";
        if (_viewKey == key) return;
        _viewKey = key;
        if (IsNodeReady() && IsOpen)
        {
            var owner = GetViewport().GuiGetFocusOwner();
            bool restoreFocus = owner is null || IsAncestorOf(owner);
            string focus = owner?.Name.ToString() ?? "";
            Rebuild();
            if (!restoreFocus) return;
            if (focus.Length > 0 && FindChild(focus, true, false) is Control control && control.IsVisibleInTree()) control.GrabFocus();
            else _initialFocus?.GrabFocus();
        }
    }

    public void SetOpen(bool open)
    {
        if (Panel is null) return;
        bool changed = IsOpen != open;
        Panel.Visible = _backdrop.Visible = open;
        if (open)
        {
            if (_oldSibling < 0)
            { _oldSibling = GetIndex(); GetParent().MoveChild(this, GetParent().GetChildCount() - 1); }
            Rebuild(); _initialFocus?.GrabFocus();
        }
        else
        {
            if (_preview is not null) _preview.Visible = false;
            if (_oldSibling >= 0)
            { GetParent().MoveChild(this, Math.Min(_oldSibling, GetParent().GetChildCount() - 1)); _oldSibling = -1; }
        }
        if (changed) OpenChanged?.Invoke(open);
    }

    public void ShowPage(string page)
    {
        if (page is not ("Main" or "New" or "Characters")) return;
        _page = page;
        if (!IsNodeReady()) return;
        Notice(""); _scroll.ScrollVertical = 0;
        if (!IsOpen) SetOpen(true);
        else { Rebuild(); _initialFocus?.GrabFocus(); }
    }

    public void Notice(string message)
    {
        if (_notice is null) return;
        _notice.Text = _notice.TooltipText = message;
        _notice.Visible = message.Length > 0;
    }

    private void UpdateLayout()
    {
        if (Panel is null) return;
        var viewport = GetViewport().GetVisibleRect().Size;
        var size = new Vector2(Math.Min(1120, viewport.X - 36), Math.Min(730, viewport.Y - 36));
        var origin = (viewport - size) / 2;
        if (_viewport == viewport && Panel.Size == size && Panel.Position == origin) return;
        _viewport = viewport;
        // Wrapped labels can initially request their unlaid-out minimum. Keep the shell in the viewport after layout settles.
        Panel.Size = size; Panel.Position = origin;
        _preview?.SetCompact(size.Y < 710, Math.Clamp(size.Y - 255, 320, 420));
    }

    private void Rebuild()
    {
        if (_body is null) return;
        if (_preview is not null) _preview.Visible = false;
        _preview = null; _initialFocus = null;
        Clear(_body); Clear(_footer);
        _subtitle.Text = _page switch
        {
            "New" => "A NEW WANDERER · CHOOSE YOUR DISCIPLINE",
            "Characters" => "YOUR CHARACTERS · SEPARATE JOURNEYS",
            _ => "CHOOSE YOUR NEXT JOURNEY"
        };
        switch (_page)
        {
            case "New": BuildNew(); break;
            case "Characters": BuildCharacters(); break;
            default: BuildMain(); break;
        }
        _preview?.SetCompact(Panel.Size.Y < 710, Math.Clamp(Panel.Size.Y - 255, 320, 420));
    }

    private void BuildMain()
    {
        CharacterSlot? next = _slots.FirstOrDefault(s => s.Filename == _continueSlot);
        var row = Columns(); _body.AddChild(row);
        var portrait = Portrait(row, next?.Appearance ?? _disciplines.FirstOrDefault()?.Appearance,
            next is { Available: true } ? next.Discipline + " · " + Mode(next) : "Your next journey begins in Greyhaven");
        portrait.AddChild(Text("Rise from the ash.", 21, Bone));
        portrait.AddChild(Text("Choose a discipline, reclaim lost power, and shape the wanderer you become.", 13, Blue));
        var actions = Stack(10); actions.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(actions);
        var resumeCard = Card("FrontContinueCard", new("628b80"), 16); actions.AddChild(resumeCard);
        var resume = Stack(7); resumeCard.AddChild(resume);
        resume.AddChild(Text("CONTINUE YOUR JOURNEY", 11, Mint));
        var summary = Text(next is null ? CatalogLoading ? "Looking for your characters…" : "No saved character yet" : Identity(next), 20, Bone);
        summary.Name = "FrontContinueSummary"; resume.AddChild(summary);
        resume.AddChild(Text(next is null ? CatalogLoading ? "You can start a new character while your saves are checked." : "Start a new character to enter Greyhaven." : $"{next.Location} · {Mode(next)}", 13, Blue));
        if (next is not null) AddSlotStatus(resume, next);
        var continueButton = ActionButton("FrontContinue", "Continue", () => RequestLoad(_continueSlot), true);
        continueButton.Disabled = next?.Available != true; resume.AddChild(continueButton);
        if (_canResume)
        {
            var current = _slots.FirstOrDefault(s => s.Filename == _activeSlot);
            var currentButton = ActionButton("FrontResume", "Resume current character", () => ResumeRequested?.Invoke());
            currentButton.TooltipText = current is null ? "Return to the character already in play." : Identity(current) + " · " + current.Location;
            actions.AddChild(currentButton); _initialFocus = currentButton;
        }
        var start = ActionButton("FrontNew", "New Character", () => ShowPage("New")); actions.AddChild(start);
        var characters = ActionButton("FrontCharacters", $"Characters  ·  {_slots.Length}", () => ShowPage("Characters")); actions.AddChild(characters);
        actions.AddChild(ActionButton("FrontSettings", "Settings", () => SettingsRequested?.Invoke()));
        _initialFocus ??= continueButton.Disabled ? start : continueButton;
        var hint = Text("Your characters and Echoes journeys use separate save slots.", 12, Blue);
        hint.SizeFlagsHorizontal = SizeFlags.ExpandFill; _footer.AddChild(hint);
        var quit = ActionButton("FrontQuit", "Quit", () => QuitRequested?.Invoke());
        quit.SizeFlagsHorizontal = SizeFlags.Fill; quit.CustomMinimumSize = new(116, 40); _footer.AddChild(quit);
    }

    private void BuildNew()
    {
        _body.AddChild(Text("Choose how you fight", 22, Bone));
        _body.AddChild(Text("Every discipline begins a separate character. Select one to preview its appearance and starting abilities.", 13, Blue));
        var choices = new HBoxContainer(); choices.AddThemeConstantOverride("separation", 6); _body.AddChild(choices);
        var group = new ButtonGroup();
        foreach (var discipline in _disciplines)
        {
            string id = discipline.Id;
            var button = ActionButton("FrontDiscipline" + id, id, () => SelectDiscipline(id));
            button.ToggleMode = true; button.ButtonGroup = group; button.CustomMinimumSize = new(0, 42);
            button.AddThemeFontSizeOverride("font_size", 13); button.SetPressedNoSignal(id == _selectedDiscipline);
            button.TooltipText = discipline.Playstyle; choices.AddChild(button);
            if (id == _selectedDiscipline) _initialFocus = button;
        }
        var selected = _disciplines.FirstOrDefault(d => d.Id == _selectedDiscipline);
        if (selected is not null)
        {
            var row = Columns(); _body.AddChild(row);
            var portrait = Portrait(row, selected.Appearance, selected.Id + " · Starting equipment");
            portrait.Name = "FrontDisciplinePreview";
            var details = Stack(10); details.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(details);
            var title = Text(selected.Id, 26, Accent(selected.Id)); title.Name = "FrontDisciplineTitle"; details.AddChild(title);
            var playstyle = Text(selected.Playstyle, 15, Bone); playstyle.Name = "FrontPlaystyle"; details.AddChild(playstyle);
            var resource = Card("FrontResource", Accent(selected.Id).Darkened(.35f), 12); details.AddChild(resource);
            var resourceWords = Stack(5); resource.AddChild(resourceWords);
            resourceWords.AddChild(Text(selected.Resource.ToUpperInvariant(), 12, Accent(selected.Id)));
            var resourceHint = Text(selected.ResourceHint, 13); resourceHint.Name = "FrontResourceHint"; resourceWords.AddChild(resourceHint);
            details.AddChild(Text("STARTING ABILITIES", 12, Gold));
            foreach (var ability in selected.Abilities)
            {
                var card = Card("FrontAbility" + ability.Id.Replace('.', '_'), new("3b5363"), 10); details.AddChild(card);
                var abilityRow = new HBoxContainer(); abilityRow.AddThemeConstantOverride("separation", 10); card.AddChild(abilityRow);
                var icon = new SkillIcon { CustomMinimumSize = new(38, 38), SizeFlagsVertical = SizeFlags.ShrinkBegin };
                icon.SetSkill(ability.Id, selected.Id, ""); abilityRow.AddChild(icon);
                var words = Stack(4); words.SizeFlagsHorizontal = SizeFlags.ExpandFill; abilityRow.AddChild(words);
                words.AddChild(Text(ability.Name, 15, Bone));
                words.AddChild(Text(ability.Description, 12));
                words.AddChild(Text(ability.ResourceCost, 11, Accent(selected.Id)));
            }
        }
        FooterBack();
        var note = Text("Creates a new save slot.", 12, Blue); note.SizeFlagsHorizontal = SizeFlags.ExpandFill; _footer.AddChild(note);
        var begin = ActionButton("FrontBegin", selected is null ? "Begin journey" : "Begin as " + selected.Id,
            () => { if (_disciplines.Any(d => d.Id == _selectedDiscipline)) CreateRequested?.Invoke(_selectedDiscipline); }, true);
        begin.Disabled = selected is null; begin.CustomMinimumSize = new(210, 40); begin.SizeFlagsHorizontal = SizeFlags.Fill; _footer.AddChild(begin);
    }

    private void BuildCharacters()
    {
        _body.AddChild(Text("Choose a journey to continue", 22, Bone));
        _body.AddChild(Text("Original characters and Echoes characters keep their own equipment, progress, and records.", 13, Blue));
        var selected = _slots.FirstOrDefault(s => s.Filename == _selectedSlot);
        if (_slots.Length == 0)
        {
            var empty = Card("FrontNoCharacters", new("435969"), 20); _body.AddChild(empty);
            var words = Stack(9); empty.AddChild(words);
            words.AddChild(Text(CatalogLoading ? "Looking for saved characters…" : "No saved characters yet", 20, Bone));
            words.AddChild(Text("Begin a new journey, or import a compatible save through the file picker.", 13));
            var create = ActionButton("FrontEmptyNew", "Create your first character", () => ShowPage("New")); words.AddChild(create); _initialFocus = create;
        }
        else
        {
            var row = Columns(); _body.AddChild(row);
            var listing = Stack(9); listing.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(listing);
            for (int index = 0; index < _slots.Length; index++)
            {
                var slot = _slots[index]; string filename = slot.Filename;
                var card = Card("FrontCharacterCard" + index, filename == _selectedSlot ? Mint : new("415669"), 12); listing.AddChild(card);
                var words = Stack(5); card.AddChild(words);
                var button = ActionButton("FrontSlot" + index, Identity(slot), () => SelectSlot(filename));
                button.ToggleMode = true; button.SetPressedNoSignal(filename == _selectedSlot);
                button.Alignment = HorizontalAlignment.Left; words.AddChild(button);
                words.AddChild(Text(slot.Location + " · " + Mode(slot), 13, slot.IsEchoes ? Blue : Mint));
                words.AddChild(Text("Saved " + SavedTime(slot), 11, Blue));
                if (slot.Filename == _activeSlot && _canResume) words.AddChild(Text("CURRENT CHARACTER", 10, Gold));
                AddSlotStatus(words, slot);
                if (filename == _selectedSlot) _initialFocus = button;
            }
            var portrait = Portrait(row, selected?.Appearance, selected is null ? "Select a character" : Identity(selected));
            if (selected is not null)
            {
                var summary = Text(Identity(selected) + " · " + Mode(selected), 15, selected.IsEchoes ? Blue : Mint);
                summary.Name = "FrontSlotSummary"; portrait.AddChild(summary);
                portrait.AddChild(Text(selected.Location, 14));
                portrait.AddChild(Text(selected.Available ? "Play resumes this saved journey." : "This save cannot currently be loaded. See the notice on its card.", 12, selected.Available ? Blue : Gold));
            }
        }
        FooterBack();
        var import = ActionButton("FrontImport", "Import save…", () => ImportRequested?.Invoke());
        import.SizeFlagsHorizontal = SizeFlags.Fill; import.CustomMinimumSize = new(142, 40); _footer.AddChild(import);
        _footer.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var play = ActionButton("FrontPlay", selected is null ? "Play selected" : "Play " + selected.Discipline, () => RequestLoad(_selectedSlot), true);
        play.Disabled = selected?.Available != true; play.SizeFlagsHorizontal = SizeFlags.Fill; play.CustomMinimumSize = new(180, 40); _footer.AddChild(play);
    }

    private VBoxContainer Portrait(HBoxContainer parent, CharacterAppearance? appearance, string caption)
    {
        var card = Card("FrontPortrait", new("3d5261"), 12);
        card.CustomMinimumSize = new(296, 0); card.SizeFlagsHorizontal = SizeFlags.Fill; card.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        parent.AddChild(card);
        var column = Stack(9); card.AddChild(column);
        if (appearance is not null)
        {
            _preview = new CharacterPreview { Name = "FrontPreview", SizeFlagsVertical = SizeFlags.Fill };
            column.AddChild(_preview); _preview.Name = "FrontPreview"; _preview.SetCaption(caption); _preview.SetAppearance(appearance);
            _preview.Visible = IsOpen;
        }
        else
        {
            column.AddChild(new EchoesSeal { Kind = "locked", CustomMinimumSize = new(270, 190), MouseFilter = MouseFilterEnum.Ignore });
            column.AddChild(Text(caption, 14, Blue));
        }
        return column;
    }

    private void SelectDiscipline(string id)
    {
        if (!_disciplines.Any(d => d.Id == id)) return;
        _selectedDiscipline = id; Rebuild(); _initialFocus?.GrabFocus();
    }
    private void SelectSlot(string filename)
    {
        if (!_slots.Any(s => s.Filename == filename)) return;
        _selectedSlot = filename; Rebuild(); _initialFocus?.GrabFocus();
    }
    private void RequestLoad(string filename)
    { if (_slots.Any(s => s.Filename == filename && s.Available)) LoadRequested?.Invoke(filename); }
    private void FooterBack()
    {
        var back = ActionButton("FrontBack", "Back [Esc]", () => ShowPage("Main"));
        back.SizeFlagsHorizontal = SizeFlags.Fill; back.CustomMinimumSize = new(116, 40); _footer.AddChild(back);
    }
    private static void AddSlotStatus(VBoxContainer parent, CharacterSlot slot)
    {
        if (slot.RecoveredBackup) parent.AddChild(Text("Recovered backup available", 12, Gold));
        if (slot.Notice.Length > 0) parent.AddChild(Text(slot.Notice, 12, Gold));
        else if (!slot.Available) parent.AddChild(Text("Save unavailable", 12, Gold));
    }
    private static string Identity(CharacterSlot slot) => slot.Level > 0 ? $"{slot.Discipline} · Level {slot.Level}" : slot.Discipline;
    private static string Mode(CharacterSlot slot) => slot.IsEchoes ? "Echoes character" : "Original character";
    private static string SavedTime(CharacterSlot slot) => slot.SavedUtc == default ? "time unavailable" :
        slot.SavedUtc.ToLocalTime().ToString("MMM d, yyyy · h:mm:ss tt", CultureInfo.CurrentCulture);
    private static Color Accent(string discipline) => discipline switch
    {
        "Veilwalker" => new("c3a9e5"),
        "Arcanist" => new("9dbfeb"),
        "Gravecaller" => new("b6d1ad"),
        "Warden" => new("96cfac"),
        _ => new("e7bc85")
    };
    private static void Clear(Node container)
    { foreach (var child in container.GetChildren()) { container.RemoveChild(child); child.QueueFree(); } }
    private static HBoxContainer Columns()
    { var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 18); return row; }
    private static VBoxContainer Stack(int gap = 8)
    { var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", gap); return box; }
    private static Label Text(string value, int size, Color? color = null)
    {
        var label = new Label
        {
            Text = value,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color ?? new Color("c7d1d8")); return label;
    }
    private static Button ActionButton(string name, string text, Action action, bool primary = false)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            CustomMinimumSize = new(0, 40),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.All,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = text
        };
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", primary ? Bone : new Color("d4dedf"));
        button.AddThemeColorOverride("font_disabled_color", new("72828d"));
        button.AddThemeColorOverride("font_hover_color", Bone);
        button.AddThemeStyleboxOverride("normal", ButtonStyle(primary ? new("294e49") : new("1a2d3b"), primary ? new("719b86") : new("425b6b")));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(new("304954"), Mint));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(new("365d55"), Mint));
        button.AddThemeStyleboxOverride("disabled", ButtonStyle(new("14212b"), new("2c3b47")));
        var focus = ButtonStyle(new Color(0, 0, 0, 0), Gold); focus.BorderWidthTop = focus.BorderWidthBottom = focus.BorderWidthLeft = focus.BorderWidthRight = 2;
        button.AddThemeStyleboxOverride("focus", focus);
        button.Pressed += action; return button;
    }
    private static StyleBoxFlat ButtonStyle(Color background, Color border) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        CornerRadiusTopLeft = 5,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5,
        CornerRadiusBottomRight = 5,
        ContentMarginLeft = 12,
        ContentMarginRight = 12,
        ContentMarginTop = 8,
        ContentMarginBottom = 8
    };
    private static PanelContainer Card(string name, Color border, int margin)
    {
        var panel = new PanelContainer { Name = name };
        var style = ButtonStyle(new("101e2aec"), border);
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 8;
        style.ContentMarginLeft = style.ContentMarginRight = margin; style.ContentMarginTop = style.ContentMarginBottom = margin;
        panel.AddThemeStyleboxOverride("panel", style); return panel;
    }
}
