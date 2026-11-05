using UnityEngine;

/// <summary>
/// Fixed-step headless clock for fast simulation. Begin() forces every frame to advance the game by a
/// constant amount (captureDeltaTime * timeScale) as fast as the CPU allows; End() restores the previous
/// values. Safe to nest and to call End() more than once. Use in try/finally:
/// <code>SimClock.Begin(); try { ... } finally { SimClock.End(); }</code>
/// </summary>
public static class SimClock
{
    public const float DefaultCaptureDeltaTime = 1f / 30f;
    public const float DefaultTimeScale = 4f;

    private static int depth;
    private static float savedCaptureDeltaTime;
    private static float savedTimeScale = 1f;
    private static int savedTargetFrameRate = -1;

    public static bool IsActive { get { return depth > 0; } }

    /// <summary>Game seconds that pass per rendered frame while the clock is active.</summary>
    public static float SimDeltaPerFrame { get { return DefaultCaptureDeltaTime * DefaultTimeScale; } }

    public static void Begin()
    {
        Begin(DefaultCaptureDeltaTime, DefaultTimeScale);
    }

    public static void Begin(float captureDeltaTime, float timeScale)
    {
        if (depth == 0)
        {
            savedCaptureDeltaTime = Time.captureDeltaTime;
            savedTimeScale = Time.timeScale;
            savedTargetFrameRate = Application.targetFrameRate;
        }
        depth++;
        Time.captureDeltaTime = captureDeltaTime;
        Time.timeScale = timeScale;
        Application.targetFrameRate = -1;
    }

    /// <summary>Undo one Begin(). The outermost End() restores the original settings. Extra calls are ignored.</summary>
    public static void End()
    {
        if (depth == 0) return;
        depth--;
        if (depth == 0) Restore();
    }

    /// <summary>Unconditionally restore the saved settings (use from a test TearDown after a failure).</summary>
    public static void ForceRestore()
    {
        if (depth == 0) return;
        depth = 0;
        Restore();
    }

    private static void Restore()
    {
        Time.captureDeltaTime = savedCaptureDeltaTime;
        Time.timeScale = savedTimeScale;
        Application.targetFrameRate = savedTargetFrameRate;
    }

    // With "Enter Play Mode without domain reload" the statics survive; do not keep a stale depth.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        depth = 0;
    }
}
