using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private bool SpineCreature => _creatureMotion is CreatureMotion.Giant or CreatureMotion.Keeper or CreatureMotion.BoneSentinel or CreatureMotion.Warden;
    private bool _wardenExposed, _wardenExposureKnown;
    private float _wardenExposure;
    private double _spineLastTime;

    /// <summary>Pose-only reflection of the current authoritative guard state.</summary>
    internal void SetWardenExposed(bool exposed)
    {
        if (_creatureMotion != CreatureMotion.Warden || IsDying) return;
        _wardenExposed = exposed;
        if (_wardenExposureKnown) return;
        _wardenExposureKnown = true;
        _wardenExposure = exposed ? 1 : 0;
    }

    private void AnimateSpineBody()
    {
        float dt = (float)Math.Clamp(_time - _spineLastTime, 0, .1);
        _spineLastTime = _time;
        _wardenExposure = Mathf.Lerp(_wardenExposure, _wardenExposed ? 1 : 0, Response(dt, 9));
        float cycle = (float)_gaitPhase * Mathf.Tau;
        float step = MathF.Sin(cycle);
        float breath = _reducedVisualEffects ? 0 : MathF.Sin((float)_time * 1.45f);
        // AbilityResolved can share a tick with Core's last displayed Windup state.
        float tell = _cue == CombatCue.Attack ? 0 : _windup;
        float exposed = _wardenExposure * (1 - tell);
        BodyRoot.Position = Vector3.Zero;
        BodyRoot.Rotation = new(0, _facing, 0);
        foreach (var limb in _limbs)
        {
            var joint = limb.CreatureKind;
            limb.Node.Position = limb.Origin;
            if (joint is CreatureJoint.LeftLeg or CreatureJoint.RightLeg)
            {
                SpineFoot(limb, joint == CreatureJoint.LeftLeg ? (float)_gaitPhase : ((float)_gaitPhase + .5f) % 1, tell);
                continue;
            }
            Vector3 pose = _creatureMotion switch
            {
                CreatureMotion.Giant => GiantPose(joint, step, breath, tell),
                CreatureMotion.Keeper => KeeperPose(joint, step, breath, tell),
                CreatureMotion.BoneSentinel => BoneSentinelPose(joint, step, breath, tell),
                _ => WardenPose(joint, step, breath, tell, exposed)
            };
            if (joint == CreatureJoint.Stem)
            {
                float settle = _creatureMotion switch
                {
                    CreatureMotion.Giant => -.04f * MathF.Abs(step) * _stride - .045f * tell - .04f * _recovery,
                    CreatureMotion.Keeper => .018f * MathF.Abs(step) * _stride + .025f * tell,
                    CreatureMotion.BoneSentinel => -.02f * tell,
                    _ => -.09f * exposed - .035f * _recovery - .02f * MathF.Abs(step) * _stride
                };
                limb.Node.Position += Vector3.Up * settle;
            }
            limb.Node.Rotation = limb.Rest + pose;
        }
    }

    private Vector3 GiantPose(CreatureJoint joint, float step, float breath, float tell) => joint switch
    {
        CreatureJoint.Stem => new(.095f * tell - .065f * _recovery + .009f * breath, -.16f * tell, step * .035f * _stride),
        CreatureJoint.LeftArm => new(.25f + .22f * tell, -.08f * tell, -.1f),
        // The oath hammer stands above the hand: positive X draws it back for the slam.
        CreatureJoint.RightArm => new(.13f * step * _stride + 1.05f * tell - .12f * _recovery, -.16f * tell, .25f * tell),
        _ => Vector3.Zero
    };

    private Vector3 KeeperPose(CreatureJoint joint, float step, float breath, float tell) => joint switch
    {
        CreatureJoint.Stem => new(-.035f * _stride - .06f * tell + .065f * _recovery + .012f * breath, -.1f * tell, .035f * step * _stride),
        // The left hand holds a book; lift it for the invocation while keeping the staff upright.
        CreatureJoint.LeftArm => new(.25f + .82f * tell - .08f * _recovery, -.17f * tell, -.1f - .24f * tell + .025f * breath),
        CreatureJoint.RightArm => new(.04f * step * _stride + .25f * tell, .14f * tell, .055f + .18f * tell),
        _ => Vector3.Zero
    };

    private Vector3 BoneSentinelPose(CreatureJoint joint, float step, float breath, float tell) => joint switch
    {
        CreatureJoint.Stem => new(-.02f * _stride + .018f * tell + .006f * breath, .22f * tell, .009f * step * _stride),
        CreatureJoint.LeftArm => new(.4f + .28f * tell, .12f * tell, -.09f),
        CreatureJoint.RightArm => new(-.18f - .32f * tell + .035f * step * _stride, -.19f * tell, .06f),
        _ => Vector3.Zero
    };

    private Vector3 WardenPose(CreatureJoint joint, float step, float breath, float tell, float exposed) => joint switch
    {
        CreatureJoint.Stem => new(.045f * tell - .12f * exposed + .007f * breath, -.11f * tell + .045f * exposed, .018f * step * _stride),
        CreatureJoint.LeftArm => new(.55f + .2f * tell - .43f * exposed, .12f - .22f * exposed, -.1f - .27f * exposed),
        CreatureJoint.RightArm => new(.065f * step * _stride + .82f * tell - .17f * exposed, -.15f * tell, .15f * tell + .2f * exposed),
        _ => Vector3.Zero
    };

    private void SpineFoot(Limb limb, float phase, float tell)
    {
        bool sentinel = _creatureMotion == CreatureMotion.BoneSentinel;
        float stance = sentinel ? .68f : .75f;
        bool planted = phase < stance;
        float progress = planted ? phase / stance : (phase - stance) / (1 - stance);
        float reach = planted ? Mathf.Lerp(-1, 1, progress) : Mathf.Lerp(1, -1, Smooth(progress));
        float length = Math.Clamp(limb.Origin.Y, .35f, 1.4f);
        float stride = _stride * (sentinel ? .7f : .52f) * (1 - tell);
        float angle = -MathF.Asin(Math.Clamp(reach * .52f * stride / length, -.7f, .7f));
        float lift = planted ? 0 : MathF.Sin(progress * Mathf.Pi) * (sentinel ? .11f : .15f) * stride;
        limb.Node.Rotation = limb.Rest + new Vector3(angle, 0, 0);
        limb.Node.Position += Vector3.Up * (length * (MathF.Cos(angle) - 1) + lift);
    }

    private void AnimateSpineCue()
    {
        float t = _cueTime / _cueDuration;
        float contact = 1 - Smooth((t - .2f) / .8f);
        float pulse = MathF.Sin(t * Mathf.Pi);
        foreach (var limb in _limbs)
        {
            var joint = limb.CreatureKind;
            float side = limb.Origin.X < 0 ? -1 : 1;
            if (_cue == CombatCue.Hit)
            {
                float weight = _creatureMotion switch { CreatureMotion.Giant => .3f, CreatureMotion.Keeper => .8f, CreatureMotion.BoneSentinel => .22f, _ => .16f + .48f * _wardenExposure };
                if (joint == CreatureJoint.Stem) limb.Node.Rotation += new Vector3(.26f, -.12f, .06f) * pulse * weight;
                else if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm) limb.Node.Rotation += new Vector3(-.19f, 0, side * .12f) * pulse * weight;
                continue;
            }
            if (_cue == CombatCue.Dodge)
            {
                if (joint == CreatureJoint.Stem) limb.Node.Rotation += new Vector3(-.1f, .08f, .1f) * pulse;
                else if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm) limb.Node.Rotation += new Vector3(.2f, 0, -side * .18f) * pulse;
                continue;
            }
            Vector3 pose = _creatureMotion switch
            {
                CreatureMotion.Giant => joint switch
                {
                    CreatureJoint.Stem => new(-.25f, .2f, -.06f),
                    CreatureJoint.LeftArm => new(.4f, -.12f, -.12f),
                    CreatureJoint.RightArm => new(-1.25f - .1f * t, .2f, -.17f),
                    _ => Vector3.Zero
                },
                CreatureMotion.Keeper => joint switch
                {
                    CreatureJoint.Stem => new(-.14f, .13f, -.04f),
                    CreatureJoint.LeftArm => new(.85f, -.18f, -.32f),
                    CreatureJoint.RightArm => new(-.65f, -.12f, .12f),
                    _ => Vector3.Zero
                },
                CreatureMotion.BoneSentinel => joint switch
                {
                    CreatureJoint.Stem => new(-.095f, -.19f, 0),
                    CreatureJoint.LeftArm => new(.67f, .08f, -.06f),
                    CreatureJoint.RightArm => new(-1.18f, -.15f, .02f),
                    _ => Vector3.Zero
                },
                _ => joint switch
                {
                    CreatureJoint.Stem => new(-.16f, .14f, -.03f),
                    CreatureJoint.LeftArm => new(.72f, -.08f, -.16f),
                    CreatureJoint.RightArm => new(-1.06f, .18f, -.14f),
                    _ => Vector3.Zero
                }
            };
            if (joint is CreatureJoint.LeftLeg or CreatureJoint.RightLeg) continue;
            limb.Node.Rotation = limb.Node.Rotation.Lerp(limb.Rest + pose, contact);
            if (joint == CreatureJoint.RightArm)
                limb.Node.Position += new Vector3(0, -.025f, _creatureMotion == CreatureMotion.Keeper ? -.045f : -.1f) * contact;
            if (joint == CreatureJoint.Stem && _creatureMotion == CreatureMotion.Giant)
                limb.Node.Position += Vector3.Down * (.045f * contact);
        }
    }

    private void AnimateSpineDeath()
    {
        float t = _cueTime / _cueDuration;
        bool warden = _creatureMotion == CreatureMotion.Warden;
        float fall = Smooth((t - (warden ? .12f : 0)) / (warden ? .88f : 1));
        BodyRoot.Position = _deathBodyPosition.Lerp(Vector3.Zero, fall);
        BodyRoot.Rotation = _deathBodyRotation.Lerp(new Vector3(0, _deathBodyRotation.Y, 0), fall);
        for (int i = 0; i < _limbs.Count; i++)
        {
            var limb = _limbs[i];
            var joint = limb.CreatureKind;
            float side = limb.Origin.X < 0 ? -1 : 1;
            float settle = fall;
            Vector3 position = limb.Origin;
            Vector3 pose = Vector3.Zero;
            if (joint == CreatureJoint.Stem)
            {
                float height = _creatureMotion switch { CreatureMotion.Giant => .58f, CreatureMotion.Keeper => .92f, CreatureMotion.BoneSentinel => .64f, _ => .63f };
                position.Y *= height;
                pose = _creatureMotion switch
                {
                    CreatureMotion.Giant => new(-.52f, -.12f, .14f),
                    CreatureMotion.Keeper => new(-.58f, .16f, -.2f),
                    CreatureMotion.BoneSentinel => new(-.37f, .09f, -.12f),
                    _ => new(-.43f, -.08f, .1f)
                };
            }
            else if (joint is CreatureJoint.LeftLeg or CreatureJoint.RightLeg)
            {
                float angle = _creatureMotion == CreatureMotion.Giant ? .98f : .85f;
                pose = new(angle, side * .08f, side * .08f);
                position.Y *= MathF.Cos(angle);
            }
            else if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm)
            {
                // The shield gives way first; the weapon arm loses tension after the torso kneels.
                if (warden) settle = Smooth((t - (side > 0 ? .24f : 0)) / (side > 0 ? .76f : .64f));
                pose = _creatureMotion switch
                {
                    CreatureMotion.Giant => new(side > 0 ? -.55f : .24f, side * .1f, side * .24f),
                    CreatureMotion.Keeper => new(side > 0 ? -.5f : .15f, side * .18f, side * .27f),
                    CreatureMotion.BoneSentinel => new(side > 0 ? -.45f : .16f, 0, side * .15f),
                    _ => new(side > 0 ? -.58f : .08f, side * .1f, side * .32f)
                };
            }
            limb.Node.Rotation = _deathFromRotations[i].Lerp(limb.Rest + pose, settle);
            limb.Node.Position = _deathFromPositions[i].Lerp(position, settle);
        }
    }
}
