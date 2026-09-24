using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private enum CreatureMotion { None, Vine, Swarm, Carrier, Antler, Rootheart, Emberling, Brute, Sentinel, Spindle, Giant, Keeper, BoneSentinel, Warden, Shadow, Echo, Breach }
    private enum CreatureJoint { Other, Stem, Head, LeftArm, RightArm, LeftLeg, RightLeg, Root, Petal, Foot }
    private CreatureMotion _creatureMotion;
    internal string CreatureMotionKind => _creatureMotion.ToString();
    private bool RootedCreature => _creatureMotion is CreatureMotion.Vine or CreatureMotion.Rootheart;

    private void ConfigureCreatureMotion(string id, string discipline, bool allied)
    {
        if (discipline.Length > 0 || allied) return;
        _creatureMotion = id switch
        {
            "enemy.carnivorous_vine" or "enemy.feeding_root" or "enemy.brood_root" => CreatureMotion.Vine,
            "enemy.needle_swarm" => CreatureMotion.Swarm,
            "enemy.bloom_carrier" => CreatureMotion.Carrier,
            "boss.antler" => CreatureMotion.Antler,
            "boss.rootheart" => CreatureMotion.Rootheart,
            "enemy.emberling" => CreatureMotion.Emberling,
            "enemy.furnace_brute" => CreatureMotion.Brute,
            "enemy.forge_sentinel" => CreatureMotion.Sentinel,
            "boss.furnace_spindle" => CreatureMotion.Spindle,
            "enemy.oath_giant" => CreatureMotion.Giant,
            "enemy.contract_keeper" => CreatureMotion.Keeper,
            "enemy.bone_sentinel" => CreatureMotion.BoneSentinel,
            "boss.covenant_warden" => CreatureMotion.Warden,
            "enemy.doubled_shadow" => CreatureMotion.Shadow,
            "enemy.breach_echo" => CreatureMotion.Echo,
            "boss.breach_heart" => CreatureMotion.Breach,
            _ => CreatureMotion.None
        };
        if (_creatureMotion == CreatureMotion.None) return;
        _weaponRig = false;
    }

    private static CreatureJoint ClassifyCreatureJoint(string motion) => motion switch
    {
        "sway" => CreatureJoint.Stem,
        "jaw" => CreatureJoint.Head,
        "left_arm" => CreatureJoint.LeftArm,
        "right_arm" => CreatureJoint.RightArm,
        "left_leg" => CreatureJoint.LeftLeg,
        "right_leg" => CreatureJoint.RightLeg,
        "root_tendril" => CreatureJoint.Root,
        "petal" => CreatureJoint.Petal,
        "creeper_leg" => CreatureJoint.Foot,
        _ => CreatureJoint.Other
    };

    private void AnimateCreatureBody()
    {
        if (_creatureMotion == CreatureMotion.None) return;
        if (CinderCreature) { AnimateCinderBody(); return; }
        if (SpineCreature) { AnimateSpineBody(); return; }
        if (HollowCreature) { AnimateHollowBody(); return; }
        float cycle = (float)_gaitPhase * Mathf.Tau;
        float breath = _reducedVisualEffects ? 0 : MathF.Sin((float)_time * 1.7f);
        if (RootedCreature || _creatureMotion == CreatureMotion.Swarm)
        {
            // Keep the ground contact stable; stems and individual insects carry the motion.
            BodyRoot.Position = Vector3.Zero;
            BodyRoot.Rotation = new(0, _facing, 0);
        }
        else
        {
            float weight = _creatureMotion == CreatureMotion.Antler ? .5f : 1;
            BodyRoot.Rotation = new(-.025f * _stride, _facing, MathF.Sin(cycle) * .035f * _stride * weight);
        }
        for (int i = 0; i < _limbs.Count; i++)
        {
            var limb = _limbs[i];
            var joint = limb.CreatureKind;
            if (joint is CreatureJoint.LeftLeg or CreatureJoint.RightLeg) continue;
            limb.Node.Position = limb.Origin;
            Vector3 pose = Vector3.Zero;
            float side = limb.Origin.X < 0 ? -1 : 1;
            if (RootedCreature)
            {
                float strength = _creatureMotion == CreatureMotion.Rootheart ? .7f : 1;
                pose = joint switch
                {
                    CreatureJoint.Stem => new((breath * .035f + _windup * .24f + _recovery * -.12f) * strength, 0, breath * .025f),
                    CreatureJoint.LeftArm or CreatureJoint.RightArm => new(-_windup * .5f + _recovery * .25f, side * -.25f * _windup, side * (.1f * breath + _windup * .48f)),
                    CreatureJoint.Root => new(MathF.Sin(cycle + limb.Amplitude) * .09f * _stride, 0, 0),
                    _ => Vector3.Zero
                };
                if (joint == CreatureJoint.Petal)
                {
                    float angle = limb.Amplitude * Mathf.Tau / 7;
                    float open = .025f * breath + .12f * _windup - .025f * _recovery;
                    limb.Node.Position += new Vector3(MathF.Cos(angle), MathF.Sin(angle), .2f) * open;
                    pose = new(MathF.Sin(angle) * -.4f * _windup, MathF.Cos(angle) * .4f * _windup, 0);
                }
            }
            else if (_creatureMotion == CreatureMotion.Swarm)
            {
                if (joint == CreatureJoint.Stem)
                {
                    float stagger = limb.Origin.X * 3;
                    limb.Node.Position = new(limb.Origin.X * (1 - .22f * _windup), .42f + .02f * MathF.Sin(cycle + stagger) * _stride + .06f * _windup, limb.Origin.Z);
                    pose = new(_windup * .3f - _recovery * .12f, MathF.Sin(cycle + stagger) * .06f * _stride, 0);
                }
                else if (joint == CreatureJoint.Foot)
                {
                    float step = MathF.Sin(cycle + ((int)limb.Amplitude % 2) * Mathf.Pi);
                    pose = new(0, step * .3f * _stride, side * (Math.Max(0, step) * .23f * _stride - .12f * _windup));
                }
            }
            else
            {
                bool antler = _creatureMotion == CreatureMotion.Antler;
                pose = joint switch
                {
                    CreatureJoint.Stem => new((antler ? .015f : .055f) * breath + .18f * _windup - .12f * _recovery, 0, MathF.Sin(cycle) * (antler ? .02f : .07f) * _stride),
                    CreatureJoint.Head => new((antler ? -.24f : -.16f) * _windup + .13f * _recovery + .035f * breath, MathF.Sin(cycle) * .025f * _stride, 0),
                    CreatureJoint.LeftArm or CreatureJoint.RightArm => new(MathF.Sin(cycle + (side > 0 ? Mathf.Pi : 0)) * .2f * _stride - _windup * (antler ? .8f : .65f), side * -.12f * _windup, side * _windup * .32f),
                    _ => Vector3.Zero
                };
            }
            limb.Node.Rotation = limb.Rest + pose;
        }
    }

    private void AnimateCreatureCue()
    {
        if (CinderCreature) { AnimateCinderCue(); return; }
        if (SpineCreature) { AnimateSpineCue(); return; }
        if (HollowCreature) { AnimateHollowCue(); return; }
        float t = _cueTime / _cueDuration;
        float strength = 1 - Smooth((t - .3f) / .7f);
        bool hit = _cue == CombatCue.Hit;
        bool dodge = _cue == CombatCue.Dodge;
        if (hit || dodge) strength = MathF.Sin(t * Mathf.Pi);
        float weight = _creatureMotion switch { CreatureMotion.Rootheart => .32f, CreatureMotion.Antler => .55f, _ => 1 };
        for (int i = 0; i < _limbs.Count; i++)
        {
            var limb = _limbs[i];
            var joint = limb.CreatureKind;
            float side = limb.Origin.X < 0 ? -1 : 1;
            Vector3 pose = limb.Node.Rotation;
            if (dodge)
            {
                // Defensive tuck stays within the creature silhouette, especially for rooted rigs.
                if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm) pose += new Vector3(.4f, 0, -side * .25f) * strength;
                else if (joint == CreatureJoint.Petal) pose += new Vector3(.2f, side * .2f, 0) * strength;
                else if (joint == CreatureJoint.Foot) pose += new Vector3(0, 0, -side * .3f) * strength;
                else if (joint == CreatureJoint.Head) pose += new Vector3(.25f, 0, 0) * strength;
            }
            else if (hit)
            {
                if (joint is CreatureJoint.Stem or CreatureJoint.Head) pose += new Vector3(.24f, side * .08f, -.07f) * strength * weight;
                else if (joint == CreatureJoint.Petal) pose += new Vector3(.16f, side * .12f, 0) * strength;
            }
            else if (RootedCreature)
            {
                if (joint == CreatureJoint.Stem) pose = limb.Rest + new Vector3(-.42f * strength * weight, .08f * strength, 0);
                else if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm) pose = limb.Rest + new Vector3(.8f, -side * .45f, side * .2f) * strength;
                else if (joint == CreatureJoint.Petal)
                {
                    float angle = limb.Amplitude * Mathf.Tau / 7;
                    limb.Node.Position = limb.Origin + new Vector3(-MathF.Cos(angle), -MathF.Sin(angle), -.45f) * (.095f * strength);
                    pose = limb.Rest + new Vector3(MathF.Sin(angle) * .28f, -MathF.Cos(angle) * .28f, 0) * strength;
                }
            }
            else if (_creatureMotion == CreatureMotion.Swarm)
            {
                if (joint == CreatureJoint.Stem)
                {
                    pose = limb.Rest + new Vector3(-.23f, side * .13f, .05f * side) * strength;
                    limb.Node.Position += new Vector3(limb.Origin.X * .16f, -.04f, -.16f) * strength;
                }
                else if (joint == CreatureJoint.Foot) pose += new Vector3(0, -.18f * side, .16f * side) * strength;
            }
            else
            {
                if (joint == CreatureJoint.Stem) pose = limb.Rest + new Vector3(-.23f, .1f, 0) * strength;
                else if (joint == CreatureJoint.Head) pose = limb.Rest + new Vector3(.3f, -.12f, 0) * strength;
                else if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm)
                    pose = limb.Rest + new Vector3(1.15f - t * .45f, side * -.2f, side * .15f) * strength;
            }
            limb.Node.Rotation = pose;
        }
    }

    private void AnimateCreatureDeath()
    {
        if (CinderCreature) { AnimateCinderDeath(); return; }
        if (SpineCreature) { AnimateSpineDeath(); return; }
        if (HollowCreature) { AnimateHollowDeath(); return; }
        float fall = Smooth(_cueTime / _cueDuration);
        BodyRoot.Position = _deathBodyPosition.Lerp(Vector3.Zero, fall);
        BodyRoot.Rotation = _deathBodyRotation.Lerp(new Vector3(0, _deathBodyRotation.Y, 0), fall);
        for (int i = 0; i < _limbs.Count; i++)
        {
            var limb = _limbs[i];
            var joint = limb.CreatureKind;
            float side = limb.Origin.X < 0 ? -1 : 1;
            Vector3 position = limb.Origin;
            Vector3 pose = limb.Rest;
            if (RootedCreature)
            {
                pose += joint switch
                {
                    CreatureJoint.Stem => new(-1.05f, 0, .12f),
                    CreatureJoint.Petal => new(.25f, side * .45f, 0),
                    CreatureJoint.LeftArm or CreatureJoint.RightArm => new(.7f, 0, -side * .65f),
                    _ => Vector3.Zero
                };
            }
            else if (_creatureMotion == CreatureMotion.Swarm)
            {
                if (joint == CreatureJoint.Stem) { position = new(limb.Origin.X * 1.25f, .18f, limb.Origin.Z); pose += new Vector3(.12f, .2f * side, .7f * side); }
                else if (joint == CreatureJoint.Foot) pose += new Vector3(0, side * .35f, -side * .9f);
            }
            else
            {
                if (joint == CreatureJoint.Stem) { position.Y *= .45f; pose += new Vector3(-.45f, 0, .15f); }
                else if (joint is CreatureJoint.LeftLeg or CreatureJoint.RightLeg) { position.Y *= .45f; pose += new Vector3(.98f, .1f * side, .12f * side); }
                else if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm) pose += new Vector3(.55f, 0, .2f * side);
                else if (joint == CreatureJoint.Head) pose += new Vector3(-.6f, 0, .12f);
            }
            limb.Node.Rotation = _deathFromRotations[i].Lerp(pose, fall);
            limb.Node.Position = _deathFromPositions[i].Lerp(position, fall);
        }
    }
}
