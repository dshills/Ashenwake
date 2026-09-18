using System.Net.Http.Json;
using Ashenwake.Core.Content;

namespace Ashenwake.Server;

public sealed class ControlPlane : IDisposable
{
    private readonly HttpClient _http;
    public ControlPlane(Uri baseAddress, string serverKey)
    {
        if (serverKey.Length < 32) throw new InvalidDataException("Server key must have at least 32 characters.");
        _http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(5) };
        _http.DefaultRequestHeaders.Add("Authorization", "Bearer " + serverKey);
    }
    public Task<OnlineAllocation> Get(string id, CancellationToken token) => Read<OnlineAllocation>(HttpMethod.Get, $"v1/server/allocations/{Uri.EscapeDataString(id)}", null, token);
    public Task<OnlineJoin> Join(string id, string ticket, CancellationToken token) => Read<OnlineJoin>(HttpMethod.Post, $"v1/server/allocations/{Uri.EscapeDataString(id)}/consume-ticket", new { ticket }, token);
    public Task<CheckpointResult> Checkpoint(string id, CheckpointWrite request, CancellationToken token) => Read<CheckpointResult>(HttpMethod.Post, $"v1/server/allocations/{Uri.EscapeDataString(id)}/checkpoint", request, token);
    private async Task<T> Read<T>(HttpMethod method, string path, object? value, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (value is not null) request.Content = JsonContent.Create(value, options: JsonData.Options);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var bounded = new MemoryStream();
        byte[] buffer = new byte[8192]; int count;
        while ((count = await body.ReadAsync(buffer, token)) > 0)
        {
            if (bounded.Length + count > 2 * 1024 * 1024) throw new InvalidDataException("Control response too large.");
            bounded.Write(buffer, 0, count);
        }
        return JsonData.Read<T>(System.Text.Encoding.UTF8.GetString(bounded.ToArray()));
    }
    public void Dispose() => _http.Dispose();
}
