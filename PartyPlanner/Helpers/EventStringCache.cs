using Humanizer;
using PartyPlanner.Models;
using System;
using System.Collections.Generic;

namespace PartyPlanner.Helpers;

/// <summary>
/// Caches formatted display strings for events.
/// Refreshes every 30 seconds to update humanized times.
/// </summary>
public class EventStringCache
{
    private readonly Dictionary<int, CachedEventStrings> cache = [];
    private DateTime lastCacheUpdate = DateTime.Now;
    private string cachedLastUpdateString = string.Empty;

    /// <summary>
    /// Gets cached strings for an event, computing them if not cached or if cache expired.
    /// </summary>
    public CachedEventStrings GetOrCompute(EventType ev)
    {
        var shouldRefreshCache = (DateTime.Now - lastCacheUpdate).TotalSeconds > 30;

        if (shouldRefreshCache || !cache.TryGetValue(ev.Id, out var cached))
        {
            if (shouldRefreshCache)
            {
                cache.Clear();
                lastCacheUpdate = DateTime.Now;
                cachedLastUpdateString = string.Empty;
            }

            var now = DateTime.UtcNow;
            var minutesToStart = (ev.StartsAt - now).TotalMinutes;

            cached = new CachedEventStrings
            {
                StartsAtHumanized = ev.StartsAt.Humanize(),
                EndsAtHumanized = ev.EndsAt.Humanize(),
                StartsAtLocal = FormatLocal(ev.StartsAt),
                EndsAtLocal = FormatLocal(ev.EndsAt),
                FormattedTags = string.Join(", ", ev.Tags),
                Location = ev.LocationData?.Server != null
                    ? string.Format("[{0}] {1}", ev.LocationData.Server.Name, ev.Location)
                    : ev.Location,
                IsLive = ev.StartsAt <= now && ev.EndsAt >= now,
                StartsSoonLabel = minutesToStart > 0 && minutesToStart <= 60
                    ? string.Format("Starts in {0} min", Math.Max(1, (int)Math.Round(minutesToStart)))
                    : string.Empty,
            };
            cache[ev.Id] = cached;
        }

        return cached;
    }

    /// <summary>
    /// Absolute local time. The day of the week is always included; the clock style follows
    /// <see cref="Configuration.TimeFormat"/>.
    /// </summary>
    private static string FormatLocal(DateTime utc)
    {
        var local = utc.ToLocalTime();
        return Plugin.Config.TimeFormat switch
        {
            TimeFormat.TwentyFourHour => local.ToString("ddd d MMM HH:mm"),
            TimeFormat.TwelveHour     => local.ToString("ddd d MMM h:mm tt"),
            _                         => local.ToString(),
        };
    }

    /// <summary>
    /// Gets the "Updated X ago" string, refreshing it every 30 seconds.
    /// </summary>
    public string GetLastUpdateString(DateTime lastUpdate)
    {
        if ((DateTime.Now - lastCacheUpdate).TotalSeconds > 30 || string.IsNullOrEmpty(cachedLastUpdateString))
        {
            cachedLastUpdateString = string.Format("Updated {0}", lastUpdate.Humanize());
        }
        return cachedLastUpdateString;
    }

    /// <summary>
    /// Clears all cached strings.
    /// </summary>
    public void Clear()
    {
        cache.Clear();
        cachedLastUpdateString = string.Empty;
        lastCacheUpdate = DateTime.Now;
    }
}
