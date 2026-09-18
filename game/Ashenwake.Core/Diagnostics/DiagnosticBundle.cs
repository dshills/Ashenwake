using System.Text.RegularExpressions;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Diagnostics;

public sealed record DiagnosticEvent(long Tick, string Kind, int ActorId, int TargetId, int Amount, string ContentId);
public sealed record DiagnosticBundle(int SchemaVersion, string BuildId, string ContentHash, string FailureCategory,
    string ExceptionType, long DroppedEvents, DiagnosticEvent[] RecentEvents, CombatReplay? OptInReplay, string DataPolicy);

/// <summary>A bounded, identifier-only local diagnostic ring; raw exception messages, paths, and environment values are excluded.</summary>
public sealed partial class DiagnosticBuffer
{
    public const int MaximumEvents = 256;
    public const int MaximumReplayFrames = 300;
    public const int MaximumBundleBytes = 2 * 1024 * 1024;
    private readonly Queue<DiagnosticEvent> events = [];
    private long dropped;
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)] private static partial Regex KindPattern();
    [GeneratedRegex("^(skill|fragment|enemy|item|effect|mutation|encounter|room|boss|manifestation|concordance)\\.[a-z0-9_.]{1,90}$", RegexOptions.CultureInvariant)] private static partial Regex ContentPattern();
    [GeneratedRegex("^[a-fA-F0-9]{7,64}$", RegexOptions.CultureInvariant)] private static partial Regex IdentityPattern();
    public void Record(CombatEvent value)
    {
        if (value is null || value.Tick < 0) return;
        string kind = KindPattern().IsMatch(value.Kind ?? "") ? value.Kind! : "Redacted";
        string id = value.ContentId == "" || ContentPattern().IsMatch(value.ContentId ?? "") ? value.ContentId! : "redacted";
        if (events.Count == MaximumEvents) { events.Dequeue(); dropped++; }
        events.Enqueue(new(value.Tick, kind!, value.ActorId, value.TargetId, value.Amount, id!));
    }
    public DiagnosticBundle Capture(string buildId, string contentHash, string failureCategory = "Unspecified",
        Exception? exception = null, CombatReplay? replay = null, bool includeReplay = false)
    {
        if (!IdentityPattern().IsMatch(buildId ?? "") || contentHash is not { Length: 64 } || !contentHash.All(Uri.IsHexDigit))
            throw new InvalidDataException("Diagnostics require a hexadecimal build/content identity.");
        string category = failureCategory is "Unspecified" or "Simulation" or "Save" or "Content" or "Replay" or "Presentation" ? failureCategory : "Unspecified";
        string exceptionType = exception?.GetType().Name ?? "None";
        if (!KindPattern().IsMatch(exceptionType)) exceptionType = "Exception";
        CombatReplay? selected = null;
        if (includeReplay && replay is not null)
        {
            if (replay.Frames is null || replay.Frames.Length > MaximumReplayFrames) throw new InvalidDataException("Opt-in diagnostic replay exceeds 300 frames; record a fresh bounded segment.");
            selected = JsonData.Copy(replay);
        }
        var result = new DiagnosticBundle(1, buildId!, contentHash, category, exceptionType, dropped, events.ToArray(), selected,
            includeReplay && selected is not null ? "Local only. Replay contains gameplay state and commands; included by explicit opt-in. No upload is performed." : "Local only. No paths, raw messages, environment values, profile names, or replay. No upload is performed.");
        if (System.Text.Encoding.UTF8.GetByteCount(JsonData.Write(result)) > MaximumBundleBytes) throw new InvalidDataException("Diagnostic bundle exceeds its 2 MiB limit.");
        return result;
    }
    public static void WriteLocal(string path, DiagnosticBundle bundle)
    {
        if (bundle is null || bundle.SchemaVersion != 1 || bundle.RecentEvents is null || bundle.RecentEvents.Length > MaximumEvents ||
            !IdentityPattern().IsMatch(bundle.BuildId ?? "") || bundle.ContentHash is not { Length: 64 } || !bundle.ContentHash.All(Uri.IsHexDigit) ||
            bundle.DroppedEvents < 0 || bundle.FailureCategory is not ("Unspecified" or "Simulation" or "Save" or "Content" or "Replay" or "Presentation") ||
            !KindPattern().IsMatch(bundle.ExceptionType ?? "") || bundle.RecentEvents.Any(e => e is null || e.Tick < 0 || !KindPattern().IsMatch(e.Kind ?? "") ||
                (e.ContentId is not ("" or "redacted") && !ContentPattern().IsMatch(e.ContentId ?? ""))) ||
            (bundle.OptInReplay is not null && (bundle.OptInReplay.Frames is null || bundle.OptInReplay.Frames.Length > MaximumReplayFrames)))
            throw new InvalidDataException("Invalid diagnostic bundle.");
        string json = JsonData.Write(bundle);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBundleBytes) throw new InvalidDataException("Diagnostic bundle exceeds its 2 MiB limit.");
        AtomicFile.Write(path, json);
    }
}
