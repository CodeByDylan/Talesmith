namespace Talesmith.Runtime.Hosting;

/// <summary>Picks the display refreshes that run a frame, so frames average a minimum frame time at any refresh rate.</summary>
/// <remarks>Each frame runs on the refresh nearest its deadline, and the difference carries over to the next one.</remarks>
public sealed class RefreshCap
{
    private double _budget;

    public void Reset() => _budget = 0;

    /// <summary>Records a refresh and tells whether it runs a frame.</summary>
    /// <param name="refreshSeconds">Time since the previous refresh.</param>
    /// <param name="minimumSeconds">The shortest average time between frames; zero runs a frame on every refresh.</param>
    public bool ShouldRun(double refreshSeconds, double minimumSeconds)
    {
        if (minimumSeconds <= 0)
            return true;
        _budget += refreshSeconds;
        if (_budget + refreshSeconds / 2 < minimumSeconds)
            return false;
        _budget = Math.Clamp(_budget - minimumSeconds, -minimumSeconds, minimumSeconds);
        return true;
    }
}
