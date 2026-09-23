using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private enum CombatCue { None, Hit, Attack, Dodge, Death }
    private enum MotionStyle { Melee, Vanguard, Veilwalker, Arcanist, Gravecaller, Warden, Archer, Caster, Armored, Beast, Hound, Bell, Plant, Swarm, Anchor }
    private CombatCue _cue;
    private MotionStyle _motionStyle;
    private float _cueTime, _cueDuration;
    private bool _isWinding, _shieldAttack;
    private Vector3 _deathBodyRotation, _deathBodyPosition;
    private Vector3[] _deathFromRotations = [], _deathFromPositions = [];

    public bool IsDying => _cue == CombatCue.Death;
    public bool DeathFinished => IsDying && _cueTime >= _cueDuration;
    public string ActiveCue => _cue switch
    {
        CombatCue.Attack => "attack",
        CombatCue.Dodge => "dodge",
        CombatCue.Hit => "hit",
        CombatCue.Death => "death",
        _ => ""
    };

    private void ConfigureAnimation(string definitionId, string role, string discipline, bool allied)
    {
        string id = definitionId.ToLowerInvariant();
        string kind = discipline.ToLowerInvariant();
        string family = role.ToLowerInvariant().Replace(" elite", "", StringComparison.Ordinal);
        _motionStyle = kind switch
        {
            "vanguard" => MotionStyle.Vanguard,
            "veilwalker" => MotionStyle.Veilwalker,
            "arcanist" => MotionStyle.Arcanist,
            "gravecaller" => MotionStyle.Gravecaller,
            "warden" => MotionStyle.Warden,
            _ when allied => MotionStyle.Gravecaller,
            _ when id.Contains("bell_saint", StringComparison.Ordinal) || id.Contains("serath", StringComparison.Ordinal) => MotionStyle.Bell,
            _ when id.Contains("antler", StringComparison.Ordinal) || id.Contains("bell_beast", StringComparison.Ordinal) || family == "beast" => MotionStyle.Beast,
            _ when id.Contains("memory_archer", StringComparison.Ordinal) => MotionStyle.Archer,
            _ when id.Contains("needle_swarm", StringComparison.Ordinal) => MotionStyle.Swarm,
            _ when id.Contains("root", StringComparison.Ordinal) || id.Contains("vine", StringComparison.Ordinal) || id.Contains("ilyra", StringComparison.Ordinal) => MotionStyle.Plant,
            _ when id.Contains("hound", StringComparison.Ordinal) || family == "rusher" && !id.Contains("emberling", StringComparison.Ordinal) => MotionStyle.Hound,
            _ when family == "anchor" => MotionStyle.Anchor,
            _ when family is "bell" or "bellsaint" => MotionStyle.Bell,
            _ when family == "armored" || id.Contains("vael", StringComparison.Ordinal) || id.Contains("furnace_spindle", StringComparison.Ordinal) => MotionStyle.Armored,
            _ when family is "ranged" or "support" || id.Contains("nhal", StringComparison.Ordinal) || id.Contains("breach_heart", StringComparison.Ordinal) => MotionStyle.Caster,
            _ => MotionStyle.Melee
        };
        ConfigureWeaponMotion(id, kind, allied);
    }

    /// <summary>React to an already-authoritative event. Clips never move the actor or schedule combat.</summary>
    public void React(string cue, string skillId = "")
    {
        CombatCue incoming = cue switch
        {
            "attack" => CombatCue.Attack,
            "dodge" => CombatCue.Dodge,
            "hit" => CombatCue.Hit,
            "death" => CombatCue.Death,
            _ => CombatCue.None
        };
        if (incoming == CombatCue.None || IsDying || (int)incoming < (int)_cue) return;
        // A flinch must not suggest that damage interrupted an authoritative attack tell.
        if (incoming == CombatCue.Hit && (_isWinding || _cue == CombatCue.Hit)) return;
        _cue = incoming;
        _cueTime = 0;
        if (incoming == CombatCue.Attack) SelectWeaponAttack(skillId);
        _cueDuration = incoming switch
        {
            CombatCue.Dodge => .4f,
            CombatCue.Hit => .22f,
            CombatCue.Death => _motionStyle is MotionStyle.Bell or MotionStyle.Beast ? 1.12f : .86f,
            _ when _weaponRig => WeaponAttackDuration,
            _ => _motionStyle switch
            {
                MotionStyle.Vanguard => .44f,
                MotionStyle.Veilwalker => .34f,
                MotionStyle.Arcanist => .58f,
                MotionStyle.Gravecaller => .64f,
                MotionStyle.Warden => .5f,
                MotionStyle.Bell or MotionStyle.Beast or MotionStyle.Armored => .62f,
                MotionStyle.Caster or MotionStyle.Plant or MotionStyle.Anchor => .56f,
                _ => .4f
            }
        };
        if (!IsDying) return;
        _deathBodyRotation = BodyRoot.Rotation;
        _deathBodyPosition = BodyRoot.Position;
        for (int i = 0; i < _limbs.Count; i++)
        {
            _deathFromRotations[i] = _limbs[i].Node.Rotation;
            _deathFromPositions[i] = _limbs[i].Node.Position;
        }
    }

    /// <summary>A new authoritative action interrupts old cosmetic follow-through.</summary>
    public void BeginAttackWindup()
    {
        if (_cue is CombatCue.Hit or CombatCue.Attack) _cue = CombatCue.None;
        _isWinding = true;
    }

    private void AdvanceCue(float delta, bool windup)
    {
        _isWinding = windup;
        // Core's displayed state can still be Windup on the tick it emits AbilityResolved.
        // Only a new start event cancels attack follow-through; the old tell must not erase contact.
        if (_cue == CombatCue.Hit && windup) _cue = CombatCue.None;
        if (_cue == CombatCue.None) return;
        _cueTime = Math.Min(_cueTime + delta, _cueDuration);
        if (_cueTime >= _cueDuration) _cue = CombatCue.None;
    }

    private void AnimateFamilyAnticipation()
    {
        AnimateWeaponAnticipation();
        if (_motionStyle == MotionStyle.Hound)
        {
            // Rear back into a low spring, then let the head/shoulders sag after the pounce.
            BodyRoot.Rotation += new Vector3(.43f * _windup - .23f * _recovery, 0, 0);
            BodyRoot.Position += Vector3.Down * (.12f * _windup + .075f * _recovery);
            foreach (var limb in _limbs)
            {
                if (limb.Motion is "left_leg" or "right_leg")
                    limb.Node.Rotation += new Vector3((limb.Motion == "left_leg" ? -.34f : .34f) * _windup, 0, 0);
                else if (limb.Motion == "sway")
                    limb.Node.Rotation += new Vector3(-.1f * _recovery, 0, .04f * _windup);
            }
        }
        else if (_motionStyle == MotionStyle.Swarm)
        {
            // Gather and rear the three insects before a strike; recovery spreads them low.
            BodyRoot.Rotation += new Vector3(.31f * _windup - .19f * _recovery, 0, 0);
            foreach (var limb in _limbs)
            {
                if (limb.Motion != "sway") continue;
                float spread = -.26f * _windup + .2f * _recovery;
                limb.Node.Position += new Vector3(limb.Origin.X * spread, .12f * _windup - .11f * _recovery, limb.Origin.Z * spread);
                limb.Node.Rotation += new Vector3(.25f * _windup - .13f * _recovery, 0, -.18f * limb.Origin.X * _windup);
            }
        }
    }

    private void AnimateCue()
    {
        if (_cue == CombatCue.None) return;
        float t = _cueTime / _cueDuration;
        float weight = Smooth(t / .13f) * (1 - Smooth((t - .68f) / .32f));
        // Resolved attacks start at contact; only authoritative windup poses anticipate a hit.
        if (_cue == CombatCue.Attack && _weaponRig) weight = 1 - Smooth((t - .48f) / .52f);
        float strike = _cue == CombatCue.Attack && _weaponRig ? .52f + .48f * Smooth(t / .55f) : Smooth((t - .16f) / .43f);
        float pulse = MathF.Sin(t * Mathf.Pi);
        Vector3 lean = Vector3.Zero;
        if (_cue == CombatCue.Dodge)
        {
            lean = new(-.46f * pulse, .1f * pulse, .65f * pulse);
            BodyRoot.Position += Vector3.Down * (.19f * pulse);
        }
        else if (_cue == CombatCue.Hit) lean = new(.25f * pulse, 0, -.12f * pulse);
        else
        {
            lean = _motionStyle switch
            {
                MotionStyle.Vanguard => new(-.12f * strike, Mathf.Lerp(-.28f, .38f, strike), -.08f),
                MotionStyle.Veilwalker => new(-.22f, .17f * MathF.Sin(t * Mathf.Tau), -.1f),
                MotionStyle.Arcanist => new(-.07f, -.1f, .08f),
                MotionStyle.Gravecaller => new(-.12f, .04f, 0),
                MotionStyle.Warden => new(-.14f, Mathf.Lerp(.17f, -.14f, strike), -.08f),
                MotionStyle.Hound => new(Mathf.Lerp(.12f, -.32f, strike), 0, 0),
                MotionStyle.Bell => new(Mathf.Lerp(.18f, -.26f, strike), -.13f + strike * .26f, .09f),
                MotionStyle.Beast or MotionStyle.Armored => new(Mathf.Lerp(.18f, -.28f, strike), -.13f, .1f),
                MotionStyle.Swarm => new(-.15f, .25f, .08f),
                _ => new(-.16f * strike, Mathf.Lerp(-.13f, .13f, strike), 0)
            };
            if (_weaponRig) lean = WeaponBodyPose(strike);
            lean *= weight;
            if (_motionStyle is MotionStyle.Hound or MotionStyle.Swarm)
                BodyRoot.Position += Vector3.Up * (.15f * pulse);
        }
        BodyRoot.Rotation += lean;

        foreach (var limb in _limbs)
        {
            bool left = limb.Motion == "left_arm", right = limb.Motion == "right_arm";
            bool leg = limb.Motion is "left_leg" or "right_leg";
            Vector3 pose = Vector3.Zero, shift = Vector3.Zero;
            float blend = weight;
            if (_cue == CombatCue.Dodge)
            {
                blend = pulse;
                pose = left ? new(1.1f, 0, -.55f) : right ? new(.55f, 0, .4f) :
                    leg ? new(limb.Motion == "left_leg" ? -.65f : .8f, 0, 0) : new(.15f, 0, -.13f);
            }
            else if (_cue == CombatCue.Hit)
            {
                blend = pulse;
                pose = left ? new(-.22f, 0, -.25f) : right ? new(-.3f, 0, .25f) : new(.09f, 0, .04f);
            }
            else if (left || right)
            {
                pose = _weaponRig ? WeaponArmPose(left, strike) : AttackArm(left, t, strike);
                if (_weaponRig) shift = WeaponArmShift(left, strike) * weight;
                else if (_motionStyle == MotionStyle.Veilwalker)
                {
                    float thrust = left ? Pulse(t, .04f, .48f) : Pulse(t, .27f, .84f);
                    shift.Z = -.30f * thrust;
                }
                else if (_motionStyle == MotionStyle.Warden && right) shift.Z = -.27f * strike * weight;
            }
            else if (leg) pose = new((limb.Motion == "left_leg" ? -.24f : .24f) * strike, 0, 0);
            else if (limb.Motion == "jaw") pose = new(.22f * strike, 0, 0);
            else pose = new(-.08f * strike, .06f * strike, -.05f * strike);
            limb.Node.Rotation = limb.Node.Rotation.Lerp(limb.Rest + pose, blend);
            limb.Node.Position += shift;
        }
    }

    private Vector3 AttackArm(bool left, float t, float strike)
    {
        float side = left ? -1 : 1;
        return _motionStyle switch
        {
            MotionStyle.Vanguard when _shieldAttack => left ? new(1.35f, .18f, -.22f) : new(-.4f, -.15f, .25f),
            MotionStyle.Vanguard => left ? new(.9f, .2f, -.3f) : new(Mathf.Lerp(-1.45f, .85f, strike), Mathf.Lerp(-.35f, .4f, strike), Mathf.Lerp(.9f, -.65f, strike)),
            MotionStyle.Veilwalker => new(-.9f - .45f * (left ? Pulse(t, .04f, .48f) : Pulse(t, .27f, .84f)), side * .2f, side * .45f),
            MotionStyle.Arcanist => left ? new(1.42f, -.25f, -.55f) : new(-.42f, .22f, .28f),
            MotionStyle.Gravecaller => new(left ? .88f : -.52f, side * -.3f, side * .85f),
            MotionStyle.Warden => left ? new(1.15f, -.3f, -.2f) : new(-1.05f - strike * .22f, .12f, .18f),
            MotionStyle.Archer => left ? new(.25f, -.6f, -.55f) : new(1.23f, -.12f, .18f),
            MotionStyle.Caster or MotionStyle.Anchor => new(left ? 1.4f : -.65f, side * .28f, side * .65f),
            MotionStyle.Plant => new(-.4f + strike * 1.2f, side * .3f, side * (.7f - strike * .45f)),
            MotionStyle.Bell => new(Mathf.Lerp(-1.1f, 1.3f, strike), side * -.12f, side * (.75f - strike * .55f)),
            MotionStyle.Beast or MotionStyle.Armored => new(Mathf.Lerp(-1.3f, 1.4f, strike), side * .1f, side * (.5f - strike * .23f)),
            _ => new(Mathf.Lerp(left ? -.65f : -1.05f, left ? .85f : 1.4f, strike), side * .2f, side * .4f)
        };
    }

    private void AnimateDeath(float delta)
    {
        if (DeathFinished) return;
        _cueTime = Math.Min(_cueTime + delta, _cueDuration);
        float t = _cueTime / _cueDuration;
        float fall = Smooth((t - .1f) / .69f);
        float settle = MathF.Sin(Smooth((t - .7f) / .3f) * Mathf.Pi) * .035f;
        bool low = _motionStyle is MotionStyle.Hound or MotionStyle.Swarm;
        Vector3 fallen = new(low ? .25f : 1.47f, _deathBodyRotation.Y, low ? 1.12f : .12f);
        BodyRoot.Rotation = _deathBodyRotation.Lerp(fallen, fall);
        BodyRoot.Position = _deathBodyPosition.Lerp(new Vector3(0, Math.Clamp(Height * .12f, .22f, .48f) + settle, 0), fall);
        for (int i = 0; i < _limbs.Count; i++)
        {
            var limb = _limbs[i];
            Vector3 collapse = limb.Motion switch
            {
                "left_arm" => new(.18f, -.22f, -.46f),
                "right_arm" => new(.38f, .12f, .5f),
                "left_leg" => new(-.26f, -.08f, -.12f),
                "right_leg" => new(.18f, .08f, .18f),
                "jaw" => new(.25f, 0, 0),
                _ => new(.12f, .06f, .08f)
            };
            limb.Node.Rotation = _deathFromRotations[i].Lerp(limb.Rest + collapse, fall);
            limb.Node.Position = _deathFromPositions[i].Lerp(limb.Origin, fall);
        }
    }

    private static float Smooth(float value)
    {
        float t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static float Pulse(float t, float start, float end)
    {
        float progress = Math.Clamp((t - start) / (end - start), 0, 1);
        return MathF.Sin(progress * Mathf.Pi);
    }
}
