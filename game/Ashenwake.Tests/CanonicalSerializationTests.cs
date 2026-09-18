using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ashenwake.Core.Content;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CanonicalSerializationTests
{
    [Theory]
    [InlineData("fixtures/phase2-expedition-hub.json")]
    [InlineData("fixtures/phase3-production-hub.json")]
    [InlineData("fixtures/phase4-campaign-complete.json")]
    public void Utf8OptimizationPreservesHistoricalCanonicalHashBytes(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name)));
        var state = document.RootElement.GetProperty("state");
        string oldHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonData.Write(state))));
        Assert.Equal(document.RootElement.GetProperty("stateHash").GetString(), oldHash);
        Assert.Equal(oldHash, JsonData.Hash(state));
        Assert.Equal(oldHash, JsonData.Hash(JsonData.Copy(state)));
    }
    [Fact]
    public void Utf8CopyRetainsEscapesNumbersAndDetachedCollectionOwnership()
    {
        var original = new SortedDictionary<string, string[]>
        {
            ["雪🌙<>&\"\\\n"] = ["\u0001", "e\u0301", "𝄞"],
            ["empty"] = []
        };
        var copy = JsonData.Copy(original);
        Assert.Equal(JsonData.Write(original), JsonData.Write(copy));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonData.Write(original)))), JsonData.Hash(original));
        copy.Values.First(values => values.Length > 0)[0] = "changed";
        Assert.NotEqual(JsonData.Hash(original), JsonData.Hash(copy));
        Assert.Throws<InvalidDataException>(() => JsonData.Copy<object?>(null));
    }
}
