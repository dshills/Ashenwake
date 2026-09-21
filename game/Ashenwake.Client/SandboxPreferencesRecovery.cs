using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private const int MaximumPreferencesBytes = 64 * 1024;
    private string _settingsRecoveryNotice = "";

    private string ResolveReleasePreferencesPath()
        => Argument("--preferences=") ?? (Argument("--output=") is not null || OS.GetCmdlineUserArgs().Any(a => a.EndsWith("-smoke", StringComparison.Ordinal))
            ? Path.Combine(_output, "settings.json") : ProjectSettings.GlobalizePath("user://sandbox-settings.json"));

    private Preferences ReadValidatedPreferences(string path)
    {
        if (new FileInfo(path).Length > MaximumPreferencesBytes) throw new InvalidDataException("Settings exceed the size limit.");
        string json = File.ReadAllText(path);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) throw new InvalidDataException("Settings must be an object.");
        var preferences = JsonData.Read<Preferences>(json);
        if (preferences.Keys is null || preferences.Keys.Count > 64 || preferences.MinimumLootRarity is < 0 or > 5 ||
            preferences.Keys.Any(p => p.Key is null || p.Key.Length > 64 || !Enum.IsDefined((Key)p.Value) || (Key)p.Value == Key.None))
            throw new InvalidDataException("Settings contain an unsupported value.");
        if (new[] { preferences.MasterVolume, preferences.MusicVolume, preferences.EffectsVolume, preferences.InterfaceVolume }
            .Any(value => !float.IsFinite(value) || value is < 0 or > 1))
            throw new InvalidDataException("Settings contain an unsupported audio volume.");
        var effectiveKeys = new Dictionary<string, Key>(DefaultKeys);
        foreach (var pair in preferences.Keys) if (effectiveKeys.ContainsKey(pair.Key)) effectiveKeys[pair.Key] = (Key)pair.Value;
        if (effectiveKeys.Values.Distinct().Count() != effectiveKeys.Count || effectiveKeys.Values.Any(key => !CanBindSettingsKey(key)))
            throw new InvalidDataException("Settings contain conflicting or reserved key bindings.");
        return preferences with { GraphicsQuality = preferences.GraphicsQuality == "Performance" ? "Performance" : "High" };
    }

    private void LoadReleasePreferences()
    {
        Preferences? preferences = null;
        foreach (string path in new[] { _preferencesPath, _preferencesPath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try { preferences = ReadValidatedPreferences(path); if (path.EndsWith(".bak", StringComparison.Ordinal)) _settingsRecoveryNotice = "Settings recovered from the previous valid copy."; break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            { _settingsRecoveryNotice = "Settings could not be read. Safe defaults are active; existing files are preserved."; }
        }
        if (preferences is null) { ApplySettingsAudio(); return; }
        _masterVolume = preferences.MasterVolume; _musicVolume = preferences.MusicVolume;
        _effectsVolume = preferences.EffectsVolume; _interfaceVolume = preferences.InterfaceVolume;
        ApplySettingsAudio();
        _reduceEffects = preferences.ReducedEffects; _reduceShake = preferences.ReducedShake;
        _graphicsQuality = preferences.GraphicsQuality;
        _minimumLootRarity = preferences.MinimumLootRarity; _compatibleLootOnly = preferences.CompatibleLootOnly;
        foreach (var pair in preferences.Keys) if (_keys.ContainsKey(pair.Key)) SetKey(pair.Key, (Key)pair.Value);
    }

    private void SaveReleasePreferences()
    {
        try
        {
            if (File.Exists(_preferencesPath))
            {
                try { ReadValidatedPreferences(_preferencesPath); AtomicFile.Write(_preferencesPath + ".bak", File.ReadAllText(_preferencesPath)); }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException) { }
            }
            AtomicFile.Write(_preferencesPath, JsonData.Write(new Preferences(_reduceEffects, _reduceShake,
                _keys.ToDictionary(p => p.Key, p => (long)p.Value), _minimumLootRarity, _compatibleLootOnly,
                _masterVolume, _musicVolume, _effectsVolume, _interfaceVolume, _graphicsQuality)));
            _settingsSaveFailed = false; SettingsNotice("Settings saved on this device.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _settingsSaveFailed = true; ReleaseStatus("Settings changed for this session, but could not be saved to this device. Check the settings folder and try again."); }
    }
}
