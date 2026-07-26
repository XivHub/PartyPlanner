namespace PartyPlanner
{
    /// <summary>Quick "when" filter applied on top of the tag and search filters.</summary>
    public enum TimeFilter
    {
        All,
        Now,
        Today,
        Week
    }

    /// <summary>How absolute event times are written out.</summary>
    public enum TimeFormat
    {
        /// <summary>Whatever the client culture's general date/time format is.</summary>
        Culture,
        TwentyFourHour,
        TwelveHour
    }
}
