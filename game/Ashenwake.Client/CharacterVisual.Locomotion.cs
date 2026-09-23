using Ashenwake.Core.Simulation;
using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private double _gaitPhase;
    private float _turnLean, _travelLean, _poseRelease;
    private bool _hasCosmeticPose, _previousWindup;
    private CombatCue _previousCue;
    private Vector3 _lastBodyPosition, _lastBodyRotation, _releaseBodyPosition, _releaseBodyRotation;
    private Vector3[] _lastPoseRotations = [], _lastPosePositions = [], _releaseRotations = [], _releasePositions = [];

    private void AnimateLocomotion(float dt, Vector3 tickMovement, bool windup, string state, Vector3? facing)
    {
        // Sandbox supplies displacement per authoritative tick, repeated at render frequency.
        // Integrating its velocity makes equal travel yield equal gait at 30, 60 or 144 Hz.
        tickMovement.Y = 0;
        float speed = Math.Min(tickMovement.Length() / (float)FixedStepClock.SecondsPerTick, 9);
        float strideTarget = speed > .025f ? Math.Clamp(MathF.Sqrt(speed / 4.5f), .2f, 1.15f) : 0;
        _stride = Mathf.Lerp(_stride, strideTarget, Response(dt, strideTarget > _stride ? 15 : 20));
        _windup = Mathf.Lerp(_windup, windup ? 1 : 0, Response(dt, 16));
        _recovery = Mathf.Lerp(_recovery, state == "Recover" && !windup ? 1 : 0, Response(dt, 14));

        Vector3 direction = facing ?? tickMovement;
        direction.Y = 0;
        float oldFacing = _facing;
        if (direction.LengthSquared() > .00001f)
            _facing = Mathf.LerpAngle(_facing, Mathf.Atan2(-direction.X, -direction.Z), Response(dt, 14));
        // Wrap the stored angle too: repeated turns must never accumulate precision loss.
        _facing = Mathf.Wrap(_facing, -Mathf.Pi, Mathf.Pi);
        float turnSpeed = dt > 0 ? Mathf.AngleDifference(oldFacing, _facing) / dt : 0;
        _turnLean = Mathf.Lerp(_turnLean, Math.Clamp(-turnSpeed * .025f, -.13f, .13f) * Math.Min(_stride, 1), Response(dt, 12));
        _travelLean = Mathf.Lerp(_travelLean, Math.Min(speed / 4.5f, 1.3f) * -.055f, Response(dt, 10));
        if (strideTarget > 0 && _cue != CombatCue.Dodge)
            _gaitPhase = (_gaitPhase + speed * dt / (2 * strideTarget)) % 1;

        float cycle = (float)_gaitPhase * Mathf.Tau;
        float bob = .018f * MathF.Sin((float)_time * 2) + .035f * _stride * MathF.Abs(MathF.Sin(cycle));
        BodyRoot.Rotation = new(_windup * -.16f + _travelLean, _facing, _turnLean);
        BodyRoot.Position = new(.022f * MathF.Sin(cycle) * _stride, bob, 0);
        foreach (var limb in _limbs)
        {
            limb.Node.Position = limb.Origin;
            if (limb.Motion is "left_leg" or "right_leg")
            {
                AnimateFoot(limb, limb.Motion == "left_leg" ? (float)_gaitPhase : ((float)_gaitPhase + .5f) % 1, bob);
                continue;
            }
            float swing = MathF.Sin(cycle) * _stride * .4f * limb.Amplitude;
            Vector3 rotation = limb.Motion switch
            {
                "left_arm" => new(-swing - _windup * .95f * limb.Amplitude, 0, -_windup * .2f),
                "right_arm" => new(swing - _windup * 1.3f * limb.Amplitude, 0, _windup * .25f),
                "jaw" => new((.06f + _windup * .2f) * (1 + MathF.Sin((float)_time * 3)), 0, 0),
                _ => new(.035f * MathF.Sin((float)_time * 2) * limb.Amplitude, 0, .025f * MathF.Sin((float)_time * 1.5f) * limb.Amplitude)
            };
            if (limb.Motion.Contains("arm", StringComparison.Ordinal)) rotation.X += _recovery * .4f;
            limb.Node.Rotation = limb.Rest + rotation;
        }
    }

    private void AnimateFoot(Limb limb, float phase, float bodyBob)
    {
        // A longer, linear planted phase and a shorter lifted return reduce skating.
        // Correct the rigid leg's height so swinging does not pull its foot below ground.
        const float stance = .6f;
        bool planted = phase < stance;
        float swing = planted ? phase / stance : (phase - stance) / (1 - stance);
        float reach = planted ? Mathf.Lerp(-1, 1, swing) : Mathf.Lerp(1, -1, Smooth(swing));
        float length = Math.Clamp(Math.Abs(limb.Origin.Y), .35f, 1.4f);
        float offset = reach * .6f * _stride * Math.Min(limb.Amplitude, 1);
        float angle = -MathF.Asin(Math.Clamp(offset / length, -.8f, .8f));
        float lift = planted ? 0 : MathF.Sin(swing * Mathf.Pi) * .16f * _stride;
        float compensation = limb.Node.GetParent() == BodyRoot ? bodyBob : 0;
        limb.Node.Rotation = limb.Rest + new Vector3(angle, 0, 0);
        limb.Node.Position += new Vector3(0, length * (MathF.Cos(angle) - 1) + lift - compensation, 0);
    }

    private void BlendPoseRelease(float dt, bool windup)
    {
        if (_lastPoseRotations.Length != _limbs.Count)
        {
            _lastPoseRotations = new Vector3[_limbs.Count]; _lastPosePositions = new Vector3[_limbs.Count];
            _releaseRotations = new Vector3[_limbs.Count]; _releasePositions = new Vector3[_limbs.Count];
            _hasCosmeticPose = false;
        }
        // Resolved attacks, dodges and authoritative tells are immediately readable.
        // Only their return to locomotion blends from the last displayed cosmetic pose.
        if (_cue != CombatCue.None || windup) _poseRelease = 0;
        else if (_hasCosmeticPose && (_previousCue != CombatCue.None || _previousWindup))
        {
            _poseRelease = .09f;
            _releaseBodyRotation = _lastBodyRotation; _releaseBodyPosition = _lastBodyPosition;
            Array.Copy(_lastPoseRotations, _releaseRotations, _limbs.Count);
            Array.Copy(_lastPosePositions, _releasePositions, _limbs.Count);
        }
        if (_poseRelease > 0)
        {
            _poseRelease = Math.Max(0, _poseRelease - dt);
            float blend = Smooth(1 - _poseRelease / .09f);
            BodyRoot.Rotation = BlendAngles(_releaseBodyRotation, BodyRoot.Rotation, blend);
            BodyRoot.Position = _releaseBodyPosition.Lerp(BodyRoot.Position, blend);
            for (int i = 0; i < _limbs.Count; i++)
            {
                _limbs[i].Node.Rotation = BlendAngles(_releaseRotations[i], _limbs[i].Node.Rotation, blend);
                _limbs[i].Node.Position = _releasePositions[i].Lerp(_limbs[i].Node.Position, blend);
            }
        }
        _lastBodyPosition = BodyRoot.Position; _lastBodyRotation = BodyRoot.Rotation;
        for (int i = 0; i < _limbs.Count; i++)
        {
            _lastPoseRotations[i] = _limbs[i].Node.Rotation; _lastPosePositions[i] = _limbs[i].Node.Position;
        }
        _hasCosmeticPose = true; _previousCue = _cue; _previousWindup = windup;
    }

    private static float Response(float dt, float rate) => 1 - MathF.Exp(-dt * rate);
    private static Vector3 BlendAngles(Vector3 from, Vector3 to, float amount) => new(
        Mathf.LerpAngle(from.X, to.X, amount), Mathf.LerpAngle(from.Y, to.Y, amount), Mathf.LerpAngle(from.Z, to.Z, amount));
}
