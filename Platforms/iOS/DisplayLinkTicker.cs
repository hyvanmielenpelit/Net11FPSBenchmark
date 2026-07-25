#if IOS
using CoreAnimation;
using Foundation;

namespace Net11FPSBenchmark;

/// <summary>
/// iOS CADisplayLink-based frame ticker. Receives callbacks synced to
/// the display refresh rate (typically 60Hz or 120Hz ProMotion).
/// Same implementation as GnollHack's DisplayLinkTicker.
/// </summary>
public class DisplayLinkTicker
{
    private CADisplayLink? _displayLink;
    private Action<double>? _onFrame;
    private double _lastTimestamp;

    public void Start(Action<double> onFrame)
    {
        _onFrame = onFrame;
        _lastTimestamp = 0;

        _displayLink = CADisplayLink.Create(() =>
        {
            if (_displayLink == null)
                return;

            if (_lastTimestamp == 0)
            {
                _lastTimestamp = _displayLink.Timestamp;
            }

            var deltaTime = _displayLink.Timestamp - _lastTimestamp;
            _lastTimestamp = _displayLink.Timestamp;

            _onFrame?.Invoke(deltaTime);
        });

        _displayLink?.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Default);
    }

    public void Stop()
    {
        _displayLink?.Invalidate();
        _displayLink?.Dispose();
        _displayLink = null;
    }

    public double GetRefreshRateHz()
    {
        double duration = _displayLink?.Duration ?? 0.0;
        return duration <= 0.0 ? 60.0 : 1.0 / duration;
    }
}
#endif
