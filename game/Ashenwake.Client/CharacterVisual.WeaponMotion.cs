using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private enum WeaponMotion { Unarmed, Sword, Dagger, Axe, Hammer, Spear, Bow, Staff }
    private WeaponMotion _weaponMotion;
    private bool _weaponRig, _hasShield, _dualBlades, _castAttack;
    private Node3D? _weaponArm, _shieldArm, _weaponGrip;
    private bool _weaponGripResolved;
    private Vector3 _weaponTip;

    public string ActiveWeaponMotion => _weaponMotion switch
    {
        WeaponMotion.Sword => "sword",
        WeaponMotion.Dagger => "dagger",
        WeaponMotion.Axe => "axe",
        WeaponMotion.Hammer => "hammer",
        WeaponMotion.Spear => "spear",
        WeaponMotion.Bow => "bow",
        WeaponMotion.Staff => "staff",
        _ => "unarmed"
    };
    public string AttackEffectCue => _shieldAttack ? "shield" : _castAttack ? "spell" : _weaponMotion switch
    {
        WeaponMotion.Axe or WeaponMotion.Hammer => "heavy_slash",
        WeaponMotion.Spear => "thrust",
        WeaponMotion.Bow => "arrow",
        WeaponMotion.Staff => "spell",
        WeaponMotion.Sword or WeaponMotion.Dagger => "slash",
        _ => "strike"
    };

    private void ConfigureWeaponMotion(string id, string discipline, bool allied)
    {
        bool hero = discipline.Length > 0 || allied;
        _weaponRig = hero || _motionStyle is MotionStyle.Melee or MotionStyle.Armored or MotionStyle.Archer or MotionStyle.Caster;
        // Large authored bosses keep their bespoke motion rather than inheriting a guard's polearm.
        if (!hero && (id.StartsWith("boss.", StringComparison.Ordinal) || id.StartsWith("champion.", StringComparison.Ordinal) || id == "training.effigy")) _weaponRig = false;
        string kind = allied && discipline.Length == 0 ? "gravecaller" : discipline;
        string weapon = _appearance is null ? "starter_mainhand" : ItemKind(_appearance.MainHand);
        if (weapon == "starter_mainhand") weapon = kind switch
        { "veilwalker" => "dagger", "arcanist" or "gravecaller" => "staff", "warden" => "pilgrim_pike", _ => "sword" };
        _weaponMotion = hero ? weapon switch
        {
            "" => WeaponMotion.Unarmed,
            "ash_axe" or "ashcleaver" => WeaponMotion.Axe,
            "oath_hammer" => WeaponMotion.Hammer,
            "pilgrim_pike" => WeaponMotion.Spear,
            "staff" or "bone_staff" or "greatstaff" => WeaponMotion.Staff,
            "dagger" => WeaponMotion.Dagger,
            _ => WeaponMotion.Sword // Matches EquippedWeapon's visible fallback mesh, including Widowthorn.
        } : _motionStyle switch
        {
            MotionStyle.Archer => WeaponMotion.Bow,
            MotionStyle.Caster => WeaponMotion.Staff,
            MotionStyle.Armored => WeaponMotion.Spear,
            _ => WeaponMotion.Unarmed
        };
        _hasShield = hero ? _appearance is null ? kind == "vanguard" :
            _appearance.OffHand.DefinitionId.Length > 0 && (kind == "vanguard" || ItemKind(_appearance.OffHand) == "griefs_reprieve") : _motionStyle == MotionStyle.Armored;
        _dualBlades = hero && kind == "veilwalker" && (_appearance is null ||
            _appearance.OffHand.DefinitionId.Length > 0 && ItemKind(_appearance.OffHand) != "griefs_reprieve");
        _weaponArm = _limbs.FirstOrDefault(l => l.Motion == "right_arm")?.Node;
        _shieldArm = _limbs.FirstOrDefault(l => l.Motion == "left_arm")?.Node;
        _weaponTip = !hero ? _weaponMotion switch
        {
            WeaponMotion.Bow => new(.22f, -.35f, -1.12f),
            WeaponMotion.Staff => new(.17f, .69f, -.3f),
            WeaponMotion.Spear => new(0, 1.22f, -.12f),
            _ => new(.24f, -1.15f, -.65f)
        } : _weaponMotion switch
        {
            WeaponMotion.Axe => new(.56f, .32f, -.07f),
            WeaponMotion.Hammer => new(.1f, .29f, -.07f),
            WeaponMotion.Spear => new(.075f, 1.28f, -.075f),
            WeaponMotion.Staff => new(.075f, .85f, -.075f),
            WeaponMotion.Dagger => new(.11f, .08f, -.055f),
            WeaponMotion.Unarmed => new(.08f, -.68f, -.08f),
            _ => weapon == "widowthorn" ? new(.1f, .86f, -.07f) : new(.11f, .5f, -.055f)
        };
        if (hero && _appearance is null)
        {
            if (kind == "gravecaller") _weaponTip = new(.17f, .7f, -.075f);
            else if (kind == "warden") _weaponTip = new(.075f, 1.215f, -.075f);
        }
    }

    /// <summary>Animated contact point in this actor's local space; no scene-tree/global-transform dependency.</summary>
    public bool TryGetWeaponEffectAnchor(out Vector3 localPoint)
    {
        bool handCast = _castAttack && _weaponMotion != WeaponMotion.Staff;
        Node3D? joint = _shieldAttack || handCast ? _shieldArm : _weaponArm;
        localPoint = _shieldAttack ? new(-.08f, -.4f, -.39f) : handCast ? new(-.06f, -.62f, -.16f) : _weaponTip;
        if (!_weaponRig || joint is null) { localPoint = default; return false; }
        if (!_shieldAttack && !handCast && _weaponGrip is not null) localPoint = _weaponGrip.Transform * localPoint;
        while (joint is not null && joint != this)
        {
            localPoint = joint.Transform * localPoint;
            joint = joint.GetParent() as Node3D;
        }
        return joint == this;
    }

    private void SelectWeaponAttack(string skillId)
    {
        _shieldAttack = _hasShield && skillId is "skill.iron_guard" or "skill.shield_breaker" or "skill.charge";
        _castAttack = skillId is "skill.fire_lance" or "skill.frost_nova" or "skill.storm_arc" or "skill.vent" or
            "skill.ember_stride" or "skill.starfall" or "skill.grave_bolt" or "skill.bone_lance" or "skill.raise_ancestor" or
            "skill.soul_siphon" or "skill.grave_command" or "skill.procession" or "skill.entangle" or "skill.feral_companion" or
            "skill.barkskin" or "skill.primal_awakening" or "skill.terror" or "skill.shroud" or "skill.echo_storm";
    }

    private float WeaponAttackDuration => _shieldAttack ? .42f : _castAttack ? .54f : _weaponMotion switch
    { WeaponMotion.Axe or WeaponMotion.Hammer => .6f, WeaponMotion.Dagger => .3f, WeaponMotion.Spear => .43f, WeaponMotion.Bow => .38f, WeaponMotion.Staff => .54f, _ => .39f };

    private Vector3 WeaponBodyPose(float strike)
    {
        if (_shieldAttack) return new(-.2f, -.12f, -.07f);
        if (_castAttack || _weaponMotion == WeaponMotion.Staff)
            return _motionStyle == MotionStyle.Gravecaller ? new(-.13f, .16f, -.035f) : new(-.075f, -.14f, .065f);
        return _weaponMotion switch
        {
            WeaponMotion.Axe => new(Mathf.Lerp(-.17f, -.3f, strike), Mathf.Lerp(-.45f, .55f, strike), -.12f),
            WeaponMotion.Hammer => new(Mathf.Lerp(-.1f, -.34f, strike), .08f, -.045f),
            WeaponMotion.Sword => new(-.1f, Mathf.Lerp(-.34f, .39f, strike), -.07f),
            WeaponMotion.Dagger => new(-.23f, Mathf.Lerp(-.19f, .24f, strike), -.1f),
            WeaponMotion.Spear => new(-.21f, Mathf.Lerp(.2f, -.14f, strike), .015f),
            WeaponMotion.Bow => new(-.035f, -.24f, .025f),
            _ => new(-.2f, Mathf.Lerp(-.25f, .25f, strike), .04f)
        };
    }

    private Vector3 WeaponArmPose(bool left, float strike)
    {
        if (_shieldAttack) return left ? new(1.25f, -.16f, -.24f) : new(-.3f, -.12f, .28f);
        if (_castAttack || _weaponMotion == WeaponMotion.Staff)
            return left ? new(1.25f, -.22f, -.5f) : new(Mathf.Lerp(-.28f, -.64f, strike), .22f, .3f);
        if (left && _hasShield) return new(.55f, .15f, -.25f);
        if (!left && _weaponGrip is not null && _weaponMotion is WeaponMotion.Axe or WeaponMotion.Hammer or WeaponMotion.Sword or WeaponMotion.Dagger)
        {
            // Equipped meshes point up from the hand. Articulate their wrist about the grip,
            // so the arm can reach forward while the blade continues through the target.
            float follow = Math.Clamp((strike - .52f) / .48f, 0, 1);
            float pitch = _weaponMotion switch
            {
                WeaponMotion.Axe => Mathf.Lerp(-2.48f, -2.9f, follow),
                WeaponMotion.Hammer => Mathf.Lerp(-2.5f, -3.12f, follow),
                WeaponMotion.Dagger => -2.36f,
                _ => Mathf.Lerp(-2.36f, -2.6f, follow)
            };
            float weight = 1 - Smooth((_cueTime / _cueDuration - .48f) / .52f);
            Vector3 grip = new(.1f, -.62f, -.07f);
            Basis wrist = Basis.FromEuler(new(pitch * weight, 0, 0));
            _weaponGrip.Transform = new(wrist, grip - wrist * grip);
            return _weaponMotion switch
            {
                WeaponMotion.Axe => new(Mathf.Lerp(1.04f, .62f, follow), Mathf.Lerp(-.26f, .5f, follow), Mathf.Lerp(.14f, -.4f, follow)),
                WeaponMotion.Hammer => new(Mathf.Lerp(1.13f, .72f, follow), .06f, .1f),
                WeaponMotion.Dagger => new(1.08f, .18f, .14f),
                _ => new(.97f, Mathf.Lerp(-.4f, .42f, follow), Mathf.Lerp(.28f, -.42f, follow))
            };
        }
        return _weaponMotion switch
        {
            WeaponMotion.Axe => left ? new(.32f, -.12f, -.5f) : new(Mathf.Lerp(-.55f, -2.05f, strike), Mathf.Lerp(-.32f, .42f, strike), Mathf.Lerp(.82f, -.63f, strike)),
            WeaponMotion.Hammer => left ? new(.42f, .12f, -.38f) : new(Mathf.Lerp(-.4f, -2.2f, strike), .09f, .2f),
            WeaponMotion.Sword => left ? new(.45f, -.2f, -.4f) : new(-.65f - strike * .85f, -.55f + strike * 1.05f, .75f - strike * 1.3f),
            WeaponMotion.Dagger when !left || _dualBlades => new(-.85f - .38f * strike, left ? -.26f : .26f, left ? -.4f : .4f),
            WeaponMotion.Spear => left ? new(.5f, -.32f, -.25f) : new(-1.47f, .08f, .1f),
            // Archer holds the rendered bow in the right hand, drawing with the left.
            WeaponMotion.Bow => left ? new(.72f, -.7f, -.42f) : new(.16f, -.18f, .12f),
            _ => left ? new(.42f, -.2f, -.32f) : new(1.15f, .18f, .12f)
        };
    }

    private Vector3 WeaponArmShift(bool left, float strike)
    {
        if (_shieldAttack) return left ? new(0, 0, -.17f) : Vector3.Zero;
        if (_castAttack) return left ? new(0, .035f, -.1f) : Vector3.Zero;
        return _weaponMotion switch
        {
            WeaponMotion.Spear when !left => new(0, -.02f, -.29f * strike),
            WeaponMotion.Bow when left => new(.08f, .08f, .18f * strike),
            WeaponMotion.Dagger when !left || _dualBlades => new(0, 0, -.16f * strike),
            WeaponMotion.Unarmed when !left => new(0, 0, -.15f * strike),
            _ => Vector3.Zero
        };
    }

    private void AnimateWeaponAnticipation()
    {
        if (!_weaponGripResolved)
        {
            // Equipment is built after ConfigureAnimation. Resolve its existing module once.
            _weaponGrip = _weaponArm?.GetNodeOrNull<Node3D>("EquipmentMainHand");
            _weaponGripResolved = true;
        }
        if (_weaponGrip is not null) _weaponGrip.Transform = Transform3D.Identity;
        if (!_weaponRig || _windup < .001f) return;
        BodyRoot.Rotation += _weaponMotion switch
        {
            WeaponMotion.Bow => new(0, -.22f * _windup, 0),
            WeaponMotion.Staff => new(.1f * _windup, -.15f * _windup, 0),
            WeaponMotion.Spear => new(.12f * _windup, .24f * _windup, 0),
            _ => new(.08f * _windup, -.28f * _windup, 0)
        };
        foreach (var limb in _limbs)
        {
            bool left = limb.Motion == "left_arm";
            if (!left && limb.Motion != "right_arm") continue;
            Vector3 pose = _weaponMotion switch
            {
                WeaponMotion.Bow => left ? new(.8f, -.65f, -.45f) : new(.18f, -.2f, .12f),
                WeaponMotion.Staff => left ? new(.85f, -.3f, -.7f) : new(-.4f, .1f, .38f),
                WeaponMotion.Spear => left ? new(.75f, .12f, -.22f) : new(-1.1f, .24f, .3f),
                WeaponMotion.Unarmed => left ? new(-.9f, -.25f, -.65f) : new(-1.15f, .3f, .8f),
                _ => left ? new(.6f, -.12f, -.3f) : new(-1.35f, -.4f, .72f)
            };
            limb.Node.Rotation = limb.Node.Rotation.Lerp(limb.Rest + pose, _windup);
            if (_weaponMotion == WeaponMotion.Bow && left) limb.Node.Position += new Vector3(.11f, .1f, .24f) * _windup;
            else if (_weaponMotion == WeaponMotion.Spear && !left) limb.Node.Position += Vector3.Back * (.13f * _windup);
        }
    }
}
