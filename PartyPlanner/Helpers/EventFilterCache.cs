using PartyPlanner.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PartyPlanner.Helpers;

public class EventFilterCache
{
    private readonly Dictionary<string, (List<EventType> filteredEvents, int stateHash)> cache = [];

    public List<EventType> GetFiltered(
        string dataCenterName,
        List<EventType> allEvents,
        List<string> selectedTags,
        string searchText,
        SortMode sortMode,
        TimeFilter timeFilter = TimeFilter.All,
        int minAttendees = 0,
        DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var stateHash = ComputeStateHash(selectedTags, searchText, sortMode, timeFilter, minAttendees, now);

        if (cache.TryGetValue(dataCenterName, out var cached) && cached.stateHash == stateHash)
            return cached.filteredEvents;

        IEnumerable<EventType> result = selectedTags.Count == 0
            ? allEvents
            : allEvents.Where(ev => selectedTags.All(t => ev.TagsSet.Contains(t)));

        if (!string.IsNullOrEmpty(searchText))
            result = result.Where(ev => Matches(ev, searchText));

        if (timeFilter != TimeFilter.All)
            result = result.Where(ev => InWindow(ev, timeFilter, now));

        if (minAttendees > 0)
            result = result.Where(ev => ev.AttendeeCount >= minAttendees);

        result = sortMode switch
        {
            SortMode.StartsAtDesc      => result.OrderByDescending(e => e.StartsAt),
            SortMode.EndsAtAsc         => result.OrderBy(e => e.EndsAt),
            SortMode.EndsAtDesc        => result.OrderByDescending(e => e.EndsAt),
            SortMode.AttendeeCountDesc => result.OrderByDescending(e => e.AttendeeCount),
            _                          => result.OrderBy(e => e.StartsAt),
        };

        var list = result.ToList();
        cache[dataCenterName] = (list, stateHash);
        return list;
    }

    public void Clear() => cache.Clear();

    /// <summary>
    /// Free-text match over everything a user is likely to type: the title, the description, the
    /// venue string, the world it is on, and the tags.
    /// </summary>
    private static bool Matches(EventType ev, string term)
    {
        const StringComparison cmp = StringComparison.OrdinalIgnoreCase;

        if (ev.Title.Contains(term, cmp) ||
            ev.Description.Contains(term, cmp) ||
            ev.Location.Contains(term, cmp))
            return true;

        var server = ev.LocationData?.Server?.Name;
        if (server != null && server.Contains(term, cmp))
            return true;

        foreach (var tag in ev.Tags)
            if (tag.Contains(term, cmp))
                return true;

        return false;
    }

    /// <summary>
    /// Whether an event falls inside the quick "when" window. "Today" means it starts before the
    /// end of the player's local day; all windows exclude events that have already ended.
    /// </summary>
    private static bool InWindow(EventType ev, TimeFilter filter, DateTime nowUtc)
    {
        if (ev.EndsAt < nowUtc) return false;

        return filter switch
        {
            TimeFilter.Now   => ev.StartsAt <= nowUtc,
            TimeFilter.Today => ev.StartsAt < EndOfLocalDayUtc(nowUtc),
            TimeFilter.Week  => ev.StartsAt < nowUtc.AddDays(7),
            _                => true,
        };
    }

    /// <summary>Midnight after the player's current local day, expressed in UTC.</summary>
    private static DateTime EndOfLocalDayUtc(DateTime nowUtc)
    {
        var localMidnight = DateTime.SpecifyKind(nowUtc.ToLocalTime().Date.AddDays(1), DateTimeKind.Local);
        return localMidnight.ToUniversalTime();
    }

    private static int ComputeStateHash(List<string> selectedTags, string searchText, SortMode sortMode,
        TimeFilter timeFilter, int minAttendees, DateTime nowUtc)
    {
        unchecked
        {
            int hash = 17;
            foreach (var tag in selectedTags.OrderBy(t => t))
                hash = hash * 31 + tag.GetHashCode();
            hash = hash * 31 + searchText.GetHashCode();
            hash = hash * 31 + (int)sortMode;
            hash = hash * 31 + (int)timeFilter;
            hash = hash * 31 + minAttendees;
            // Time-relative filters have to age out, so bucket "now" to the minute.
            if (timeFilter != TimeFilter.All)
                hash = hash * 31 + (int)(nowUtc.Ticks / TimeSpan.TicksPerMinute);
            return hash;
        }
    }
}
