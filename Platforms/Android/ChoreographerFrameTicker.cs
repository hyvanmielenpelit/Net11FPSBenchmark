#if ANDROID
using Android.Views;

namespace Net11FPSBenchmark;

/// <summary>
/// Android Choreographer-based frame ticker. Receives callbacks aligned to
/// the hardware vsync signal, ensuring consistent frame pacing.
/// Same implementation as GnollHack's ChoreographerFrameTicker.
/// </summary>
public class ChoreographerFrameTicker : Java.Lang.Object, Choreographer.IFrameCallback
{
    private Action<long>? _onFrame;
    private bool _running;

    public void Start(Action<long> onFrame)
    {
        if (_running)
            return;

        _running = true;
        _onFrame = onFrame;
        Choreographer.Instance?.PostFrameCallback(this);
    }

    public void Stop()
    {
        _running = false;
        Choreographer.Instance?.RemoveFrameCallback(this);
    }

    public void DoFrame(long frameTimeNanos)
    {
        if (!_running)
            return;

        _onFrame?.Invoke(frameTimeNanos);

        // Queue next frame callback
        Choreographer.Instance?.PostFrameCallback(this);
    }
}
#endif
