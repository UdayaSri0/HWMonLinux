namespace HwMonLinux.Core;

public static class GroupPath
{
    public static IReadOnlyList<string> From(params string[] segments) =>
        From((IEnumerable<string>)segments);

    public static IReadOnlyList<string> From(IEnumerable<string> segments) =>
        segments.Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(segment => segment.Trim())
            .ToArray();
}
