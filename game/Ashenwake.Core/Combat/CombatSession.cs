using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    public const int ActorRadius = 280;
    public const int MaxActors = 160;
    public const int MaxSummons = 8;
    public const int MaxProjectiles = 128;
    public const int MaxAreas = 64;
    public const int MaxEffectsPerTick = 256;
    public const int MaxChainDepth = 6;
    public static IReadOnlyList<string> Presets { get; } = Array.AsReadOnly(new[] { "standard", "dense", "projectiles", "summons", "chain" });
    private readonly CombatContent _content;
    private readonly SpatialWorld _spatial;
    private readonly CombatSnapshot _state;
    private readonly Queue<Hit> _effects = new();
    private readonly List<CombatEvent> _events = [];
    private readonly HashSet<string> _triggers = new(StringComparer.Ordinal);
    private int _processed;
    private sealed record Hit(int SourceId, int OwnerId, int TargetId, int Damage, DamageFamily Family, string ContentId, long ActionId, int Depth, bool Dot = false, bool Reflected = false, string Status = "", string OriginSkill = "", int SourceGeneration = 0, string FragmentId = "");
    private CombatSession(CombatContent content, CombatSnapshot state) { _content = content; _spatial = new(ResolveRoom(content, state)); _state = state; }
    public long Tick => _state.Tick;
    public string ContentHash => _state.ContentHash;
    public string StateHash => JsonData.Hash(_state);
    public CombatSnapshot Capture() => JsonData.Copy(_state);
    /// <summary>Projects an already-owned anatomy loadout while ending effects owned by removed fragments.</summary>
    public void ApplyAnatomy(IReadOnlyDictionary<string, string> anatomy)
    {
        if (anatomy is null || anatomy.Count > 6 || anatomy.Any(pair => !_content.Fragments.Any(f => f.Id == pair.Value && f.Slot.ToString() == pair.Key)))
            throw new InvalidDataException("Invalid anatomy projection.");
        var selected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in anatomy) selected.Add(pair.Key, pair.Value);
        foreach (var old in _state.Fragments.Values.Where(id => !selected.Values.Contains(id)).ToArray()) RemoveFragment(old);
        foreach (var pair in selected) _state.Fragments[pair.Key] = pair.Value;
    }
    private CombatActor Player => _state.Actors.Single(a => a.Id == 1);
    public static CombatSession Create(string contentJson, ulong seed = 42, string preset = "standard")
    {
        var content = CombatContent.Parse(contentJson);
        if (!Presets.Contains(preset)) throw new ArgumentException("Unknown arena preset.", nameof(preset));
        var state = new CombatSnapshot { ContentHash = content.Identity, Seed = seed, Rng = SeededRandom.Streams(seed), Preset = preset };
        state.Actors.Add(new() { Id = 1, DefinitionId = "player.vanguard", Position = content.Room.PlayerSpawn, Faction = CombatFaction.Player, Role = "Vanguard", Health = 350, MaxHealth = 350 });
        foreach (var fragment in content.Fragments) state.Fragments.TryAdd(fragment.Slot.ToString(), fragment.Id);
        foreach (var item in content.Items.Where(i => i.Id != "item.ashcleaver"))
        {
            var instance = new CombatItem(state.NextObjectId++, item.Id, item.Name, item.Slot, "Common", item.Damage, item.Armor, item.CriticalBasisPoints);
            state.Inventory.Add(instance); state.Equipment.TryAdd(item.Slot, instance.Id);
        }
        var session = new CombatSession(content, state);
        int count = preset switch { "dense" => 72, "projectiles" => 32, "summons" => 24, "chain" => 30, _ => 6 };
        var rng = state.Rng.Encounters;
        for (int i = 0; i < count; i++)
        {
            var arenaEnemies = content.Enemies.Where(e => e.Role is "Melee" or "Ranged" or "Armored").ToArray();
            var definition = preset == "projectiles" ? arenaEnemies.First(e => e.Role == "Ranged") : arenaEnemies[i % arenaEnemies.Length];
            var position = new Position(1800 + i % 8 * 1250, -7000 + i / 8 * 1400);
            if (preset == "standard") position = new(1200 + i % 3 * 2300, -2300 + i / 3 * 4600);
            position = new(position.X, position.Z + SeededRandom.Range(ref rng, 401) - 200);
            if (!session._spatial.CanOccupy(position, ActorRadius)) continue;
            bool elite = i == count - 1;
            state.Actors.Add(new()
            {
                Id = state.NextActorId++,
                DefinitionId = definition.Id,
                Position = position,
                Faction = CombatFaction.Enemy,
                Role = definition.Role,
                Health = preset == "chain" ? 45 : definition.Health * (elite ? 2 : 1),
                MaxHealth = preset == "chain" ? 45 : definition.Health * (elite ? 2 : 1),
                Armor = definition.Armor,
                Elite = elite
            });
        }
        state.Rng = state.Rng with { Encounters = rng };
        if (preset is "chain" or "summons")
        {
            // These explicitly named diagnostic presets begin with low health enemies and a fragment-generated spirit.
            var heart = content.Fragments.FirstOrDefault(f => f.Effect == "Spirit");
            if (heart is not null) session.SpawnSpirit(state.Actors[1], heart, 1, state.NextActionId++, 1);
            state.Momentum = 100;
        }
        return session;
    }
    public static CombatSession Restore(string contentJson, CombatSnapshot snapshot)
    {
        var content = CombatContent.Parse(contentJson);
        if (snapshot is null) throw new InvalidDataException("Missing combat snapshot.");
        CombatSnapshot state;
        try { state = JsonData.Copy(snapshot); }
        catch (System.Text.Json.JsonException ex) { throw new InvalidDataException("Malformed combat snapshot.", ex); }
        var session = new CombatSession(content, state);
        session.ValidateSnapshot(); return session;
    }
    public CombatView View => new(Tick, _state.Preset,
        _state.Actors.OrderBy(a => a.Id).Select(a => new CombatActorView(a.Id, a.Position, a.Health, a.MaxHealth, a.Faction, a.Elite ? a.Role + " Elite" : a.Role,
            a.Pending is null ? 0 : (int)Math.Max(0, a.Pending.ResolveTick - Tick), a.State, a.Barrier,
            a.Statuses.Select(s => new CombatStatusView(s.Id, s.SourceId, Math.Max(0, s.ExpiresTick - Tick), s.Stacks)).ToArray(), a.DefinitionId, a.Pending?.Target, TelegraphRadius(a), ActorVisible(a), (_state.ConsumedCorpseIds.Contains(a.Id) || !LeavesCorpse(a)), _state.Campaign?.Actors.GetValueOrDefault(a.Id)?.Modifiers.ToArray() ?? [], a.Health > 0 && CampaignDefenseBonus(a) > 0, a.Health > 0 && CampaignShielded(a))).ToArray(),
        _state.Projectiles.Select(p => new CombatProjectileView(p.Id, p.Position, p.Target, p.SkillId, p.OwnerId)).ToArray(),
        _state.Areas.Select(a => new CombatAreaView(a.Id, a.Position, a.Radius, a.SkillId, Math.Max(0, a.ExpiresTick - Tick), a.OwnerId)).ToArray(),
        _state.Loot.ToArray(), _state.Inventory.ToArray(),
        SelectedSkills.Select(s => new CombatSkillView(s.Id, s.Name, Mutation(s.Id)?.Shape ?? s.Shape, s.Cost + (Mutation(s.Id)?.ExtraCost ?? 0), s.Generate, s.Cooldown,
            Remaining(_state.Cooldowns.GetValueOrDefault(s.Id)), _state.Mutations.GetValueOrDefault(s.Id, ""), s.ResourceMode, SkillAvailable(s))).ToArray(),
        _content.Fragments.Select(f => new CombatFragmentView(f.Id, f.Name, f.Slot, f.Lineage, f.Resonance, f.Description, _state.Fragments.Values.Contains(f.Id))).ToArray(),
        _content.Mutations.Select(m => new CombatMutationView(m.Id, m.SkillId, m.Name, m.Description)).ToArray(), new SortedDictionary<string, long>(_state.Equipment, StringComparer.Ordinal),
        _state.Momentum, 100, Player.Barrier, _state.PotionCharges, Remaining(_state.PotionReadyTick), Remaining(_state.DodgeReadyTick),
        _content.Fragments.Where(f => _state.Fragments.Values.Contains(f.Id)).Sum(f => f.Resonance), _effects.Count, _state.PeakEffects, _state.RejectedEffects, _content.ContentVersion, Discipline, ResourceName, _state.CapturedSkillId, Remaining(_state.CapturedUntil), FalseSilhouettes().ToArray(), _state.FragmentHeat, _state.SeismicCharge, CampaignHazards().ToArray(), CampaignRule, _state.Campaign?.BossPhase ?? 0, _state.Campaign?.SuppressedFragmentId ?? "", EndgameView);
    private int Remaining(long until) => (int)Math.Clamp(until - Tick, 0, int.MaxValue);
    private CombatMutation? Mutation(string skill) => _content.Mutations.FirstOrDefault(m => m.Id == _state.Mutations.GetValueOrDefault(skill));
    private void Emit(string kind, int actor = 0, int target = 0, int amount = 0, string content = "", long action = 0, int depth = 0)
    { var ev = new CombatEvent(Tick, kind, actor, target, amount, content, action, depth); _events.Add(ev); ObserveEndgameEvent(ev); ObserveBorrowedMemory(ev); }
    private void Reject(CombatCommand command, string reason) => Emit("CommandRejected", command.ActorId, command.TargetId, content: reason);
    public IReadOnlyList<CombatEvent> Step(IEnumerable<CombatCommand>? commands = null)
    {
        var inputs = (commands ?? []).Take(65).ToArray();
        if (inputs.Length > 64 || inputs.Any(c => c is null)) throw new InvalidDataException("At most 64 nonnull combat commands per tick.");
        _events.Clear(); _triggers.Clear(); _processed = 0;
        TickCampaign();
        TickEndgame();
        TickProductionState();
        UpdateBorrowedMemory();
        foreach (var actor in _state.Actors)
        {
            actor.Statuses.RemoveAll(s => s.ExpiresTick <= Tick);
            if (actor.Faction == CombatFaction.Ally && actor.ExpiresTick <= Tick) { OnSummonExpired(actor); actor.Health = 0; actor.DeathProcessed = true; actor.State = "Dead"; }
        }
        _state.Actors.RemoveAll(a => a.Faction == CombatFaction.Ally && a.Health <= 0);
        if (_state.BufferedCommand is { } buffered && Player.Pending is null && Player.RecoveryUntil <= Tick && !Stunned(Player))
        {
            _state.BufferedCommand = null;
            if (_state.BufferExpiresTick >= Tick) Handle(buffered, false);
        }
        foreach (var command in inputs) Handle(command, true);
        MovePlayer();
        foreach (var actor in _state.Actors.ToArray().OrderBy(a => a.Id))
        {
            if (actor.Health <= 0) continue;
            if (actor.Pending is { } pending && pending.ResolveTick <= Tick)
            {
                actor.Pending = null;
                if (!Stunned(actor)) Resolve(actor, pending);
            }
            if (actor.Id != 1) Think(actor);
            foreach (var status in actor.Statuses.ToArray())
            {
                if (status.NextTick > Tick || status.Id is not ("Burning" or "Poisoned" or "Bleeding")) continue;
                status.NextTick += status.Id == "Burning" ? 10 : 15;
                Enqueue(new(status.SourceId, status.OwnerId, actor.Id, (status.Id == "Burning" ? 6 : 4) * status.Stacks,
                    status.Id == "Burning" ? DamageFamily.Fire : status.Id == "Bleeding" ? DamageFamily.PhysicalSlash : DamageFamily.Venom, status.Id, status.ActionId, status.Depth, Dot: true, OriginSkill: status.OriginSkill, SourceGeneration: status.SourceGeneration, FragmentId: status.FragmentId));
            }
        }
        UpdateProjectiles(); UpdateAreas(); Drain();
        if (Discipline == "Vanguard" && Tick - _state.LastAggressionTick >= 90 && Tick % 15 == 0) _state.Momentum = Math.Max(0, _state.Momentum - 2);
        _state.Tick++;
        return _events.ToArray();
    }
    private bool Stunned(CombatActor actor) => actor.Statuses.Any(s => s.Id is "Staggered" or "Frozen" or "Terrified" && s.ExpiresTick > Tick);
    private void Handle(CombatCommand command, bool allowBuffer)
    {
        if (!Enum.IsDefined(command.Kind) || command.ActorId != 1 || Player.Health <= 0) { Reject(command, "invalid_actor_or_dead"); return; }
        if (command.Kind is CombatCommandKind.Move or CombatCommandKind.Dodge && (command.X is < -1 or > 1 || command.Z is < -1 or > 1)) { Reject(command, "invalid_direction"); return; }
        if (command.Kind == CombatCommandKind.Move) { Player.MoveX = command.X; Player.MoveZ = command.Z; return; }
        if (command.Kind == CombatCommandKind.Stop) { Player.MoveX = 0; Player.MoveZ = 0; _state.BufferedCommand = null; return; }
        if (Stunned(Player)) { Reject(command, "staggered"); return; }
        if (HandleProductionCommand(command)) return;
        switch (command.Kind)
        {
            case CombatCommandKind.InteractMechanism: InteractEndgameMechanism(command); break;
            case CombatCommandKind.Cast: Cast(command, allowBuffer); break;
            case CombatCommandKind.Dodge:
                if (_state.DodgeReadyTick > Tick || (command.X == 0 && command.Z == 0)) { Reject(command, "dodge_unavailable"); break; }
                Player.Pending = null; _state.BufferedCommand = null; Player.InvulnerableUntil = Tick + 7; Player.RecoveryUntil = Tick + 5; _state.DodgeReadyTick = Tick + (HasManifestation("manifestation.stone_memory") && _state.MemoryUntilTick > Tick && _state.MemoryStacks > 0 ? (Purified("fragment.orrun_bone") ? 36 : 40) : 32);
                MoveActor(Player, new(Player.Position.X + command.X * (command.Z == 0 ? 1700 : 1202), Player.Position.Z + command.Z * (command.X == 0 ? 1700 : 1202)));
                if (_state.ProgressionBuild.BarrierOnDodge) { Player.Barrier = Math.Min(200, Player.Barrier + 15); Emit("BarrierGranted", 1, 1, 15, "rune.guard"); }
                foreach (var dodgeFragment in ActiveFragments().Where(f => f.Trigger == "Dodge")) { Player.Barrier = Math.Min(200, Player.Barrier + FragmentAmount(10)); Emit("FragmentTriggered", 1, 1, FragmentAmount(10), dodgeFragment.Id); }
                Emit("Dodged", 1); break;
            case CombatCommandKind.Potion:
                if (_state.PotionReadyTick > Tick || _state.PotionCharges <= 0 || Player.Health == Player.MaxHealth) { Reject(command, "potion_unavailable"); break; }
                _state.PotionCharges--; _state.PotionReadyTick = Tick + 120;
                int healed = Math.Min(HealingAmount(HasManifestation("manifestation.burning_blood") ? (Purified("fragment.eye_vael") ? 105 : 90) : 120), Player.MaxHealth - Player.Health); Player.Health += healed; Emit("Healed", 1, 1, healed, "potion"); break;
            case CombatCommandKind.Pickup:
                var loot = _state.Loot.FirstOrDefault(l => l.Id == command.ItemId);
                if (loot is null || Position.DistanceSquared(Player.Position, loot.Position) > 2200L * 2200 || _state.Inventory.Count >= 512) { Reject(command, "loot_unavailable"); break; }
                _state.Loot.Remove(loot); _state.Inventory.Add(loot.Item); Emit("LootPickedUp", 1, amount: (int)loot.Id, content: loot.Item.DefinitionId); break;
            case CombatCommandKind.Equip:
                var item = _state.Inventory.FirstOrDefault(i => i.Id == command.ItemId);
                if (item is null || !CanEquip(item, item.Slot)) { Reject(command, "unknown_or_incompatible_item"); break; }
                _state.Equipment[item.Slot] = item.Id; Emit("ItemEquipped", 1, content: item.DefinitionId); break;
            case CombatCommandKind.EquipFragment:
                var fragment = _content.Fragments.FirstOrDefault(f => f.Id == command.ContentId);
                if (fragment is null) { Reject(command, "unknown_fragment"); break; }
                if (_state.Fragments.TryGetValue(fragment.Slot.ToString(), out var old) && old != fragment.Id) RemoveFragment(old);
                _state.Fragments.TryAdd(fragment.Slot.ToString(), fragment.Id); Emit("FragmentEquipped", 1, content: fragment.Id); break;
            case CombatCommandKind.UnequipFragment:
                if (!_state.Fragments.Values.Contains(command.ContentId)) { Reject(command, "fragment_not_equipped"); break; }
                RemoveFragment(command.ContentId); Emit("FragmentUnequipped", 1, content: command.ContentId); break;
            case CombatCommandKind.SetMutation:
                var mutation = _content.Mutations.FirstOrDefault(m => m.Id == command.ContentId);
                if (Player.Pending is not null || Player.RecoveryUntil > Tick) { Reject(command, "mutation_during_action"); break; }
                if (command.ContentId == "" && _content.Skills.Any(s => s.Id == command.SkillId)) _state.Mutations.Remove(command.SkillId);
                else if (mutation is not null && MutationUnlocked(mutation.Id) && _content.Skills.Any(s => s.Id == mutation.SkillId && s.Discipline == Discipline) && (command.SkillId == "" || command.SkillId == mutation.SkillId)) _state.Mutations[mutation.SkillId] = mutation.Id;
                else { Reject(command, "unknown_or_incompatible_mutation"); break; }
                Emit("MutationChanged", 1, content: command.ContentId); break;
            default: Reject(command, "unsupported_command"); break;
        }
    }
    private void RemoveFragment(string id)
    {
        if (_state.Campaign?.SuppressedFragmentId == id) { _state.Campaign.SuppressedFragmentId = ""; _state.Campaign.SuppressedUntil = Tick; }
        var effect = _content.Fragments.Single(f => f.Id == id).Effect;
        if (effect == "CaptureEcho") { _state.CapturedSkillId = ""; _state.CapturedUntil = Tick; }
        if (effect == "Heat") { _state.FragmentHeat = 0; _state.OverheatedActionId = 0; }
        if (effect == "SeismicCharge") _state.SeismicCharge = 0;
        foreach (var slot in _state.Fragments.Where(p => p.Value == id).Select(p => p.Key).ToArray()) _state.Fragments.Remove(slot);
        var summonIds = _state.Actors.Where(a => a.FragmentId == id).Select(a => a.Id).ToHashSet();
        _state.Actors.RemoveAll(a => summonIds.Contains(a.Id));
        _state.Projectiles.RemoveAll(p => summonIds.Contains(p.SourceId));
        foreach (var actor in _state.Actors) actor.Statuses.RemoveAll(s => s.FragmentId == id || summonIds.Contains(s.SourceId));
    }
    private void Cast(CombatCommand command, bool allowBuffer)
    {
        var skill = _content.Skills.FirstOrDefault(s => s.Id == command.SkillId);
        if (skill is null) { Reject(command, "unknown_skill"); return; }
        var rejection = ProductionCastRejection(skill);
        if (rejection is not null) { Reject(command, rejection); return; }
        var mutation = Mutation(skill.Id); var shape = mutation?.Shape ?? skill.Shape;
        var target = _state.Actors.FirstOrDefault(a => a.Id == command.TargetId && a.Faction == CombatFaction.Enemy && a.Health > 0);
        if (shape is "Melee" or "Projectile" or "Dash" or "Command" && (target is null || !ActorVisible(target) || Position.DistanceSquared(Player.Position, target.Position) > (long)skill.Range * skill.Range || !_spatial.HasLineOfSight(Player.Position, target.Position))) { Reject(command, "invalid_target_range_or_line_of_sight"); return; }
        int cost = skill.Cost + (mutation?.ExtraCost ?? 0);
        if (_state.Cooldowns.GetValueOrDefault(skill.Id) > Tick || !CanPay(skill, cost)) { Reject(command, "cooldown_or_resource"); return; }
        if (Player.Pending is not null || Player.RecoveryUntil > Tick)
        {
            if (allowBuffer && Player.RecoveryUntil - Tick <= 6) { _state.BufferedCommand = command; _state.BufferExpiresTick = Tick + 7; Emit("InputBuffered", 1, content: skill.Id); }
            else Reject(command, "action_busy"); return;
        }
        Pay(skill, cost); _state.Cooldowns[skill.Id] = Tick + AttackDuration(skill.Cooldown);
        Player.Pending = new(skill.Id, target?.Id ?? 0, target?.Position ?? Player.Position, Tick + (mutation?.Id == "mutation.orruns_patience" ? 60 : AttackDuration(skill.Windup)), _state.NextActionId++, StartTick: Tick);
        if (_state.FragmentHeat >= 100 && skill.Cost >= 20 && ActiveFragments().Any(f => f.Effect == "Heat")) { _state.FragmentHeat = 0; _state.OverheatedActionId = Player.Pending.ActionId; }
        Player.RecoveryUntil = Tick + (mutation?.Id == "mutation.orruns_patience" ? 60 : AttackDuration(skill.Windup)) + AttackDuration(skill.Recovery); Player.State = "Windup";
        Emit("AbilityStarted", 1, target?.Id ?? 0, content: skill.Id, action: Player.Pending.ActionId);
    }
    private void MovePlayer()
    {
        if (Player.Health <= 0 || Stunned(Player) || Player.Statuses.Any(s => s.Id == "Rooted") || Player.Pending is not null) return;
        int speed = Player.MoveX != 0 && Player.MoveZ != 0 ? 106 : 150;
        if (Player.Statuses.Any(s => s.Id == "Chilled")) speed = speed * 2 / 3;
        MoveActor(Player, new(Player.Position.X + Player.MoveX * speed, Player.Position.Z + Player.MoveZ * speed));
        Player.State = Player.RecoveryUntil > Tick ? "Recover" : Player.MoveX != 0 || Player.MoveZ != 0 ? "Move" : "Idle";
    }
    private void MoveActor(CombatActor actor, Position desired)
    {
        if (actor.Statuses.Any(s => s.Id is "Frozen" or "Rooted" && s.ExpiresTick > Tick)) return;
        var moved = _spatial.Move(actor.Position, EndgameHastedMove(actor, desired), ActorRadius);
        if (!_state.Actors.Any(other => other.Id != actor.Id && other.Health > 0 && Position.DistanceSquared(moved, other.Position) < 4L * ActorRadius * ActorRadius)) actor.Position = moved;
    }
    private static Position Toward(Position from, Position target, int step)
    {
        long dx = (long)target.X - from.X, dz = (long)target.Z - from.Z;
        long length = (long)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz));
        if (length <= step) return target;
        return new(from.X + (int)(dx * step / length), from.Z + (int)(dz * step / length));
    }
    private void Think(CombatActor actor)
    {
        if (actor.Statuses.Any(s => s.Id == "Terrified" && s.ExpiresTick > Tick)) { actor.Pending = null; actor.State = "Flee"; MoveActor(actor, new(actor.Position.X + Math.Sign(actor.Position.X - Player.Position.X) * 100, actor.Position.Z + Math.Sign(actor.Position.Z - Player.Position.Z) * 100)); return; }
        if (ThinkEndgameActor(actor) || ThinkCampaignActor(actor)) return;
        if (ThinkEncounterActor(actor)) return;
        if (Stunned(actor)) { actor.State = "Staggered"; actor.Pending = null; return; }
        if (actor.Pending is not null || actor.RecoveryUntil > Tick) { actor.State = actor.Pending is null ? "Recover" : "Windup"; return; }
        var target = actor.Faction == CombatFaction.Ally ? _state.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0).OrderBy(a => Position.DistanceSquared(a.Position, actor.Position)).ThenBy(a => a.Id).FirstOrDefault() : Player.Health > 0 ? Player : null;
        if (actor.Faction == CombatFaction.Ally && _state.Actors.FirstOrDefault(a => a.Id == _state.MinionTargetId && a.Health > 0) is { } commanded) target = commanded;
        if (target is null) { actor.State = "Acquire"; return; }
        var definition = _content.Enemies.FirstOrDefault(e => e.Id == actor.DefinitionId);
        int range = definition?.Range ?? (actor.Role == "Companion" ? 1600 : 3500), speed = definition?.Speed ?? 150;
        if (HasElite(actor, "Hunter")) speed = Math.Min(500, speed * 5 / 4);
        if (actor.Statuses.Any(s => s.Id == "Chilled")) speed = speed * 2 / 3;
        long distance = Position.DistanceSquared(actor.Position, target.Position);
        if (distance > (long)range * range || !_spatial.HasLineOfSight(actor.Position, target.Position)) { actor.State = "Approach"; MoveActor(actor, Toward(actor.Position, target.Position, speed)); return; }
        if (actor.Role == "Ranged" && distance < 2500L * 2500)
        {
            actor.State = "Reposition";
            var away = new Position(actor.Position.X + Math.Sign(actor.Position.X - target.Position.X) * speed, actor.Position.Z + Math.Sign(actor.Position.Z - target.Position.Z) * speed);
            MoveActor(actor, away);
        }
        // Decision staggering is observable without making movement stutter.
        if ((Tick + actor.Id) % 3 != 0) return;
        int windup = definition?.Windup ?? 10;
        var skillId = actor.Faction == CombatFaction.Ally ? (actor.Role == "Companion" ? "summon.companion_bite" : "summon.spirit_bolt") : actor.Role == "Ranged" ? "enemy.projectile" : "enemy.strike";
        if (actor.Elite && _state.Campaign is null && Tick % 180 < 30) skillId = "enemy.stormbound";
        actor.FacingX = Math.Sign(target.Position.X - actor.Position.X); actor.FacingZ = Math.Sign(target.Position.Z - actor.Position.Z);
        actor.Pending = new(skillId, target.Id, target.Position, Tick + windup, _state.NextActionId++, actor.Generation);
        actor.RecoveryUntil = Tick + windup + (definition?.Recovery ?? 15); actor.State = "Windup";
        Emit("AbilityStarted", actor.Id, target.Id, content: skillId, action: actor.Pending.ActionId, depth: actor.Generation);
    }
    private void Resolve(CombatActor actor, CombatPending pending)
    {
        if (ResolveEndgameAbility(actor, pending) || ResolveCampaignAbility(actor, pending) || ResolveProductionAbility(actor, pending) || ResolveEncounterAbility(actor, pending)) return;
        var target = _state.Actors.FirstOrDefault(a => a.Id == pending.TargetId && a.Health > 0);
        var skill = _content.Skills.FirstOrDefault(s => s.Id == pending.SkillId);
        var mutation = actor.Id == 1 ? Mutation(pending.SkillId) : null;
        var enemy = _content.Enemies.FirstOrDefault(e => e.Id == actor.DefinitionId);
        string shape = skill is not null ? mutation?.Shape ?? skill.Shape : pending.SkillId is "enemy.projectile" or "summon.spirit_bolt" ? "Projectile" : pending.SkillId == "enemy.stormbound" ? "Area" : "Melee";
        int damage = skill?.Damage ?? enemy?.Damage ?? 12;
        damage = damage * (mutation?.DamagePercent ?? 100) / 100;
        if (mutation?.Id == "mutation.orruns_patience") damage += Math.Max(pending.ChargeTicks, (int)Math.Clamp(Tick - pending.StartTick, 0, 60));
        int owner = actor.OwnerId > 0 ? actor.OwnerId : actor.Id;
        var family = skill?.Family ?? (shape == "Projectile" ? DamageFamily.Fire : DamageFamily.PhysicalCrush);
        int radius = mutation?.Radius ?? skill?.Radius ?? 2300;
        if (shape is "Melee" or "Projectile" or "Dash" && (target is null || !_spatial.HasLineOfSight(actor.Position, target.Position) || Position.DistanceSquared(actor.Position, target.Position) > (long)(skill?.Range ?? enemy?.Range ?? 3500) * (skill?.Range ?? enemy?.Range ?? 3500)))
        { Emit("AbilityMissed", actor.Id, pending.TargetId, content: pending.SkillId, action: pending.ActionId); return; }
        switch (shape)
        {
            case "Guard": actor.Barrier = Math.Min(200, actor.Barrier + damage); Emit("BarrierGranted", actor.Id, actor.Id, damage, pending.SkillId); break;
            case "Dash":
                int separation = (int)Math.Sqrt(Position.DistanceSquared(actor.Position, target!.Position));
                MoveActor(actor, Toward(actor.Position, target.Position, Math.Max(0, separation - ActorRadius * 2 - 40)));
                foreach (var nearby in Hostiles(actor, actor.Position, radius)) Enqueue(new(actor.Id, owner, nearby.Id, damage, family, pending.SkillId, pending.ActionId, pending.Depth, Status: skill?.Status ?? ""));
                break;
            case "Area":
                if (_state.Areas.Count >= MaxAreas) { Budget(pending.ActionId); break; }
                _state.Areas.Add(new(_state.NextObjectId++, owner, actor.Id, actor.Id == 1 ? actor.Position : pending.Target, radius, pending.SkillId, damage, family, Tick, Tick + 61, pending.ActionId, pending.Depth)); break;
            case "Projectile":
                if (actor.Id == 1) { LaunchProductionProjectile(actor, pending, skill!, mutation, damage, family); break; }
                if (_state.Projectiles.Count >= MaxProjectiles) { Budget(pending.ActionId); break; }
                _state.Projectiles.Add(new(_state.NextObjectId++, owner, actor.Id, actor.Position, pending.Target, pending.TargetId, pending.SkillId, damage, family, Tick + 90, pending.ActionId, pending.Depth, actor.FragmentId)); break;
            default: Enqueue(new(actor.Id, owner, target!.Id, damage, family, pending.SkillId, pending.ActionId, pending.Depth, Status: skill?.Status ?? (actor.Role == "Companion" ? "Bleeding" : ""))); break;
        }
        AfterSkillResolved(actor, pending);
        Emit("AbilityResolved", actor.Id, pending.TargetId, content: pending.SkillId, action: pending.ActionId, depth: pending.Depth);
    }
    private IEnumerable<CombatActor> Hostiles(CombatActor source, Position position, int radius) => _state.Actors.Where(a => a.Health > 0 && Hostile(source.Faction, a.Faction) && Position.DistanceSquared(position, a.Position) <= (long)radius * radius && _spatial.HasLineOfSight(position, a.Position)).OrderBy(a => a.Id);
    private static bool Hostile(CombatFaction a, CombatFaction b) => a == CombatFaction.Enemy ? b is CombatFaction.Player or CombatFaction.Ally : b == CombatFaction.Enemy;
    private void UpdateAreas()
    {
        foreach (var area in _state.Areas.ToArray())
        {
            var source = _state.Actors.FirstOrDefault(a => a.Id == area.SourceId);
            if (source is null || area.ExpiresTick <= Tick) { _state.Areas.Remove(area); continue; }
            if (area.NextTick > Tick) continue;
            var status = _content.Skills.FirstOrDefault(s => s.Id == area.SkillId)?.Status ?? (area.SkillId == "boss.chain" ? "Staggered" : area.SkillId == "enemy.detonate" ? "Burning" : "");
            foreach (var target in Hostiles(source, area.Position, area.Radius)) Enqueue(new(area.SourceId, area.OwnerId, target.Id, area.Damage, area.Family, area.SkillId, area.ActionId, area.Depth, Status: status));
            _state.Areas[_state.Areas.IndexOf(area)] = area with { NextTick = Tick + 20 };
        }
    }
    private void Enqueue(Hit hit)
    {
        if (hit.Depth > MaxChainDepth || _effects.Count + _processed >= MaxEffectsPerTick) { Budget(hit.ActionId); return; }
        _effects.Enqueue(hit); _state.PeakEffects = Math.Max(_state.PeakEffects, _effects.Count);
    }
    private void Budget(long action)
    { _state.RejectedEffects++; Emit("EffectBudgetExceeded", content: "runtime_bound", action: action); }
    private void Drain()
    {
        while (_effects.TryDequeue(out var hit)) { _processed++; ApplyHit(hit); }
    }
    private IEnumerable<CombatItem> Equipped => _state.Inventory.Where(i => _state.Equipment.Values.Contains(i.Id));
    private void ApplyHit(Hit hit)
    {
        var target = _state.Actors.FirstOrDefault(a => a.Id == hit.TargetId);
        var source = _state.Actors.FirstOrDefault(a => a.Id == hit.SourceId);
        if (target is null || target.Health <= 0) return;
        bool critical = false;
        if (!hit.Dot && !hit.Reflected && hit.OwnerId == 1 && source?.Id == 1 && target.InvulnerableUntil <= Tick)
        {
            var rng = _state.Rng.Combat; critical = SeededRandom.Range(ref rng, 10000) < Math.Min(7500, 1500 + Equipped.Sum(i => i.CriticalBasisPoints) + _state.ProgressionBuild.CriticalBasisPoints);
            _state.Rng = _state.Rng with { Combat = rng };
        }
        bool physical = hit.Family is DamageFamily.PhysicalSlash or DamageFamily.PhysicalPierce or DamageFamily.PhysicalCrush;
        int defense = physical ? target.Armor + (target.Id == 1 ? Equipped.Sum(i => i.Armor) + _state.ProgressionBuild.Armor + _state.ProgressionBuild.Defense * 100 + _state.SeismicCharge * 10 : 0) : target.Resistance;
        defense += CampaignDefenseBonus(target) + EndgameResistanceAdjustment(target, hit.Family);
        if (target.Id == 1 && HasManifestation("manifestation.stone_memory") && _state.MemoryUntilTick > Tick && _state.MemoryAttackId == hit.ContentId) defense += _state.MemoryStacks * 1000;
        if (target.Id == 1 && AshcleaverActive && _state.Build.AshcleaverEvolution == "Orrun") defense += 500;
        if (target.Id == 1 && Discipline == "Warden" && _state.ThreatFamily == hit.Family && _state.ThreatUntil > Tick) defense += _state.ThreatStacks * 500;
        if (target.Id == 1 && Player.Pending?.SkillId == "skill.shield_breaker" && Mutation("skill.shield_breaker")?.Id == "mutation.orruns_patience") defense += 1500;
        int bonus = hit.OwnerId == 1 && source?.Id == 1 && !hit.Dot && !hit.Reflected ? Equipped.Sum(i => i.Damage) + _state.ProgressionBuild.FlatDamage + (AshcleaverActive ? _state.Build.TemperLevel * 2 : 0) : 0;
        int increased = hit.OwnerId == 1 ? _state.ProgressionBuild.Offense * 200 + (Discipline == "Arcanist" ? _state.Momentum * 40 : 0) : 0;
        increased += CampaignDamageBonus(source) + EndgameDamageBonus(hit);
        if (StormOvercharged && hit.OwnerId == 1 && hit.Depth > 0) increased += 2500;
        if (source?.Statuses.Any(s => s.Id == "Cursed") == true) increased -= 2000;
        if (target.Statuses.Any(s => s.Id == "Marked") && _content.Skills.FirstOrDefault(s => s.Id == hit.ContentId)?.Behavior == "ConsumeMarked") increased += 10000;
        var result = DamageRules.Resolve(new(hit.Damage, bonus, IncreasedBasisPoints: increased, Critical: critical, Family: hit.Family, DefenseBasisPoints: defense,
            VulnerabilityBasisPoints: target.Statuses.Any(s => s.Id == "Vulnerable") ? 2500 : 0, Barrier: target.Barrier, MoreBasisPoints: EndgameFragmentPower(hit, source), MinimumDefenseBasisPoints: EndgameRule("resistance_inversion") ? -1500 : 0, Immune: target.InvulnerableUntil > Tick || IsRituallyShielded(target), DamageOverTime: hit.Dot));
        target.Barrier -= result.Absorbed;
        if (result.Absorbed > 0) Emit("BarrierAbsorbed", target.Id, target.Id, result.Absorbed, hit.ContentId, hit.ActionId, hit.Depth);
        int healthDamage = Math.Min(target.Health, result.HealthDamage); target.Health -= healthDamage;
        Emit("DamageApplied", hit.SourceId, hit.TargetId, healthDamage, hit.ContentId, hit.ActionId, hit.Depth);
        if (critical) Emit("CriticalHit", hit.SourceId, hit.TargetId, healthDamage, hit.ContentId, hit.ActionId, hit.Depth);
        if (result.BeforeBarrier <= 0) return;
        if (hit.OwnerId == 1) _state.LastAggressionTick = Tick;
        if (!hit.Dot && !hit.Reflected && source?.Id == 1)
        {
            var skill = _content.Skills.FirstOrDefault(s => s.Id == hit.ContentId);
            if (skill is not null && skill.ResourceMode != "Heat" && ClaimResourceAction(hit.ActionId))
            {
                int gain = skill.Generate;
                if (Discipline == "Veilwalker" && (long)(source.Position.X - target.Position.X) * target.FacingX + (long)(source.Position.Z - target.Position.Z) * target.FacingZ < 0) gain += 8;
                _state.Momentum = Math.Min(100, _state.Momentum + GenerationAmount(gain));
            }
        }
        if (!string.IsNullOrEmpty(hit.Status) && target.Health > 0) ApplyStatus(target, hit.Status, hit, "");
        // Trigger dispatch uses authored trigger/effect pairs; it never tests for a named loadout.
        if (!hit.Dot && !hit.Reflected && hit.OwnerId == 1)
        {
            foreach (var fragment in ActiveFragments())
            {
                bool trigger = fragment.Trigger == "CritHit" && fragment.Effect == "Burning" && critical || fragment.Trigger == "SummonHit" && source?.Faction == CombatFaction.Ally;
                if (trigger && target.Health > 0 && _triggers.Add($"{hit.ActionId}:{fragment.Id}:{target.Id}"))
                {
                    Emit("FragmentTriggered", 1, target.Id, content: fragment.Id, action: hit.ActionId, depth: hit.Depth + 1);
                    ApplyStatus(target, fragment.Effect, hit with { Depth = hit.Depth + 1 }, fragment.Id);
                }
            }
        }
        if (target.Id == 1 && result.Absorbed > 0 && !hit.Reflected && Mutation("skill.shield_breaker")?.Id == "mutation.no_ground_given" && source is not null && Position.DistanceSquared(target.Position, source.Position) <= 3000L * 3000 && _triggers.Add($"{hit.ActionId}:counter"))
            Enqueue(new(1, 1, source.Id, result.Absorbed / 2, DamageFamily.PhysicalCrush, "effect.retaliation", hit.ActionId, hit.Depth + 1, Reflected: true));
        ApplyBuildHitEffects(hit, source, target, healthDamage, physical);
        ProductionHitEffects(hit, source, target, healthDamage, critical);
        if (target.Health <= 0 && !target.DeathProcessed) Kill(target, hit);
    }
    private IEnumerable<CombatFragment> ActiveFragments() => _content.Fragments.Where(f => _state.Fragments.Values.Contains(f.Id) && !FragmentSuppressed(f.Id) && !(BorrowedMindSuppressed && f.Slot == AnatomySlot.Mind));
    private void ApplyStatus(CombatActor target, string id, Hit hit, string fragmentId)
    {
        if (hit.Depth > MaxChainDepth) { Budget(hit.ActionId); return; }
        if (id is "Staggered" or "Frozen" or "Terrified" or "Rooted" && (target.Role is "BellSaint" or "Beast" or "Bell" or "Anchor" || HasElite(target, "Hunter"))) { Emit("StatusResisted", hit.SourceId, target.Id, content: id, action: hit.ActionId); return; }
        int duration = id switch { "Staggered" => target.Elite ? 8 : 18, "Frozen" => 30, "Terrified" => 45, "Rooted" => 45, _ => 90 };
        var status = target.Statuses.FirstOrDefault(s => s.Id == id && s.OwnerId == hit.OwnerId);
        if (status is null)
        {
            if (target.Statuses.Count >= 32) { Budget(hit.ActionId); return; }
            status = new() { Id = id, SourceId = hit.SourceId, OwnerId = hit.OwnerId, FragmentId = fragmentId, NextTick = Tick + (id == "Burning" ? 10 : 15), ActionId = hit.ActionId, Depth = hit.Depth, OriginSkill = hit.ContentId, SourceGeneration = _state.Actors.FirstOrDefault(a => a.Id == hit.SourceId)?.Generation ?? hit.SourceGeneration };
            target.Statuses.Add(status);
        }
        else if (id is "Poisoned" or "Chilled") status.Stacks = Math.Min(3, status.Stacks + 1);
        // Refresh ownership is stable: the first source retains kill credit until expiration.
        status.ExpiresTick = Tick + duration;
        if (id == "Chilled" && status.Stacks >= 3) { target.Statuses.Remove(status); ApplyStatus(target, "Frozen", hit with { Depth = hit.Depth + 1 }, fragmentId); return; }
        if (id is "Staggered" or "Frozen" or "Terrified") { target.Pending = null; target.State = "Staggered"; _state.Campaign?.Hazards.RemoveAll(h => h.SourceId == target.Id); }
        Emit("StatusApplied", hit.SourceId, target.Id, status.Stacks, id, hit.ActionId, hit.Depth);
    }
    private void Kill(CombatActor target, Hit hit)
    {
        if (TryEndgameReform(target) || TryCampaignPhaseTransition(target)) return;
        target.DeathProcessed = true; target.Pending = null; target.State = "Dead"; target.MoveX = 0; target.MoveZ = 0;
        Emit(EndgameMechanicNoRewards(target) ? "MechanismDestroyed" : _state.Campaign?.Actors.GetValueOrDefault(target.Id)?.IsEcho == true ? "EliteCopyKilled" : "EntityKilled", hit.OwnerId, target.Id, content: hit.ContentId, action: hit.ActionId, depth: hit.Depth);
        if (target.Id == 1) _state.BufferedCommand = null;
        if (target.Role == "Rusher" && hit.ContentId != "enemy.detonate")
        {
            if (_state.Areas.Count < MaxAreas) { _state.Areas.Add(new(_state.NextObjectId++, target.Id, target.Id, target.Position, 2200, "enemy.detonate", 24, DamageFamily.Fire, Tick + 15, Tick + 16, hit.ActionId, Math.Min(MaxChainDepth, hit.Depth + 1))); Emit("DeathExplosionArmed", target.Id, amount: 15, content: "enemy.detonate", action: hit.ActionId); }
            else Budget(hit.ActionId);
        }
        if (target.Faction != CombatFaction.Enemy) return;
        CampaignDeath(target);
        EndgameDeath(target);
        if (_state.Loot.Count < 512 && !_state.ResurrectedActorIds.Contains(target.Id) && CampaignRewardEligible(target))
        {
            var rng = _state.Rng.Loot;
            var eligibleItems = _content.Items.Where(i => i.Id != "item.ashcleaver").ToArray();
            var definition = eligibleItems[SeededRandom.Range(ref rng, eligibleItems.Length)];
            int roll = SeededRandom.Range(ref rng, 6);
            string rarity = definition.Id == "item.echo_ring" ? "Legendary" : roll switch { 0 => "Common", 1 or 2 => "Tempered", 3 or 4 => "Rare", _ => "Relic" };
            var item = new CombatItem(_state.NextObjectId++, definition.Id, definition.Name, definition.Slot, rarity, definition.Damage + (definition.Damage > 0 ? roll : 0), definition.Armor + (definition.Armor > 0 ? roll * 50 : 0), definition.CriticalBasisPoints + roll * 30);
            _state.Rng = _state.Rng with { Loot = rng }; _state.Loot.Add(new(item.Id, target.Position, item)); Emit("LootDropped", hit.OwnerId, target.Id, (int)item.Id, definition.Id, hit.ActionId, hit.Depth);
        }
        if (!CampaignRewardEligible(target)) return;
        OnProductionKill(target, hit);
        if (!hit.Dot || hit.OwnerId != 1) return;
        foreach (var fragment in ActiveFragments().Where(f => f.Trigger == "DotDeath"))
        {
            var source = _state.Actors.FirstOrDefault(a => a.Id == hit.SourceId);
            int generation = (source?.Generation ?? hit.SourceGeneration) + 1;
            if (generation > 2 || hit.Depth >= MaxChainDepth) { Budget(hit.ActionId); continue; }
            Emit("FragmentTriggered", 1, target.Id, content: fragment.Id, action: hit.ActionId, depth: hit.Depth + 1);
            SpawnSpirit(target, fragment, generation, hit.ActionId, hit.Depth + 1);
        }
    }
    private void SpawnSpirit(CombatActor corpse, CombatFragment fragment, int generation, long action, int depth)
    {
        if (_state.Actors.Count >= MaxActors || _state.Actors.Count(a => a.Faction == CombatFaction.Ally && a.Health > 0) >= MaxSummons) { Budget(action); return; }
        if (corpse.Health == 0 && (_state.ConsumedCorpseIds.Contains(corpse.Id) || !ClaimCorpse(corpse, fragment.Id, action))) return;
        var spirit = new CombatActor { Id = _state.NextActorId++, DefinitionId = "summon.serath_spirit", Faction = CombatFaction.Ally, Role = "Spirit", Position = corpse.Position, Health = 35, MaxHealth = 35, OwnerId = 1, Generation = generation, FragmentId = fragment.Id, ExpiresTick = Tick + 180 };
        _state.Actors.Add(spirit); Emit("SummonSpawned", 1, spirit.Id, content: fragment.Id, action: action, depth: depth);
    }
    private bool ValidEffectSource(int sourceId, int ownerId, string skillId)
    {
        var source = _state.Actors.FirstOrDefault(a => a.Id == sourceId);
        if (source is not null && ownerId == sourceId && IsEncounterSkill(skillId)) return true;
        if (sourceId == 1 && ownerId == 1 && skillId == "effect.ashcleaver_wave") return true;
        return source is not null && ownerId == (source.OwnerId > 0 ? source.OwnerId : source.Id) && (sourceId == 1 ? _content.Skills.Any(s => s.Id == skillId) : source.Faction == CombatFaction.Ally ? skillId is "summon.spirit_bolt" or "summon.companion_bite" : skillId is "enemy.projectile" or "enemy.stormbound");
    }
    private void ValidateSnapshot()
    {
        void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid, string reason) { if (!valid) throw new InvalidDataException("Invalid combat snapshot: " + reason); }
        ValidateBuild(_state.Build);
        ValidateProgressionBuild(_state.ProgressionBuild);
        Require(_state.MemoryStacks is >= 0 and <= 3 && _state.MemoryUntilTick >= 0 && _state.MemoryUntilTick <= Tick + 3000 && _state.MemoryAttackId is not null, "Stone Memory state");
        Require(_state.SchemaVersion == 1 && _state.RulesVersion == "combat.1" && _state.ContentHash == _content.Identity, "incompatible rules or content");
        Require(Presets.Contains(_state.Preset) && _state.Tick is >= 0 and <= 1000000000 && _state.Momentum is >= 0 and <= 100 && _state.PotionCharges is >= 0 and <= 3, "time/resource bounds");
        Require(_state.Actors is not null && _state.Projectiles is not null && _state.Areas is not null && _state.Inventory is not null && _state.Loot is not null && _state.Fragments is not null && _state.Equipment is not null && _state.Mutations is not null && _state.Cooldowns is not null && _state.Rng is not null, "null collections");
        Require(KnownEncounter(_state.EncounterId) && _state.ResurrectedActorIds is not null && _state.ResurrectedActorIds.Count <= 2 && _state.ResurrectedActorIds.Distinct().Count() == _state.ResurrectedActorIds.Count && _state.ResurrectedActorIds.All(id => _state.Actors.Any(a => a.Id == id && a.Faction == CombatFaction.Enemy)), "encounter/resurrection state");
        Require(_state.Actors!.Count is > 0 and <= MaxActors && _state.Actors.All(a => a is not null) && _state.Actors.Select(a => a.Id).Distinct().Count() == _state.Actors.Count && _state.Actors.Count(a => a.Id == 1 && a.Faction == CombatFaction.Player) == 1, "actor identity");
        foreach (var actor in _state.Actors)
        {
            Require(actor.Id > 0 && actor.FacingX is >= -1 and <= 1 && actor.FacingZ is >= -1 and <= 1 && actor.SpecialCycle is >= 0 and <= 1000000000 && Enum.IsDefined(actor.Faction) && actor.MaxHealth is > 0 and <= 1000000 && actor.Health >= 0 && actor.Health <= actor.MaxHealth && actor.Barrier is >= 0 and <= 200 && actor.Armor is >= 0 and <= 7500 && actor.Resistance is >= 0 and <= 7500 && actor.MoveX is >= -1 and <= 1 && actor.MoveZ is >= -1 and <= 1 && actor.Generation is >= 0 and <= 2 && _spatial.CanOccupy(actor.Position, ActorRadius), "actor values");
            Require(actor.Id == 1 ? actor.DefinitionId == "player.vanguard" : actor.Faction == CombatFaction.Ally ? actor.DefinitionId is "summon.serath_spirit" or "summon.ancestor" or "summon.companion" or "summon.fire_spirit" or "summon.flaming_revenant" && actor.OwnerId == 1 && (actor.FragmentId == "" || _content.Fragments.Any(f => f.Id == actor.FragmentId && f.Effect == "Spirit")) : actor.Faction == CombatFaction.Enemy && _content.Enemies.Any(e => e.Id == actor.DefinitionId && e.Role == actor.Role), "actor definition");
            Require((actor.Health == 0) == actor.DeathProcessed, "death flag");
            Require(actor.RecoveryUntil >= 0 && actor.RecoveryUntil <= Tick + 3000 && actor.InvulnerableUntil >= 0 && actor.InvulnerableUntil <= Tick + 3000 && actor.ExpiresTick >= 0 && actor.ExpiresTick <= Tick + 3000, "actor timers");
            Require(actor.Statuses is not null && actor.Statuses.Count <= 32 && actor.Statuses.All(s => s is not null && StatusIds.Contains(s.Id) && s.Stacks is >= 1 and <= 3 && s.SourceGeneration is >= 0 and <= 2 && s.Depth is >= 0 and <= MaxChainDepth && s.SourceId > 0 && s.OwnerId > 0 && s.ActionId > 0 && s.NextTick >= 0 && s.NextTick <= Tick + 3000 && s.ExpiresTick >= 0 && s.ExpiresTick <= Tick + 3000 && s.ActionId < _state.NextActionId && s.SourceId < _state.NextActorId && (s.FragmentId == "" || _content.Fragments.Any(f => f.Id == s.FragmentId))), "statuses");
            if (actor.Pending is { } p) Require(p.ResolveTick >= Tick && p.ResolveTick <= Tick + 3000 && p.StartTick >= 0 && p.StartTick <= Tick && p.ChargeTicks is >= 0 and <= 60 && p.ActionId > 0 && p.ActionId < _state.NextActionId && p.Depth is >= 0 and <= MaxChainDepth && _spatial.CanOccupy(p.Target, 0) && (p.TargetId == 0 || _state.Actors.Any(a => a.Id == p.TargetId)) && (actor.Id == 1 ? _content.Skills.Any(s => s.Id == p.SkillId) : actor.Faction == CombatFaction.Ally ? p.SkillId is "summon.spirit_bolt" or "summon.companion_bite" : (p.SkillId is "enemy.projectile" or "enemy.strike" or "enemy.stormbound" || IsEncounterSkill(p.SkillId) || CampaignSkill(p.SkillId) || _state.Endgame is not null && p.SkillId == "endgame.boss_tell")), "pending ability");
        }
        Require(_state.Projectiles!.Count <= MaxProjectiles && _state.Areas!.Count <= MaxAreas && _state.Loot!.Count <= 512 && _state.Inventory!.Count <= 512, "object budgets");
        Require(_state.Loot.All(l => l is not null && l.Item is not null && l.Id == l.Item.Id && _spatial.CanOccupy(l.Position, 0)), "loot");
        var allItems = _state.Inventory.Concat(_state.Loot.Select(l => l.Item)).ToArray();
        Require(allItems.All(i => i is not null && i.Id > 0 && i.Damage is >= 0 and <= 1000 && i.Armor is >= 0 and <= 7500 && i.CriticalBasisPoints is >= 0 and <= 7500 && _content.Items.Any(d => d.Id == i.DefinitionId && d.Slot == i.Slot)) && allItems.Select(i => i.Id).Distinct().Count() == allItems.Length, "item values or duplicate identity");
        Require(_state.Equipment!.Values.Distinct().Count() == _state.Equipment.Count, "item equipped more than once");
        foreach (var equipped in _state.Equipment!) Require(_state.Inventory.Any(i => i.Id == equipped.Value && CanEquip(i, equipped.Key)), "equipped item");
        foreach (var fragment in _state.Fragments!) Require(_content.Fragments.Any(f => f.Id == fragment.Value && f.Slot.ToString() == fragment.Key), "fragment slot");
        Require(_content.Fragments.Any(f => f.Effect == "Heat" && _state.Fragments.Values.Contains(f.Id)) || _state.FragmentHeat == 0 && _state.OverheatedActionId == 0, "heat requires its equipped fragment");
        Require(_content.Fragments.Any(f => f.Effect == "CaptureEcho" && _state.Fragments.Values.Contains(f.Id)) || _state.Experiment?.Status == "Bound" || _state.CapturedSkillId == "", "captured echo requires its equipped fragment or scoped loan");
        Require(_content.Fragments.Any(f => f.Effect == "SeismicCharge" && _state.Fragments.Values.Contains(f.Id)) || _state.SeismicCharge == 0, "seismic charge requires its equipped fragment");
        foreach (var mutation in _state.Mutations!) Require(_content.Mutations.Any(m => m.Id == mutation.Value && m.SkillId == mutation.Key), "mutation");
        foreach (var cooldown in _state.Cooldowns!) Require((_content.Skills.Any(s => s.Id == cooldown.Key) || _content.Fragments.Any(f => f.Id == cooldown.Key) || cooldown.Key == "effect.ashcleaver_wave") && cooldown.Value >= 0 && cooldown.Value <= Tick + 3000, "cooldown");
        Require(_state.NextActorId > _state.Actors.Max(a => a.Id) && _state.NextActorId <= 1000000000 && _state.NextObjectId > 0 && _state.NextObjectId <= 1000000000000 && _state.NextActionId > 0 && _state.NextActionId <= 1000000000000, "identity counters");
        Require(_state.LastAggressionTick >= 0 && _state.LastAggressionTick <= Tick && _state.PotionReadyTick >= 0 && _state.PotionReadyTick <= Tick + 3000 && _state.DodgeReadyTick >= 0 && _state.DodgeReadyTick <= Tick + 3000 && _state.BufferExpiresTick >= 0 && _state.BufferExpiresTick <= Tick + 3000 && _state.PeakEffects is >= 0 and <= MaxEffectsPerTick && _state.RejectedEffects >= 0, "timers and budgets");
        foreach (var projectile in _state.Projectiles) Require(projectile is not null && projectile.Id > 0 && projectile.Damage is >= 0 and <= 10000 && projectile.Depth is >= 0 and <= MaxChainDepth && Enum.IsDefined(projectile.Family) && _spatial.CanOccupy(projectile.Position, 0) && _spatial.CanOccupy(projectile.Target, 0) && projectile.Pierce is >= 0 and <= 3 && projectile.Fork is >= 0 and <= 2 && projectile.Chain is >= 0 and <= 3 && projectile.ImpactRadius is >= 0 and <= 10000 && (projectile.HitIds is null || projectile.HitIds.Length <= 32 && projectile.HitIds.Distinct().Count() == projectile.HitIds.Length) && projectile.ExpiresTick >= Tick - 1 && projectile.ExpiresTick <= Tick + 3000 && projectile.ActionId > 0 && projectile.ActionId < _state.NextActionId && ValidEffectSource(projectile.SourceId, projectile.OwnerId, projectile.SkillId), "projectile");
        foreach (var area in _state.Areas) Require(area is not null && area.Id > 0 && area.Damage is >= 0 and <= 10000 && area.Radius is >= 0 and <= 10000 && area.Depth is >= 0 and <= MaxChainDepth && Enum.IsDefined(area.Family) && _spatial.CanOccupy(area.Position, 0) && area.ExpiresTick >= Tick - 1 && area.ExpiresTick <= Tick + 3000 && area.NextTick >= 0 && area.NextTick <= Tick + 3000 && area.ActionId > 0 && area.ActionId < _state.NextActionId && ValidEffectSource(area.SourceId, area.OwnerId, area.SkillId), "area");
        var objectIds = allItems.Select(i => i.Id).Concat(_state.Projectiles.Select(p => p.Id)).Concat(_state.Areas.Select(a => a.Id)).ToArray();
        Require(objectIds.Distinct().Count() == objectIds.Length && objectIds.All(id => id < _state.NextObjectId), "object identity counters");
        Require(_state.ResourceActions is not null && _state.ResourceActions.Count <= 512 && _state.ResourceActions.All(p => p.Key > 0 && p.Key < _state.NextActionId && p.Value >= Tick - 1 && p.Value <= Tick + 300), "resource action receipts");
        Require(_state.ConsumedCorpseIds is not null && _state.ConsumedCorpseIds.Count <= MaxActors && _state.ConsumedCorpseIds.All(id => _state.Actors.Any(a => a.Id == id && a.Faction == CombatFaction.Enemy)) && _state.TemporaryLife is >= 0 and <= 60 && _state.TemporaryLifeUntil >= 0 && _state.TemporaryLifeUntil <= Tick + 3000 && _state.CapturedSkillId is "" or "skill.echo_storm" && _state.CapturedUntil >= 0 && _state.CapturedUntil <= Tick + 3000 && _state.FragmentHeat is >= 0 and <= 100 && _state.SeismicCharge is >= 0 and <= 90 && _state.ThreatStacks is >= 0 and <= 4 && _state.ThreatUntil >= 0 && _state.ThreatUntil <= Tick + 3000 && _state.OverheatedActionId >= 0 && _state.OverheatedActionId < _state.NextActionId && (_state.MinionTargetId == 0 || _state.Actors.Any(a => a.Id == _state.MinionTargetId && a.Faction == CombatFaction.Enemy)) && (_state.ThreatFamily is null || Enum.IsDefined(_state.ThreatFamily.Value)), "production runtime state");
        if (_state.BufferedCommand is { } command) Require(command.Kind == CombatCommandKind.Cast && command.ActorId == 1 && _content.Skills.Any(s => s.Id == command.SkillId), "buffered input");
        ValidateCampaignSnapshot();
        ValidateEndgameSnapshot();
        ValidateBorrowedMemory();
    }
}
