using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private bool CinderCreature => _creatureMotion is CreatureMotion.Emberling or CreatureMotion.Brute or CreatureMotion.Sentinel or CreatureMotion.Spindle;
    private bool _furnaceExposed, _furnaceExposureKnown;
    private float _furnaceExposure;
    private double _cinderLastTime;

    /// <summary>Presentation of the current authoritative guard state; never a combat transition.</summary>
    internal void SetFurnaceExposed(bool exposed)
    {
        if (_creatureMotion != CreatureMotion.Spindle || IsDying) return;
        _furnaceExposed = exposed;
        if (_furnaceExposureKnown) return;
        _furnaceExposureKnown = true;
        _furnaceExposure = exposed ? 1 : 0;
    }

    private void AnimateCinderBody()
    {
        float dt = (float)Math.Clamp(_time - _cinderLastTime, 0, .1);
        _cinderLastTime = _time;
        _furnaceExposure = Mathf.Lerp(_furnaceExposure, _furnaceExposed ? 1 : 0, Response(dt, 9));
        float cycle = (float)_gaitPhase * Mathf.Tau;
        float breath = _reducedVisualEffects ? 0 : MathF.Sin((float)_time * 1.7f);
        // Resolve has already happened, even if Core still displays Windup on this tick.
        float tell = _cue == CombatCue.Attack ? 0 : _windup;
        float exposed = _furnaceExposure * (1 - tell);
        float step = MathF.Sin(cycle);
        BodyRoot.Position = Vector3.Zero;
        BodyRoot.Rotation = new(0, _facing, 0);
        foreach (var limb in _limbs)
        {
            var joint = limb.CreatureKind;
            float side = limb.Origin.X < 0 ? -1 : 1;
            limb.Node.Position = limb.Origin;
            if (joint is CreatureJoint.LeftLeg or CreatureJoint.RightLeg)
            {
                float stride = _creatureMotion switch
                {
                    CreatureMotion.Emberling => .7f,
                    CreatureMotion.Brute => .58f,
                    CreatureMotion.Sentinel => .75f,
                    _ => .55f
                };
                CinderFoot(limb, joint == CreatureJoint.LeftLeg ? (float)_gaitPhase : ((float)_gaitPhase + .5f) % 1, stride * (1 - tell));
                continue;
            }
            Vector3 pose = _creatureMotion switch
            {
                CreatureMotion.Emberling => EmberlingPose(joint, side, cycle, breath, tell),
                CreatureMotion.Brute => BrutePose(joint, step, breath, tell),
                CreatureMotion.Sentinel => SentinelPose(joint, step, breath, tell),
                _ => SpindlePose(joint, step, breath, tell, exposed)
            };
            if (joint == CreatureJoint.Stem)
            {
                float settle = _creatureMotion switch
                {
                    CreatureMotion.Emberling => -.075f * tell + .025f * MathF.Abs(step) * _stride,
                    CreatureMotion.Brute => -.025f * MathF.Abs(step) * _stride - .045f * _recovery,
                    CreatureMotion.Sentinel => -.025f * tell,
                    _ => -.07f * exposed - .025f * _recovery
                };
                limb.Node.Position += Vector3.Up * settle;
            }
            limb.Node.Rotation = limb.Rest + pose;
        }
    }

    private Vector3 EmberlingPose(CreatureJoint joint, float side, float cycle, float breath, float tell)
    {
        float scamper = MathF.Sin(cycle * 2) * _stride;
        // A small deterministic double-frequency shiver reads as brittle heat, without random state.
        float twitch = _reducedVisualEffects ? 0 : MathF.Sin((float)_time * 8) * MathF.Sin((float)_time * 2.3f) * .025f;
        return joint switch
        {
            CreatureJoint.Stem => new(.14f * tell - .08f * _recovery - .025f * _stride, twitch, scamper * .055f + twitch),
            CreatureJoint.LeftArm or CreatureJoint.RightArm => new(-side * scamper * .32f - .5f * tell + .14f * _recovery, -side * .12f * tell, side * (.1f + .25f * tell + .035f * breath)),
            _ => Vector3.Zero
        };
    }

    private Vector3 BrutePose(CreatureJoint joint, float step, float breath, float tell) => joint switch
    {
        CreatureJoint.Stem => new(.075f * tell - .06f * _recovery + .008f * breath, -.09f * tell, step * .028f * _stride),
        CreatureJoint.LeftArm => new(.16f + .14f * tell, -.06f, -.07f),
        // The pole-hammer head is above the shoulder: positive X draws it back, negative X strikes forward.
        CreatureJoint.RightArm => new(.1f * step * _stride + .92f * tell - .12f * _recovery, -.12f * tell, .2f * tell),
        _ => Vector3.Zero
    };

    private Vector3 SentinelPose(CreatureJoint joint, float step, float breath, float tell) => joint switch
    {
        CreatureJoint.Stem => new(-.025f * _stride + .035f * tell + .007f * breath, .16f * tell, step * .012f * _stride),
        CreatureJoint.LeftArm => new(.34f + .22f * tell, .08f, -.1f),
        CreatureJoint.RightArm => new(-.26f - .4f * tell + .045f * step * _stride, -.12f * tell, .055f),
        _ => Vector3.Zero
    };

    private Vector3 SpindlePose(CreatureJoint joint, float step, float breath, float tell, float exposed) => joint switch
    {
        CreatureJoint.Stem => new(.07f * tell - .08f * exposed + .008f * breath, -.045f * tell, step * .015f * _stride),
        CreatureJoint.LeftArm => new(.2f + .22f * tell + .12f * exposed, -.1f * exposed, -.1f - .16f * exposed),
        CreatureJoint.RightArm => new(.06f * step * _stride + .7f * tell - .1f * exposed, -.12f * tell, .12f * tell + .14f * exposed),
        _ => Vector3.Zero
    };

    private void CinderFoot(Limb limb, float phase, float strideScale)
    {
        const float stance = .68f;
        bool planted = phase < stance;
        float progress = planted ? phase / stance : (phase - stance) / (1 - stance);
        float reach = planted ? Mathf.Lerp(-1, 1, progress) : Mathf.Lerp(1, -1, Smooth(progress));
        float length = Math.Clamp(limb.Origin.Y, .35f, 1.4f);
        float stride = _stride * strideScale;
        float angle = -MathF.Asin(Math.Clamp(reach * .52f * stride / length, -.7f, .7f));
        float lift = planted ? 0 : MathF.Sin(progress * Mathf.Pi) * .13f * stride;
        limb.Node.Rotation = limb.Rest + new Vector3(angle, 0, 0);
        limb.Node.Position += Vector3.Up * (length * (MathF.Cos(angle) - 1) + lift);
    }

    private void AnimateCinderCue()
    {
        float t = _cueTime / _cueDuration;
        // Contact is the first frame; there is no second visual windup after AbilityResolved.
        float contact = 1 - Smooth((t - .22f) / .78f);
        float pulse = MathF.Sin(t * Mathf.Pi);
        foreach (var limb in _limbs)
        {
            var joint = limb.CreatureKind;
            float side = limb.Origin.X < 0 ? -1 : 1;
            if (_cue == CombatCue.Hit)
            {
                float weight = _creatureMotion switch { CreatureMotion.Emberling => 1, CreatureMotion.Brute => .32f, CreatureMotion.Sentinel => .24f, _ => .18f + .22f * _furnaceExposure };
                if (joint == CreatureJoint.Stem) limb.Node.Rotation += new Vector3(.27f, -.09f, .08f) * pulse * weight;
                else if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm) limb.Node.Rotation += new Vector3(-.18f, 0, side * .12f) * pulse * weight;
                continue;
            }
            if (_cue == CombatCue.Dodge)
            {
                if (joint == CreatureJoint.Stem) limb.Node.Rotation += new Vector3(-.1f, 0, .16f) * pulse;
                else if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm) limb.Node.Rotation += new Vector3(.15f, 0, -side * .16f) * pulse;
                continue;
            }
            Vector3 pose = _creatureMotion switch
            {
                CreatureMotion.Emberling => joint switch
                {
                    CreatureJoint.Stem => new(-.34f, .09f, -.04f),
                    CreatureJoint.LeftArm or CreatureJoint.RightArm => new(.95f - .2f * t, -side * .18f, side * .08f),
                    _ => Vector3.Zero
                },
                CreatureMotion.Brute => joint switch
                {
                    CreatureJoint.Stem => new(-.19f, .12f, -.055f),
                    CreatureJoint.LeftArm => new(.32f, -.1f, -.1f),
                    CreatureJoint.RightArm => new(-1.2f - .12f * t, .15f, -.16f),
                    _ => Vector3.Zero
                },
                CreatureMotion.Sentinel => joint switch
                {
                    CreatureJoint.Stem => new(-.08f, -.15f, 0),
                    CreatureJoint.LeftArm => new(.5f, .08f, -.08f),
                    CreatureJoint.RightArm => new(-1.12f, -.09f, .025f),
                    _ => Vector3.Zero
                },
                _ => joint switch
                {
                    CreatureJoint.Stem => new(-.105f, .075f, 0),
                    CreatureJoint.LeftArm => new(.52f, -.15f, -.18f),
                    CreatureJoint.RightArm => new(-.95f, .11f, -.08f),
                    _ => Vector3.Zero
                }
            };
            if (joint is CreatureJoint.LeftLeg or CreatureJoint.RightLeg) continue;
            limb.Node.Rotation = limb.Node.Rotation.Lerp(limb.Rest + pose, contact);
            if (joint == CreatureJoint.Stem && _creatureMotion == CreatureMotion.Emberling)
                limb.Node.Position += new Vector3(0, -.025f, -.09f) * contact;
            if (joint == CreatureJoint.RightArm && _creatureMotion is CreatureMotion.Brute or CreatureMotion.Sentinel or CreatureMotion.Spindle)
                limb.Node.Position += new Vector3(0, -.025f, -.12f) * contact;
        }
    }

    private void AnimateCinderDeath()
    {
        float t = _cueTime / _cueDuration;
        bool spindle = _creatureMotion == CreatureMotion.Spindle;
        float fall = Smooth(spindle ? (t - .14f) / .86f : t);
        BodyRoot.Position = _deathBodyPosition.Lerp(Vector3.Zero, fall);
        BodyRoot.Rotation = _deathBodyRotation.Lerp(new Vector3(0, _deathBodyRotation.Y, 0), fall);
        for (int i = 0; i < _limbs.Count; i++)
        {
            var limb = _limbs[i];
            var joint = limb.CreatureKind;
            float side = limb.Origin.X < 0 ? -1 : 1;
            Vector3 position = limb.Origin;
            Vector3 pose = Vector3.Zero;
            if (joint == CreatureJoint.Stem)
            {
                float height = _creatureMotion switch { CreatureMotion.Emberling => .46f, CreatureMotion.Brute => .68f, CreatureMotion.Sentinel => .72f, _ => .78f };
                position.Y *= height;
                pose = _creatureMotion switch
                {
                    CreatureMotion.Emberling => new(-.7f, .13f, -.3f),
                    CreatureMotion.Brute => new(-.42f, -.08f, .12f),
                    CreatureMotion.Sentinel => new(-.32f, .08f, -.1f),
                    _ => new(-.24f, 0, .045f)
                };
            }
            else if (joint is CreatureJoint.LeftLeg or CreatureJoint.RightLeg)
            {
                // Preserve foot height while folding a rigid leg under the kneeling torso.
                float angle = _creatureMotion == CreatureMotion.Emberling ? 1.05f : spindle ? .66f : .8f;
                pose = new(angle, side * .06f, side * .08f);
                position.Y *= MathF.Cos(angle);
            }
            else if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm)
            {
                pose = _creatureMotion switch
                {
                    CreatureMotion.Emberling => new(.75f, -side * .12f, -side * .35f),
                    CreatureMotion.Brute => new(side > 0 ? -.55f : .32f, side * .08f, side * .16f),
                    CreatureMotion.Sentinel => new(side > 0 ? -.38f : .2f, 0, side * .12f),
                    _ => new(side > 0 ? -.28f : .2f, 0, side * .16f)
                };
            }
            limb.Node.Rotation = _deathFromRotations[i].Lerp(limb.Rest + pose, fall);
            limb.Node.Position = _deathFromPositions[i].Lerp(position, fall);
        }
    }
}
