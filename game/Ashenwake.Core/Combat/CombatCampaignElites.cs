using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    private bool TryEliteAbility(CombatActor actor)
    {
        var state = _state.Campaign!.Actors.GetValueOrDefault(actor.Id);
        if (state is null || state.Modifiers.Length == 0 || state.NextEliteTick > Tick) return false;
        state.NextEliteTick = Tick + 180;
        string modifier = state.Modifiers[(actor.SpecialCycle++ / 2) % state.Modifiers.Length];
        int targetId = 1; Position target = Player.Position;
        switch (modifier)
        {
            case "Hunter": return false;
            case "Martyr": return false;
            case "Dirgebound":
                if (!DirgeAllies(actor, actor.Position, 3000).Any(ally => ally.Barrier < 24)) return false;
                target = actor.Position;
                Warn(actor, "elite.dirgebound", target, 3000, 36, 0, DamageFamily.Void);
                break;
            case "Mirrorborn": if (state.MirrorUsed) return false; break;
            case "Gravewake":
                if (state.ResurrectionUsed) return false;
                var corpse = _state.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health == 0 && !IsCampaignBoss(a) && LeavesCorpse(a) && !_state.ConsumedCorpseIds.Contains(a.Id) && !_state.ResurrectedActorIds.Contains(a.Id) && Position.DistanceSquared(actor.Position, a.Position) <= 7000L * 7000).OrderBy(a => a.Id).FirstOrDefault();
                if (corpse is null) return false; targetId = corpse.Id; target = corpse.Position; break;
            case "Stormbound":
                var link = _state.Actors.Where(a => a.Id != actor.Id && a.Health > 0 && a.Faction == CombatFaction.Enemy && Position.DistanceSquared(actor.Position, a.Position) <= 9000L * 9000).OrderBy(a => a.Id).FirstOrDefault();
                if (link is null) return false;
                Warn(actor, "elite.stormbound", actor.Position, 700, 30, 16, DamageFamily.Storm, "Shocked", link.Position); break;
            case "Devourer":
                if (state.Devoured >= 2 || actor.Health >= actor.MaxHealth) return false;
                var meal = _state.Actors.Where(a => a.Id != actor.Id && a.Health > 0 && a.Faction == CombatFaction.Enemy && !IsCampaignBoss(a) && a.Role is not ("Anchor" or "Bell") && Position.DistanceSquared(actor.Position, a.Position) <= 5000L * 5000).OrderBy(a => a.Id).FirstOrDefault();
                if (meal is null) return false; targetId = meal.Id; target = meal.Position; break;
            case "Null":
                if (_state.Campaign.SuppressionReadyTick > Tick || _state.Fragments.Count == 0) return false;
                Warn(actor, "elite.null", Player.Position, 1800, 36, 0, DamageFamily.Void); break;
            case "Riftborn":
                target = _spatial.Move(actor.Position, new(Player.Position.X + (actor.Position.X < Player.Position.X ? 1800 : -1800), Player.Position.Z + 1500), ActorRadius);
                Warn(actor, "elite.riftborn", target, 1100, 36, 12, DamageFamily.Void); break;
        }
        actor.Pending = new("elite." + modifier.ToLowerInvariant(), targetId, target, Tick + 36, _state.NextActionId++);
        actor.RecoveryUntil = Tick + 66; actor.State = "EliteWindup";
        Emit("EliteAbilityStarted", actor.Id, targetId, 36, modifier, actor.Pending.ActionId); return true;
    }
    private void ResolveElite(CombatActor actor, CombatPending pending)
    {
        var state = _state.Campaign!.Actors[actor.Id];
        switch (pending.SkillId)
        {
            case "elite.mirrorborn":
                if (state.MirrorUsed) break; state.MirrorUsed = true;
                for (int i = 0; i < 2 && _state.Campaign.Actors.Values.Count(a => a.IsEcho) < 4; i++)
                {
                    var at = _spatial.Move(actor.Position, new(actor.Position.X + (i == 0 ? -900 : 900), actor.Position.Z + 900), ActorRadius);
                    if (_state.Actors.Any(a => a.Health > 0 && Position.DistanceSquared(a.Position, at) < 4L * ActorRadius * ActorRadius)) continue;
                    if (AddCampaignActor(actor.DefinitionId, at, [], echo: true) is { } copy) Emit("EliteCopyCreated", actor.Id, copy.Id, content: "Mirrorborn");
                }
                break;
            case "elite.gravewake":
                var corpse = _state.Actors.FirstOrDefault(a => a.Id == pending.TargetId && a.Health == 0 && !_state.ConsumedCorpseIds.Contains(a.Id));
                if (corpse is not null && !state.ResurrectionUsed && LeavesCorpse(corpse))
                {
                    state.ResurrectionUsed = true; _state.ConsumedCorpseIds.Add(corpse.Id); corpse.Health = Math.Max(1, corpse.MaxHealth / 2); corpse.DeathProcessed = false; corpse.Pending = null; corpse.Statuses.Clear(); corpse.RecoveryUntil = Tick + 30;
                    Emit("EliteResurrected", actor.Id, corpse.Id, content: "Gravewake");
                }
                break;
            case "elite.devourer":
                var meal = _state.Actors.FirstOrDefault(a => a.Id == pending.TargetId && a.Health > 0 && a.Faction == CombatFaction.Enemy);
                if (meal is not null && state.Devoured < 2 && Position.DistanceSquared(actor.Position, meal.Position) <= 5000L * 5000)
                {
                    meal.Health = 0; meal.DeathProcessed = true; meal.Pending = null; _state.ConsumedCorpseIds.Add(meal.Id); state.Devoured++; state.Empowerment = Math.Min(2, state.Empowerment + 1);
                    ClearCampaignSupport(meal);
                    int amount = Math.Min(actor.MaxHealth - actor.Health, 60); actor.Health += amount;
                    Emit("EliteDevoured", actor.Id, meal.Id, amount, "Devourer");
                }
                break;
            case "elite.riftborn": MoveActor(actor, pending.Target); Emit("EliteTeleported", actor.Id, content: "Riftborn"); break;
        }
        Emit("EliteAbilityResolved", actor.Id, pending.TargetId, content: pending.SkillId, action: pending.ActionId);
    }
    private bool CampaignSkill(string id) => _state.Campaign is not null && (id == "campaign.rush" || id is "elite.mirrorborn" or "elite.gravewake" or "elite.stormbound" or "elite.devourer" or "elite.null" or "elite.riftborn" or "elite.dirgebound");

    private IEnumerable<CombatActor> DirgeAllies(CombatActor source, Position center, int radius)
        => _state.Actors.Where(ally => ally.Id != source.Id && ally.Health > 0 && ally.Faction == CombatFaction.Enemy &&
            !IsCampaignBoss(ally) && ally.Role is not ("Anchor" or "Bell") &&
            _state.Campaign!.Actors.GetValueOrDefault(ally.Id)?.IsEcho == false &&
            Position.DistanceSquared(center, ally.Position) <= (long)radius * radius &&
            _spatial.HasLineOfSight(center, ally.Position)).OrderBy(ally => ally.Id);

    private void ResolveDirge(CombatActor source, CampaignHazard hazard)
    {
        // The announced circle is fixed. An ally that crosses its boundary or takes
        // cover before the chant ends gets no ward; repeated chants cannot stack it.
        if (source.Health <= 0 || Stunned(source) || !HasElite(source, "Dirgebound")) return;
        foreach (var ally in DirgeAllies(source, hazard.Position, hazard.Radius))
        {
            int granted = Math.Max(0, 24 - ally.Barrier);
            if (granted == 0) continue;
            ally.Barrier += granted;
            Emit("BarrierGranted", source.Id, ally.Id, granted, "elite.dirgebound", hazard.ActionId);
        }
    }
    private void ValidateCampaignSnapshot()
    {
        var definition = CampaignEncounter; var campaign = _state.Campaign;
        if (definition is null) { if (campaign is not null) throw new InvalidDataException("Campaign effects cannot survive leaving their encounter."); return; }
        void Check(bool valid, string field) { if (!valid) throw new InvalidDataException("Invalid campaign runtime " + field); }
        Check(campaign is not null, "state");
        Check(campaign!.StartedTick >= 0 && campaign.StartedTick <= Tick && campaign.RuleUntil == (definition.DurationTicks == 0 ? 0 : campaign.StartedTick + definition.DurationTicks) && campaign.NextHazardTick >= 0 && campaign.NextHazardTick <= Tick + 150 && campaign.HazardCycle is >= 0 and <= 10000000 && campaign.BossPhase is >= 1 and <= 3, "rule/timers");
        Check(campaign.SuppressedFragmentId is not null && (campaign.SuppressedFragmentId == "" || _state.Fragments.Values.Contains(campaign.SuppressedFragmentId)) && campaign.SuppressedUntil >= 0 && campaign.SuppressedUntil <= Tick + 75 && campaign.SuppressionReadyTick >= 0 && campaign.SuppressionReadyTick <= Tick + 225, "fragment suppression");
        Check(campaign.Actors is not null && campaign.Actors.Count <= MaxActors && campaign.Hazards is not null && campaign.Hazards.Count <= 32, "budgets");
        Check(campaign.Actors!.Keys.Order().SequenceEqual(_state.Actors.Where(a => a.Faction == CombatFaction.Enemy).Select(a => a.Id).Order()), "actor ownership");
        foreach (var pair in campaign.Actors)
        {
            Check(pair.Value is not null, "actor values"); var actor = pair.Value;
            ValidateEliteModifiers(actor!.Modifiers);
            Check(actor.NextEliteTick >= 0 && actor.NextEliteTick <= Tick + 180 && actor.GuardedUntil >= 0 && actor.GuardedUntil <= Tick + 90 && actor.Empowerment is >= 0 and <= 2 && actor.Devoured is >= 0 and <= 2 && actor.ExpiresTick >= 0 && actor.ExpiresTick <= Tick + 180 && (!actor.IsEcho || actor.Modifiers.Length == 0), "elite values");
        }
        Check(campaign.Actors.Values.Count(a => a.IsEcho) <= 4, "copy budget");
        foreach (var hazard in campaign.Hazards!)
            Check(hazard is not null && hazard.Id > 0 && hazard.Id < _state.NextObjectId && hazard.Kind is "Circle" or "Line" && CampaignHazardPointInBounds(hazard.Position) && CampaignHazardPointInBounds(hazard.End) && hazard.Radius is >= 100 and <= 3000 && hazard.ResolveTick >= Tick - 1 && hazard.ResolveTick <= Tick + 180 && campaign.Actors.ContainsKey(hazard.SourceId) && hazard.Damage is >= 0 and <= 1000 && Enum.IsDefined(hazard.Family) && (hazard.Status == "" || StatusIds.Contains(hazard.Status)) && hazard.ActionId > 0 && hazard.ActionId < _state.NextActionId && (hazard.ContentId.StartsWith("campaign.", StringComparison.Ordinal) || hazard.ContentId.StartsWith("rule.", StringComparison.Ordinal) || hazard.ContentId is "elite.stormbound" or "elite.null" or "elite.riftborn" or "elite.dirgebound"), "hazard");
        var existing = _state.Inventory.Select(i => i.Id).Concat(_state.Loot.Select(l => l.Id)).Concat(_state.Projectiles.Select(p => p.Id)).Concat(_state.Areas.Select(a => a.Id)).Concat(campaign.Hazards!.Select(h => h.Id)).ToArray();
        foreach (var hazard in campaign.Hazards.Where(h => h.ContentId == "elite.dirgebound"))
            Check(hazard.Kind == "Circle" && hazard.Position == hazard.End && hazard.Radius == 3000 &&
                hazard.Damage == 0 && hazard.Family == DamageFamily.Void && hazard.Status == "" &&
                campaign.Actors[hazard.SourceId].Modifiers.Contains("Dirgebound") &&
                _state.Actors.Any(a => a.Id == hazard.SourceId && a.Health > 0), "dirge ownership/geometry");
        Check(existing.Distinct().Count() == existing.Length, "effect identity");
        ValidateMidgameSupport();
        ValidateOathWard();
    }
}
