using System.Text.Json;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Coop;

/// <summary>One server-owned world. Clients submit intent; they never supply actor state or rewards.</summary>
public sealed partial class CoopCombatSession
{
    public const int MaxActors = 24, MaxProjectiles = 64, MaxWarnings = 48, MaxQueuedInputsPerPlayer = 8;
    public const int MaxInputAge = 6, MaxFutureTicks = 6, InputTimeoutTicks = 10, ActorRadius = 280;
    public const string RulesVersion = "coop.1";
    public static IReadOnlyList<string> EncounterIds { get; } = Array.AsReadOnly(new[] { "coop.ossuary", "coop.cloister", "coop.bell_saint.1", "coop.bell_saint.2", "coop.bell_saint.3" });
    private static readonly string[] SkillIds = ["skill.cleave", "skill.shield_breaker", "skill.seismic_wave", "skill.charge", "skill.iron_guard", "skill.cataclysm"];
    private static readonly string[][] Loadouts = [["item.ash_axe", "item.march_plate", "item.ember_lens"], ["item.oath_hammer", "item.march_plate", "item.ember_lens"]];
    private readonly CombatContent _content;
    private readonly SpatialWorld _spatial;
    private readonly CoopSnapshot _state;
    private readonly List<CoopEvent> _events = [];
    private CoopCombatSession(CombatContent content, CoopSnapshot state) { _content = content; _spatial = new(content.Room); _state = state; }
    public long Tick => _state.Tick;
    public string ContentHash => _content.Identity;
    public string StateHash => JsonData.Hash(_state);
    public RoomDefinition Room => JsonData.Copy(_content.Room);
    public string EncounterId => EncounterIds[_state.EncounterIndex];
    public CoopSnapshot Capture() => JsonData.Copy(_state);
    public CoopView View => new(Tick, ContentHash, _state.MatchId, EncounterId, _state.EncounterIndex, _state.Attempt,
        $"{_state.MatchId}:{_state.EncounterIndex}:{_state.Attempt}", _state.Cleared, _state.AwaitingRetry, _state.Completed, Room,
        _state.Actors.Select(a => new CoopActorView(a.Id, a.PlayerId, a.DefinitionId, a.Role, a.Position, a.Health, a.MaxHealth, a.Barrier,
            a.Health <= 0 ? "Dead" : a.Pending is not null ? "Windup" : a.RecoveryUntil > Tick ? "Recover" : "Active",
            a.Statuses.Select(s => s.Id).ToArray(), a.Pending?.SkillId, (int)Math.Max(0, (a.Pending?.ResolveTick ?? Tick) - Tick), Shielded(a))).ToArray(),
        _state.Players.Select(p => new CoopPlayerView(p.Id, p.Connected, p.Ready, p.Id == 1 ? "Ash axe Vanguard" : "Oath hammer Vanguard", p.Momentum, p.PotionCharges,
            Remaining(p.PotionReadyTick), Remaining(p.DodgeReadyTick), p.AcceptedSequence, p.ProcessedSequence,
            SkillIds.Select(id => _content.Skills.Single(s => s.Id == id)).Select(s => new CoopSkillView(s.Id, s.Name, s.Shape, s.Cost, s.Generate, Remaining(p.Cooldowns.GetValueOrDefault(s.Id)), s.Range, s.Radius)).ToArray())).ToArray(),
        _state.Projectiles.ToArray(), _state.Warnings.ToArray(), JsonData.Copy(_state.Rewards.ToArray()),
        new(_state.Actors.Count, _state.Projectiles.Count, _state.Warnings.Count, _state.Inputs.Count, _state.PeakActors, _state.PeakProjectiles, _state.PeakWarnings, _state.PeakQueuedInputs));
    private int Remaining(long until) => (int)Math.Max(0, until - Tick);
    public static CoopCombatSession Create(string combatJson, ulong seed = 42, string encounterId = "coop.ossuary", string matchId = "local")
    {
        var content = CombatContent.Parse(combatJson);
        if (!EncounterIds.Contains(encounterId)) throw new ArgumentException("Unknown co-op slice encounter.", nameof(encounterId));
        if (!ValidMatchId(matchId)) throw new ArgumentException("Invalid co-op match ID.", nameof(matchId));
        if (SkillIds.Any(id => !content.Skills.Any(s => s.Id == id && s.Discipline == "Vanguard")) || Loadouts.SelectMany(x => x).Any(id => !content.Items.Any(i => i.Id == id))) throw new InvalidDataException("Co-op fixed loadouts require the authored Vanguard slice definitions.");
        var random = SeededRandom.Streams(seed);
        var state = new CoopSnapshot { Seed = seed, MatchId = matchId, ContentHash = content.Identity, CombatRng = random.Combat, LootRng = random.Loot, EncounterIndex = EncounterIds.ToList().IndexOf(encounterId) };
        foreach (int id in new[] { 1, 2 })
        {
            state.Players.Add(new() { Id = id });
            state.Actors.Add(new() { Id = id, PlayerId = id, DefinitionId = "player.vanguard", Role = "Vanguard", Health = id == 1 ? 350 : 390, MaxHealth = id == 1 ? 350 : 390, Armor = Loadouts[id - 1].Sum(item => content.Items.Single(i => i.Id == item).Armor) });
        }
        var session = new CoopCombatSession(content, state); session.Populate(restore: true); session.Validate(); return session;
    }
    public static CoopCombatSession Restore(string combatJson, CoopSnapshot snapshot)
    {
        if (snapshot is null) throw new InvalidDataException("Missing co-op snapshot.");
        var content = CombatContent.Parse(combatJson);
        var session = new CoopCombatSession(content, JsonData.Copy(snapshot)); session.Validate(); return session;
    }
    public CoopInputResult SubmitJson(int authenticatedPlayerId, string json)
    {
        if (json is null || json.Length > 4096) return Rejected(authenticatedPlayerId, 0, "invalid_payload");
        try { return Submit(authenticatedPlayerId, JsonData.Read<CoopInput>(json)); }
        catch (JsonException) { return Rejected(authenticatedPlayerId, 0, "invalid_payload"); }
    }
    public CoopInputResult Submit(int authenticatedPlayerId, CoopInput input)
    {
        var player = _state.Players.FirstOrDefault(p => p.Id == authenticatedPlayerId);
        long sequence = input?.Sequence ?? 0;
        if (player is null) return Rejected(authenticatedPlayerId, sequence, "unknown_player");
        if (input is null || !ValidInput(input)) return Rejected(player.Id, sequence, "invalid_intent");
        if (!player.Connected) return Rejected(player.Id, sequence, "disconnected");
        string hash = JsonData.Hash(input);
        var receipt = player.Receipts.FirstOrDefault(r => r.Sequence == sequence);
        if (receipt is not null) return new(receipt.Hash == hash, receipt.Hash == hash ? "duplicate" : "changed_duplicate", sequence, Tick, player.AcceptedSequence, player.ProcessedSequence);
        if (sequence <= player.AcceptedSequence || sequence - player.AcceptedSequence > 1024) return Rejected(player.Id, sequence, "sequence_window");
        if (input.ClientTick < Math.Max(0, Tick - MaxInputAge) || input.ClientTick > Tick + MaxFutureTicks) return Rejected(player.Id, sequence, "tick_window");
        long applyTick = Math.Max(Tick + 1, input.ClientTick);
        if (_state.Inputs.Count(q => q.PlayerId == player.Id) >= MaxQueuedInputsPerPlayer || _state.Inputs.Any(q => q.PlayerId == player.Id && q.ApplyTick >= applyTick)) return Rejected(player.Id, sequence, "queue_full_or_tick_occupied");
        _state.Inputs.Add(new(player.Id, applyTick, input)); player.AcceptedSequence = sequence;
        player.Receipts.Add(new(sequence, hash)); if (player.Receipts.Count > 64) player.Receipts.RemoveAt(0);
        _state.PeakQueuedInputs = Math.Max(_state.PeakQueuedInputs, _state.Inputs.Count);
        return new(true, "accepted", sequence, Tick, player.AcceptedSequence, player.ProcessedSequence);
    }
    private CoopInputResult Rejected(int id, long sequence, string code)
    {
        var player = _state.Players.FirstOrDefault(p => p.Id == id);
        return new(false, code, sequence, Tick, player?.AcceptedSequence ?? 0, player?.ProcessedSequence ?? 0);
    }
    private static bool ValidInput(CoopInput input) => input.Sequence is > 0 and <= 1000000000000 && input.ClientTick is >= 0 and <= 1000000000 && input.MoveX is >= -1 and <= 1 && input.MoveZ is >= -1 and <= 1 && Enum.IsDefined(input.Action)
        && input.SkillId is not null && input.TargetId is >= 0 and <= 1000000000 && (input.Action == CoopInputAction.Cast ? SkillIds.Contains(input.SkillId) : input.SkillId == "" && input.TargetId == 0);
    public void SetConnected(int playerId, bool connected)
    {
        var player = _state.Players.SingleOrDefault(p => p.Id == playerId) ?? throw new ArgumentOutOfRangeException(nameof(playerId));
        player.Connected = connected; player.Ready = false; player.MoveX = player.MoveZ = 0;
        _state.Inputs.RemoveAll(q => q.PlayerId == playerId);
        // Existing actions, damage, resources and receipts remain authoritative across reconnects.
    }
    public IReadOnlyList<CoopEvent> Step()
    {
        if (Tick >= 1000000000) throw new InvalidOperationException("Co-op tick budget exhausted.");
        _events.Clear(); _state.Tick++;
        foreach (var queued in _state.Inputs.Where(q => q.ApplyTick <= Tick).OrderBy(q => q.PlayerId).ThenBy(q => q.Input.Sequence).ToArray())
        {
            _state.Inputs.Remove(queued); var player = Player(queued.PlayerId);
            player.ProcessedSequence = queued.Input.Sequence;
            if (player.Connected) ApplyInput(player, queued.Input);
        }
        if ((_state.Cleared || _state.AwaitingRetry) && !_state.Completed && _state.Players.All(p => p.Connected && p.Ready))
        {
            bool retry = _state.AwaitingRetry;
            if (retry) _state.Attempt++; else { _state.EncounterIndex++; _state.Attempt = 0; }
            Populate(restore: retry); Emit(retry ? "EncounterRetried" : "EncounterEntered", content: EncounterId);
        }
        if (!_state.AwaitingRetry && !_state.Completed)
        {
            UpdateStatuses();
            foreach (var player in _state.Players)
            {
                if (!player.Connected || Tick - player.LastInputTick > InputTimeoutTicks) player.MoveX = player.MoveZ = 0;
                var actor = Actor(player.Id);
                if (actor.Health > 0 && actor.Pending is null && !Stunned(actor))
                {
                    int speed = player.MoveX != 0 && player.MoveZ != 0 ? 106 : 150;
                    Move(actor, new(actor.Position.X + player.MoveX * speed, actor.Position.Z + player.MoveZ * speed));
                }
            }
            foreach (var actor in _state.Actors.Where(a => a.Health > 0 && a.Pending is not null && a.Pending.ResolveTick <= Tick).OrderBy(a => a.Id).ToArray()) Resolve(actor);
            UpdateProjectiles(); UpdateWarnings();
            if (!_state.Cleared) foreach (var actor in _state.Actors.Where(a => a.PlayerId == 0 && a.Health > 0).OrderBy(a => a.Id).ToArray()) Think(actor);
            CheckEncounter();
        }
        _state.PeakActors = Math.Max(_state.PeakActors, _state.Actors.Count);
        _state.PeakProjectiles = Math.Max(_state.PeakProjectiles, _state.Projectiles.Count);
        _state.PeakWarnings = Math.Max(_state.PeakWarnings, _state.Warnings.Count);
        return _events.ToArray();
    }
    private void ApplyInput(CoopPlayer player, CoopInput input)
    {
        player.LastInputTick = Tick; player.MoveX = input.MoveX; player.MoveZ = input.MoveZ;
        var actor = Actor(player.Id);
        if (input.Action == CoopInputAction.Ready)
        {
            if (_state.Cleared || _state.AwaitingRetry) { player.Ready = true; Emit("PlayerReady", actor.Id); }
            else Emit("InputRejected", actor.Id, content: "encounter_not_resolved");
            return;
        }
        if (input.Action == CoopInputAction.None) return;
        if (actor.Health <= 0 || _state.Cleared || _state.AwaitingRetry || _state.Completed || Stunned(actor)) { Emit("InputRejected", actor.Id, content: "actor_unavailable"); return; }
        switch (input.Action)
        {
            case CoopInputAction.Cast: Cast(player, actor, input); break;
            case CoopInputAction.Dodge:
                if (player.DodgeReadyTick > Tick || input.MoveX == 0 && input.MoveZ == 0) { Emit("InputRejected", actor.Id, content: "dodge_unavailable"); break; }
                actor.Pending = null; actor.InvulnerableUntil = Tick + 7; actor.RecoveryUntil = Tick + 5; player.DodgeReadyTick = Tick + 32;
                int step = input.MoveX != 0 && input.MoveZ != 0 ? 1202 : 1700;
                Move(actor, new(actor.Position.X + input.MoveX * step, actor.Position.Z + input.MoveZ * step)); Emit("Dodged", actor.Id); break;
            case CoopInputAction.Potion:
                if (player.PotionCharges == 0 || player.PotionReadyTick > Tick || actor.Health == actor.MaxHealth) { Emit("InputRejected", actor.Id, content: "potion_unavailable"); break; }
                player.PotionCharges--; player.PotionReadyTick = Tick + 120; int healed = Math.Min(120, actor.MaxHealth - actor.Health); actor.Health += healed; Emit("Healed", actor.Id, actor.Id, healed, "potion"); break;
        }
    }
    private void Cast(CoopPlayer player, CoopActor actor, CoopInput input)
    {
        var skill = _content.Skills.Single(s => s.Id == input.SkillId);
        var target = _state.Actors.FirstOrDefault(a => a.Id == input.TargetId && a.PlayerId == 0 && a.Health > 0);
        if (actor.Pending is not null || actor.RecoveryUntil > Tick || player.Cooldowns.GetValueOrDefault(skill.Id) > Tick || player.Momentum < skill.Cost) { Emit("InputRejected", actor.Id, content: "busy_cooldown_or_resource"); return; }
        if (skill.Shape is "Melee" or "Projectile" or "Dash" && (target is null || Position.DistanceSquared(actor.Position, target.Position) > (long)skill.Range * skill.Range || !_spatial.HasLineOfSight(actor.Position, target.Position))) { Emit("InputRejected", actor.Id, content: "invalid_target_range_or_sight"); return; }
        player.Momentum -= skill.Cost; player.Cooldowns[skill.Id] = Tick + skill.Cooldown;
        actor.Pending = new(skill.Id, target?.Id ?? 0, target?.Position ?? actor.Position, Tick + skill.Windup, _state.NextActionId++);
        actor.RecoveryUntil = Tick + skill.Windup + skill.Recovery; Emit("AbilityStarted", actor.Id, target?.Id ?? 0, content: skill.Id, action: actor.Pending.ActionId);
    }
    private void Populate(bool restore)
    {
        _state.Actors.RemoveAll(a => a.PlayerId == 0); _state.Projectiles.Clear(); _state.Warnings.Clear(); _state.Inputs.Clear();
        _state.Cleared = _state.AwaitingRetry = _state.Completed = false;
        foreach (var player in _state.Players)
        {
            player.Ready = false; player.MoveX = player.MoveZ = 0;
            var actor = Actor(player.Id); actor.Position = new(_content.Room.PlayerSpawn.X, _content.Room.PlayerSpawn.Z + (player.Id == 1 ? -450 : 450));
            actor.Pending = null; actor.Statuses.Clear(); actor.Barrier = 0; actor.RecoveryUntil = Tick; actor.InvulnerableUntil = Tick;
            if (restore) { actor.Health = actor.MaxHealth; player.PotionCharges = 3; player.Momentum = 0; player.Cooldowns.Clear(); player.PotionReadyTick = player.DodgeReadyTick = Tick; }
            else if (actor.Health == 0) actor.Health = actor.MaxHealth / 2;
            actor.DeathProcessed = false;
        }
        switch (_state.EncounterIndex)
        {
            case 0: AddEnemy("enemy.ash_ghoul", new(-1000, -1500)); AddEnemy("enemy.ash_ghoul", new(2000, 1500)); AddEnemy("enemy.cinder_acolyte", new(4300, 2000)); AddEnemy("enemy.emberling", new(4500, -2200)); break;
            case 1: AddEnemy("enemy.furnace_brute", new(1200, 0)); AddEnemy("enemy.cinder_priest", new(5000, 1500)); AddEnemy("enemy.ash_ghoul", new(2500, -1800)); AddEnemy("enemy.emberling", new(4600, -2500)); break;
            case 2: AddEnemy("enemy.bell_saint", new(3200, 0)); break;
            case 3:
                AddEnemy("enemy.bell_saint", new(2500, 0)); AddEnemy("enemy.ritual_anchor", new(-5500, 0)); AddEnemy("enemy.ritual_anchor", new(5500, 0));
                AddEnemy("enemy.ash_ghoul", new(-2200, -2500), true); AddEnemy("enemy.ash_ghoul", new(2200, 2500), true); break;
            case 4: AddEnemy("enemy.bell_beast", new(1800, 0)); AddEnemy("enemy.broken_bell", new(-5000, -5500)); AddEnemy("enemy.broken_bell", new(5000, 5500)); break;
        }
        _state.PeakActors = Math.Max(_state.PeakActors, _state.Actors.Count);
    }
    private void AddEnemy(string id, Position position, bool corpse = false)
    {
        if (_state.Actors.Count >= MaxActors) return;
        var definition = _content.Enemies.Single(e => e.Id == id); int health = definition.Health * 8 / 5;
        if (!_spatial.CanOccupy(position, ActorRadius)) throw new InvalidDataException("Co-op authored spawn is obstructed.");
        _state.Actors.Add(new() { Id = _state.NextActorId++, DefinitionId = id, Role = definition.Role, Position = position, MaxHealth = health, Health = corpse ? 0 : health, Armor = definition.Armor, DeathProcessed = corpse });
    }
    private void CheckEncounter()
    {
        if (!_state.Cleared && _state.Actors.All(a => a.PlayerId > 0 || a.Health == 0))
        {
            _state.Cleared = true; _state.Completed = _state.EncounterIndex == 4;
            _state.Projectiles.Clear(); _state.Warnings.Clear();
            foreach (var actor in _state.Actors) { actor.Pending = null; actor.Statuses.Clear(); }
            foreach (var player in _state.Players)
            {
                string receiptId = $"{_state.MatchId}:{_state.EncounterIndex}:player:{player.Id}";
                if (_state.Rewards.Any(r => r.Id == receiptId)) continue;
                var definitions = _content.Items.Where(i => i.Id != "item.ashcleaver").ToArray();
                ulong random = _state.LootRng; var item = definitions[SeededRandom.Range(ref random, definitions.Length)]; int roll = SeededRandom.Range(ref random, 6); _state.LootRng = random;
                var reward = new CoopRewardReceipt(receiptId, player.Id, EncounterId, _state.Attempt, _state.EncounterIndex == 4 ? 250 : 100, _state.EncounterIndex == 4 ? 60 : 20,
                    new(_state.NextObjectId++, item.Id, item.Name, item.Slot, roll >= 4 ? "Rare" : "Tempered", item.Damage + roll, item.Armor, item.CriticalBasisPoints));
                _state.Rewards.Add(reward); Emit("PersonalRewardGranted", player.Id, amount: reward.Experience, content: reward.Id);
            }
            Emit(_state.Completed ? "SliceCompleted" : "EncounterCleared", content: EncounterId);
        }
        else if (!_state.Cleared && !_state.AwaitingRetry && _state.Actors.Where(a => a.PlayerId > 0).All(a => a.Health == 0))
        {
            _state.AwaitingRetry = true; _state.Projectiles.Clear(); _state.Warnings.Clear();
            foreach (var actor in _state.Actors) { actor.Pending = null; actor.Statuses.Clear(); }
            foreach (var player in _state.Players) player.Ready = false;
            Emit("PartyWiped", content: EncounterId);
        }
    }
    private CoopPlayer Player(int id) => _state.Players.Single(p => p.Id == id);
    private CoopActor Actor(int id) => _state.Actors.Single(a => a.Id == id);
    private void Emit(string kind, int actor = 0, int target = 0, int amount = 0, string content = "", long action = 0)
    {
        if (_events.Count < 512) _events.Add(new(Tick, kind, actor, target, amount, content, action));
    }
    private static bool ValidMatchId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
