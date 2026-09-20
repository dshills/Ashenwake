namespace Ashenwake.Client;

/// <summary>Recovers a local slot selector without deleting or rewriting character archives.</summary>
internal static class ClientSaveSelection
{
    internal static (string Filename, string Notice) Read(string directory)
    {
        const string fallback = "endgame.save.json";
        string pointer = Path.Combine(directory, "current-save.txt");
        bool unavailable = false;
        try
        {
            if (File.Exists(pointer))
            {
                if (new FileInfo(pointer).Length > 256) throw new InvalidDataException();
                string filename = File.ReadAllText(pointer).Trim();
                if (Valid(filename) && (File.Exists(Path.Combine(directory, filename)) || File.Exists(Path.Combine(directory, filename + ".bak")))) return (filename, "");
                unavailable = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { unavailable = true; }
        try
        {
            if (File.Exists(Path.Combine(directory, fallback)) || File.Exists(Path.Combine(directory, fallback + ".bak")))
                return (fallback, unavailable ? "The recent character selection was unavailable. Continue loads the default saved character; all archives are preserved." : "");
            if (Directory.Exists(directory))
            {
                string? candidate = Directory.EnumerateFiles(directory, "endgame*.save.json*")
                    .Select(Path.GetFileName).Where(n => n is not null).Select(n => n!.EndsWith(".bak", StringComparison.Ordinal) ? n[..^4] : n)
                    .Where(Valid).Distinct(StringComparer.Ordinal).Take(ClientCharacterCatalog.MaximumSlots).Order(StringComparer.Ordinal).FirstOrDefault();
                if (candidate is not null) return (candidate, "Recovered a saved character selection. Continue loads " + candidate + "; all archives are preserved.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { return (fallback, "The recent character selection could not be read. The default slot remains available; existing archives have not been changed."); }
        return (fallback, unavailable ? "The recent character selection is missing or invalid. Existing archives have not been changed." : "");
    }
    private static bool Valid(string filename) => ClientCharacterCatalog.IsValidFilename(filename) && !ClientCharacterCatalog.IsEchoesFilename(filename);
}
