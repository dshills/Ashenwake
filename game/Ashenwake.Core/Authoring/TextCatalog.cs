using System.Globalization;
using System.Text;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Authoring;

public sealed record TextEntry(string Other, string? One = null);
public sealed record TextDefinition(int SchemaVersion, string Locale, SortedDictionary<string, TextEntry> Messages);

/// <summary>Small, strict message format. Named tokens and English singular/other are explicit;
/// additional languages must supply a plural rule rather than borrowing English silently.</summary>
public sealed class TextCatalog
{
    private readonly TextDefinition definition;
    public string Hash { get; }
    public static TextCatalog Parse(string json) => new(JsonData.Read<TextDefinition>(json));
    public TextCatalog(TextDefinition source)
    {
        if (source is null || source.SchemaVersion != 1 || source.Locale is not ("en" or "qps-ploc") || source.Messages is not { Count: > 0 and <= 10000 })
            throw new InvalidDataException("Text requires schema 1, supported locale en/qps-ploc, and bounded messages.");
        foreach (var (key, entry) in source.Messages)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 120 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_') || entry is null)
                throw new InvalidDataException("Invalid text key or entry.");
            var tokens = Tokens(entry.Other);
            if (entry.One is not null && !tokens.SetEquals(Tokens(entry.One))) throw new InvalidDataException("Plural forms must use the same tokens: " + key);
            if (entry.One is not null && !tokens.Contains("count")) throw new InvalidDataException("Plural forms require {count}: " + key);
        }
        var copy = JsonData.Copy(source);
        definition = copy with { Messages = new(copy.Messages, StringComparer.Ordinal) };
        Hash = JsonData.Hash(definition);
    }
    public TextDefinition Capture() => definition with { Messages = new(definition.Messages, StringComparer.Ordinal) };
    public string Format(string key, IReadOnlyDictionary<string, string>? values = null, int? count = null)
    {
        if (!definition.Messages.TryGetValue(key, out var entry)) throw new KeyNotFoundException("Missing text: " + key);
        string template = count == 1 && entry.One is not null ? entry.One : entry.Other;
        var supplied = values is null ? new Dictionary<string, string>() : new Dictionary<string, string>(values, StringComparer.Ordinal);
        if (count is not null) supplied["count"] = count.Value.ToString(CultureInfo.InvariantCulture);
        if (!Tokens(template).SetEquals(supplied.Keys) || supplied.Values.Any(v => v is null || v.Length > 4096))
            throw new ArgumentException("Text arguments must exactly match its bounded named tokens.");
        var output = new StringBuilder();
        for (int index = 0; index < template.Length; index++)
        {
            if (template[index] != '{') { output.Append(template[index]); continue; }
            int end = template.IndexOf('}', index + 1);
            output.Append(supplied[template[(index + 1)..end]]); index = end;
        }
        return output.ToString();
    }
    public TextCatalog PseudoLocalize()
    {
        static string Expand(string source)
        {
            const string plain = "aeiouAEIOU", accented = "àëïôüÀËÏÔÜ";
            var output = new StringBuilder("["); bool token = false; int visible = 0;
            foreach (char c in source)
            {
                if (c == '{') token = true;
                int i = plain.IndexOf(c);
                output.Append(!token && i >= 0 ? accented[i] : c);
                if (!token) visible++;
                if (c == '}') token = false;
            }
            return output.Append('~', Math.Max(1, visible / 3)).Append(']').ToString();
        }
        return new(new(1, "qps-ploc", new(definition.Messages.ToDictionary(p => p.Key,
            p => new TextEntry(Expand(p.Value.Other), p.Value.One is null ? null : Expand(p.Value.One))), StringComparer.Ordinal)));
    }
    public void RequireKeys(IEnumerable<string> keys)
    {
        var missing = keys.Distinct().Where(k => !definition.Messages.ContainsKey(k)).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length != 0) throw new InvalidDataException("Missing text keys: " + string.Join(", ", missing));
    }
    private static HashSet<string> Tokens(string template)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Length > 4096) throw new InvalidDataException("Text must be nonempty and at most 4096 characters.");
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] == '}') throw new InvalidDataException("Unmatched text token closing brace.");
            if (template[i] != '{') continue;
            int end = template.IndexOf('}', i + 1);
            if (end < 0) throw new InvalidDataException("Unclosed text token.");
            string token = template[(i + 1)..end];
            if (token.Length is < 1 or > 64 || !token.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) throw new InvalidDataException("Invalid named text token.");
            tokens.Add(token); i = end;
        }
        return tokens;
    }
}
