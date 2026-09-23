using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    private CampaignCombatEncounter? CampaignEncounter => _content.Campaign?.Encounters.FirstOrDefault(e => e.Id == _state.EncounterId) ?? EndgameCampaignEncounter ?? RegionalHuntCombat.Find(_state.EncounterId);
    private string CampaignRule => CampaignEncounter?.Rule ?? "";
    private string CampaignPattern(CombatActor actor) => _content.Campaign?.Behaviors.FirstOrDefault(b => b.EnemyId == actor.DefinitionId)?.Pattern ?? "";
    private bool HasElite(CombatActor actor, string id) => _state.Campaign?.Actors.GetValueOrDefault(actor.Id)?.Modifiers.Contains(id) == true;
    private bool IsCampaignBoss(CombatActor actor) => _state.Campaign is not null && actor.DefinitionId.StartsWith("boss.", StringComparison.Ordinal) && _state.Campaign.Actors.GetValueOrDefault(actor.Id)?.IsEcho != true;
    private bool StormOvercharged => CampaignRule == "Storm" && _state.Campaign is { } campaign && Tick >= campaign.StartedTick + 45 && Tick < campaign.RuleUntil && (Tick - campaign.StartedTick - 45) % 150 < 45;
    private bool FragmentSuppressed(string id) => _state.Campaign is { } campaign && campaign.SuppressedUntil > Tick && campaign.SuppressedFragmentId == id;
    private bool CampaignRewardEligible(CombatActor actor) => !EndgameMechanicNoRewards(actor) && _state.Campaign?.Actors.GetValueOrDefault(actor.Id)?.IsEcho != true && !_state.ConsumedCorpseIds.Contains(actor.Id);
    private IEnumerable<CombatHazardView> CampaignHazards() => _state.Campaign?.Hazards.Select(CampaignHazardView) ?? [];
    private void PopulateCampaignEncounter()
    {
        _state.Campaign = null;
        if (CampaignEncounter is not { } encounter) return;
        _state.Campaign = new() { StartedTick = Tick, RuleUntil = encounter.DurationTicks == 0 ? 0 : Tick + encounter.DurationTicks, NextHazardTick = Tick + 45 };
        foreach (var spawn in encounter.Spawns) AddCampaignActor(spawn.EnemyId, spawn.Position, spawn.Modifiers, spawn.Hidden);
        if (encounter.Rule == "Rootheart")
            foreach (var position in new[] { new Position(-1200, -3500), new Position(2500, 3500), new Position(5500, -2500) }) AddCampaignActor("enemy.feeding_root", position, []);
        if (encounter.Rule == "Breach")
            foreach (var position in new[] { new Position(-1000, -3500), new Position(2500, 3500), new Position(5500, -2500) }) AddCampaignActor("enemy.seal_channel", position, []);
    }
    private CombatActor? AddCampaignActor(string definitionId, Position position, string[] modifiers, bool hidden = false, bool echo = false)
    {
        if (_state.Actors.Count >= MaxActors) return null;
        var positions = new[] { position, new(position.X - 900, position.Z), new(position.X + 900, position.Z), new(position.X, position.Z - 900), new(position.X, position.Z + 900), new(position.X - 1800, position.Z), new(position.X + 1800, position.Z) };
        var free = positions.Where(p => _spatial.CanOccupy(p, ActorRadius) && !_state.Actors.Any(a => a.Health > 0 && Position.DistanceSquared(a.Position, p) < 4L * ActorRadius * ActorRadius)).ToArray();
        if (free.Length == 0) { Budget(0); return null; }
        position = free[0];
        var actor = AddEncounterActor(definitionId, position);
        actor = actor with { Elite = modifiers.Length > 0 || definitionId.StartsWith("boss.", StringComparison.Ordinal), Hidden = hidden, Health = echo ? Math.Max(1, actor.MaxHealth / 3) : actor.Health };
        _state.Actors[^1] = actor;
        _state.Campaign!.Actors.Add(actor.Id, new() { Modifiers = modifiers.ToArray(), NextEliteTick = Tick + 30, IsEcho = echo, ExpiresTick = echo ? Tick + 180 : 0 });
        return actor;
    }
    private void TickCampaign()
    {
        if (_state.Campaign is not { } campaign) return;
        if (campaign.SuppressedUntil <= Tick && campaign.SuppressedFragmentId != "") { Emit("FragmentSuppressionEnded", 1, content: campaign.SuppressedFragmentId); campaign.SuppressedFragmentId = ""; }
        foreach (var pair in campaign.Actors)
        {
            var actor = _state.Actors.First(a => a.Id == pair.Key);
            if (pair.Value.ForgeOverchargeUntil != 0 && (pair.Value.ForgeOverchargeUntil <= Tick || actor.Health <= 0))
                pair.Value.ForgeOverchargeUntil = 0;
            if (pair.Value.IsEcho && pair.Value.ExpiresTick <= Tick && actor.Health > 0) { actor.Health = 0; actor.DeathProcessed = true; actor.Pending = null; Emit("EliteCopyExpired", actor.Id); CampaignDeath(actor); }
        }
        if (campaign.RuleUntil > 0 && campaign.RuleUntil <= Tick)
        {
            campaign.Hazards.RemoveAll(h => h.ContentId.StartsWith("rule.", StringComparison.Ordinal));
            if (campaign.NextHazardTick != 0)
            {
                campaign.NextHazardTick = 0;
                if (CampaignRule == "Storm")
                {
                    foreach (int id in campaign.Actors.Keys.ToArray()) campaign.Actors[id] = campaign.Actors[id] with { Modifiers = campaign.Actors[id].Modifiers.Where(m => m != "Stormbound").ToArray() };
                    campaign.Hazards.RemoveAll(h => h.ContentId == "elite.stormbound");
                }
                Emit("CampaignRuleExpired", content: CampaignRule);
            }
        }
        else if (Player.Health > 0 && _state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0) && campaign.NextHazardTick > 0 && campaign.NextHazardTick <= Tick)
        {
            ScheduleCampaignRule(); campaign.HazardCycle++; campaign.NextHazardTick = Tick + 150;
        }
        foreach (var hazard in campaign.Hazards.Where(h => h.ResolveTick <= Tick).ToArray())
        {
            campaign.Hazards.Remove(hazard);
            var source = _state.Actors.FirstOrDefault(a => a.Id == hazard.SourceId);
            if (source is null) continue;
            if (ResolveMidgameSupport(source, hazard) || ResolveOathWard(source, hazard))
            {
                Emit("CampaignHazardResolved", source.Id, content: hazard.ContentId, action: hazard.ActionId);
                continue;
            }
            if (hazard.ContentId == "elite.dirgebound")
            {
                ResolveDirge(source, hazard);
                Emit("CampaignHazardResolved", source.Id, content: hazard.ContentId, action: hazard.ActionId);
                continue;
            }
            foreach (var target in _state.Actors.Where(a => a.Health > 0 && Hostile(source.Faction, a.Faction)).OrderBy(a => a.Id))
            {
                if (!HazardContains(hazard, target.Position)) continue;
                if (hazard.ContentId == "elite.null")
                {
                    if (target.Id == 1 && target.InvulnerableUntil <= Tick && campaign.SuppressionReadyTick <= Tick && _state.Fragments.Count > 0)
                    {
                        campaign.SuppressedFragmentId = _state.Fragments.Values.Order(StringComparer.Ordinal).First(); campaign.SuppressedUntil = Tick + 75; campaign.SuppressionReadyTick = Tick + 225;
                        Emit("FragmentSuppressed", source.Id, 1, 75, campaign.SuppressedFragmentId, hazard.ActionId);
                    }
                }
                else Enqueue(new(source.Id, source.Id, target.Id, hazard.Damage, hazard.Family, hazard.ContentId, hazard.ActionId, 0, Status: hazard.Status));
            }
            Emit("CampaignHazardResolved", source.Id, content: hazard.ContentId, action: hazard.ActionId);
        }
    }
    public static bool HazardContains(CombatHazardView hazard, Position position) => ContainsGeometry(hazard.Kind, hazard.Position, hazard.End, hazard.Radius, position);
    private static bool HazardContains(CampaignHazard hazard, Position position) => ContainsGeometry(hazard.Kind, hazard.Position, hazard.End, hazard.Radius, position);
    private static bool ContainsGeometry(string kind, Position start, Position end, int radius, Position point)
    {
        if (kind == "Circle") return Position.DistanceSquared(start, point) <= (long)radius * radius;
        double dx = end.X - start.X, dz = end.Z - start.Z, length = dx * dx + dz * dz;
        double fraction = length == 0 ? 0 : Math.Clamp(((point.X - start.X) * dx + (point.Z - start.Z) * dz) / length, 0, 1);
        double x = start.X + dx * fraction - point.X, z = start.Z + dz * fraction - point.Z;
        return x * x + z * z <= (long)radius * radius;
    }
    private void Warn(CombatActor source, string id, Position center, int radius, int delay, int damage, DamageFamily family, string status = "", Position? end = null)
    {
        if (_state.Campaign!.Hazards.Count >= 32) { Budget(0); return; }
        // Warning geometry is not an actor spawn: it may cross pillars. Keep fixed
        // lanes visible in smaller authored arenas instead of dropping the attack.
        Position Clip(Position p) => new(Math.Clamp(p.X, -_room.HalfWidth, _room.HalfWidth), Math.Clamp(p.Z, -_room.HalfDepth, _room.HalfDepth));
        center = Clip(center);
        if (end is { } endpoint) end = Clip(endpoint);
        long action = _state.NextActionId++;
        _state.Campaign.Hazards.Add(new(_state.NextObjectId++, end.HasValue ? "Line" : "Circle", center, end ?? center, radius, Tick + delay, id, source.Id, damage, family, status, action));
        Emit("CampaignHazardWarned", source.Id, amount: delay, content: id, action: action);
    }
    private bool CampaignHazardPointInBounds(Position position)
        => Math.Abs((long)position.X) <= _room.HalfWidth && Math.Abs((long)position.Z) <= _room.HalfDepth;
    private void ScheduleCampaignRule()
    {
        var source = _state.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        int cycle = _state.Campaign!.HazardCycle;
        switch (CampaignRule)
        {
            case "SonicLanes":
            case "Conveyor":
            case "Storm":
                int z = cycle % 2 == 0 ? -2200 : 2200;
                Warn(source, "rule." + CampaignRule.ToLowerInvariant(), new(-9000, z), 850, 36, 16, CampaignRule == "SonicLanes" ? DamageFamily.Storm : DamageFamily.Fire, CampaignRule == "Storm" ? "Burning" : "", new(9000, z));
                if (CampaignRule == "Storm") Emit("FragmentOverchargeWindow", 1, amount: 45, content: "rule.storm");
                break;
            case "PoisonLanes":
            case "Quarantine":
                Warn(source, "rule.poison_bloom", Player.Position, 1800, 36, 12, DamageFamily.Venom, "Poisoned"); break;
            case "Faults":
            case "Memory":
                for (int i = 0; i < 3; i++)
                {
                    int lane = CampaignRule == "Memory" ? 2 - i : i;
                    Warn(source, "rule.fault." + (i + 1), new(-8000, (lane - 1) * 3000), 750, 30 + i * 20, 18, DamageFamily.PhysicalCrush, "", new(8000, (lane - 1) * 3000));
                }
                break;
            case "OathZones":
            case "CausalEchoes":
                Warn(source, "rule." + CampaignRule.ToLowerInvariant(), Player.Position, 1600, 42, 18, CampaignRule == "OathZones" ? DamageFamily.PhysicalCrush : DamageFamily.Void, "Vulnerable"); break;
        }
    }
    private bool ThinkCampaignActor(CombatActor actor)
    {
        if (_state.Campaign is null || actor.Faction != CombatFaction.Enemy) return false;
        if (Stunned(actor)) return false;
        if (_state.Campaign.Actors.GetValueOrDefault(actor.Id)?.GuardedUntil > Tick && actor.RecoveryUntil > Tick) { actor.State = "Guarded"; return true; }
        if (actor.Pending is not null || actor.RecoveryUntil > Tick || Player.Health <= 0) return false;
        if (TryEliteAbility(actor)) return true;
        string pattern = CampaignPattern(actor);
        if (pattern is "" or "SupportFire") return false;
        if (pattern == "Root") { actor.State = "Feeding"; return true; }
        var definition = _content.Enemies.Single(e => e.Id == actor.DefinitionId);
        if (TryMidgameSupport(actor, pattern) || TryOathWard(actor, pattern)) return true;
        pattern = pattern switch { "SporeMend" => "PoisonBurst", "ForgeBellows" => "HeatVent", "OathWard" => "OathMark", _ => pattern };
        bool close = pattern is "Swarm" or "PoisonBurst" or "ForgeSweep" or "Fault" or "ShadowDouble" or "Antler";
        if (close && Position.DistanceSquared(actor.Position, Player.Position) > (long)definition.Range * definition.Range)
        { actor.State = pattern == "Antler" ? "BurrowApproach" : "Approach"; MoveTowardTarget(actor, Player.Position, HasElite(actor, "Hunter") ? Math.Min(500, definition.Speed * 5 / 4) : definition.Speed); return true; }
        if ((Tick + actor.Id) % 3 != 0) return true;
        if (IsCampaignBoss(actor)) { BeginCampaignBoss(actor, pattern); return true; }
        string status = pattern is "VenomPod" or "Swarm" or "PoisonBurst" ? "Poisoned" : pattern == "OathMark" ? "Rooted" : pattern == "ShadowDouble" ? "Vulnerable" : "";
        var family = status == "Poisoned" ? DamageFamily.Venom : pattern is "HeatVent" or "ForgeSweep" ? DamageFamily.Fire : pattern is "MemoryArrow" or "CausalEcho" or "ShadowDouble" ? DamageFamily.Void : DamageFamily.PhysicalCrush;
        bool line = pattern is "SonicLane" or "MemoryArrow" or "ForgeSweep" or "Fault";
        Warn(actor, "campaign." + pattern.ToLowerInvariant(), line ? actor.Position : Player.Position, line ? 650 : 1500, definition.Windup, definition.Damage, family, status, line ? Player.Position : null);
        actor.RecoveryUntil = Tick + definition.Windup + definition.Recovery; actor.State = "Windup"; actor.SpecialCycle++;
        return true;
    }
    private void BeginCampaignBoss(CombatActor actor, string pattern)
    {
        var definition = _content.Enemies.Single(e => e.Id == actor.DefinitionId); int cycle = actor.SpecialCycle++, phase = _state.Campaign!.BossPhase;
        actor.RecoveryUntil = Tick + definition.Windup + definition.Recovery; actor.State = "Windup";
        switch (pattern)
        {
            case "Bell":
                if (phase == 2 && _state.ResurrectedActorIds.Count < 2 && _state.Actors.Any(a => a.Role == "Melee" && a.Health == 0 && !_state.ConsumedCorpseIds.Contains(a.Id) && !_state.ResurrectedActorIds.Contains(a.Id)))
                { actor.Pending = new("boss.resurrect", 1, actor.Position, Tick + 36, _state.NextActionId++); break; }
                Warn(actor, phase == 3 ? "campaign.bell_beast" : cycle % 2 == 0 ? "campaign.chain" : "campaign.sonic", Player.Position, phase == 3 ? 1900 : cycle % 2 == 0 ? 1200 : 2100, 30, definition.Damage, phase == 3 ? DamageFamily.PhysicalCrush : DamageFamily.Storm);
                if (phase == 3) actor.Pending = new("campaign.rush", 1, Player.Position, Tick + 30, _state.NextActionId++);
                break;
            case "Rootheart":
                Warn(actor, "campaign.root_tangle", Player.Position, 1500, 36, definition.Damage, DamageFamily.Venom, "Rooted");
                if (cycle % 2 == 1) { actor.Pending = new("campaign.rush", 1, Player.Position, Tick + 36, _state.NextActionId++); Warn(actor, "campaign.root_spores", actor.Position, 2100, 55, 12, DamageFamily.Venom, "Poisoned"); }
                break;
            case "Furnace":
                _state.Campaign.Actors[actor.Id].GuardedUntil = Tick + 50;
                bool horizontal = cycle % 2 == 0;
                Warn(actor, "campaign.furnace_vent", horizontal ? new(-8500, 2000) : new(1500, -7500), 1100, 36, definition.Damage, DamageFamily.Fire, "Burning", horizontal ? new(8500, 2000) : new(1500, 7500));
                Warn(actor, "campaign.slag", Player.Position, 1500, 50, 16, DamageFamily.Fire);
                Emit("BossCoreWindow", actor.Id, amount: definition.Recovery, content: actor.DefinitionId); break;
            case "Covenant":
                _state.Campaign.Actors[actor.Id].GuardedUntil = Tick + 54;
                Warn(actor, "campaign.oath_mark", Player.Position, 1500, 36, 20, DamageFamily.PhysicalCrush, "Vulnerable");
                Warn(actor, "campaign.covenant_fault", new(-8000, cycle % 2 == 0 ? -2500 : 2500), 1000, 54, definition.Damage, DamageFamily.PhysicalCrush, "", new(8000, cycle % 2 == 0 ? -2500 : 2500)); break;
            case "Breach":
                Warn(actor, "campaign.breach_echo", Player.Position, 1700, 30, definition.Damage, DamageFamily.Void);
                if (phase >= 2) Warn(actor, "campaign.returning_echo", Player.Position, 1700, 70, definition.Damage, DamageFamily.Void);
                if (phase == 3) Warn(actor, "campaign.seal_sweep", new(-8000, -4000), 750, 50, 18, DamageFamily.Void, "", new(8000, 4000)); break;
            case "Antler":
                actor.InvulnerableUntil = Tick + 20; actor.State = "Burrow";
                Warn(actor, "campaign.antler_charge", actor.Position, 1000, 36, definition.Damage, DamageFamily.PhysicalPierce, "Rooted", Player.Position);
                actor.Pending = new("campaign.rush", 1, Player.Position, Tick + 36, _state.NextActionId++); break;
        }
        Emit("BossPatternStarted", actor.Id, amount: phase, content: pattern);
    }
    private bool ResolveCampaignAbility(CombatActor actor, CombatPending pending)
    {
        if (_state.Campaign is null) return false;
        if (pending.SkillId == "campaign.rush")
        { int distance = (int)Math.Sqrt(Position.DistanceSquared(actor.Position, pending.Target)); MoveActor(actor, Toward(actor.Position, pending.Target, Math.Max(0, distance - 650))); return true; }
        if (!pending.SkillId.StartsWith("elite.", StringComparison.Ordinal)) return false;
        ResolveElite(actor, pending); return true;
    }
    private bool CampaignShielded(CombatActor target)
    {
        if (!IsCampaignBoss(target)) return false;
        if (target.DefinitionId == "boss.bell_saint" && _state.Campaign!.BossPhase == 2) return _state.Actors.Any(a => a.Role == "Anchor" && a.Health > 0);
        if (target.DefinitionId == "boss.rootheart") return _state.Actors.Count(a => a.DefinitionId == "enemy.feeding_root" && a.Health > 0) >= 3;
        if (target.DefinitionId == "boss.breach_heart") return _state.Actors.Count(a => a.DefinitionId == "enemy.seal_channel" && a.Health > 0) >= 3;
        return false;
    }
    private bool TryCampaignPhaseTransition(CombatActor actor)
    {
        if (!IsCampaignBoss(actor)) return false;
        int maximum = actor.DefinitionId is "boss.bell_saint" or "boss.breach_heart" ? 3 : actor.DefinitionId == "boss.antler" ? 1 : 2;
        if (_state.Campaign!.BossPhase >= maximum) return false;
        _state.Campaign.BossPhase++; actor.Health = actor.MaxHealth; actor.DeathProcessed = false; actor.Pending = null; actor.Statuses.Clear(); actor.RecoveryUntil = Tick + 45;
        _state.Campaign.Hazards.RemoveAll(h => h.SourceId == actor.Id);
        Emit("BossPhaseChanged", actor.Id, amount: _state.Campaign.BossPhase, content: actor.DefinitionId);
        if (actor.DefinitionId == "boss.bell_saint")
        {
            if (_state.Campaign.BossPhase == 2)
            {
                AddCampaignActor("enemy.ritual_anchor", new(-5500, 0), []); AddCampaignActor("enemy.ritual_anchor", new(5500, 0), []);
                foreach (var position in new[] { new Position(-2200, -2500), new Position(2200, 2500) })
                    if (AddCampaignActor("enemy.ash_ghoul", position, []) is { } corpse) { corpse.Health = 0; corpse.DeathProcessed = true; corpse.State = "Dead"; }
            }
            else { AddCampaignActor("enemy.broken_bell", new(-5000, -5500), []); AddCampaignActor("enemy.broken_bell", new(5000, 5500), []); }
        }
        if (actor.DefinitionId == "boss.breach_heart" && _state.Campaign.BossPhase == 2)
        { AddCampaignActor("enemy.breach_echo", new(-1000, -2500), []); AddCampaignActor("enemy.breach_echo", new(3500, 2500), []); }
        return true;
    }
    private int CampaignDamageBonus(CombatActor? actor) => actor is null ? 0 :
        (_state.Campaign?.Actors.GetValueOrDefault(actor.Id)?.Empowerment ?? 0) * 1500 + (ForgeOverchargeTicks(actor) > 0 ? 2000 : 0);
    private int CampaignDefenseBonus(CombatActor actor) => _state.Campaign?.Actors.GetValueOrDefault(actor.Id)?.GuardedUntil > Tick ? 6000 : 0;
    private void CampaignDeath(CombatActor actor)
    {
        if (_state.Campaign is null) return;
        ClearCampaignSupport(actor);
        _state.Campaign.Hazards.RemoveAll(h => h.SourceId == actor.Id);
        if (HasElite(actor, "Martyr"))
            foreach (var ally in _state.Actors.Where(a => a.Id != actor.Id && a.Health > 0 && a.Faction == CombatFaction.Enemy && Position.DistanceSquared(actor.Position, a.Position) <= 7000L * 7000))
            { var state = _state.Campaign.Actors.GetValueOrDefault(ally.Id); if (state is null) continue; state.Empowerment = Math.Min(2, state.Empowerment + 1); ally.Barrier = Math.Min(200, ally.Barrier + 20); Emit("EliteEmpowered", actor.Id, ally.Id, state.Empowerment, "Martyr"); }
        if (!_state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
        { _state.Campaign.Hazards.Clear(); _state.Campaign.SuppressedFragmentId = ""; _state.Campaign.SuppressedUntil = Tick; _state.Campaign.NextHazardTick = 0; }
    }
}
