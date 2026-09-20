using Ashenwake.Core.Coop;

namespace Ashenwake.Client;

/// <summary>Tracks authoritative frames separately from the interpolation interval between simulation ticks.</summary>
internal sealed class CoopClientSnapshots
{
    public CoopView? Current { get; private set; }
    public CoopView? Previous { get; private set; }
    public double ReceivedAt { get; private set; }
    public double InterpolationStartedAt { get; private set; }

    public bool Apply(CoopView view, bool joined, double receivedAt)
    {
        if (joined || Current is null)
        {
            Previous = null;
            InterpolationStartedAt = receivedAt;
        }
        else
        {
            if (view.Tick < Current.Tick) return false;
            if (view.Tick > Current.Tick)
            {
                Previous = Current;
                InterpolationStartedAt = receivedAt;
            }
        }
        // Admissions, disconnects and input acknowledgments can change the state
        // without advancing the tick. Applying them must not restart interpolation.
        Current = view;
        ReceivedAt = receivedAt;
        return true;
    }
}
