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
        SortMode sortMode)
    {
        var stateHash = ComputeStateHash(selectedTags, searchText, sortMode);

        if (cache.TryGetValue(dataCenterName, out var cached) && cached.stateHash == stateHash)
            return cached.filteredEvents;

        IEnumerable<EventType> result = selectedTags.Count == 0
            ? allEvents
            : allEvents.Where(ev => selectedTags.All(t => ev.TagsSet.Contains(t)));

        if (!string.IsNullOrEmpty(searchText))
            result = result.Where(ev => Matches(ev, searchText));

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

    private static int ComputeStateHash(List<string> selectedTags, string searchText, SortMode sortMode)
    {
        unchecked
        {
            int hash = 17;
            foreach (var tag in selectedTags.OrderBy(t => t))
                hash = hash * 31 + tag.GetHashCode();
            hash = hash * 31 + searchText.GetHashCode();
            hash = hash * 31 + (int)sortMode;
            return hash;
        }
    }
}
