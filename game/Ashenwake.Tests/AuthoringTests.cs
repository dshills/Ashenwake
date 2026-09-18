using Ashenwake.Core.Authoring;
using Xunit;

namespace Ashenwake.Tests;

public sealed class AuthoringTests
{
    [Fact]
    public void NamedArgumentsAreReplacedOnceAndPluralFormsMatch()
    {
        var catalog = new TextCatalog(new(1, "en", new() { ["items"] = new("{name}: {count} items", "{name}: {count} item") }));
        Assert.Equal("{count}: 1 item", catalog.Format("items", new Dictionary<string, string> { ["name"] = "{count}" }, 1));
        Assert.Equal("Ash: 2 items", catalog.Format("items", new Dictionary<string, string> { ["name"] = "Ash" }, 2));
        Assert.Throws<ArgumentException>(() => catalog.Format("items", count: 1));
        Assert.Throws<InvalidDataException>(() => new TextCatalog(new(1, "en", new() { ["x"] = new("{count} items", "one") })));
    }
    [Fact]
    public void PseudoLocalizationPreservesTokensAndSnapshotsAreDetached()
    {
        var source = new TextDefinition(1, "en", new() { ["greet"] = new("Hello {name}") });
        var catalog = new TextCatalog(source); source.Messages.Clear();
        string text = catalog.PseudoLocalize().Format("greet", new Dictionary<string, string> { ["name"] = "Mara" });
        Assert.Contains("Mara", text); Assert.StartsWith("[", text); Assert.EndsWith("~]", text);
        Assert.Throws<InvalidDataException>(() => catalog.RequireKeys(["missing"]));
        Assert.Throws<InvalidDataException>(() => new TextCatalog(new(1, "en", new() { ["x"] = new("{oops") })));
        Assert.Throws<InvalidDataException>(() => new TextCatalog(new(1, "en", new() { ["x"] = new("oops}") })));
    }
}
