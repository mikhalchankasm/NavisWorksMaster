namespace NavisHelper.McpServer.Services;

internal static class ElapsedTimeFormatter
{
    public const long ReportThresholdMs = 60000;

    public static string Format(long elapsedMs)
    {
        if (elapsedMs < 0)
            elapsedMs = 0;

        var span = TimeSpan.FromMilliseconds(elapsedMs);
        var parts = new List<string>();

        if (span.Days > 0)
            parts.Add(span.Days + " " + PluralEn(span.Days, "day"));
        if (span.Hours > 0)
            parts.Add(span.Hours + " " + PluralEn(span.Hours, "hour"));
        if (span.Minutes > 0)
            parts.Add(span.Minutes + " " + PluralEn(span.Minutes, "minute"));

        var seconds = span.Seconds;
        if (parts.Count == 0 || seconds > 0)
            parts.Add(seconds + " " + PluralEn(seconds, "second"));

        return string.Join(" ", parts);
    }

    public static string BuildUserMessage(long elapsedMs, string prefix = "Elapsed")
    {
        return prefix + ": " + Format(elapsedMs) + ".";
    }

    private static string PluralEn(int value, string singular)
    {
        return value == 1 ? singular : singular + "s";
    }
}
