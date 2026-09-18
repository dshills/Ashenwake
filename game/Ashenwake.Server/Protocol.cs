using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;

namespace Ashenwake.Server;

public static class NetworkProtocol
{
    public const string Version = "coop-net.1";
    public const int MaxInputBytes = 4096;
    public static async Task<T?> Receive<T>(WebSocket socket, int limit, CancellationToken token)
    {
        using var stream = new MemoryStream();
        byte[] buffer = new byte[Math.Min(limit, 8192)];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) return default;
            if (result.MessageType != WebSocketMessageType.Text || stream.Length + result.Count > limit)
                throw new InvalidDataException("Message bounds exceeded.");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonSerializer.Deserialize<T>(stream.ToArray(), JsonData.Options) ?? throw new InvalidDataException("Null message.");
    }
    public static Task Send<T>(WebSocket socket, T value, CancellationToken token) => socket.SendAsync(
        JsonSerializer.SerializeToUtf8Bytes(value, JsonData.Options), WebSocketMessageType.Text, true, token);
}
public sealed record ClientHello([property: JsonRequired] string ProtocolVersion, [property: JsonRequired] string ContentHash,
    [property: JsonRequired] string Ticket);
public sealed record NetworkFrame(string ProtocolVersion, string Kind, int PlayerId, long Revision, string StateHash,
    CoopView View, CoopInputResult? InputResult = null);
public sealed record OnlinePlayer(int Slot, string CharacterId, string AccountId, bool Ready, string ProtocolVersion, string ContentHash);
public sealed record OnlineCharacter(string Id, string AccountId, string Name, string Discipline, long Revision, JsonElement State);
public sealed record OnlineAllocation(string AllocationId, string PartyId, long Revision, string ServerUrl, string ProtocolVersion,
    string ContentHash, OnlinePlayer[] Players, JsonElement Snapshot, OnlineCharacter[] Characters);
public sealed record OnlineJoin(string AllocationId, int Slot, string CharacterId, string AccountId, long Revision, JsonElement State);
public sealed record CharacterWrite(string CharacterId, long ExpectedRevision, JsonElement State);
public sealed record RewardWrite(string CharacterId, string EventId, JsonElement Payload);
public sealed record CheckpointWrite(string OperationId, long ExpectedRevision, JsonElement Snapshot, CharacterWrite[] Characters, RewardWrite[] Rewards);
public sealed record CharacterRevision(string CharacterId, long Revision);
public sealed record CheckpointResult(long Revision, CharacterRevision[] Characters);
public sealed record OnlineCharacterState(string RulesVersion, string MatchId, int PlayerId, long Tick, int Experience, int Ash, CoopRewardReceipt[] Rewards);
