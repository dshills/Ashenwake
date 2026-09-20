using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Server;

/// <summary>One sequential authority for one allocated world. Failed durability freezes simulation.</summary>
public sealed class MatchHost : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly ControlPlane _control;
    private readonly CoopCombatSession _session;
    private CoopRecorder _recorder;
    private readonly OnlineAllocation _allocation;
    private readonly Dictionary<int, Peer> _peers = [];
    private readonly HashSet<Peer> _servingPeers = [];
    private readonly HashSet<int> _deferredDisconnects = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, long> _characterRevisions;
    private readonly Task _loop;
    private CheckpointWrite? _pending;
    private long _revision;
    private long _lastCheckpointTick;
    private int _committedRewards;
    private int _started;
    private int _consecutiveFailures;
    private long _retryAt;
    private long _idleSince = Stopwatch.GetTimestamp();
    private long _checkpointAt = Stopwatch.GetTimestamp();
    private bool _dirty;
    private bool _completed;
    private bool _evictionPrepared;
    private int _peerCount;
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private readonly Queue<double> _tickSamples = new();
    public double TickP99Ms { get; private set; }
    public long TickSamples { get; private set; }
    public long TickOverruns { get; private set; }
    public long PersistenceFailures { get; private set; }
    public long Reconnects { get; private set; }
    public long RejectedInputs { get; private set; }
    private long _snapshotsSent;
    public long SnapshotsSent => Interlocked.Read(ref _snapshotsSent);
    public bool Completed => Volatile.Read(ref _completed);
    public int ConnectedPeers => Volatile.Read(ref _peerCount);
    public string Id => _allocation.AllocationId;
    public bool Draining { get; private set; }
    public bool PersistencePaused => _pending is not null;
    private string EnvironmentContent { get; init; } = "";
    private MatchHost(ControlPlane control, OnlineAllocation allocation, CoopCombatSession session, string content)
    {
        EnvironmentContent = content; _control = control; _allocation = allocation; _session = session; _recorder = new(session); _revision = allocation.Revision;
        _characterRevisions = allocation.Characters.ToDictionary(c => c.Id, c => c.Revision, StringComparer.Ordinal);
        foreach (int id in new[] { 1, 2 }) _recorder.SetConnected(_session, id, false);
        _dirty = true; _completed = session.View.Completed;
        // A progressed allocation already passed its initial two-player admission.
        // Restarts and idle eviction must preserve its ability to continue with one peer.
        _started = session.Tick > 0 ? 1 : 0;
        _committedRewards = session.View.Rewards.Length;
        _lastCheckpointTick = session.Tick;
        _loop = Run();
    }
    public static async Task<MatchHost> Create(ControlPlane control, string id, string content, CancellationToken token)
    {
        var allocation = await control.Get(id, token);
        var empty = CoopCombatSession.Create(content, matchId: id);
        if (allocation.ProtocolVersion != NetworkProtocol.Version || allocation.ContentHash != empty.ContentHash ||
            allocation.Players.Length != 2 || !allocation.Players.Select(p => p.Slot).Order().SequenceEqual(new[] { 1, 2 }) ||
            allocation.Characters.Length != 2 || allocation.Players.Any(p => !allocation.Characters.Any(c => c.Id == p.CharacterId && c.AccountId == p.AccountId && c.Discipline == "Vanguard")))
            throw new InvalidDataException("Allocation compatibility failed.");
        var session = allocation.Snapshot.ValueKind == JsonValueKind.Object && allocation.Snapshot.EnumerateObject().Any()
            ? CoopCombatSession.Restore(content, JsonData.Read<CoopSnapshot>(allocation.Snapshot.GetRawText())) : empty;
        if (session.View.MatchId != id) throw new InvalidDataException("Allocation snapshot mismatch.");
        foreach (var character in allocation.Characters)
        {
            var retained = ValidateCharacterState(character.State)?.Rewards ?? [];
            int slot = allocation.Players.Single(p => p.CharacterId == character.Id).Slot;
            int alreadyRetained = session.View.Rewards.Count(r => r.PlayerId == slot && retained.Any(old => old.Id == r.Id));
            if (retained.Length + 5 - alreadyRetained > 256) throw new InvalidDataException("Prototype character receipt capacity cannot accommodate this match.");
        }
        return new(control, allocation, session, content);
    }
    private static OnlineCharacterState? ValidateCharacterState(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any()) return null;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("rulesVersion", out var rules) || rules.ValueKind != JsonValueKind.String || rules.GetString() != "coop.1")
            throw new InvalidDataException("Unsupported online character rules. Preserve this character for its matching server.");
        var state = JsonData.Read<OnlineCharacterState>(value.GetRawText());
        if (string.IsNullOrWhiteSpace(state.MatchId) || state.MatchId.Length > 64 || state.MatchId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) || state.PlayerId is < 1 or > 2 || state.Tick is < 0 or > 1000000000 || state.Rewards is null || state.Rewards.Length > 256 ||
            state.Rewards.Any(r => r is null || string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 128 || r.PlayerId is < 1 or > 2 || r.Experience is < 0 or > 1000000 || r.Ash is < 0 or > 1000000 || r.Item is null) ||
            state.Rewards.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != state.Rewards.Length || state.Experience != state.Rewards.Sum(r => r.Experience) || state.Ash != state.Rewards.Sum(r => r.Ash))
            throw new InvalidDataException("Invalid retained online character state.");
        return state;
    }
    public async Task Serve(WebSocket socket, OnlineJoin join, CancellationToken requestToken)
    {
        CancellationTokenSource linked;
        Peer peer;
        await _gate.WaitAsync(requestToken);
        try
        {
            if (Draining || _pending is not null || join.AllocationId != Id || !_allocation.Players.Any(p => p.Slot == join.Slot && p.CharacterId == join.CharacterId && p.AccountId == join.AccountId))
                throw new InvalidDataException("Join ownership mismatch.");
            linked = CancellationTokenSource.CreateLinkedTokenSource(requestToken, _stop.Token);
            peer = new Peer(socket, linked);
            if (_peers.Remove(join.Slot, out var old)) { Reconnects++; old.Stop.Cancel(); old.Socket.Abort(); }
            _peers[join.Slot] = peer;
            _servingPeers.Add(peer); Volatile.Write(ref _peerCount, _peers.Count);
            _recorder.SetConnected(_session, join.Slot, true);
            _dirty = true;
            if (_peers.Count == 2) _started = 1;
            peer.Outbox.Writer.TryWrite(Frame("joined", join.Slot));
            foreach (var other in _peers.Where(p => p.Key != join.Slot)) other.Value.Outbox.Writer.TryWrite(Frame("snapshot", other.Key));
        }
        finally { _gate.Release(); }
        using var ownedStop = linked;
        var send = Send(peer);
        try
        {
            while (!linked.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var input = await NetworkProtocol.Receive<CoopInput>(socket, NetworkProtocol.MaxInputBytes, timeout.Token);
                if (input is null) break;
                await _gate.WaitAsync(linked.Token);
                try
                {
                    if (!_peers.TryGetValue(join.Slot, out var current) || !ReferenceEquals(peer, current)) break;
                    if (!peer.Allow()) throw new InvalidDataException("Input rate exceeded.");
                    if (_pending is not null || Draining) continue;
                    CoopInputResult result;
                    if (Completed)
                    {
                        var player = _session.View.Players.Single(p => p.Id == join.Slot);
                        result = new(false, "match_completed", input.Sequence, _session.Tick, player.AcceptedSequence, player.ProcessedSequence);
                    }
                    else { result = _recorder.Submit(_session, join.Slot, input); _dirty = true; }
                    if (!result.Accepted) RejectedInputs++;
                    peer.Outbox.Writer.TryWrite(Frame("ack", join.Slot, result));
                }
                finally { _gate.Release(); }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or InvalidDataException or JsonException) { }
        finally
        {
            linked.Cancel();
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                if (_peers.TryGetValue(join.Slot, out var current) && ReferenceEquals(peer, current))
                {
                    _peers.Remove(join.Slot); Volatile.Write(ref _peerCount, _peers.Count);
                    if (_peers.Count == 0) _idleSince = Stopwatch.GetTimestamp();
                    if (_pending is not null) _deferredDisconnects.Add(join.Slot);
                    else { _recorder.SetConnected(_session, join.Slot, false); _dirty = true; if (!Draining) Broadcast(); }
                }
            }
            finally { _gate.Release(); }
            try { await send; }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
            finally
            {
                socket.Abort();
                await _gate.WaitAsync(CancellationToken.None);
                try { _servingPeers.Remove(peer); peer.Completion.TrySetResult(); }
                finally { _gate.Release(); }
            }
        }
    }
    private async Task Send(Peer peer)
    {
        try
        {
            await foreach (var frame in peer.Outbox.Reader.ReadAllAsync(peer.Stop.Token))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(peer.Stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await NetworkProtocol.Send(peer.Socket, frame, timeout.Token); Interlocked.Increment(ref _snapshotsSent);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            peer.Stop.Cancel(); peer.Socket.Abort(); throw;
        }
    }
    private NetworkFrame Frame(string kind, int id, CoopInputResult? result = null) => new(NetworkProtocol.Version, kind, id, _revision, _session.StateHash, _session.View, result);
    private void Broadcast() { foreach (var entry in _peers) entry.Value.Outbox.Writer.TryWrite(Frame("snapshot", entry.Key)); }
    private async Task Run()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / 30));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                long start = Stopwatch.GetTimestamp();
                bool measured = false;
                await _gate.WaitAsync(_stop.Token);
                try
                {
                    if (_pending is not null)
                    {
                        if (Stopwatch.GetTimestamp() < _retryAt) continue;
                        measured = true;
                        if (!await Persist()) continue;
                    }
                    if (Draining) continue;
                    if (_started == 0 || _peers.Count == 0 || Completed)
                    {
                        // Admission/connection journals also need a bound while simulation is idle.
                        if (_dirty && Stopwatch.GetElapsedTime(_checkpointAt).TotalSeconds >= 1) { PrepareCheckpoint(); if (await Persist()) Broadcast(); }
                        continue;
                    }
                    string before = _session.View.ContextKey;
                    measured = true;
                    _recorder.Step(_session);
                    _dirty = true;
                    var view = _session.View;
                    Volatile.Write(ref _completed, view.Completed);
                    if (view.Rewards.Length != _committedRewards || view.ContextKey != before || view.Completed || _session.Tick - _lastCheckpointTick >= 30)
                    {
                        PrepareCheckpoint();
                        if (!await Persist()) continue;
                    }
                    foreach (var entry in _peers) entry.Value.Outbox.Writer.TryWrite(Frame("snapshot", entry.Key));
                }
                finally
                {
                    _gate.Release();
                    if (measured)
                    {
                        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        if (elapsed > 33.34) TickOverruns++;
                        _tickSamples.Enqueue(elapsed); if (_tickSamples.Count > 600) _tickSamples.Dequeue();
                        TickSamples++; var sorted = _tickSamples.Order().ToArray(); TickP99Ms = sorted[(int)Math.Ceiling(sorted.Length * .99) - 1];
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Draining = true;
            Console.WriteLine(JsonData.Write(new { kind = "CoopMatchStopped", matchId = Id, category = ex.GetType().Name }));
        }
    }
    private void PrepareCheckpoint()
    {
        if (_pending is not null) return;
        var snapshot = _session.Capture();
        var replay = _recorder.Capture();
        var check = CoopReplayRunner.Run(EnvironmentContent, replay);
        if (!check.Success || check.StateHash != _session.StateHash) throw new InvalidDataException("Server replay diverged.");
        string directory = Environment.GetEnvironmentVariable("ASHENWAKE_SERVER_ARTIFACTS") ?? "artifacts/online-server";
        AtomicFile.Write(Path.Combine(directory, Id, $"segment.{_revision + 1}.json"), JsonData.Write(replay));
        var characters = _allocation.Players.Select(p =>
        {
            var previous = _allocation.Characters.Single(c => c.Id == p.CharacterId).State;
            var retained = ValidateCharacterState(previous)?.Rewards ?? [];
            var rewards = retained.Concat(snapshot.Rewards.Where(r => r.PlayerId == p.Slot)).GroupBy(r => r.Id, StringComparer.Ordinal)
                .Select(g => g.Last()).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
            if (rewards.Length > 256) throw new InvalidDataException("Prototype character receipt capacity reached.");
            return new CharacterWrite(p.CharacterId, _characterRevisions[p.CharacterId], JsonSerializer.SerializeToElement(new OnlineCharacterState("coop.1", Id, p.Slot, snapshot.Tick,
                rewards.Sum(r => r.Experience), rewards.Sum(r => r.Ash), rewards), JsonData.Options));
        }).ToArray();
        var writes = snapshot.Rewards.Skip(_committedRewards).Select(r => new RewardWrite(_allocation.Players.Single(p => p.Slot == r.PlayerId).CharacterId,
            r.Id, JsonSerializer.SerializeToElement(r, JsonData.Options))).ToArray();
        _pending = new($"checkpoint.{Id}.{_revision + 1}.{Guid.NewGuid():N}", _revision,
            JsonSerializer.SerializeToElement(snapshot, JsonData.Options), characters, writes);
    }
    private async Task<bool> Persist()
    {
        if (_pending is null) return true;
        try
        {
            var result = await _control.Checkpoint(Id, _pending, _stop.Token);
            if (result.Revision != _revision + 1 || result.Characters.Length != 2 || result.Characters.Any(c => !_characterRevisions.TryGetValue(c.CharacterId, out long previous) || c.Revision != previous + 1))
                throw new InvalidDataException("Unexpected committed revision.");
            _revision = result.Revision;
            foreach (var character in result.Characters) _characterRevisions[character.CharacterId] = character.Revision;
            _lastCheckpointTick = _pending.Snapshot.GetProperty("tick").GetInt64();
            _committedRewards = _pending.Snapshot.GetProperty("rewards").GetArrayLength(); _pending = null; _recorder = new(_session); _consecutiveFailures = 0;
            _checkpointAt = Stopwatch.GetTimestamp(); _dirty = false;
            foreach (int slot in _deferredDisconnects.Order()) { _recorder.SetConnected(_session, slot, false); _dirty = true; }
            if (_deferredDisconnects.Count > 0) Broadcast();
            _deferredDisconnects.Clear(); return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException)
        { PersistenceFailures++; _consecutiveFailures = Math.Min(5, _consecutiveFailures + 1); _retryAt = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * Math.Min(5, .125 * (1 << _consecutiveFailures))); return false; }
    }
    public async Task<bool> TryEvict(TimeSpan idleGrace)
    {
        if (idleGrace < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleGrace));
        await _gate.WaitAsync();
        try
        {
            if (Draining || _pending is not null || _peers.Count != 0 || _servingPeers.Count != 0 || Stopwatch.GetElapsedTime(_idleSince) < idleGrace) return false;
            PrepareCheckpoint();
            if (!await Persist()) return false;
            // New joins see Draining and must reload the now-durable allocation after eviction.
            _evictionPrepared = true; Draining = true; return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            Draining = true;
            Console.WriteLine(JsonData.Write(new { kind = "CoopEvictionDeferred", matchId = Id, category = ex.GetType().Name }));
            return false;
        }
        finally { _gate.Release(); }
    }
    public async Task<bool> Drain()
    {
        await _gate.WaitAsync();
        try
        {
            Draining = true;
            if (_evictionPrepared) return true;
            if (_pending is not null && !await Persist()) return false;
            foreach (int id in new[] { 1, 2 }) _recorder.SetConnected(_session, id, false);
            _dirty = true; PrepareCheckpoint(); return await Persist();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            Console.WriteLine(JsonData.Write(new { kind = "CoopDrainDeferred", matchId = Id, category = ex.GetType().Name }));
            return false;
        }
        finally { _gate.Release(); }
    }
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync) return new(_disposeTask ??= DisposeCore());
    }
    private async Task DisposeCore()
    {
        bool durable = false;
        for (int attempt = 0; attempt < 3 && !durable; attempt++) durable = await Drain();
        if (!durable) Console.WriteLine(JsonData.Write(new { kind = "CoopDrainUncommitted", matchId = Id }));
        _stop.Cancel();
        try { await _loop; } catch (OperationCanceledException) { }
        Peer[] peers;
        await _gate.WaitAsync();
        try { peers = _servingPeers.ToArray(); foreach (var peer in peers) peer.Socket.Abort(); }
        finally { _gate.Release(); }
        await Task.WhenAll(peers.Select(p => p.Completion.Task));
        _stop.Dispose();
    }
    private sealed class Peer(WebSocket socket, CancellationTokenSource stop)
    {
        public WebSocket Socket { get; } = socket;
        public CancellationTokenSource Stop { get; } = stop;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Channel<NetworkFrame> Outbox { get; } = Channel.CreateBounded<NetworkFrame>(new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
        private long _window = Stopwatch.GetTimestamp(); private int _received;
        public bool Allow()
        {
            if (Stopwatch.GetElapsedTime(_window).TotalSeconds >= 1) { _window = Stopwatch.GetTimestamp(); _received = 0; }
            return ++_received <= 90;
        }
    }
}
