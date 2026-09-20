using System.Globalization;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private CombatRewardFeed _rewardFeed = null!;
    private Control _experiencePanel = null!;
    private ProgressBar _experienceBar = null!;
    private Label _experienceText = null!;
    private bool _rewardBaselineReady;
    private string _rewardDiscipline = "";
    private int _rewardLevel;
    private long _rewardExperience;
    private HashSet<string> _rewardMasteredSkills = [];
    private readonly HashSet<string> _rewardSeenAvailableSkills = [];
    private readonly HashSet<string> _rewardSeenDisciplines = [];

    public IReadOnlyList<CombatRewardNotice> RewardNotices => _rewardFeed?.VisibleNotices ?? [];
    public int PendingRewardCount => _rewardFeed?.PendingCount ?? 0;
    public long RewardNoticeCount => _rewardFeed?.AcceptedCount ?? 0;
    public string ExperienceText => _experienceText?.Text ?? "";
    public long ExperienceIntoLevel { get; private set; }
    public long ExperienceForNextLevel { get; private set; }
    public bool ExperienceAtMaximumLevel { get; private set; }

    private void BuildRewardPresentation()
    {
        _rewardFeed = new CombatRewardFeed(); _hud.AddChild(_rewardFeed);
        _experiencePanel = new Control { Name = "HudExperience", CustomMinimumSize = new(0, 20), Size = new(700, 20), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _hud.AddChild(_experiencePanel);
        _experienceBar = new ProgressBar { Name = "HudExperienceBar", MinValue = 0, MaxValue = 1, ShowPercentage = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _experiencePanel.AddChild(_experienceBar); _experienceBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _experienceBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new("15252ced"), BorderColor = new("58737b"), BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1 });
        _experienceBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new("416d67b8") });
        _experienceText = new Label
        {
            Name = "HudExperienceText",
            ClipText = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        _experiencePanel.AddChild(_experienceText); _experienceText.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _experienceText.AddThemeFontSizeOverride("font_size", 11); _experienceText.AddThemeColorOverride("font_color", new Color("e4e9dc"));
        _experienceText.AddThemeColorOverride("font_outline_color", new Color("102028")); _experienceText.AddThemeConstantOverride("outline_size", 2);
    }

    private void ResetRewardPresentation()
    {
        _rewardFeed?.Reset(); _rewardBaselineReady = false; _rewardDiscipline = ""; _rewardLevel = 0; _rewardExperience = 0;
        _rewardMasteredSkills.Clear(); _rewardSeenAvailableSkills.Clear(); _rewardSeenDisciplines.Clear();
        ExperienceIntoLevel = ExperienceForNextLevel = 0; ExperienceAtMaximumLevel = false;
        if (_experiencePanel is not null) _experiencePanel.Visible = false;
        if (_experienceText is not null) _experienceText.Text = "";
        if (_experienceBar is not null) _experienceBar.Value = 0;
    }

    /// <summary>Uses the authoritative level floor; opening or restoring a character establishes a quiet baseline.</summary>
    public void PresentProgression(ProgressionView view, long levelStartExperience, IReadOnlyList<CombatSkillView> skills,
        IReadOnlyCollection<string>? unlockedDisciplines = null)
    {
        if (_experiencePanel is null || _rewardFeed is null) return;
        ExperienceIntoLevel = Math.Max(0, view.Experience - levelStartExperience);
        ExperienceForNextLevel = Math.Max(0, view.NextLevelExperience - levelStartExperience);
        ExperienceAtMaximumLevel = ExperienceForNextLevel == 0;
        _experiencePanel.Visible = true;
        _experienceBar.MaxValue = Math.Max(1, ExperienceForNextLevel);
        _experienceBar.Value = ExperienceAtMaximumLevel ? 1 : Math.Min(ExperienceIntoLevel, ExperienceForNextLevel);
        _experienceText.Text = ExperienceAtMaximumLevel
            ? $"LEVEL {view.Level} · MAX LEVEL · {view.Experience:N0} XP"
            : $"LEVEL {view.Level} · {ExperienceIntoLevel:N0} / {ExperienceForNextLevel:N0} XP";

        var mastered = view.MasteredSkills.ToHashSet(StringComparer.Ordinal);
        var available = skills.Where(skill => skill.Available).Select(skill => skill.Id).ToHashSet(StringComparer.Ordinal);
        // The progression view may contain ultimates from earlier disciplines; only show this discipline's abilities.
        available.UnionWith(view.UltimateSkills.Where(id => skills.Any(skill => skill.Id == id)));
        bool forward = _rewardBaselineReady && view.Experience >= _rewardExperience && view.Level >= _rewardLevel;
        bool returningDiscipline = _rewardDiscipline != view.Discipline && _rewardSeenDisciplines.Contains(view.Discipline);
        if (forward)
        {
            if (_rewardDiscipline == view.Discipline && view.Level > _rewardLevel && view.Experience > _rewardExperience)
                _rewardFeed.PresentProgress("LevelUp", "level:" + view.Level, "Level " + view.Level + " reached", "Open Skills to review your build.");
            foreach (string id in mastered.Except(_rewardMasteredSkills).Order(StringComparer.Ordinal))
            {
                var skill = skills.FirstOrDefault(candidate => candidate.Id == id);
                if (skill is null) continue;
                int mutations = _view.Mutations.Count(mutation => mutation.SkillId == id);
                string detail = mutations > 0 ? (mutations == 1 ? "Mutation unlocked" : "Mutations unlocked") + " · Visit Mara" : "Mastery milestone · Open Skills";
                _rewardFeed.PresentProgress("Mastery", "mastery:" + id, skill.Name + " mastered", detail, id, view.Discipline, skill.Shape);
            }
            foreach (string id in available.Except(_rewardSeenAvailableSkills).Where(_ => !returningDiscipline).Order(StringComparer.Ordinal))
            {
                var skill = skills.FirstOrDefault(candidate => candidate.Id == id);
                string name = skill?.Name ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Readable(id));
                _rewardFeed.PresentProgress("AbilityUnlocked", "ability:" + id, name + " unlocked",
                    view.UltimateSkills.Contains(id) ? "Ultimate ability · Open Skills" : "New ability · Open Skills", id, view.Discipline, skill?.Shape ?? "");
            }
        }
        _rewardBaselineReady = true; _rewardDiscipline = view.Discipline; _rewardLevel = view.Level; _rewardExperience = view.Experience;
        _rewardMasteredSkills = mastered;
        // Retain observed unlocks when retraining back to an earlier discipline in this session.
        // The initial view also identifies previously earned ultimates from other disciplines.
        _rewardSeenAvailableSkills.UnionWith(available); _rewardSeenAvailableSkills.UnionWith(view.UltimateSkills);
        // Merge after detecting this transition: a genuinely new retrain already appears in the new snapshot.
        // The first quiet baseline seeds previously unlocked disciplines from saved permanent progression.
        if (unlockedDisciplines is not null) _rewardSeenDisciplines.UnionWith(unlockedDisciplines);
        _rewardSeenDisciplines.Add(view.Discipline);
    }

    private void PresentLootReward(CombatEvent reward)
    {
        if (_rewardFeed is null || reward.Kind != "LootPickedUp" || reward.ActorId != 1) return;
        // Core's pickup event carries CombatLoot.Id, which is the retained CombatItem.Id.
        var item = _view.Inventory.FirstOrDefault(candidate => candidate.Id == reward.Amount);
        if (item is not null && item.DefinitionId == reward.ContentId) _rewardFeed.PresentLoot(item, _view.Discipline);
    }

    private void AdvanceRewardPresentation(double delta) => _rewardFeed?.Advance(delta, IsPaused);
}
