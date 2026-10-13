namespace NotchHub.Demo.Infrastructure;

/// <summary>Small date helpers shared by the sample data (all demo times are relative to "now" in local time).</summary>
internal static class DemoTime
{
    /// <summary>Local midnight of the day that contains <paramref name="moment"/>, with the correct UTC offset for that date.</summary>
    public static DateTimeOffset StartOfDay(DateTimeOffset moment) => At(DateOnly.FromDateTime(moment.LocalDateTime), 0, 0);

    /// <summary>Local wall-clock time on <paramref name="date"/> as a <see cref="DateTimeOffset"/> (DST aware).</summary>
    public static DateTimeOffset At(DateOnly date, int hour, int minute)
    {
        var local = date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    /// <summary>Today's local date.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
