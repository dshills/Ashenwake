using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;

namespace Ashenwake.Client;

internal sealed record CoopServerFrame(string ProtocolVersion, string Kind, int PlayerId, long Revision,
    string StateHash, CoopView? View, CoopInputResult? InputResult = null);

/// <summary>One authenticated WebSocket peer. No credential is written to logs, preferences or reports.</summary>
internal sealed class CoopClientTransport : IDisposable
{
    internal const string Protocol = "coop-net.1";
    private const int MaximumFrameBytes = 1024 * 1024;
    private readonly ClientWebSocket socket = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly SemaphoreSlim sender = new(1, 1);
    private readonly ConcurrentQueue<CoopServerFrame> frames = new();
    private volatile string status = "Disconnected";
    public string Status => status;
    public bool Connected => socket.State == WebSocketState.Open;
    public long ReceivedFrames { get; private set; }
    public long DroppedFrames { get; private set; }

    public async Task Connect(Uri uri, string contentHash, string ticket)
    {
        try
        {
            status = "Connecting";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(uri, timeout.Token);
            await SendJson(JsonData.Write(new { protocolVersion = Protocol, contentHash, ticket }));
            status = "Waiting for the session";
            _ = Receive();
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        { status = "Connection unavailable. Check the session address and obtain a fresh join ticket."; }
    }
    public bool TryRead(out CoopServerFrame? frame) => frames.TryDequeue(out frame);
    public async Task Send(CoopInput input)
    {
        if (!Connected) return;
        try { await SendJson(JsonData.Write(input)); }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        { status = "Connection interrupted. Rejoin with a fresh ticket."; }
    }
    private async Task SendJson(string json)
    {
        await sender.WaitAsync(cancellation.Token);
        try { await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellation.Token); }
        finally { sender.Release(); }
    }
    private async Task Receive()
    {
        var bytes = new byte[8192];
        try
        {
            while (!cancellation.IsCancellationRequested && Connected)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(bytes, cancellation.Token);
                    if (part.MessageType == WebSocketMessageType.Close) { status = "Session closed. Rejoin with a fresh ticket."; return; }
                    if (part.MessageType != WebSocketMessageType.Text || message.Length + part.Count > MaximumFrameBytes)
                        throw new InvalidDataException("Invalid frame.");
                    message.Write(bytes, 0, part.Count);
                } while (!part.EndOfMessage);
                var frame = JsonData.Read<CoopServerFrame>(Encoding.UTF8.GetString(message.ToArray()));
                if (frame.ProtocolVersion != Protocol || frame.Kind is not ("joined" or "ack" or "snapshot"))
                    throw new InvalidDataException("Unsupported session protocol.");
                if (!ValidView(frame.View))
                    throw new InvalidDataException("Snapshot exceeds scene limits.");
                while (frames.Count >= 64 && frames.TryDequeue(out _)) DroppedFrames++;
                frames.Enqueue(frame); ReceivedFrames++;
                status = "Connected";
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException or InvalidDataException or System.Text.Json.JsonException or ObjectDisposedException)
        { if (!cancellation.IsCancellationRequested) status = "Session interrupted or incompatible. Rejoin with a fresh ticket."; }
        finally { socket.Abort(); }
    }
    private static bool ValidView(CoopView? view)
    {
        if (view is not
            {
                Actors: not null, Players: not null, Projectiles: not null, Warnings: not null, Rewards: not null,
                Room: { HalfWidth: > 0 and <= 100000, HalfDepth: > 0 and <= 100000, Obstacles: not null }
            } ||
            view.Tick < 0 || view.EncounterIndex is < 0 or > 4 || view.ContextKey is not { Length: > 0 and <= 256 } ||
            view.Room.Obstacles.Length > 128 || view.Actors.Length > 24 || view.Players.Length != 2 || view.Projectiles.Length > 64 || view.Warnings.Length > 48 || view.Rewards.Length > 32) return false;
        if (view.Players.Any(p => p is null || p.Id is < 1 or > 2 || p.Loadout is null || p.Skills is not { Length: 6 } ||
            p.Skills.Any(s => s is null || s.Id is null || s.Name is null)) || view.Players.Select(p => p.Id).Distinct().Count() != 2) return false;
        if (view.Actors.Any(a => a is null || a.DefinitionId is null || a.Role is null) ||
            view.Actors.Select(a => a.Id).Distinct().Count() != view.Actors.Length ||
            view.Players.Any(p => view.Actors.Count(a => a.PlayerId == p.Id) != 1)) return false;
        return view.Projectiles.All(p => p is not null) &&
            view.Warnings.All(w => w is not null && w.Shape is "Circle" or "Line" && w.Radius is >= 0 and <= 100000) &&
            view.Rewards.All(r => r is not null && r.Item is { Name: not null });
    }
    public void Dispose()
    {
        cancellation.Cancel(); socket.Abort(); socket.Dispose();
        status = "Disconnected";
    }
}
