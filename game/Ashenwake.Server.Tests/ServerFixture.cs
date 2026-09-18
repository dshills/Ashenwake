using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;
using Ashenwake.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ashenwake.Server.Tests;

internal sealed class ServerFixture : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly object _sync = new();
    private readonly Dictionary<string, CheckpointResult> _commits = [];
    private readonly string? _previousArtifacts;
    public static string Content => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    public string ArtifactDirectory { get; } = Path.Combine(Path.GetTempPath(), "ashenwake-server-tests-" + Guid.NewGuid().ToString("N"));
    public OnlineAllocation Allocation { get; private set; }
    public ControlPlane Control { get; private set; } = null!;
    public ConcurrentQueue<CheckpointWrite> Requests { get; } = new();
    public bool FailWrites;
    public bool LoseNextCommittedResponse;
    private ServerFixture(WebApplication app)
    {
        _app = app; string id = "test-" + Guid.NewGuid().ToString("N");
        var empty = JsonSerializer.SerializeToElement(new { });
        Allocation = new(id, "party", 0, "ws://unused", NetworkProtocol.Version, CoopCombatSession.Create(Content).ContentHash,
            [new(1, "char-1", "account-1", true, NetworkProtocol.Version, ""), new(2, "char-2", "account-2", true, NetworkProtocol.Version, "")], empty,
            [new("char-1", "account-1", "One", "Vanguard", 0, empty), new("char-2", "account-2", "Two", "Vanguard", 0, empty)]);
        _previousArtifacts = Environment.GetEnvironmentVariable("ASHENWAKE_SERVER_ARTIFACTS");
        Environment.SetEnvironmentVariable("ASHENWAKE_SERVER_ARTIFACTS", ArtifactDirectory);
    }
    public static async Task<ServerFixture> Create()
    {
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        var fixture = new ServerFixture(builder.Build());
        fixture._app.MapGet("/v1/server/allocations/{id}", () => { lock (fixture._sync) return Results.Json(fixture.Allocation, JsonData.Options); });
        fixture._app.MapPost("/v1/server/allocations/{id}/checkpoint", async (HttpContext context) =>
        {
            var request = await JsonSerializer.DeserializeAsync<CheckpointWrite>(context.Request.Body, JsonData.Options, context.RequestAborted) ?? throw new InvalidDataException();
            fixture.Requests.Enqueue(request);
            lock (fixture._sync)
            {
                if (fixture.FailWrites) return Results.StatusCode(503);
                if (fixture._commits.TryGetValue(request.OperationId, out var prior)) return Results.Json(prior, JsonData.Options);
                if (request.ExpectedRevision != fixture.Allocation.Revision || request.Characters.Any(c => fixture.Allocation.Characters.Single(p => p.Id == c.CharacterId).Revision != c.ExpectedRevision)) return Results.StatusCode(409);
                var revisions = request.Characters.Select(c => new CharacterRevision(c.CharacterId, c.ExpectedRevision + 1)).ToArray();
                var result = new CheckpointResult(fixture.Allocation.Revision + 1, revisions);
                fixture.Allocation = fixture.Allocation with
                {
                    Revision = result.Revision,
                    Snapshot = request.Snapshot,
                    Characters = fixture.Allocation.Characters.Select(c => c with { Revision = c.Revision + 1, State = request.Characters.Single(w => w.CharacterId == c.Id).State }).ToArray()
                };
                fixture._commits.Add(request.OperationId, result);
                if (fixture.LoseNextCommittedResponse) { fixture.LoseNextCommittedResponse = false; return Results.StatusCode(503); }
                return Results.Json(result, JsonData.Options);
            }
        });
        await fixture._app.StartAsync();
        var url = fixture._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        fixture.Control = new(new Uri(url + "/"), new string('s', 32)); return fixture;
    }
    public void CharacterState(JsonElement state)
    {
        lock (_sync) Allocation = Allocation with { Characters = Allocation.Characters.Select((c, i) => i == 0 ? c with { State = state } : c).ToArray() };
    }
    public void CompletedSnapshot(CoopSnapshot snapshot)
    {
        lock (_sync) Allocation = Allocation with
        {
            Snapshot = JsonSerializer.SerializeToElement(snapshot, JsonData.Options),
            Characters = Allocation.Characters.Select((c, index) =>
            {
                var rewards = snapshot.Rewards.Where(r => r.PlayerId == index + 1).ToArray();
                return c with { State = JsonSerializer.SerializeToElement(new OnlineCharacterState("coop.1", snapshot.MatchId, index + 1, snapshot.Tick, rewards.Sum(r => r.Experience), rewards.Sum(r => r.Ash), rewards), JsonData.Options) };
            }).ToArray()
        };
    }
    public Task<MatchHost> Host() => MatchHost.Create(Control, Allocation.AllocationId, Content, CancellationToken.None);
    public OnlineJoin Join(int slot) => new(Allocation.AllocationId, slot, "char-" + slot, "account-" + slot, 0, JsonSerializer.SerializeToElement(new { }));
    public async ValueTask DisposeAsync()
    {
        Control.Dispose(); await _app.DisposeAsync(); Environment.SetEnvironmentVariable("ASHENWAKE_SERVER_ARTIFACTS", _previousArtifacts);
        if (Directory.Exists(ArtifactDirectory)) Directory.Delete(ArtifactDirectory, true);
    }
}

internal sealed class TestSocket(bool blockSend = false) : WebSocket
{
    private readonly Channel<byte[]> _inputs = Channel.CreateUnbounded<byte[]>();
    private readonly CancellationTokenSource _aborted = new();
    private WebSocketState _state = WebSocketState.Open;
    public Channel<NetworkFrame> Frames { get; } = Channel.CreateUnbounded<NetworkFrame>();
    public TaskCompletionSource SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override WebSocketState State => _state;
    public override string? SubProtocol => null;
    public void Feed(CoopInput input) => _inputs.Writer.TryWrite(Encoding.UTF8.GetBytes(JsonData.Write(input)));
    public override void Abort() { _state = WebSocketState.Aborted; _aborted.Cancel(); }
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { Abort(); return Task.CompletedTask; }
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => CloseAsync(closeStatus, statusDescription, cancellationToken);
    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _aborted.Token);
        var bytes = await _inputs.Reader.ReadAsync(linked.Token); bytes.CopyTo(buffer.Array!, buffer.Offset);
        return new(bytes.Length, WebSocketMessageType.Text, true);
    }
    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        SendStarted.TrySetResult();
        if (blockSend) await Task.Delay(Timeout.Infinite, cancellationToken);
        else Frames.Writer.TryWrite(JsonData.Read<NetworkFrame>(Encoding.UTF8.GetString(buffer)));
    }
    public override void Dispose() { Abort(); _aborted.Dispose(); }
}
