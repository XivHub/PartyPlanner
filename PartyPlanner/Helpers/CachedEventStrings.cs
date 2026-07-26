namespace PartyPlanner.Helpers;

/// <summary>
/// Stores pre-formatted display strings for an event.
/// </summary>
public class CachedEventStrings
{
    public string StartsAtHumanized { get; set; } = string.Empty;
    public string EndsAtHumanized { get; set; } = string.Empty;
    public string StartsAtLocal { get; set; } = string.Empty;
    public string EndsAtLocal { get; set; } = string.Empty;
    public string FormattedTags { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;

    /// <summary>Event has started and has not ended yet.</summary>
    public bool IsLive { get; set; }

    /// <summary>Set when the event starts within the hour, e.g. "Starts in 42 minutes".</summary>
    public string StartsSoonLabel { get; set; } = string.Empty;
}
