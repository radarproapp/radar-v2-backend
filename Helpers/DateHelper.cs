namespace RadarV2.Helpers;

public static class DateHelper
{
    public static string Humanize(this DateTime dt)
    {
        var diff = DateTime.UtcNow - dt.ToUniversalTime();
        return diff.TotalMinutes switch
        {
            < 2    => "just now",
            < 60   => $"{(int)diff.TotalMinutes}m ago",
            < 1440 => $"{(int)diff.TotalHours}h ago",
            < 2880 => "yesterday",
            _      => $"{(int)diff.TotalDays}d ago"
        };
    }
}
