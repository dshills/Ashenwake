using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Read-only regional audio director. Cosmetic clocks, synthesis and voices never enter a save or replay.</summary>
internal sealed partial class OpeningAudio : Node3D
{
    private sealed class Bank(AudioStreamPlayer[] players)
    {
        internal readonly AudioStreamPlayer[] Players = players;
        internal string Style = "";
        internal float Gain;
    }
    private sealed record Prepared(string Style, byte[][] Samples, string Error = "");
    private readonly List<Bank> _banks = [];
    private readonly List<AudioStreamPlayer3D> _effects = [];
    private readonly List<AudioStreamPlayer> _warnings = [];
    private readonly Dictionary<string, double> _lastCues = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedStyles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preparedStyles = new(StringComparer.Ordinal);
    private readonly double[] _warningUntil = new double[2];
    private readonly int[] _warningPriority = new int[2];
    private AudioListener3D _listener = null!;
    private Task<Prepared>? _preparing;
    private int _activeBank = -1, _effectIndex;
    private Vector3 _lastPosition;
    private long _lastTick = -1;
    private float _stepDistance, _combatHold, _duckHold;
    private double _time, _lastFootstep = -1, _lastLowHealth = -10;
    private bool _lowHealthArmed;
    private (int Id, bool Guarded)? _guardedBossState;
    private (int Id, bool Shielded)? _breachState;

    internal string DesiredStyle { get; private set; } = "";
    internal string PlayingStyle => _activeBank < 0 ? "" : _banks[_activeBank].Style;
    internal string Mode { get; private set; } = "exploration";
    internal int BankStarts { get; private set; }
    internal int FootstepCount { get; private set; }
    internal int CueCount { get; private set; }
    internal string LastCue { get; private set; } = "";
    internal float DuckGain { get; private set; } = 1;
    internal float CombatGain { get; private set; }
    internal float BossGain { get; private set; }
    internal bool Paused { get; private set; }
    internal const int VoiceCapacity = 10;
    internal const float MusicBaseDb = -12;
    internal int MusicVoiceCount => _banks.Sum(b => b.Players.Length);
    internal int ActiveMusicBanks => _banks.Count(b => b.Style.Length > 0);
    internal bool PreparationPending => _preparing is not null;
    internal IReadOnlyList<AudioStreamPlayer> MusicPlayers => _banks.SelectMany(b => b.Players).ToArray();
    internal IReadOnlyList<AudioStreamPlayer3D> EffectPlayers => _effects;
    internal IReadOnlyList<AudioStreamPlayer> WarningPlayers => _warnings;

    public override void _Ready()
    {
        ClientAudio.EnsureBuses();
        OpeningFoley.Prewarm();
        for (int bank = 0; bank < 2; bank++)
        {
            var players = new AudioStreamPlayer[3];
            for (int stem = 0; stem < players.Length; stem++)
            {
                players[stem] = new() { Name = $"Score{bank}_{stem}", Bus = ClientAudio.MusicBus, VolumeDb = -80 };
                AddChild(players[stem]);
            }
            _banks.Add(new(players));
        }
        for (int i = 0; i < 8; i++)
        {
            // Six combat voices plus two footsteps. A player-centered listener avoids camera-distance attenuation.
            var voice = new AudioStreamPlayer3D
            {
                Name = "Foley" + i,
                Bus = ClientAudio.EffectsBus,
                UnitSize = 8,
                MaxDistance = 36,
                MaxDb = -18,
                PanningStrength = .6f,
                VolumeDb = -18
            };
            AddChild(voice); _effects.Add(voice);
        }
        for (int i = 0; i < 2; i++)
        {
            var warning = new AudioStreamPlayer { Name = i == 0 ? "CreatureWarning" : "CriticalWarning", Bus = ClientAudio.EffectsBus };
            AddChild(warning); _warnings.Add(warning);
        }
        _listener = new AudioListener3D { Name = "PlayerAudioListener" }; AddChild(_listener); _listener.MakeCurrent();
    }

    public override void _ExitTree()
    {
        // Detach active playback while its owning world is leaving. Merely stopping
        // a voice can leave its native stream/playback held until an audio mix tick.
        foreach (var bank in _banks)
        {
            foreach (var player in bank.Players) { player.Stop(); player.Stream = null; }
            bank.Style = ""; bank.Gain = 0;
        }
        foreach (var voice in _effects) { voice.Stop(); voice.Stream = null; }
        foreach (var warning in _warnings) { warning.Stop(); warning.Stream = null; }
        _activeBank = -1;
        // Preparation uses only managed sample buffers and catches its own faults.
        // A departing world must not adopt that result or start new playback.
        _preparing = null;
    }

    internal void SetStyle(string style)
    {
        string desired = OpeningScore.StyleNames.Contains(style) ? style : "";
        if (DesiredStyle == desired) return;
        DesiredStyle = desired;
        _stepDistance = 0; _lastTick = -1; _combatHold = 0;
        _guardedBossState = null;
        _breachState = null;
        // An outgoing arena must not leave a warning or positional effect playing in the next one.
        StopEffects();
    }

    internal void Reset(CombatView view)
    {
        StopEffects(); _lastCues.Clear(); _stepDistance = 0; _combatHold = 0; _duckHold = 0;
        DuckGain = 1; CombatGain = BossGain = 0; Mode = "exploration";
        var player = view.Actors.FirstOrDefault(a => a.Id == 1);
        _lastTick = view.Tick; _lastPosition = WorldPosition(player);
        _lowHealthArmed = player is { Health: > 0 } && player.Health > player.MaxHealth * .25;
        _lastFootstep = _time; _lastLowHealth = _time - 10;
        ObserveGuardedBoss(view, baseline: true);
        ObserveBreach(view, baseline: true);
    }

    private void StopEffects()
    {
        foreach (var voice in _effects) voice.Stop();
        foreach (var warning in _warnings) warning.Stop();
        Array.Clear(_warningUntil);
        Array.Clear(_warningPriority);
    }

    internal void Observe(CombatView view, bool dodged = false)
    {
        var player = view.Actors.FirstOrDefault(a => a.Id == 1);
        Vector3 position = WorldPosition(player);
        long ticks = view.Tick - _lastTick;
        float distance = position.DistanceTo(_lastPosition);
        bool baseline = _lastTick < 0 || ticks < 0 || ticks > 5;
        _lastTick = view.Tick; _lastPosition = position;
        // BossCoreWindow is emitted when guarding STARTS. Announce the actual opening
        // only when the authoritative guard projection falls, independently of footsteps.
        ObserveGuardedBoss(view, baseline || Paused || ticks == 0 || player is null || player.Health <= 0);
        ObserveBreach(view, baseline || Paused || ticks == 0 || player is null || player.Health <= 0);
        if (Paused || DesiredStyle.Length == 0 || player is null || player.Health <= 0 || baseline || dodged || distance > 1.8f)
        { _stepDistance = 0; return; }
        if (ticks == 0) return;
        if (player.Health >= player.MaxHealth * .4) _lowHealthArmed = true;
        if (_lowHealthArmed && player.Health <= player.MaxHealth * .25 && _time - _lastLowHealth >= 8)
        {
            int count = CueCount;
            Play("low_health", position);
            if (CueCount != count) { _lowHealthArmed = false; _lastLowHealth = _time; }
        }
        if (distance <= .001f) { _stepDistance = 0; return; }
        _stepDistance += distance;
        if (_stepDistance >= 1.05f && _time - _lastFootstep >= .2)
        {
            string surface = DesiredStyle is "verdant_ruins" or "verdant_hunt" or "verdant_shrine" or "verdant_heart"
                ? "moss" : DesiredStyle is "cinder_extraction" or "cinder_foundry" or "cinder_furnace" ? "metal"
                : DesiredStyle is "road" or "cinder_fields" or "cinder_storm" ? "dirt" : "stone";
            Play(OpeningFoley.FootstepCue(surface, FootstepCount), position);
            FootstepCount++; _lastFootstep = _time; _stepDistance %= 1.05f;
        }
    }

    private void ObserveGuardedBoss(CombatView view, bool baseline)
    {
        string definition = DesiredStyle switch
        {
            "cinder_furnace" => "boss.furnace_spindle",
            "spine_warden" => "boss.covenant_warden",
            _ => ""
        };
        var actor = definition.Length > 0 ? view.Actors.FirstOrDefault(a => a.DefinitionId == definition && a.Health > 0) : null;
        if (!baseline && actor is not null && _guardedBossState is { } previous &&
            previous.Id == actor.Id && previous.Guarded && !actor.Guarded)
            Play(definition == "boss.covenant_warden" ? "warden_exposed" : "furnace_exposed", WorldPosition(actor));
        _guardedBossState = actor is null ? null : (actor.Id, actor.Guarded);
    }

    // Mirrorborn copies share the definition. Keep the original identity even after
    // death so a surviving copy cannot inherit its score, exposure or victory cue.
    internal static CombatActorView? PrimaryBreach(CombatView view)
        => view.Actors.Where(a => a.DefinitionId == "boss.breach_heart").MinBy(a => a.Id);

    private void ObserveBreach(CombatView view, bool baseline)
    {
        var actor = DesiredStyle == "hollow_breach" ? PrimaryBreach(view) : null;
        if (actor is not { Health: > 0 }) { _breachState = null; return; }
        if (!baseline && _breachState is { } previous && previous.Id == actor.Id &&
            previous.Shielded && !actor.Shielded)
            Play("breach_exposed", WorldPosition(actor));
        _breachState = (actor.Id, actor.Shielded);
    }

    /// <summary>Returns true when opening audio owns the cue, including a coalesced or paused cue.</summary>
    internal bool Play(string cue, Vector3 position)
    {
        if (DesiredStyle.Length == 0 || !OpeningFoley.CueNames.Contains(cue)) return false;
        if (Paused) return true;
        var metadata = OpeningFoley.Describe(cue);
        double cooldown = metadata.IsWarning ? .35 : .065;
        if (_lastCues.TryGetValue(cue, out double last) && _time - last < cooldown) return true;
        _lastCues[cue] = _time;
        if (metadata.IsWarning)
        {
            int slot = metadata.Category == "enemy_tell" ? 0 : 1;
            int priority = cue is "rootheart_fall" or "furnace_shutdown" or "warden_defeat" or "breach_containment" ? 3 :
                cue is "saint_tell" or "rootheart_tell" or "antler_tell" or "furnace_tell" or "warden_oath" or "warden_fault" or
                    "breach_echo" or "breach_return" or "breach_sweep" || metadata.Category == "phase_warning" ? 2 : 1;
            // Boss tells outrank lesser creatures. Phases outrank heartbeat; final collapse
            // cannot be cut off by a same-tick phase or status cue. Equal phases may replace.
            if (_warningUntil[slot] > _time && (priority < _warningPriority[slot] ||
                slot == 0 && priority == _warningPriority[slot] || cue == "low_health")) return true;
            var voice = _warnings[slot]; voice.Stream = OpeningFoley.GetStream(cue); voice.VolumeDb = metadata.SuggestedGainDb;
            voice.Play(); voice.StreamPaused = Paused;
            _warningUntil[slot] = _time + metadata.DurationSeconds;
            _warningPriority[slot] = priority;
            _duckHold = Math.Max(_duckHold, (float)metadata.DurationSeconds + .12f);
        }
        else
        {
            bool step = metadata.Category == "footstep";
            int slot = step ? 6 + (FootstepCount & 1) : _effectIndex;
            if (!step) _effectIndex = (_effectIndex + 1) % 6;
            var voice = _effects[slot];
            voice.Position = position + Vector3.Up * .8f;
            voice.Stream = OpeningFoley.GetStream(cue); voice.VolumeDb = metadata.SuggestedGainDb;
            voice.MaxDb = metadata.SuggestedGainDb; // Proximity may not amplify a cue beyond its authored mix level.
            voice.Play(); voice.StreamPaused = Paused;
        }
        CueCount++; LastCue = cue;
        return true;
    }

    internal void Advance(double delta, CombatView view, bool paused, Basis listenerBasis)
    {
        SetPaused(paused);
        var hero = view.Actors.FirstOrDefault(a => a.Id == 1);
        _listener.Transform = new(listenerBasis, WorldPosition(hero) + Vector3.Up * 1.5f);
        PrepareMusic(); // Pure synthesis may finish while paused; playback starts only on resume.
        if (paused) return;
        float dt = (float)Math.Clamp(delta, 0, .1); _time += dt;
        bool alive = hero is { Health: > 0 };
        bool boss = false, combat = false;
        string bossDefinition = "";
        bool bossGuarded = false;
        var breach = DesiredStyle == "hollow_breach" ? PrimaryBreach(view) : null;
        int lostRoots = 0;
        if (alive && DesiredStyle.Length > 0)
            for (int i = 0; i < view.Actors.Count; i++)
            {
                var actor = view.Actors[i];
                if (actor.DefinitionId == "enemy.feeding_root" && actor.Health <= 0) lostRoots++;
                if (actor.Health <= 0 || actor.Faction != CombatFaction.Enemy) continue;
                if (actor.DefinitionId is "boss.bell_saint" or "enemy.bell_saint" or "enemy.bell_beast" or "boss.rootheart" or "boss.antler" or "boss.furnace_spindle" or "boss.covenant_warden")
                { boss = true; bossDefinition = actor.DefinitionId; bossGuarded = actor.Guarded; }
                if (!combat && DesiredStyle != "greyhaven" && actor.Visible)
                    combat = Ashenwake.Core.Simulation.Position.DistanceSquared(actor.Position, hero!.Position) < 144_000_000;
            }
        if (alive && breach is { Health: > 0 }) { boss = true; bossDefinition = breach.DefinitionId; }
        _combatHold = combat ? 4 : Math.Max(0, _combatHold - dt);
        Mode = boss ? "boss" : alive && _combatHold > 0 ? "combat" : "exploration";
        CombatGain = Mathf.MoveToward(CombatGain, Mode == "exploration" ? 0 : 1, dt / (Mode == "exploration" ? 3 : .8f));
        float bossTarget = !boss ? 0 : bossDefinition switch
        {
            "boss.rootheart" => view.BossPhase >= 2 ? 1 : .4f + Math.Min(3, lostRoots) * .1f,
            "boss.antler" => .65f,
            "boss.breach_heart" => view.BossPhase >= 3 ? 1 : view.BossPhase == 2 ? .8f : breach!.Shielded ? .4f : .6f,
            "boss.furnace_spindle" or "boss.covenant_warden" => view.BossPhase >= 2 ? bossGuarded ? .75f : 1 : bossGuarded ? .45f : .7f,
            _ => view.BossPhase >= 3 ? 1 : view.BossPhase == 2 ? .75f : .4f
        };
        BossGain = Mathf.MoveToward(BossGain, bossTarget, dt / 1.2f);
        _duckHold = Math.Max(0, _duckHold - dt);
        DuckGain = Mathf.MoveToward(DuckGain, _duckHold > 0 ? .26f : 1, dt / (_duckHold > 0 ? .06f : 1.2f));
        StartPreparedMusic();
        for (int i = 0; i < _banks.Count; i++)
        {
            var bank = _banks[i];
            bank.Gain = Mathf.MoveToward(bank.Gain, i == _activeBank && DesiredStyle.Length > 0 ? 1 : 0, dt / 1.6f);
            for (int stem = 0; stem < 3; stem++)
            {
                float layer = stem == 0 ? 1 : stem == 1 ? CombatGain * .78f : BossGain * .85f;
                float gain = bank.Gain * layer * DuckGain * (alive ? 1 : .35f);
                bank.Players[stem].VolumeDb = gain < .0001f ? -80 : MusicBaseDb + Mathf.LinearToDb(gain);
            }
            if (bank.Gain == 0 && bank.Style.Length > 0 && (i != _activeBank || DesiredStyle.Length == 0))
            {
                foreach (var player in bank.Players) { player.Stop(); player.Stream = null; }
                bank.Style = "";
                if (i == _activeBank) _activeBank = -1;
            }
        }
    }

    internal void SetPaused(bool paused)
    {
        Paused = paused;
        foreach (var bank in _banks) foreach (var player in bank.Players) player.StreamPaused = paused;
        foreach (var voice in _effects)
        {
            voice.StreamPaused = paused;
            // 3D Play creates a playback handle before the physics update registers it with
            // AudioServer. A handle alone cannot tell us whether the pause took effect.
            // Cancel any unpaused request so it cannot start behind an open menu.
            if (paused && !voice.StreamPaused) voice.Stop();
        }
        foreach (var warning in _warnings) warning.StreamPaused = paused;
    }

    internal void Emphasize(float seconds)
    {
        if (!Paused && DesiredStyle.Length > 0) _duckHold = Math.Max(_duckHold, Math.Clamp(seconds, 0, 3));
    }

    private void PrepareMusic()
    {
        if (_preparing is { IsCompleted: true })
        {
            // The worker catches exceptions into its result, including when this node is freed before completion.
            var prepared = _preparing.GetAwaiter().GetResult(); _preparing = null;
            if (prepared.Error.Length > 0)
            { _failedStyles.Add(prepared.Style); GD.PushWarning("Opening score unavailable: " + prepared.Error); }
            else
            {
                for (int i = 0; i < 3; i++) OpeningScore.CachePrepared(prepared.Style, OpeningScore.StemNames[i], prepared.Samples[i]);
                _preparedStyles.Add(prepared.Style);
            }
        }
        if (DesiredStyle.Length > 0 && OpeningScore.StemNames.All(stem => OpeningScore.IsPrepared(DesiredStyle, stem)))
            _preparedStyles.Add(DesiredStyle);
        if (_preparing is not null || DesiredStyle.Length == 0 || _preparedStyles.Contains(DesiredStyle) || _failedStyles.Contains(DesiredStyle)) return;
        string style = DesiredStyle;
        _preparing = Task.Run(() =>
        {
            try { return new Prepared(style, OpeningScore.StemNames.Select(stem => OpeningScore.CreateSamples(style, stem)).ToArray()); }
            catch (Exception ex) { return new Prepared(style, [], ex.Message); }
        });
    }

    private void StartPreparedMusic()
    {
        if (DesiredStyle.Length == 0 || PlayingStyle == DesiredStyle || !_preparedStyles.Contains(DesiredStyle)) return;
        int next = _activeBank == 0 ? 1 : 0;
        var bank = _banks[next];
        if (bank.Style.Length > 0) return; // Two-bank bound; finish the current transition before another one.
        double phase = _activeBank < 0 ? 0 : _banks[_activeBank].Players[0].GetPlaybackPosition() % OpeningScore.DurationSeconds;
        for (int stem = 0; stem < 3; stem++)
        {
            bank.Players[stem].Stream = OpeningScore.GetStream(DesiredStyle, OpeningScore.StemNames[stem]);
            bank.Players[stem].VolumeDb = -80;
        }
        // Submit the aligned stems between audio mixing callbacks, including a loop-boundary transition.
        AudioServer.Lock();
        try { foreach (var player in bank.Players) player.Play((float)phase); }
        finally { AudioServer.Unlock(); }
        bank.Style = DesiredStyle; bank.Gain = 0; _activeBank = next; BankStarts++;
    }

    private static Vector3 WorldPosition(CombatActorView? actor) => actor is null ? Vector3.Zero : new(actor.Position.X * .001f, 0, actor.Position.Z * .001f);
}
