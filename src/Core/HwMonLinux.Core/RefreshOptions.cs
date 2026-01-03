namespace HwMonLinux.Core;

/// <summary>
/// Options that control how often sensor readings are updated.
/// </summary>
public sealed class RefreshOptions
{
    public static TimeSpan MinimumInterval { get; } = TimeSpan.FromMilliseconds(500);
    public static TimeSpan MaximumInterval { get; } = TimeSpan.FromMinutes(5);

    public RefreshOptions(TimeSpan interval)
    {
        Interval = ClampInterval(interval);
    }

    public TimeSpan Interval { get; }

    public static RefreshOptions Default { get; } = new(TimeSpan.FromSeconds(2));

    private static TimeSpan ClampInterval(TimeSpan interval)
    {
        if (interval < MinimumInterval)
        {
            return MinimumInterval;
        }

        if (interval > MaximumInterval)
        {
            return MaximumInterval;
        }

        return interval;
    }
}
