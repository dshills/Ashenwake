using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CultureSerializationTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;
        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() => CultureInfo.CurrentCulture = previous;
    }
    private static (string Combat, AdventureContent Adventure, ProgressionContent Policy, CampaignContent Campaign, EndgameContent Endgame) Content()
    {
        var endgame = EndgameContent.Parse(Read("endgame.json"));
        var combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), endgame).CombatJson;
        return (combat, AdventureContent.Parse(Read("adventure.json")), ProgressionContent.Parse(Read("progression.json")), CampaignContent.Parse(Read("campaign.json")), endgame);
    }

    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("sk-SK")]
    public void FreshAndExistingEnglishCharactersRetainIdentityAcrossCultures(string culture)
    {
        string archive, expected, combatIdentity, permanentIdentity;
        using (new CultureScope("en-US"))
        {
            var c = Content(); var session = EndgameRuntimeSession.Create(c.Combat, c.Adventure, c.Policy, c.Campaign, c.Endgame);
            expected = session.StateHash; combatIdentity = session.Combat.ContentHash; permanentIdentity = session.Production.Content.Hash;
            // Reproduce the pre-fix serializer exactly, including its default
            // en-US collection comparers; do not generate the compatibility input
            // with the converter that this test is intended to verify.
            var legacy = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
            };
            var oldState = JsonSerializer.Deserialize<EndgameRuntimeSnapshot>(JsonSerializer.Serialize(session.Capture(), legacy), legacy)!;
            string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(oldState, legacy)));
            Assert.Equal(expected, hash);
            archive = JsonSerializer.Serialize(new EndgameRuntimeSave(1, hash, oldState), legacy);
        }
        using (new CultureScope(culture))
        {
            var c = Content(); var fresh = EndgameRuntimeSession.Create(c.Combat, c.Adventure, c.Policy, c.Campaign, c.Endgame);
            Assert.Equal(combatIdentity, fresh.Combat.ContentHash); Assert.Equal(permanentIdentity, fresh.Production.Content.Hash);
            Assert.Equal(expected, fresh.StateHash);
            Assert.Equal(expected, EndgameRuntimeSession.Restore(c.Combat, c.Adventure, c.Policy, c.Campaign, c.Endgame, fresh.Capture()).StateHash);
            Assert.Equal(expected, EndgameRuntimeSaveStore.Read(c.Combat, c.Adventure, c.Policy, c.Campaign, c.Endgame, archive).StateHash);
            string directory = Path.Combine(Path.GetTempPath(), "ashenwake-culture-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(directory, "endgame.save.json");
                EndgameRuntimeSaveStore.Write(path, c.Combat, c.Adventure, c.Policy, c.Campaign, c.Endgame, fresh.Capture());
                Assert.Equal(expected, EndgameRuntimeSaveStore.Load(path, c.Combat, c.Adventure, c.Policy, c.Campaign, c.Endgame).Session.StateHash);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        }
    }

    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("sk-SK")]
    public void MaintainedOlderArchivesRemainReadableAndMigratableInOtherCultures(string culture)
    {
        using var scope = new CultureScope(culture);
        var c = Content();
        var previous = CampaignCombatContent.Parse(Read("fixtures/combat-phase4.json"), Read("fixtures/campaign-combat-phase4.json")).CombatJson;
        var old = CampaignRuntimeSaveStore.Read(previous, c.Adventure, c.Policy, c.Campaign, Read("fixtures/phase4-campaign-complete.json"));
        using var manifest = JsonDocument.Parse(Read("fixtures/phase4-migration-manifest.json"));
        Assert.Equal(manifest.RootElement.GetProperty("stateHash").GetString(), old.StateHash);
        var upgraded = EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), previous, c.Combat, c.Adventure, c.Policy, c.Campaign, c.Endgame);
        Assert.True(upgraded.View.Unlocked); Assert.Equal(old.Production.ProgressionView.Experience, upgraded.Production.ProgressionView.Experience);
        Assert.Equal(upgraded.StateHash, EndgameRuntimeSession.Restore(c.Combat, c.Adventure, c.Policy, c.Campaign, c.Endgame, upgraded.Capture()).StateHash);
        Assert.Equal(PhaseTwoMigration.CombatHash, CombatContent.Parse(Read("fixtures/combat-phase2.json")).Identity);
        var imported = PhaseTwoMigration.Read(Read("fixtures/phase2-expedition-hub.json"), Read("combat.json"), c.Adventure, c.Policy);
        Assert.Equal("room.greyhaven", imported.View.RoomId);
    }

    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("sk-SK")]
    public void StringCollectionsUseOrdinalOrderingWhenCopiedAndWritten(string culture)
    {
        using var scope = new CultureScope(culture);
        var dictionary = new SortedDictionary<string, int> { ["Chest"] = 1, ["MainHand"] = 2, ["affix.chain"] = 3, ["affix.damage"] = 4 };
        var set = new SortedSet<string>(dictionary.Keys);
        Assert.Equal("{\"Chest\":1,\"MainHand\":2,\"affix.chain\":3,\"affix.damage\":4}", JsonData.Write(dictionary));
        Assert.Equal("[\"Chest\",\"MainHand\",\"affix.chain\",\"affix.damage\"]", JsonData.Write(set));
        Assert.Same(StringComparer.Ordinal, JsonData.Copy(dictionary).Comparer);
        Assert.Same(StringComparer.Ordinal, JsonData.Copy(set).Comparer);
    }
}
