using System.Diagnostics.CodeAnalysis;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Coop;

public sealed partial class CoopCombatSession
{
    private void Validate()
    {
        static void Require([DoesNotReturnIf(false)] bool value, string reason) { if (!value) throw new InvalidDataException("Invalid co-op snapshot: " + reason); }
        Require(_state.SchemaVersion == 1 && _state.RulesVersion == RulesVersion && _state.ContentHash == _content.Identity, "schema, rules or content identity");
        Require(ValidMatchId(_state.MatchId) && Tick is >= 0 and <= 1000000000 && _state.EncounterIndex is >= 0 and < 5 && _state.Attempt is >= 0 and <= 100000, "match, time or encounter");
        Require(_state.Actors is not null && _state.Players is not null && _state.Projectiles is not null && _state.Warnings is not null && _state.Inputs is not null && _state.Rewards is not null, "null collections");
        Require(_state.Actors.Count is >= 3 and <= MaxActors && _state.Actors.All(a => a is not null) && _state.Actors.Select(a => a.Id).Distinct().Count() == _state.Actors.Count, "actor identity");
        Require(_state.Players.Count == 2 && _state.Players.All(p => p is not null) && _state.Players.Select(p => p.Id).Order().SequenceEqual(new[] { 1, 2 }), "player slots");
        Require(_state.NextActorId > _state.Actors.Max(a => a.Id) && _state.NextActorId <= 1000000 && _state.NextObjectId is > 0 and <= 1000000000 && _state.NextActionId is > 0 and <= 1000000000, "counters");
        Require(SkillIds.All(id => _content.Skills.Any(s => s.Id == id && s.Discipline == "Vanguard")) && Loadouts.SelectMany(x => x).All(id => _content.Items.Any(i => i.Id == id)), "fixed loadout definitions");
        bool Timer(long value) => value >= 0 && value <= Tick + 3000;
        bool Point(Position p) => Math.Abs((long)p.X) <= _content.Room.HalfWidth && Math.Abs((long)p.Z) <= _content.Room.HalfDepth;
        bool Action(long id) => id > 0 && id < _state.NextActionId;
        bool Object(long id) => id > 0 && id < _state.NextObjectId;
        bool EnemySkill(string id) => id is "enemy.strike" or "enemy.projectile" or "enemy.mend" or "enemy.detonate" or "boss.chain" or "boss.sonic" or "boss.resurrect" or "boss.beast_rush" or "boss.bell_ring";
        foreach (var actor in _state.Actors)
        {
            Require(actor.Id > 0 && actor.PlayerId is >= 0 and <= 2 && _spatial.CanOccupy(actor.Position, ActorRadius), "actor position or ownership");
            if (actor.PlayerId > 0)
                Require(actor.Id == actor.PlayerId && actor.DefinitionId == "player.vanguard" && actor.Role == "Vanguard" && actor.MaxHealth == (actor.Id == 1 ? 350 : 390) && actor.Armor == Loadouts[actor.Id - 1].Sum(id => _content.Items.Single(i => i.Id == id).Armor), "server fixed player build");
            else
            {
                var definition = _content.Enemies.FirstOrDefault(e => e.Id == actor.DefinitionId);
                Require(actor.Id > 2 && definition is not null && actor.Role == definition.Role && actor.MaxHealth == definition.Health * 8 / 5 && actor.Armor == definition.Armor, "authored enemy");
            }
            Require(actor.Health >= 0 && actor.Health <= actor.MaxHealth && (actor.Health == 0) == actor.DeathProcessed && actor.Barrier is >= 0 and <= 200 && actor.Cycle is >= 0 and <= 1000000000, "health, death or barrier");
            Require(Timer(actor.RecoveryUntil) && Timer(actor.InvulnerableUntil) && Timer(actor.StaggerReadyTick), "actor timers");
            Require(actor.Statuses is not null && actor.Statuses.Count <= 3 && actor.Statuses.All(s => s is not null && s.Id is "Burning" or "Staggered" or "Vulnerable" && _state.Actors.Any(a => a.Id == s.SourceId) && Timer(s.ExpiresTick) && Timer(s.NextTick) && Action(s.ActionId)) && actor.Statuses.Select(s => s.Id).Distinct().Count() == actor.Statuses.Count, "statuses");
            if (actor.Pending is { } pending)
                Require(actor.Health > 0 && pending.ResolveTick >= Tick && Timer(pending.ResolveTick) && Action(pending.ActionId) && Point(pending.Target) && (pending.TargetId == 0 || _state.Actors.Any(a => a.Id == pending.TargetId)) && (actor.PlayerId > 0 ? SkillIds.Contains(pending.SkillId) : EnemySkill(pending.SkillId)), "pending action");
        }
        Require(_state.Actors.Count(a => a.PlayerId == 1) == 1 && _state.Actors.Count(a => a.PlayerId == 2) == 1, "shared player actors");
        foreach (var player in _state.Players)
        {
            Require(player.Momentum is >= 0 and <= 100 && player.PotionCharges is >= 0 and <= 3 && Timer(player.PotionReadyTick) && Timer(player.DodgeReadyTick) && player.MoveX is >= -1 and <= 1 && player.MoveZ is >= -1 and <= 1 && player.LastInputTick >= 0 && player.LastInputTick <= Tick, "player resources or input");
            Require(player.AcceptedSequence is >= 0 and <= 1000000000000 && player.ProcessedSequence >= 0 && player.ProcessedSequence <= player.AcceptedSequence && (player.Connected || player.MoveX == 0 && player.MoveZ == 0 && !player.Ready), "connection or acknowledgments");
            Require(player.Cooldowns is not null && player.Cooldowns.Count <= 6 && player.Cooldowns.All(c => SkillIds.Contains(c.Key) && Timer(c.Value)), "cooldowns");
            Require(player.Receipts is not null && player.Receipts.Count <= 64 && player.Receipts.All(r => r is not null && r.Sequence > 0 && r.Sequence <= player.AcceptedSequence && r.Hash is not null && r.Hash.Length == 64 && r.Hash.All(char.IsAsciiHexDigit)) && player.Receipts.Select(r => r.Sequence).SequenceEqual(player.Receipts.Select(r => r.Sequence).Distinct().Order()) && (player.AcceptedSequence == 0 ? player.Receipts.Count == 0 : player.Receipts.Count > 0 && player.Receipts[^1].Sequence == player.AcceptedSequence), "input receipt ledger");
        }
        Require(_state.Inputs.Count <= MaxQueuedInputsPerPlayer * 2 && _state.Inputs.All(q => q is not null && q.PlayerId is 1 or 2 && q.Input is not null && ValidInput(q.Input) && q.ApplyTick > Tick && q.ApplyTick <= Tick + MaxFutureTicks && Player(q.PlayerId).Connected && q.Input.Sequence > Player(q.PlayerId).ProcessedSequence && Player(q.PlayerId).Receipts.Any(r => r.Sequence == q.Input.Sequence && r.Hash == JsonData.Hash(q.Input))), "queued intents");
        foreach (var group in _state.Inputs.GroupBy(q => q.PlayerId)) Require(group.Count() <= MaxQueuedInputsPerPlayer && group.Select(q => q.ApplyTick).Distinct().Count() == group.Count() && group.OrderBy(q => q.ApplyTick).Select(q => q.Input.Sequence).SequenceEqual(group.Select(q => q.Input.Sequence).Order()), "queue order");
        Require(_state.Projectiles.Count <= MaxProjectiles && _state.Projectiles.All(p => p is not null && Object(p.Id) && _state.Actors.Any(a => a.Id == p.SourceId) && Point(p.Position) && Point(p.Target) && p.Damage is >= 0 and <= 1000 && Enum.IsDefined(p.Family) && p.Radius is >= 0 and <= 10000 && p.Status is "" or "Burning" or "Staggered" or "Vulnerable" && p.ExpiresTick > Tick && Timer(p.ExpiresTick) && Action(p.ActionId)), "projectiles");
        Require(_state.Warnings.Count <= MaxWarnings && _state.Warnings.All(w => w is not null && Object(w.Id) && _state.Actors.Any(a => a.Id == w.SourceId && a.PlayerId == 0) && EnemySkill(w.SkillId) && w.Shape is "Circle" or "Line" && Point(w.Position) && Point(w.End) && w.Radius is > 0 and <= 20000 && w.Damage is > 0 and <= 1000 && Enum.IsDefined(w.Family) && w.ResolveTick >= Tick && Timer(w.ResolveTick) && Action(w.ActionId)), "warnings");
        Require(_state.Rewards.Count <= 10 && _state.Rewards.All(r => r is not null && r.PlayerId is 1 or 2 && EncounterIds.Contains(r.EncounterId) && r.Attempt is >= 0 and <= 100000 && r.Item is not null && Object(r.Item.Id) && _content.Items.Any(i => i.Id == r.Item.DefinitionId && i.Slot == r.Item.Slot) && r.Item.Damage is >= 0 and <= 1000 && r.Item.Armor is >= 0 and <= 7500 && r.Item.CriticalBasisPoints is >= 0 and <= 7500 && r.Item.Rarity is "Tempered" or "Rare" && r.Experience == (r.EncounterId == EncounterIds[4] ? 250 : 100) && r.Ash == (r.EncounterId == EncounterIds[4] ? 60 : 20) && r.Id == $"{_state.MatchId}:{EncounterIds.ToList().IndexOf(r.EncounterId)}:player:{r.PlayerId}" && EncounterIds.ToList().IndexOf(r.EncounterId) <= _state.EncounterIndex && (r.EncounterId != EncounterId || _state.Cleared)), "personal reward receipts");
        Require(_state.Rewards.Select(r => r.Id).Distinct().Count() == _state.Rewards.Count, "duplicate rewards");
        var ids = _state.Projectiles.Select(p => p.Id).Concat(_state.Warnings.Select(w => w.Id)).Concat(_state.Rewards.Select(r => r.Item.Id)).ToArray(); Require(ids.Distinct().Count() == ids.Length, "object identity");
        Require(!_state.Cleared || _state.Actors.Where(a => a.PlayerId == 0).All(a => a.Health == 0), "unearned clear");
        Require(!_state.AwaitingRetry || !_state.Cleared && _state.Actors.Where(a => a.PlayerId > 0).All(a => a.Health == 0), "unearned retry");
        Require(_state.Completed == (_state.Cleared && _state.EncounterIndex == 4), "completion");
        Require(_state.PeakActors >= _state.Actors.Count && _state.PeakActors <= MaxActors && _state.PeakProjectiles >= _state.Projectiles.Count && _state.PeakProjectiles <= MaxProjectiles && _state.PeakWarnings >= _state.Warnings.Count && _state.PeakWarnings <= MaxWarnings && _state.PeakQueuedInputs >= _state.Inputs.Count && _state.PeakQueuedInputs <= MaxQueuedInputsPerPlayer * 2, "population counters");
    }
}
