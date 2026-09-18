using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Ashenwake.Core.Coop;
using Ashenwake.Server;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders(); // Session credentials and save payloads never enter request logs.
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASHENWAKE_LISTEN") ?? "http://127.0.0.1:5180");
var app = builder.Build();
string content = File.ReadAllText(Environment.GetEnvironmentVariable("ASHENWAKE_COMBAT_CONTENT") ?? "content/combat.json");
string contentHash = CoopCombatSession.Create(content).ContentHash;
using var control = new ControlPlane(new Uri(Environment.GetEnvironmentVariable("ASHENWAKE_CONTROL_URL") ?? "http://127.0.0.1:8088/"),
    Environment.GetEnvironmentVariable("ASHENWAKE_SERVER_KEY") ?? throw new InvalidDataException("ASHENWAKE_SERVER_KEY is required."));
var matches = new ConcurrentDictionary<string, MatchHost>(StringComparer.Ordinal);
using var allocationGate = new SemaphoreSlim(1);
bool draining = false;
int idleGraceSeconds = int.TryParse(Environment.GetEnvironmentVariable("ASHENWAKE_IDLE_EVICT_SECONDS"), out int configuredGrace) ? Math.Clamp(configuredGrace, 1, 600) : 60;
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
app.MapGet("/healthz", () => Results.Ok(new { status = "alive", protocolVersion = NetworkProtocol.Version, contentHash }));
app.MapGet("/readyz", () => draining || matches.Count >= 4 || matches.Values.Any(m => m.Draining || m.PersistencePaused) ? Results.StatusCode(503) : Results.Ok(new { status = "ready", capacity = 4, active = matches.Count }));
app.MapGet("/metrics", () => Results.Text(string.Join('\n', new[] {
    $"ashenwake_matches {matches.Count}", $"ashenwake_tick_overruns_total {matches.Values.Sum(m => m.TickOverruns)}",
    $"ashenwake_persistence_failures_total {matches.Values.Sum(m => m.PersistenceFailures)}",
    $"ashenwake_reconnects_total {matches.Values.Sum(m => m.Reconnects)}",
    $"ashenwake_rejected_inputs_total {matches.Values.Sum(m => m.RejectedInputs)}",
    $"ashenwake_snapshots_total {matches.Values.Sum(m => m.SnapshotsSent)}" }) + "\n", "text/plain"));
app.MapGet("/diagnostics", () => Results.Ok(new
{
    protocolVersion = NetworkProtocol.Version,
    contentHash,
    managedBytes = GC.GetTotalMemory(false),
    matches = matches.Values.Select(m => new { m.TickSamples, m.TickP99Ms, m.TickOverruns, m.PersistenceFailures, m.Reconnects, m.RejectedInputs, m.SnapshotsSent, m.Completed }).ToArray(),
    timing = "Rolling last600 full server-loop iterations, including synchronous persistence/replay, excluding client rendering and control-service CPU. Completed and idle matches stop sampling."
}));
app.Map("/v1/matches/{id}/socket", async (HttpContext context, string id) =>
{
    if (draining || id.Length is < 1 or > 80 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) { context.Response.StatusCode = 503; return; }
    if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    try
    {
        using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        helloTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var hello = await NetworkProtocol.Receive<ClientHello>(socket, NetworkProtocol.MaxInputBytes, helloTimeout.Token);
        if (hello is null || hello.ProtocolVersion != NetworkProtocol.Version || hello.ContentHash != contentHash || hello.Ticket is null || hello.Ticket.Length is < 32 or > 256)
            throw new InvalidDataException("Incompatible handshake.");
        var join = await control.Join(id, hello.Ticket, helloTimeout.Token);
        MatchHost match;
        await allocationGate.WaitAsync(helloTimeout.Token);
        try
        {
            if (draining) throw new InvalidDataException("Server is draining.");
            if (!matches.TryGetValue(id, out match!))
            {
                if (matches.Count >= 4) throw new InvalidDataException("Prototype capacity reached.");
                match = await MatchHost.Create(control, id, content, helloTimeout.Token);
                if (!matches.TryAdd(id, match)) { await match.DisposeAsync(); throw new InvalidDataException("Allocation conflict."); }
            }
        }
        finally { allocationGate.Release(); }
        await match.Serve(socket, join, context.RequestAborted);
    }
    catch (Exception ex) when (ex is InvalidDataException or JsonException or HttpRequestException or WebSocketException or OperationCanceledException)
    {
        if (socket.State == WebSocketState.Open)
        {
            using var close = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Session unavailable or incompatible", close.Token); }
            catch (Exception closeError) when (closeError is WebSocketException or OperationCanceledException) { }
        }
    }
});
Task drain = Task.CompletedTask;
app.Lifetime.ApplicationStopping.Register(() =>
{
    draining = true;
    drain = Task.WhenAll(matches.Values.Select(m => m.Drain()));
});
var maintenance = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    try
    {
        while (await timer.WaitForNextTickAsync(app.Lifetime.ApplicationStopping))
        {
            var evicted = new List<MatchHost>();
            await allocationGate.WaitAsync(app.Lifetime.ApplicationStopping);
            try
            {
                foreach (var entry in matches.ToArray())
                    if (await entry.Value.TryEvict(TimeSpan.FromSeconds(idleGraceSeconds)) && matches.TryRemove(entry.Key, out var removed)) evicted.Add(removed);
            }
            finally { allocationGate.Release(); }
            foreach (var match in evicted) await match.DisposeAsync();
        }
    }
    catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested) { }
});
Console.WriteLine(JsonSerializer.Serialize(new { kind = "CoopServerReady", protocolVersion = NetworkProtocol.Version, contentHash, maximumMatches = 4, ticksPerSecond = 30 }));
try { await app.RunAsync(); }
finally
{
    await maintenance;
    await drain;
    await Task.WhenAll(matches.Values.Select(m => m.DisposeAsync().AsTask()));
}
