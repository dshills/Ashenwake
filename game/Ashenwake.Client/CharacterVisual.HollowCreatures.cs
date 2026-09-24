using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private bool HollowCreature => _creatureMotion is CreatureMotion.Shadow or CreatureMotion.Echo or CreatureMotion.Breach;
    private bool _breachExposed, _breachExposureKnown;
    private float _breachExposure;
    private double _hollowLastTime;

    /// <summary>Pose-only reflection of Core's shield state; never grants or removes protection.</summary>
    internal void SetBreachExposed(bool exposed)
    {
        if (_creatureMotion != CreatureMotion.Breach || IsDying) return;
        _breachExposed = exposed;
        if (_breachExposureKnown) return;
        _breachExposureKnown = true;
        _breachExposure = exposed ? 1 : 0;
    }

    private void AnimateHollowBody()
    {
        float dt = (float)Math.Clamp(_time - _hollowLastTime, 0, .1);
        _hollowLastTime = _time;
        _breachExposure = Mathf.Lerp(_breachExposure, _breachExposed ? 1 : 0, Response(dt, 9));
        float cycle = (float)_gaitPhase * Mathf.Tau;
        float step = MathF.Sin(cycle);
        float breath = _reducedVisualEffects ? 0 : MathF.Sin((float)_time * 1.8f);
        float drift = _reducedVisualEffects ? 0 : MathF.Sin((float)_time * 3.1f + .8f);
        // A resolved strike takes priority over Core's same-tick displayed Windup state.
        float tell = _cue == CombatCue.Attack ? 0 : _windup;
        float exposed = _breachExposure * (1 - tell);
        BodyRoot.Position = Vector3.Zero;
        BodyRoot.Rotation = new(0, _facing, 0);
        foreach (var limb in _limbs)
        {
            var joint = limb.CreatureKind;
            float side = limb.Origin.X < 0 ? -1 : 1;
            bool lower = limb.Amplitude > .8f;
            limb.Node.Position = limb.Origin;
            if (joint is CreatureJoint.LeftLeg or CreatureJoint.RightLeg)
            {
                HollowFoot(limb, joint == CreatureJoint.LeftLeg ? (float)_gaitPhase : ((float)_gaitPhase + .5f) % 1, tell);
                continue;
            }
            Vector3 pose = _creatureMotion switch
            {
                CreatureMotion.Shadow => joint switch
                {
                    CreatureJoint.Stem => new(-.045f * _stride + .15f * tell - .09f * _recovery, -.2f * tell + .025f * drift, step * .05f * _stride + .025f * breath),
                    CreatureJoint.Head => new(-.18f * tell + .12f * _recovery, .09f * breath - .035f * drift, -.04f * drift),
                    CreatureJoint.LeftArm => new(.28f + .24f * tell + .11f * step * _stride, -.2f * tell, -.19f - .07f * breath),
                    CreatureJoint.RightArm => new(-.1f - .45f * tell - .18f * step * _stride, .17f * tell, .12f + .05f * drift),
                    _ => Vector3.Zero
                },
                CreatureMotion.Echo => joint switch
                {
                    CreatureJoint.Stem => new(-.035f * _stride + .075f * tell - .065f * _recovery, -.13f * tell, .025f * breath + .03f * step * _stride),
                    // Lift the book to invoke; the upright staff draws back before the cast.
                    CreatureJoint.LeftArm => new(.2f + .95f * tell - .12f * _recovery, -.27f * tell, -.1f - .36f * tell + .035f * drift),
                    CreatureJoint.RightArm => new(.44f * tell - .11f * _recovery, .23f * tell, .09f + .21f * tell - .025f * breath),
                    _ => Vector3.Zero
                },
                _ => joint switch
                {
                    CreatureJoint.Stem => new(.13f * tell - .12f * exposed + .015f * breath, -.08f * tell, .025f * step * _stride),
                    CreatureJoint.LeftArm or CreatureJoint.RightArm => new(
                        (lower ? .22f : .45f) + (lower ? .55f : -.24f) * tell - .28f * exposed + .025f * breath,
                        side * (-.27f + (lower ? .28f : -.16f) * tell + .22f * exposed),
                        side * (.16f + (lower ? .16f : .4f) * tell + .28f * exposed + .025f * drift)),
                    _ => Vector3.Zero
                }
            };
            if (joint == CreatureJoint.Stem)
            {
                limb.Node.Position += _creatureMotion switch
                {
                    CreatureMotion.Shadow => new Vector3(.035f * drift, -.025f * MathF.Abs(step) * _stride - .045f * tell, .025f * breath),
                    CreatureMotion.Echo => new Vector3(.025f * drift, .035f * breath + .025f * MathF.Abs(step) * _stride + .045f * tell, 0),
                    _ => new Vector3(0, .035f * breath - .11f * exposed + .055f * tell, 0)
                };
            }
            limb.Node.Rotation = limb.Rest + pose;
        }
    }

    private void HollowFoot(Limb limb, float phase, float tell)
    {
        const float stance = .7f;
        bool planted = phase < stance;
        float progress = planted ? phase / stance : (phase - stance) / (1 - stance);
        float reach = planted ? Mathf.Lerp(-1, 1, progress) : Mathf.Lerp(1, -1, Smooth(progress));
        float length = Math.Clamp(limb.Origin.Y, .35f, 1.4f);
        float stride = _stride * .65f * (1 - tell);
        float angle = -MathF.Asin(Math.Clamp(reach * .52f * stride / length, -.7f, .7f));
        float lift = planted ? 0 : MathF.Sin(progress * Mathf.Pi) * .1f * stride;
        limb.Node.Rotation = limb.Rest + new Vector3(angle, 0, 0);
        limb.Node.Position += Vector3.Up * (length * (MathF.Cos(angle) - 1) + lift);
    }

    private void AnimateHollowCue()
    {
        float t = _cueTime / _cueDuration;
        float contact = 1 - Smooth((t - .2f) / .8f);
        float pulse = MathF.Sin(t * Mathf.Pi);
        foreach (var limb in _limbs)
        {
            var joint = limb.CreatureKind;
            float side = limb.Origin.X < 0 ? -1 : 1;
            bool lower = limb.Amplitude > .8f;
            if (_cue == CombatCue.Hit)
            {
                float weight = _creatureMotion == CreatureMotion.Breach ? .25f + .5f * _breachExposure : 1;
                if (joint is CreatureJoint.Stem or CreatureJoint.Head) limb.Node.Rotation += new Vector3(.22f, -.12f, .1f) * pulse * weight;
                else if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm) limb.Node.Rotation += new Vector3(-.16f, side * .08f, side * .14f) * pulse * weight;
                continue;
            }
            if (_cue == CombatCue.Dodge)
            {
                if (joint == CreatureJoint.Stem) limb.Node.Rotation += new Vector3(-.12f, .09f, .12f) * pulse;
                else if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm) limb.Node.Rotation += new Vector3(.2f, 0, -side * .25f) * pulse;
                continue;
            }
            Vector3 pose = _creatureMotion switch
            {
                CreatureMotion.Shadow => joint switch
                {
                    CreatureJoint.Stem => new(-.24f, .24f, -.05f),
                    CreatureJoint.Head => new(.22f, -.18f, .06f),
                    CreatureJoint.LeftArm => new(.72f, -.22f, -.25f),
                    CreatureJoint.RightArm => new(1.4f - .18f * t, -.3f, -.15f),
                    _ => Vector3.Zero
                },
                CreatureMotion.Echo => joint switch
                {
                    CreatureJoint.Stem => new(-.14f, .16f, -.025f),
                    CreatureJoint.LeftArm => new(1.16f, .19f, -.48f),
                    CreatureJoint.RightArm => new(-.82f, -.18f, .13f),
                    _ => Vector3.Zero
                },
                _ => joint switch
                {
                    CreatureJoint.Stem => new(-.2f, .07f, 0),
                    CreatureJoint.LeftArm or CreatureJoint.RightArm => new(lower ? .84f : 1.2f, side * -.32f, side * (lower ? .27f : .48f)),
                    _ => Vector3.Zero
                }
            };
            if (joint is CreatureJoint.LeftLeg or CreatureJoint.RightLeg) continue;
            limb.Node.Rotation = limb.Node.Rotation.Lerp(limb.Rest + pose, contact);
            if (joint == CreatureJoint.Stem)
                limb.Node.Position += new Vector3(0, -.035f, _creatureMotion == CreatureMotion.Shadow ? -.12f : -.06f) * contact;
            else if (joint is CreatureJoint.LeftArm or CreatureJoint.RightArm)
                limb.Node.Position += Vector3.Forward * ((_creatureMotion == CreatureMotion.Shadow && side > 0 ? .12f : .04f) * contact);
        }
    }

    private void AnimateHollowDeath()
    {
        float t = _cueTime / _cueDuration;
        bool breach = _creatureMotion == CreatureMotion.Breach;
        float fall = Smooth(t);
        float dissolve = Smooth((t - (breach ? .4f : .25f)) / (breach ? .6f : .75f));
        // Collapse the cosmetic model into a shadow/wisp; no shared opacity or mesh resources change.
        Vector3 remnant = _creatureMotion switch
        {
            CreatureMotion.Shadow => new(.52f, .07f, .74f),
            CreatureMotion.Echo => new(.07f, .25f, .07f),
            _ => new(.28f, .18f, .28f)
        };
        BodyRoot.Scale = Vector3.One.Lerp(remnant, dissolve);
        BodyRoot.Position = _deathBodyPosition.Lerp(Vector3.Zero, fall);
        BodyRoot.Rotation = _deathBodyRotation.Lerp(new Vector3(0, _deathBodyRotation.Y, 0), fall);
        for (int i = 0; i < _limbs.Count; i++)
        {
            var limb = _limbs[i];
            var joint = limb.CreatureKind;
            float side = limb.Origin.X < 0 ? -1 : 1;
            bool lower = limb.Amplitude > .8f;
            float settle = breach && joint is CreatureJoint.LeftArm or CreatureJoint.RightArm
                ? Smooth((t - (lower ? .22f : .06f)) / (lower ? .78f : .94f)) : fall;
            Vector3 position = limb.Origin;
            Vector3 pose = joint switch
            {
                CreatureJoint.Stem => new(-.38f, .15f, _creatureMotion == CreatureMotion.Shadow ? -.2f : .08f),
                CreatureJoint.Head => new(-.4f, -.16f, .2f),
                CreatureJoint.LeftArm or CreatureJoint.RightArm => new(breach ? .75f : .38f, side * -.35f, -side * (breach ? .44f : .22f)),
                CreatureJoint.LeftLeg or CreatureJoint.RightLeg => new(.76f, side * .1f, side * .1f),
                _ => Vector3.Zero
            };
            if (joint == CreatureJoint.Stem) position.Y *= breach ? .72f : .8f;
            else if (joint is CreatureJoint.LeftLeg or CreatureJoint.RightLeg) position.Y *= MathF.Cos(.76f);
            limb.Node.Rotation = _deathFromRotations[i].Lerp(limb.Rest + pose, settle);
            limb.Node.Position = _deathFromPositions[i].Lerp(position, settle);
        }
    }
}
