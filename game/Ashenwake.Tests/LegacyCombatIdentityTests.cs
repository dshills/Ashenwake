using System.Text.Json.Nodes;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class LegacyCombatIdentityTests
{
    private static string Legacy => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures/combat-phase2.json"));

    [Fact]
    public void GenuineLegacyContentRetainsItsMaintainedIdentity()
        => Assert.Equal(PhaseTwoMigration.CombatHash, CombatContent.Parse(Legacy).Identity);

    [Theory]
    [InlineData("skills", "behavior", "\"Vent\"")]
    [InlineData("skills", "resourceMode", "\"Heat\"")]
    [InlineData("skills", "discipline", "\"Vanguard\"")]
    [InlineData("items", "compatibleSlots", "[\"MainHand\",\"OffHand\"]")]
    [InlineData("items", "hands", "2")]
    [InlineData("items", "disciplines", "[\"Vanguard\"]")]
    public void ExplicitNewRulesCannotCollideWithLegacyContent(string collection, string field, string value)
    {
        var authored = JsonNode.Parse(Legacy)!;
        authored[collection]![0]![field] = JsonNode.Parse(value);
        string changed = authored.ToJsonString();
        Assert.NotEqual(CombatContent.Parse(Legacy).Identity, CombatContent.Parse(changed).Identity);
        var state = CombatSession.Create(Legacy).Capture();
        string archive = JsonData.Write(new CombatSave(1, JsonData.Hash(state), state));
        Assert.Equal(state.ContentHash, CombatSaveStore.Read(archive, Legacy).ContentHash);
        Assert.Throws<SaveCompatibilityException>(() => CombatSaveStore.Read(archive, changed));
    }

    [Fact]
    public void DistinctExplicitBehaviorValuesHaveDistinctContentIdentities()
    {
        var authored = JsonNode.Parse(Legacy)!;
        authored["skills"]![0]!["behavior"] = "Vent";
        string vent = CombatContent.Parse(authored.ToJsonString()).Identity;
        authored["skills"]![0]!["behavior"] = "Vanish";
        Assert.NotEqual(vent, CombatContent.Parse(authored.ToJsonString()).Identity);
    }
}
