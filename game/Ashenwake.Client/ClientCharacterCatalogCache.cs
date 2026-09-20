using System.Security;
using Ashenwake.Core.Endgame;

namespace Ashenwake.Client;

public sealed record CharacterCatalogScan(CharacterSlot[] Slots, bool LimitReached, int ValidatedArchives);

public sealed partial class ClientCharacterCatalog
{
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly Dictionary<string, CachedSlot> _previewCache = new(StringComparer.Ordinal);
    private sealed record FileStamp(bool Exists, long Length, long Modified, long Created, FileAttributes Attributes, string? Link);
    private sealed record ArchiveStamp(FileStamp Primary, FileStamp Backup, FileStamp Profile, FileStamp ProfileBackup);
    private sealed record CachedSlot(ArchiveStamp Stamp, CharacterSlot Slot);

    /// <summary>One bounded worker refreshes advisory previews. Inspect/Load still validate the selected archive afresh.</summary>
    public async Task<CharacterCatalogScan> ScanAsync(string directory, string recent = "", CancellationToken cancellation = default)
    {
        await _scanGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ScanCached(directory, recent, cancellation), cancellation).ConfigureAwait(false);
        }
        finally { _scanGate.Release(); }
    }

    private CharacterCatalogScan ScanCached(string directory, string recent, CancellationToken cancellation)
    {
        directory = Path.GetFullPath(directory);
        var (names, limit) = SlotNames(directory, cancellation);
        if (IsValidFilename(recent) && !names.Contains(recent) &&
            (File.Exists(Path.Combine(directory, recent)) || File.Exists(Path.Combine(directory, recent + ".bak"))))
        {
            if (names.Count == MaximumSlots) names.Remove(names.Last());
            names.Add(recent);
        }
        // Keep exactly this bounded window before adding changed previews. Evicting
        // an arbitrary key while scanning could discard an unchanged retained slot.
        var retained = names.Select(name => Path.Combine(directory, name)).ToHashSet(StringComparer.Ordinal);
        foreach (string key in _previewCache.Keys.Where(key => !retained.Contains(key)).ToArray()) _previewCache.Remove(key);
        var slots = new List<CharacterSlot>(); int validated = 0;
        foreach (string name in names)
        {
            cancellation.ThrowIfCancellationRequested();
            string path = Path.Combine(directory, name);
            var stamp = Stamp(path);
            if (stamp is not null && _previewCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                slots.Add(cached.Slot);
            else
            {
                var slot = Inspect(directory, name); validated++;
                cancellation.ThrowIfCancellationRequested();
                // Do not cache a preview if a save/profile was replaced during validation.
                if (stamp is not null && stamp == Stamp(path))
                {
                    _previewCache[path] = new(stamp, slot);
                }
                else _previewCache.Remove(path);
                slots.Add(slot);
            }
        }
        return new(SortSlots(slots), limit, validated);
    }

    private static ArchiveStamp? Stamp(string path)
    {
        try
        {
            string profile = EndgameRuntimeSaveStore.ProfilePath(path);
            return new(FileStatus(path), FileStatus(path + ".bak"), FileStatus(profile), FileStatus(profile + ".bak"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return null; }
    }

    private static FileStamp FileStatus(string path)
    {
        var file = new FileInfo(path);
        return new(file.Exists, file.Exists ? file.Length : 0, file.LastWriteTimeUtc.Ticks,
            file.CreationTimeUtc.Ticks, file.Attributes, file.LinkTarget);
    }
}
