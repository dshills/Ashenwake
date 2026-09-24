using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private sealed class FloatingLabel(int actorId, Label3D node, Vector3 origin, Color color)
    {
        public int ActorId = actorId;
        public Label3D Node = node;
        public Vector3 Origin = origin;
        public Color Color = color;
        public float Age;
    }
    private readonly List<FloatingLabel> _floatingLabels = [];
    private readonly Dictionary<string, double> _lastSounds = [];
    private CombatEffects _combatEffects = null!;
    private double _cosmeticTime;
    private int _importantVoiceIndex;
    private readonly LootDropCues _lootDropCues = new();
    internal int SpecialLootDropCount { get; private set; }
    internal string LastSpecialLootCue { get; private set; } = "";
    public bool ReducedEffects => _reduceEffects;

    /// <summary>Present each batch once after Core advances. This never advances or changes combat.</summary>
    public void PresentCombatEvents(IReadOnlyList<CombatEvent> events, CombatSession? source = null)
    {
        bool replaced = source is not null && CrossedCombatBoundary(_session, source);
        if (source is not null && !ReferenceEquals(source, _session)) AdoptSessionCore(source);
        PresentCombatEventsCore(events, replaced);
    }

    private static bool CrossedCombatBoundary(CombatSession previous, CombatSession next)
        => !ReferenceEquals(previous, next) && (previous.EncounterId != next.EncounterId || next.Tick < previous.Tick ||
            previous.View.Actors.Any(a => a.Id == 1 && a.Health <= 0) && next.View.Actors.Any(a => a.Id == 1 && a.Health > 0));

    private void PresentCombatEventsCore(IReadOnlyList<CombatEvent> events, bool replaced)
    {
        _view = _session.View;
        SynchronizeWorld();
        _lootDropCues.Trim(_view.Loot);
        foreach (var e in events)
        {
            _eventLog.Add(e); if (_eventLog.Count > 8192) _eventLog.RemoveAt(0);
            // A room transition or respawn can replace Core's session inside the command callback.
            // IDs in that completed batch belong to the old arena, not its freshly spawned actors.
            if (replaced && e.Kind is "AbilityStarted" or "EliteAbilityStarted" or "BossPatternStarted" or "CampaignHazardWarned" or
                "AbilityResolved" or "EliteAbilityResolved" or "CampaignHazardResolved" or "Dodged" or
                "DamageApplied" or "BarrierAbsorbed" or "BarrierGranted" or "Healed" or "EntityKilled" or "EliteCopyKilled" or
                "MechanismDestroyed" or "BossPhaseChanged" or "LootDropped" or "EnemyOvercharged" or
                "LegendaryReadied" or "LegendaryCharged" or "LegendaryTriggered") continue;
            if (e.Kind is "CampaignHazardWarned" or "CampaignHazardResolved" && e.ContentId.StartsWith("rule.", StringComparison.Ordinal))
            { if (e.Kind == "CampaignHazardWarned") PlayTone("tell"); continue; }
            // A support chant resolving is not a weapon swing or a hit at the player.
            if ((e.ContentId == "elite.dirgebound" || IsMidgameSupport(e.ContentId)) && e.Kind is "CampaignHazardResolved" or "EliteAbilityResolved") continue;
            _actors.TryGetValue(e.ActorId, out var actor);
            int targetId = e.TargetId == 0 ? e.ActorId : e.TargetId;
            _actors.TryGetValue(targetId, out var target);
            Vector3 direction = actor is not null && target is not null ? target.Current - actor.Current : Vector3.Forward;
            if (direction.LengthSquared() < .001f) direction = actor?.Facing ?? Vector3.Forward;
            if (e.TargetId == 0 && e.ActorId != 1 && actor is not null && _actors.TryGetValue(1, out var player))
                direction = actor.Facing ?? player.Current - actor.Current;
            switch (e.Kind)
            {
                case "AbilityStarted":
                case "EliteAbilityStarted":
                case "BossPatternStarted":
                case "CampaignHazardWarned":
                    actor?.Body.BeginAttackWindup();
                    if (actor is not null && direction.LengthSquared() > .001f) actor.Facing = direction;
                    if (_view.Actors.Any(a => a.Id == e.ActorId && a.Faction == CombatFaction.Enemy) &&
                        !PlayOpeningTell(e, actor?.Current ?? Vector3.Zero)) PlayTone("tell");
                    break;
                case "AbilityResolved":
                case "EliteAbilityResolved":
                case "CampaignHazardResolved":
                    if (actor is null || actor.Health <= 0 || actor.LastAttackTick == e.Tick) break;
                    actor.LastAttackTick = e.Tick;
                    if (e.Kind == "CampaignHazardResolved" && _actors.TryGetValue(1, out var playerTarget))
                        direction = actor.Facing ?? playerTarget.Current - actor.Current;
                    actor.Facing = direction; actor.Body.React("attack", e.ContentId);
                    var skill = _content.Skills.FirstOrDefault(s => s.Id == e.ContentId);
                    var attacker = _view.Actors.FirstOrDefault(a => a.Id == e.ActorId);
                    string discipline = e.ActorId == 1 ? _view.Discipline : "";
                    string sound = discipline switch
                    {
                        "Vanguard" => "blade",
                        "Veilwalker" => "blade",
                        "Arcanist" => "arcane",
                        "Gravecaller" => "bone",
                        "Warden" => "spear",
                        _ when attacker?.DefinitionId == "enemy.memory_archer" => "bow",
                        _ when attacker?.DefinitionId is "boss.bell_saint" or "enemy.bell_saint" && _view.BossPhase < 3 => "bell",
                        _ when attacker?.Faction == CombatFaction.Ally => "bone",
                        _ when attacker?.Role is "Ranged" or "Support" or "Ranged Elite" or "Support Elite" => "arcane",
                        _ => "enemy"
                    };
                    string cue = sound is "arcane" or "bone" or "bell" || skill is { Shape: "Projectile" or "Area" } ? "spell" : sound is "spear" or "bow" ? "thrust" : "slash";
                    if (actor.Body.TryGetWeaponEffectAnchor(out _))
                        _combatEffects.EmitWeapon(actor.Body, SkillColor(skill), _reduceEffects);
                    else _combatEffects.Emit(cue, actor.Current, direction, SkillColor(skill), _reduceEffects);
                    PlayTone(sound); break;
                case "Dodged":
                    if (actor is null) break;
                    actor.Body.React("dodge"); actor.Facing = actor.Current - actor.Previous;
                    _combatEffects.Emit("dodge", actor.Previous, actor.Current - actor.Previous, new Color("9fafa9"), _reduceEffects);
                    PlayTone("dodge"); break;
                case "DamageApplied":
                    if (target is null) break;
                    if (!target.Windup) target.Body.React("hit");
                    _combatEffects.Emit("hit", target.Current, direction, _ember, _reduceEffects);
                    Feedback(targetId, $"−{e.Amount}", "hit");
                    if (!PlayOpeningImpact(e, target.Current)) PlayTone("hit");
                    if (targetId == 1) _shake = Math.Max(_shake, .25);
                    break;
                case "BarrierAbsorbed":
                    if (target is not null) _combatEffects.Emit("block", target.Current, direction, _mint, _reduceEffects);
                    Feedback(targetId, $"BLOCK {e.Amount}", "heal");
                    if (!_openingAudio.Play("impact_armor", target?.Current ?? Vector3.Zero)) PlayTone("armor"); break;
                case "BarrierGranted":
                    if (e.ContentId is not ("elite.dirgebound" or "campaign.oath_ward")) break;
                    if (target is not null) _combatEffects.Emit("block", target.Current, Vector3.Up, _mint, _reduceEffects);
                    PlayTone("armor"); break;
                case "Healed": Feedback(targetId, $"+{e.Amount}", "heal"); PlayTone("heal"); break;
                case "EnemyOvercharged":
                    if (target is not null) _combatEffects.Emit("block", target.Current, Vector3.Up, new("f5b565"), _reduceEffects);
                    PlayTone("armor"); break;
                case "LegendaryReadied":
                case "LegendaryCharged":
                case "LegendaryTriggered":
                    PresentLegendaryEvent(e, actor, target, direction); break;
                case "EntityKilled":
                case "EliteCopyKilled":
                case "MechanismDestroyed":
                    if (target is null || target.Health > 0) break;
                    target.Body.React("death");
                    bool boss = _view.Actors.Any(a => a.Id == targetId && a.DefinitionId is "boss.bell_saint" or "enemy.bell_saint" or "enemy.bell_beast");
                    _combatEffects.Emit(boss ? "victory" : "death", target.Current, direction, new Color("e6bf7d"), _reduceEffects);
                    Feedback(targetId, boss ? "SILENCED" : "FALLEN", "death");
                    if (!PlayVerdantDeath(targetId, target.Current)) PlayTone(boss ? "victory" : "death");
                    _shake = Math.Max(_shake, boss ? .6 : .2); break;
                case "BossPhaseChanged":
                    if (actor is null) break;
                    _combatEffects.Emit("phase", actor.Current, Vector3.Forward, new Color("cfb1e6"), _reduceEffects);
                    if (!PlayRegionalPhase(e, actor.Current))
                        PlayTone(e.Amount >= 3 ? "chain" : "bell"); break;
                case "LootDropped":
                    var drop = _lootDropCues.Observe(e, _view.Loot);
                    if (drop is null || !IsLootVisible(drop)) break;
                    string dropCue = LootDropCues.Cue(drop.Item.Rarity);
                    _combatEffects.Emit(dropCue, PositionOf(drop.Position.X, drop.Position.Z), Vector3.Forward, LootVisual.RarityColor(drop.Item.Rarity), _reduceEffects);
                    PlayTone(dropCue); SpecialLootDropCount++; LastSpecialLootCue = dropCue;
                    Message($"{drop.Item.Rarity} discovered · {EquipmentNames.For(drop.Item)}"); break;
                case "LootPickedUp": PresentLootReward(e); Message($"Collected {EquipmentNames.For(e.ContentId)}. Open inventory to compare."); PlayTone("loot"); break;
                case "FragmentTriggered": Message($"{Readable(e.ContentId)} triggered · action {e.ActionId} / chain {e.Depth}"); break;
                case "SummonSpawned": Message("A Serath spirit rises from a damage-over-time death."); break;
                case "StatusApplied": if (e.ContentId is "Burning" or "Poisoned") Message($"{e.ContentId} → actor {e.TargetId} · owner {e.ActorId}"); break;
                case "CommandRejected": if (!_smoke) Message($"Action unavailable: {Readable(e.ContentId)}"); break;
                case "EffectBudgetExceeded": Message("Effect safety budget reached; see event log for origin."); break;
            }
        }
        _openingAudio.Observe(_view, events.Any(e => e.Kind == "Dodged" && e.ActorId == 1));
    }

    private static Color SkillColor(CombatSkill? skill) => skill?.Family switch
    {
        DamageFamily.Fire => new("ffc078"),
        DamageFamily.Frost => new("98def2"),
        DamageFamily.Storm => new("c0b2ff"),
        DamageFamily.Decay or DamageFamily.Venom => new("afce8c"),
        DamageFamily.Void => new("bc9fe5"),
        _ => new("f3dfa7")
    };

    private void AdvanceCombatFeedback(double delta)
    {
        foreach (var voice in _voices) voice.StreamPaused = IsPaused;
        _combatEffects.Advance(delta, IsPaused, _reduceEffects);
        if (IsPaused) return;
        float dt = (float)Math.Clamp(delta, 0, .1); _cosmeticTime += dt;
        for (int i = _floatingLabels.Count - 1; i >= 0; i--)
        {
            var label = _floatingLabels[i]; label.Age += dt;
            if (label.Age >= .65f)
            {
                _floatingLabels.RemoveAt(i); _floatingActors.Remove(label.ActorId);
                _transientNodes.Remove(label.Node); label.Node.QueueFree(); continue;
            }
            float t = label.Age / .65f;
            label.Node.Position = label.Origin + Vector3.Up * t * (_reduceEffects ? .5f : 1.4f);
            label.Node.Modulate = new Color(label.Color, 1 - t);
        }
    }
}
