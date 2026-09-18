using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;
using Ashenwake.Server;

if (args.Length != 2 || args[0] is not ("setup" or "run")) throw new ArgumentException("NetworkProbe setup|run <new-output-directory>");
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
Directory.CreateDirectory(output);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
using var api = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("ASHENWAKE_CONTROL_URL") ?? "http://127.0.0.1:8088/"), Timeout = TimeSpan.FromSeconds(10) };
var content = File.ReadAllText("content/combat.json");
string contentHash = CoopCombatSession.Create(content).ContentHash;
var accounts = new JsonElement[2]; var characters = new JsonElement[2];
for (int i = 0; i < 2; i++)
{
    accounts[i] = await Post("v1/sessions/anonymous", new { });
    characters[i] = await Post("v1/characters", new { name = $"Probe{i + 1}", discipline = "Vanguard" }, Token(i));
}
var party = await Post("v1/parties", new { characterId = characters[0].GetProperty("id").GetString() }, Token(0));
string partyId = party.GetProperty("id").GetString()!;
var invite = await Post($"v1/parties/{partyId}/invites", new { }, Token(0));
await Post("v1/party-invites/accept", new { inviteToken = invite.GetProperty("inviteToken").GetString(), characterId = characters[1].GetProperty("id").GetString() }, Token(1));
for (int i = 0; i < 2; i++) await Post($"v1/parties/{partyId}/ready", new { ready = true, protocolVersion = NetworkProtocol.Version, contentHash }, Token(i));
var allocation = await Post($"v1/parties/{partyId}/allocate", new { operationId = "probe." + Guid.NewGuid().ToString("N") }, Token(0));
string allocationId = allocation.GetProperty("allocationId").GetString()!;
string socketUrl = allocation.GetProperty("serverUrl").GetString()!;
if (args[0] == "setup")
{
    string ticket = await Ticket(0), peerTicket = await Ticket(1);
    string file = Path.Combine(output, "connection.json");
    File.WriteAllText(file, JsonData.Write(new { allocationId, socketUrl, contentHash, ticket, peerTicket }));
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    Console.WriteLine(JsonData.Write(new { kind = "CoopClientSetup", path = file, allocationId, contentHash })); return;
}
Process? server = null;
var sockets = new ClientWebSocket[2];
var latest = new NetworkFrame?[2];
var receives = new Task[2];
var sequence = new long[2]; var sentTick = new long[] { -1, -1 };
long drops = 0, duplicates = 0, stale = 0, delayed = 0, maxTick = 0; bool reconnected = false, restarted = false;
try
{
    server = await StartServer();
    await Connect(0); await Connect(1);
    var watch = Stopwatch.StartNew();
    while (!timeout.IsCancellationRequested)
    {
        await Task.Delay(8, timeout.Token);
        var views = latest.Select(v => v?.View).ToArray();
        if (views.Any(v => v is null)) continue;
        maxTick = Math.Max(maxTick, views.Max(v => v!.Tick));
        if (!reconnected && maxTick >= 240)
        {
            sockets[1].Abort(); await Task.Delay(180, timeout.Token); await Connect(1); reconnected = true; continue;
        }
        if (!restarted && maxTick >= 480)
        {
            foreach (var socket in sockets) socket.Abort();
            server.Kill(true); await server.WaitForExitAsync(timeout.Token); server.Dispose();
            server = await StartServer();
            for (int i = 0; i < 2; i++) { latest[i] = null; await Connect(i); }
            restarted = true; continue;
        }
        if (latest.All(v => v?.View.Completed == true))
        {
            if (latest[0]!.StateHash != latest[1]!.StateHash) continue;
            var rewards = latest[0]!.View.Rewards;
            if (rewards.Length == 0 || rewards.GroupBy(r => r.Id).Any(g => g.Count() != 1) || rewards.Select(r => r.PlayerId).Distinct().Count() != 2)
                throw new InvalidDataException("Shared reward receipt mismatch.");
            if (!reconnected || !restarted) throw new InvalidDataException("Recovery scenarios were not exercised.");
            var saved = new JsonElement[2];
            for (int i = 0; i < 2; i++) saved[i] = await Get("v1/characters", Token(i));
            for (int i = 0; i < 2; i++)
            {
                var character = saved[i].EnumerateArray().Single(c => c.GetProperty("id").GetString() == characters[i].GetProperty("id").GetString());
                var state = JsonData.Read<OnlineCharacterState>(character.GetProperty("state").GetRawText());
                if (!state.Rewards.Select(r => r.Id).SequenceEqual(rewards.Where(r => r.PlayerId == i + 1).OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Id)))
                    throw new InvalidDataException("Durable personal rewards differ.");
            }
            int replaySegments = 0; string? previousReplayHash = null;
            string replayDirectory = Path.Combine(Environment.GetEnvironmentVariable("ASHENWAKE_SERVER_ARTIFACTS") ?? "artifacts/online-server", allocationId);
            foreach (string path in Directory.GetFiles(replayDirectory, "segment.*.json").OrderBy(p => long.Parse(Path.GetFileName(p).Split('.')[1], System.Globalization.CultureInfo.InvariantCulture)))
            {
                long revision = long.Parse(Path.GetFileName(path).Split('.')[1], System.Globalization.CultureInfo.InvariantCulture);
                if (revision != replaySegments + 1) throw new InvalidDataException("Server replay revision gap.");
                var replay = JsonData.Read<CoopReplay>(File.ReadAllText(path));
                if (previousReplayHash is not null && JsonData.Hash(replay.InitialState) != previousReplayHash) throw new InvalidDataException("Server replay origin chain diverged.");
                var checkedReplay = CoopReplayRunner.Run(content, replay);
                if (!checkedReplay.Success) throw new InvalidDataException("Recorded server replay diverged.");
                previousReplayHash = checkedReplay.StateHash; replaySegments++;
            }
            if (replaySegments == 0 || replaySegments != latest[0]!.Revision || previousReplayHash != latest[0]!.StateHash)
                throw new InvalidDataException("Completed network outcome lacks a complete durable replay chain.");
            using var diagnosticsClient = new HttpClient();
            string diagnostics = await diagnosticsClient.GetStringAsync("http://127.0.0.1:5180/diagnostics", timeout.Token);
            File.WriteAllText(Path.Combine(output, "server-diagnostics.json"), diagnostics);
            var report = new
            {
                kind = "CoopNetworkProbePassed",
                replaySegments,
                protocolVersion = NetworkProtocol.Version,
                contentHash,
                allocationId,
                stateHash = latest[0]!.StateHash,
                tick = latest[0]!.View.Tick,
                receipts = rewards.Length,
                reconnected,
                restarted,
                drops,
                duplicates,
                stale,
                delayed,
                elapsedSeconds = watch.Elapsed.TotalSeconds,
                impairment = "Deterministic application-intent omission every17, duplicates every23, stale sequence every29, 12-24ms jitter every7; TCP remains reliable; one reconnect and one abrupt server restart."
            };
            File.WriteAllText(Path.Combine(output, "report.json"), JsonData.Write(report));
            File.WriteAllText(Path.Combine(output, "final-view.json"), JsonData.Write(latest[0]));
            Console.WriteLine(JsonData.Write(report)); break;
        }
        for (int i = 0; i < 2; i++)
        {
            var frame = latest[i];
            if (frame is null || frame.View.Tick == sentTick[i] || sockets[i].State != WebSocketState.Open) continue;
            sentTick[i] = frame.View.Tick;
            sequence[i] = Math.Max(sequence[i], frame.View.Players.Single(p => p.Id == i + 1).AcceptedSequence) + 1;
            var input = CoopSmoke.Input(frame.View, i + 1, sequence[i]);
            if (sequence[i] % 17 == 0) { drops++; continue; }
            if (sequence[i] % 7 == 0) { delayed++; await Task.Delay(12 + (int)(sequence[i] % 3) * 6, timeout.Token); }
            await NetworkProtocol.Send(sockets[i], input, timeout.Token);
            if (sequence[i] % 23 == 0) { duplicates++; await NetworkProtocol.Send(sockets[i], input, timeout.Token); }
            if (sequence[i] % 29 == 0) { stale++; await NetworkProtocol.Send(sockets[i], input with { Sequence = Math.Max(1, input.Sequence - 5) }, timeout.Token); }
        }
    }
    timeout.Token.ThrowIfCancellationRequested();
}
finally
{
    foreach (var socket in sockets) socket?.Abort();
    if (server is { HasExited: false }) { server.Kill(true); await server.WaitForExitAsync(); }
    server?.Dispose();
}
string Token(int index) => accounts[index].GetProperty("sessionToken").GetString()!;
async Task<string> Ticket(int index) => (await Post($"v1/allocations/{allocationId}/join-ticket", new { }, Token(index))).GetProperty("ticket").GetString()!;
async Task<JsonElement> Post(string path, object body, string? token = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: JsonData.Options) };
    if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var response = await api.SendAsync(request, timeout.Token); response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
}
async Task<JsonElement> Get(string path, string token)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Authorization = new("Bearer", token);
    using var response = await api.SendAsync(request, timeout.Token); response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
}
async Task Connect(int index)
{
    sockets[index]?.Dispose(); sockets[index] = new ClientWebSocket();
    await sockets[index].ConnectAsync(new Uri(socketUrl), timeout.Token);
    await NetworkProtocol.Send(sockets[index], new ClientHello(NetworkProtocol.Version, contentHash, await Ticket(index)), timeout.Token);
    var first = await NetworkProtocol.Receive<NetworkFrame>(sockets[index], 1024 * 1024, timeout.Token) ?? throw new IOException("Join closed.");
    latest[index] = first; sequence[index] = first.View.Players.Single(p => p.Id == index + 1).AcceptedSequence; sentTick[index] = -1;
    var ownSocket = sockets[index];
    receives[index] = Task.Run(async () =>
    {
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                var next = await NetworkProtocol.Receive<NetworkFrame>(ownSocket, 1024 * 1024, timeout.Token);
                if (next is null) break;
                if (ReferenceEquals(sockets[index], ownSocket)) Volatile.Write(ref latest[index], next);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
    });
}
async Task<Process> StartServer()
{
    string assembly = Path.GetFullPath("game/Ashenwake.Server/bin/Debug/net8.0/Ashenwake.Server.dll");
    var info = new ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!, "dotnet")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    info.ArgumentList.Add(assembly);
    var process = Process.Start(info) ?? throw new IOException("Cannot start dedicated server.");
    _ = process.StandardOutput.ReadToEndAsync(); _ = process.StandardError.ReadToEndAsync();
    using var health = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
    for (int i = 0; i < 100; i++)
    {
        if (process.HasExited) throw new IOException("Dedicated server exited before readiness.");
        try { using var result = await health.GetAsync("http://127.0.0.1:5180/readyz", timeout.Token); if (result.IsSuccessStatusCode) return process; }
        catch (HttpRequestException) { }
        await Task.Delay(100, timeout.Token);
    }
    process.Kill(true); throw new IOException("Dedicated server readiness timed out.");
}
