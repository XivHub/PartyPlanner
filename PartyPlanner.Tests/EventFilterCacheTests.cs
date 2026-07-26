using PartyPlanner;
using PartyPlanner.Helpers;
using PartyPlanner.Models;
using System;
using System.Collections.Generic;

namespace PartyPlanner.Tests;

public class EventFilterCacheTests
{
    private static EventType MakeEvent(int id, params string[] tags) => new()
    {
        Id = id,
        Title = $"Event {id}",
        Tags = tags,
        StartsAt = DateTime.UtcNow.AddHours(1),
        EndsAt = DateTime.UtcNow.AddHours(3),
    };

    private static List<EventType> SampleEvents() =>
    [
        MakeEvent(1, "dance", "rp"),
        MakeEvent(2, "dance"),
        MakeEvent(3, "rp"),
        MakeEvent(4, "housing", "rp"),
        MakeEvent(5),
    ];

    private static List<EventType> SearchableEvents() =>
    [
        new() { Id = 1, Title = "Moonlit Gala", Description = "Live music all night.", Location = "Mist W6 P12",
                Tags = ["dance"], LocationData = new EventLocationData { Server = new EventServerData { Name = "Balmung" } } },
        new() { Id = 2, Title = "Card Night", Description = "Triple Triad tournament.", Location = "Goblet W3 P5",
                Tags = ["games"], LocationData = new EventLocationData { Server = new EventServerData { Name = "Zodiark" } } },
    ];

    private static List<EventType> Filter(EventFilterCache cache, List<EventType> events, string search) =>
        cache.GetFiltered("Chaos", events, [], search, SortMode.StartsAtAsc);

    [Fact]
    public void NoTagsReturnsAllEvents()
    {
        var cache = new EventFilterCache();
        var events = SampleEvents();
        var result = cache.GetFiltered("Chaos", events, [], string.Empty, SortMode.StartsAtAsc);
        Assert.Equal(events.Count, result.Count);
    }

    [Fact]
    public void SingleTagFiltersCorrectly()
    {
        var cache = new EventFilterCache();
        var result = cache.GetFiltered("Chaos", SampleEvents(), ["dance"], string.Empty, SortMode.StartsAtAsc);
        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Contains("dance", e.Tags));
    }

    [Fact]
    public void MultipleTagsAreAndedTogether()
    {
        var cache = new EventFilterCache();
        var result = cache.GetFiltered("Chaos", SampleEvents(), ["dance", "rp"], string.Empty, SortMode.StartsAtAsc);
        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }

    [Fact]
    public void NoMatchingTagReturnsEmpty()
    {
        var cache = new EventFilterCache();
        var result = cache.GetFiltered("Chaos", SampleEvents(), ["unknown-tag"], string.Empty, SortMode.StartsAtAsc);
        Assert.Empty(result);
    }

    [Fact]
    public void CacheHitReturnsSameList()
    {
        var cache = new EventFilterCache();
        var events = SampleEvents();
        var first = cache.GetFiltered("Chaos", events, ["rp"], string.Empty, SortMode.StartsAtAsc);
        var second = cache.GetFiltered("Chaos", events, ["rp"], string.Empty, SortMode.StartsAtAsc);
        Assert.Same(first, second);
    }

    [Fact]
    public void DifferentDcHasIsolatedCache()
    {
        var cache = new EventFilterCache();
        var events = SampleEvents();
        var chaos = cache.GetFiltered("Chaos", events, ["dance"], string.Empty, SortMode.StartsAtAsc);
        var light = cache.GetFiltered("Light", events, ["rp"], string.Empty, SortMode.StartsAtAsc);
        Assert.Equal(2, chaos.Count);
        Assert.Equal(3, light.Count);
    }

    [Fact]
    public void ClearInvalidatesCache()
    {
        var cache = new EventFilterCache();
        var events = SampleEvents();
        var before = cache.GetFiltered("Chaos", events, ["dance"], string.Empty, SortMode.StartsAtAsc);
        cache.Clear();
        var after = cache.GetFiltered("Chaos", events, ["dance"], string.Empty, SortMode.StartsAtAsc);
        Assert.NotSame(before, after);
        Assert.Equal(before.Count, after.Count);
    }

    [Fact]
    public void TagOrderDoesNotAffectCacheKey()
    {
        var cache = new EventFilterCache();
        var events = SampleEvents();
        var first = cache.GetFiltered("Chaos", events, ["rp", "dance"], string.Empty, SortMode.StartsAtAsc);
        var second = cache.GetFiltered("Chaos", events, ["dance", "rp"], string.Empty, SortMode.StartsAtAsc);
        Assert.Same(first, second);
    }

    [Theory]
    [InlineData("moonlit", 1)]   // title
    [InlineData("MOONLIT", 1)]   // case-insensitive
    [InlineData("triad", 2)]     // description
    [InlineData("goblet", 2)]    // venue string
    [InlineData("balmung", 1)]   // world name
    [InlineData("games", 2)]     // tag
    public void SearchMatchesEveryDisplayedField(string term, int expectedId)
    {
        var cache = new EventFilterCache();
        var result = Filter(cache, SearchableEvents(), term);
        Assert.Single(result);
        Assert.Equal(expectedId, result[0].Id);
    }

    [Fact]
    public void SearchWithNoMatchReturnsEmpty()
    {
        var cache = new EventFilterCache();
        Assert.Empty(Filter(cache, SearchableEvents(), "chocobo racing"));
    }

    [Fact]
    public void SearchToleratesMissingLocationData()
    {
        var cache = new EventFilterCache();
        // SampleEvents have no LocationData at all.
        Assert.Empty(Filter(cache, SampleEvents(), "balmung"));
    }

    [Fact]
    public void SortByAttendeesOrdersDescending()
    {
        var cache = new EventFilterCache();
        var events = SampleEvents();
        events[2].AttendeeCount = 50;
        events[0].AttendeeCount = 10;
        var result = cache.GetFiltered("Chaos", events, [], string.Empty, SortMode.AttendeeCountDesc);
        Assert.Equal(3, result[0].Id);
    }
}
