namespace Ashenwake.Core.Endgame;

public sealed partial class EndgameRuntimeSession
{
    /// <summary>Stable identity for read-only expedition navigation, independent of display names.</summary>
    public string CurrentRunContentId => State.Run?.ContentId ?? "";
}
