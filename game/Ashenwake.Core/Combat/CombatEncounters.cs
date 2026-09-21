using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    private static readonly string[] EncounterIds = ["", "hub", "encounter.ossuary", "encounter.cloister", "bell_saint.1", "bell_saint.2", "bell_saint.3", "clear"];
    public string EncounterId => _state.EncounterId;
    public CombatBuildModifiers Build => _state.Build with { };
    public static CombatSession CreateEncounter(string contentJson, ulong seed, string encounterId, CombatSnapshot? previous = null, bool restoreAtAnchor = false)
    {
        var original = previous is null ? Create(contentJson, seed) : Restore(contentJson, previous);
        if (encounterId == "" || !original.KnownEncounter(encounterId)) throw new ArgumentException("Unknown combat encounter.", nameof(encounterId));
        var state = original.Capture() with { EncounterId = encounterId, Preset = "standard", Endgame = null, RoomEncounterId = null };
        if (state.Experiment?.Status == "Bound") { state.CapturedSkillId = ""; state.CapturedUntil = state.Tick; }
        state.Experiment = null;
        state.Legendary = null;
        var player = state.Actors.Single(a => a.Id == 1);
        if (player.Health <= 0 && encounterId != "hub" && !restoreAtAnchor) throw new InvalidOperationException("A dead character must return to an anchor.");
        if (previous is null)
        {
            state.Fragments.Clear(); state.Fragments["Eyes"] = "fragment.eye_vael";
            // Adventure reward ownership is enforced by its coordinator, not the diagnostic arena's unlocked collection.
            var starterIds = state.Equipment.Values.ToHashSet(); state.Inventory.RemoveAll(i => !starterIds.Contains(i.Id));
        }
        state.Actors.Clear(); state.Actors.Add(player); state.Projectiles.Clear(); state.Areas.Clear(); state.Loot.Clear(); state.ResurrectedActorIds.Clear(); state.ConsumedCorpseIds.Clear();
        if (state.TemporaryLife > 0) { player = player with { MaxHealth = Math.Max(1, player.MaxHealth - state.TemporaryLife) }; player.Health = Math.Min(player.Health, player.MaxHealth); state.Actors[0] = player; state.TemporaryLife = 0; }
        state.MinionTargetId = 0;
        state.BufferedCommand = null; state.MemoryAttackId = ""; state.MemoryStacks = 0; state.MemoryUntilTick = state.Tick;
        player.Pending = null; player.Statuses.Clear(); player.Barrier = 0; player.MoveX = 0; player.MoveZ = 0;
        player.Position = ResolveRoom(original._content, state).PlayerSpawn; player.InvulnerableUntil = state.Tick; player.RecoveryUntil = state.Tick; player.State = "Idle";
        if (encounterId == "hub" || restoreAtAnchor)
        {
            player.Health = player.MaxHealth; player.DeathProcessed = false; state.PotionCharges = 3;
            state.PotionReadyTick = state.Tick; state.DodgeReadyTick = state.Tick; state.Cooldowns.Clear();
        }
        var session = new CombatSession(original._content, state);
        switch (encounterId)
        {
            case "encounter.ossuary":
                session.AddEncounterActor("enemy.ash_ghoul", new(-1000, -1500));
                session.AddEncounterActor("enemy.ash_ghoul", new(2000, 1500));
                session.AddEncounterActor("enemy.cinder_acolyte", new(4300, 2000));
                session.AddEncounterActor("enemy.emberling", new(4500, -2200)); break;
            case "encounter.cloister":
                session.AddEncounterActor("enemy.furnace_brute", new(1200, 0));
                session.AddEncounterActor("enemy.cinder_priest", new(5000, 1500));
                session.AddEncounterActor("enemy.ash_ghoul", new(2500, -1800));
                session.AddEncounterActor("enemy.emberling", new(4600, -2500)); break;
            case "bell_saint.1":
                session.AddEncounterActor("enemy.bell_saint", new(3200, 0)); break;
            case "bell_saint.2":
                session.AddEncounterActor("enemy.bell_saint", new(2500, 0));
                session.AddEncounterActor("enemy.ritual_anchor", new(-5500, 0));
                session.AddEncounterActor("enemy.ritual_anchor", new(5500, 0));
                session.AddEncounterActor("enemy.ash_ghoul", new(-2200, -2500), corpse: true);
                session.AddEncounterActor("enemy.ash_ghoul", new(2200, 2500), corpse: true); break;
            case "bell_saint.3":
                session.AddEncounterActor("enemy.bell_beast", new(1800, 0));
                session.AddEncounterActor("enemy.broken_bell", new(-5000, -5500));
                session.AddEncounterActor("enemy.broken_bell", new(5000, 5500)); break;
        }
        session.PopulateAuthoredEncounter(encounterId);
        session.PopulateCampaignEncounter();
        session.ValidateSnapshot(); return session;
    }
    private CombatActor AddEncounterActor(string definitionId, Position position, bool corpse = false)
    {
        var definition = _content.Enemies.FirstOrDefault(e => e.Id == definitionId) ?? throw new InvalidDataException("Missing encounter enemy: " + definitionId);
        var actor = new CombatActor
        {
            Id = _state.NextActorId++,
            DefinitionId = definition.Id,
            Position = position,
            Faction = CombatFaction.Enemy,
            Role = definition.Role,
            Health = corpse ? 0 : definition.Health,
            MaxHealth = definition.Health,
            Armor = definition.Armor,
            DeathProcessed = corpse,
            State = corpse ? "Dead" : "Acquire",
            Elite = definition.Role is "BellSaint" or "Beast"
        };
        _state.Actors.Add(actor); return actor;
    }
    public void ApplyAdventureBuild(CombatBuildModifiers modifiers)
    {
        ValidateBuild(modifiers);
        if (_state.Build.Manifestation != modifiers.Manifestation || _state.Build.SecondaryManifestation != modifiers.SecondaryManifestation) { _state.MemoryAttackId = ""; _state.MemoryStacks = 0; _state.MemoryUntilTick = Tick; }
        _state.Build = modifiers with { };
    }
    private bool HasManifestation(string id) => _state.Build.Manifestation == id || _state.Build.SecondaryManifestation == id;
    private static void ValidateBuild(CombatBuildModifiers modifiers)
    {
        if (modifiers is null || modifiers.SecondaryManifestation is not ("" or "manifestation.burning_blood" or "manifestation.stone_memory" or "manifestation.whispering_shadow" or "manifestation.voracious_renewal") || modifiers.SecondaryManifestation != "" && modifiers.SecondaryManifestation == modifiers.Manifestation || modifiers.Manifestation is not ("" or "manifestation.burning_blood" or "manifestation.stone_memory" or "manifestation.whispering_shadow" or "manifestation.voracious_renewal") || modifiers.AshcleaverStacks is < 0 or > 5 || modifiers.TemperLevel is < 0 or > 5 || modifiers.AshcleaverEvolution is not ("" or "Serath" or "Orrun") || modifiers.AshcleaverEvolution != "" && !modifiers.AshcleaverAwakened)
            throw new InvalidDataException("Invalid adventure combat modifiers.");
    }
    private bool AshcleaverActive => _state.Build.AshcleaverEquipped && Equipped.Any(i => i.Slot == "MainHand" && i.DefinitionId == "item.ashcleaver");
    private int AttackDuration(int ticks) => AshcleaverActive ? Math.Max(1, (ticks * 100 + 99 + _state.Build.AshcleaverStacks * 5) / (100 + _state.Build.AshcleaverStacks * 5)) : ticks;
    private bool IsRituallyShielded(CombatActor actor) => EndgameShielded(actor) || CampaignShielded(actor) || _state.EncounterId == "bell_saint.2" && actor.Role == "BellSaint" && _state.Actors.Any(a => a.Role == "Anchor" && a.Health > 0);
    private static int TelegraphRadius(CombatActor actor) => actor.Pending?.SkillId switch { "boss.chain" => 1500, "boss.sonic" => 2300, "boss.resurrect" => 0, "boss.beast_rush" => 2200, "boss.bell_ring" => 2000, "enemy.detonate" => 2200, _ => 0 };
    private static bool IsEncounterSkill(string id) => id is "enemy.mend" or "enemy.detonate" or "boss.chain" or "boss.sonic" or "boss.resurrect" or "boss.beast_rush" or "boss.bell_ring";
    private bool ThinkEncounterActor(CombatActor actor)
    {
        if (actor.Role is not ("Support" or "Rusher" or "BellSaint" or "Anchor" or "Bell" or "Beast")) return false;
        if (actor.Role == "Anchor") { actor.State = "Ritual"; return true; }
        if (Stunned(actor)) { actor.Pending = null; actor.State = "Staggered"; return true; }
        if (actor.Pending is not null || actor.RecoveryUntil > Tick) { actor.State = actor.Pending is null ? "Recover" : "Windup"; return true; }
        if (Player.Health <= 0) { actor.State = "Acquire"; return true; }
        var definition = _content.Enemies.Single(e => e.Id == actor.DefinitionId);
        if (actor.Role == "Rusher" && Position.DistanceSquared(actor.Position, Player.Position) > (long)definition.Range * definition.Range)
        { actor.State = "Rush"; MoveTowardTarget(actor, Player.Position, definition.Speed); return true; }
        if ((Tick + actor.Id) % 3 != 0) return true;
        var target = Player;
        string skill = actor.Role switch
        {
            "Support" => "enemy.mend",
            "Rusher" => "enemy.detonate",
            "Beast" => "boss.beast_rush",
            "Bell" => "boss.bell_ring",
            _ => _state.EncounterId == "bell_saint.2" && _state.ResurrectedActorIds.Count < 2 && _state.Actors.Any(a => a.Health == 0 && a.Role == "Melee" && !_state.ResurrectedActorIds.Contains(a.Id) && !_state.ConsumedCorpseIds.Contains(a.Id)) ? "boss.resurrect" : actor.SpecialCycle % 2 == 0 ? "boss.chain" : "boss.sonic"
        };
        if (actor.Role == "Support")
        {
            var wounded = _state.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && a.Health < a.MaxHealth && Position.DistanceSquared(a.Position, actor.Position) < 9000L * 9000 && _spatial.HasLineOfSight(actor.Position, a.Position)).OrderBy(a => (long)a.Health * 100 / a.MaxHealth).ThenBy(a => a.Id).FirstOrDefault();
            if (wounded is null) skill = "enemy.projectile"; else target = wounded;
        }
        if (skill is "boss.chain" or "boss.beast_rush" && Position.DistanceSquared(actor.Position, Player.Position) > (long)definition.Range * definition.Range)
        { actor.State = "Approach"; MoveTowardTarget(actor, Player.Position, definition.Speed); return true; }
        actor.SpecialCycle++;
        actor.Pending = new(skill, target.Id, skill == "enemy.detonate" ? actor.Position : target.Position, Tick + definition.Windup, _state.NextActionId++);
        actor.RecoveryUntil = Tick + definition.Windup + definition.Recovery; actor.State = "Windup";
        Emit("AbilityStarted", actor.Id, target.Id, content: skill, action: actor.Pending.ActionId); return true;
    }
    private bool ResolveEncounterAbility(CombatActor actor, CombatPending pending)
    {
        if (!IsEncounterSkill(pending.SkillId)) return false;
        var definition = _content.Enemies.Single(e => e.Id == actor.DefinitionId);
        switch (pending.SkillId)
        {
            case "enemy.mend":
                var target = _state.Actors.FirstOrDefault(a => a.Id == pending.TargetId && a.Health > 0 && a.Faction == CombatFaction.Enemy);
                if (target is not null && _spatial.HasLineOfSight(actor.Position, target.Position) && Position.DistanceSquared(actor.Position, target.Position) <= 9000L * 9000)
                {
                    int healing = Math.Min(30, target.MaxHealth - target.Health); target.Health += healing; target.Barrier = Math.Min(200, target.Barrier + 12);
                    Emit("Healed", actor.Id, target.Id, healing, pending.SkillId, pending.ActionId); Emit("BarrierGranted", actor.Id, target.Id, 12, pending.SkillId, pending.ActionId);
                }
                if (_state.Campaign is not null && CampaignPattern(actor) == "SupportFire") Warn(actor, "campaign.priest_flame", Player.Position, 1500, 30, 12, DamageFamily.Fire, "Burning");
                break;
            case "boss.resurrect":
                var corpse = _state.Actors.Where(a => a.Health == 0 && a.Role == "Melee" && !_state.ResurrectedActorIds.Contains(a.Id) && !_state.ConsumedCorpseIds.Contains(a.Id)).OrderBy(a => a.Id).FirstOrDefault();
                if (corpse is not null && _state.ResurrectedActorIds.Count < 2)
                {
                    _state.ResurrectedActorIds.Add(corpse.Id); corpse.Health = corpse.MaxHealth / 2; corpse.DeathProcessed = false; corpse.Statuses.Clear(); corpse.State = "Acquire";
                    corpse.RecoveryUntil = Tick + 15; Emit("CorpseResurrected", actor.Id, corpse.Id, content: pending.SkillId, action: pending.ActionId);
                }
                break;
            case "boss.beast_rush":
                int distance = (int)Math.Sqrt(Position.DistanceSquared(actor.Position, pending.Target));
                MoveActor(actor, Toward(actor.Position, pending.Target, Math.Max(0, distance - 650)));
                foreach (var hostile in Hostiles(actor, actor.Position, 2200)) Enqueue(new(actor.Id, actor.Id, hostile.Id, definition.Damage, DamageFamily.PhysicalCrush, pending.SkillId, pending.ActionId, 0, Status: "Vulnerable"));
                break;
            case "enemy.detonate":
                foreach (var hostile in Hostiles(actor, pending.Target, 2200)) Enqueue(new(actor.Id, actor.Id, hostile.Id, definition.Damage, DamageFamily.Fire, pending.SkillId, pending.ActionId, 0, Status: "Burning"));
                actor.Health = 0; Kill(actor, new(actor.Id, actor.Id, actor.Id, 0, DamageFamily.Fire, pending.SkillId, pending.ActionId, 0)); break;
            default:
                if (_state.Areas.Count >= MaxAreas) { Budget(pending.ActionId); break; }
                int radius = pending.SkillId == "boss.chain" ? 1500 : pending.SkillId == "boss.sonic" ? 2300 : 2000;
                _state.Areas.Add(new(_state.NextObjectId++, actor.Id, actor.Id, pending.Target, radius, pending.SkillId, definition.Damage, pending.SkillId == "boss.chain" ? DamageFamily.PhysicalPierce : DamageFamily.Storm, Tick, Tick + 1, pending.ActionId, 0)); break;
        }
        Emit("AbilityResolved", actor.Id, pending.TargetId, content: pending.SkillId, action: pending.ActionId); return true;
    }
    private void ApplyBuildHitEffects(Hit hit, CombatActor? source, CombatActor target, int healthDamage, bool physical)
    {
        if (target.Id == 1 && healthDamage > 0)
        {
            if (HasManifestation("manifestation.stone_memory"))
            {
                _state.MemoryStacks = _state.MemoryUntilTick > Tick && _state.MemoryAttackId == hit.ContentId ? Math.Min(3, _state.MemoryStacks + 1) : 1;
                _state.MemoryAttackId = hit.ContentId; _state.MemoryUntilTick = Tick + 150;
                Emit("ManifestationTriggered", 1, 1, _state.MemoryStacks, "manifestation.stone_memory", hit.ActionId);
            }
            foreach (var fragment in ActiveFragments().Where(f => f.Trigger == "DamageTaken" && _state.Cooldowns.GetValueOrDefault(f.Id) <= Tick))
            {
                target.Barrier = Math.Min(200, target.Barrier + FragmentAmount(12)); _state.Cooldowns[fragment.Id] = Tick + 45;
                Emit("FragmentTriggered", 1, 1, FragmentAmount(12), fragment.Id, hit.ActionId, hit.Depth + 1);
            }
        }
        if (target.Id == 1 && healthDamage > 0 && physical && !hit.Reflected && HasManifestation("manifestation.burning_blood") && _triggers.Add($"{hit.ActionId}:burning_blood"))
        {
            foreach (var hostile in Hostiles(Player, Player.Position, 1800)) Enqueue(new(1, 1, hostile.Id, 8, DamageFamily.Fire, "effect.burning_blood", hit.ActionId, hit.Depth + 1, Reflected: true));
            Emit("ManifestationTriggered", 1, 1, content: "manifestation.burning_blood", action: hit.ActionId);
        }
        if (source?.Id != 1 || hit.Dot || hit.Reflected || !_content.Skills.Any(s => s.Id == hit.ContentId)) return;
        if (!AshcleaverActive) return;
        if (_state.Build.AshcleaverEvolution == "Serath" && healthDamage > 0)
        {
            int healing = Math.Min(Math.Max(1, healthDamage / 10), Player.MaxHealth - Player.Health); Player.Health += HealingAmount(healing);
            Emit("Healed", 1, 1, HealingAmount(healing), "evolution.serath", hit.ActionId);
        }
        if (_state.Build.AshcleaverEvolution == "Orrun" && healthDamage > 0) { Player.Barrier = Math.Min(200, Player.Barrier + 4); Emit("BarrierGranted", 1, 1, 4, "evolution.orrun", hit.ActionId); }
        if (!_state.Build.AshcleaverAwakened || _state.Build.AshcleaverStacks < 5 || _state.Cooldowns.GetValueOrDefault("effect.ashcleaver_wave") > Tick) return;
        _state.Cooldowns["effect.ashcleaver_wave"] = Tick + 30;
        if (_state.Projectiles.Count >= MaxProjectiles) { Budget(hit.ActionId); return; }
        if (_state.Build.AshcleaverEvolution == "Orrun")
        {
            foreach (var enemy in Hostiles(source, target.Position, 2600)) Enqueue(new(1, 1, enemy.Id, 36, DamageFamily.PhysicalCrush, "effect.molten_seismic", hit.ActionId, hit.Depth + 1, Reflected: true, Status: "Burning"));
        }
        else _state.Projectiles.Add(new(_state.NextObjectId++, 1, 1, source.Position, target.Position, target.Id, "effect.ashcleaver_wave", 28, DamageFamily.Fire, Tick + 90, hit.ActionId, hit.Depth + 1));
        Emit("FlameWaveCreated", 1, target.Id, content: "item.ashcleaver", action: hit.ActionId, depth: hit.Depth + 1);
    }
}
