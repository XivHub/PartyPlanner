namespace PartyPlanner
{
    /// <summary>
    /// Order the event list is presented in. Kept out of <see cref="Configuration"/> so the
    /// filtering helpers (and their tests) don't need the Dalamud config types.
    /// </summary>
    public enum SortMode
    {
        StartsAtAsc,
        StartsAtDesc,
        EndsAtAsc,
        EndsAtDesc,
        AttendeeCountDesc
    }
}
