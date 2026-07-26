using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using PartyPlanner.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;


namespace PartyPlanner.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly PartyVerseApi partyVerseApi;
    // All the events
    private readonly List<Models.EventType> partyVerseEvents = new(50);
    private readonly Dictionary<string, List<Models.EventType>> eventsByDc = [];
    // Tag names offered per data center. Read-only data; what the user ticked lives in Configuration.
    private readonly Dictionary<string, SortedSet<string>> tagsByDc = [];
    private DateTime lastUpdate = DateTime.Now;
    private string? displayError = null;
    private Configuration Configuration { get; init; }

    // Caches for string formatting, event filtering, and attachment images
    private readonly EventStringCache eventStringCache = new();
    private readonly EventFilterCache eventFilterCache = new();
    private readonly AttachmentImageCache attachmentImageCache = new(Plugin.TextureProvider);

    private CancellationTokenSource _cts = new();
    private readonly object _dataLock = new();

    private readonly Dictionary<string, string> _searchByDc = [];

    private static readonly string[] SortLabels = ["Starts (earliest)", "Starts (latest)", "Ends (earliest)", "Ends (latest)", "Most attendees"];

    private bool _isLoading = false;
    private string _loadingStatus = string.Empty;
    private int _refreshInFlight;

    public MainWindow(Configuration configuration) : base("PartyPlanner", ImGuiWindowFlags.None)
    {
        partyVerseApi = new PartyVerseApi();
        this.Configuration = configuration;

        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(1000, 500);

        try
        {
            System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(Plugin.PluginInterface.AssemblyLocation.FullName);
            string version = fvi.FileVersion!;
            WindowName = string.Format("PartyPlanner v{0}", version);
        }
        catch (Exception e)
        {
            Plugin.Logger.Error(e, "error loading assembly");
        }

        Task.Run(() => UpdateEvents(_cts.Token));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        partyVerseApi.Dispose();
        attachmentImageCache.Dispose();
    }

    public async Task UpdateEvents(CancellationToken ct = default)
    {
        // One refresh at a time: the reload button, OnOpen and the initial load can all fire.
        if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) == 1)
            return;

        try
        {
            await FetchEvents(ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInFlight, 0);
        }
    }

    private async Task FetchEvents(CancellationToken ct)
    {
        lock (_dataLock)
        {
            displayError = null;
            _isLoading = true;
            _loadingStatus = "Fetching active events...";
        }

        var localEvents = new List<Models.EventType>(50);
        var localByDc = new Dictionary<string, List<Models.EventType>>();
        var localTagsByDc = new Dictionary<string, SortedSet<string>>();

        int page = 0;
        bool queryMore = true;

        try
        {
            while (queryMore)
            {
                ct.ThrowIfCancellationRequested();
                lock (_dataLock) { _loadingStatus = string.Format("Fetching active events (page {0})...", page + 1); }
                var newEvents = await partyVerseApi.GetActiveEvents(page);
                queryMore = newEvents.Count >= 100;
                localEvents.AddRange(newEvents);
                page += 1;
            }

            page = 0;
            queryMore = true;
            while (queryMore)
            {
                ct.ThrowIfCancellationRequested();
                lock (_dataLock) { _loadingStatus = string.Format("Fetching events (page {0})...", page + 1); }
                var newEvents = await partyVerseApi.GetEvents(page);
                queryMore = newEvents.Count >= 100;
                localEvents.AddRange(newEvents);
                page += 1;
            }

            var localLastUpdate = DateTime.Now;

            localEvents = localEvents.DistinctBy(e => e.Id).ToList();

            var now = DateTime.UtcNow;
            var cutoff = now.AddMonths(1);

            // Drop events that have already ended or start more than 1 month away.
            localEvents = localEvents
                .Where(e => e.EndsAt >= now && e.StartsAt <= cutoff)
                .ToList();

            // Per venue (server + location), keep only the nearest upcoming/active event.
            localEvents = localEvents
                .GroupBy(e => (
                    dc: e.LocationData?.DataCenter?.Name ?? string.Empty,
                    server: e.LocationData?.Server?.Name ?? string.Empty,
                    loc: e.Location
                ))
                .Select(g => g.OrderBy(e => e.StartsAt).First())
                .ToList();

            foreach (var ev in localEvents)
            {
                if (ev.LocationData == null || ev.LocationData.DataCenter == null) continue;
                var key = ev.LocationData.DataCenter.Name;

                if (!localByDc.TryGetValue(key, out var dcEvents))
                    localByDc[key] = dcEvents = [];
                dcEvents.Add(ev);

                if (!localTagsByDc.TryGetValue(key, out var dcTags))
                    localTagsByDc[key] = dcTags = [];
                foreach (var tag in ev.Tags)
                    dcTags.Add(tag);
            }

            eventFilterCache.Clear();
            eventStringCache.Clear();

            lock (_dataLock)
            {
                partyVerseEvents.Clear();
                partyVerseEvents.AddRange(localEvents);
                eventsByDc.Clear();
                foreach (var kvp in localByDc) eventsByDc[kvp.Key] = kvp.Value;
                tagsByDc.Clear();
                foreach (var kvp in localTagsByDc) tagsByDc[kvp.Key] = kvp.Value;
                lastUpdate = localLastUpdate;
            }

            lock (_dataLock) { _isLoading = false; _loadingStatus = string.Empty; }
        }
        catch (OperationCanceledException)
        {
            lock (_dataLock) { _isLoading = false; _loadingStatus = string.Empty; }
        }
        catch (Exception ex)
        {
            Plugin.Logger.Error(ex, "error getting events");
            lock (_dataLock)
            {
                _isLoading = false;
                _loadingStatus = string.Empty;
                displayError = string.Format("Error getting events: {0}, {1}", ex.Message, ex.InnerException);
            }
        }
    }

    public override void OnOpen()
    {
        base.OnOpen();
        TryAutoSelectHomeWorld();
        if (lastUpdate.AddMinutes(5).CompareTo(DateTime.Now) <= 0)
        {
            Task.Run(() => UpdateEvents(_cts.Token));
        }
    }

    private void TryAutoSelectHomeWorld()
    {
        if (this.Configuration.HomeWorldAutoSelected) return;
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null)
        {
            Plugin.Logger.Debug("TryAutoSelectHomeWorld: LocalPlayer is null, skipping");
            return;
        }
        var homeWorldId = (int)localPlayer.HomeWorld.RowId;
        Plugin.Logger.Debug("TryAutoSelectHomeWorld: homeWorldId={0}", homeWorldId);
        if (partyVerseApi.TryGetRegionForWorld(homeWorldId, out var regionIdx, out var dcName))
        {
            Plugin.Logger.Debug("TryAutoSelectHomeWorld: resolved region={0} dc={1}", PartyVerseApi.RegionList[regionIdx], dcName);
            this.Configuration.SelectedRegion = PartyVerseApi.RegionList[regionIdx];
            this.Configuration.SelectedDataCenter = dcName;
            this.Configuration.SelectedRegionSet = false;
            this.Configuration.SelectedDataCenterSet = false;
            this.Configuration.HomeWorldAutoSelected = true;
            this.Configuration.Save();
        }
        else
        {
            Plugin.Logger.Warning("TryAutoSelectHomeWorld: could not resolve worldId={0} to a known region/DC", homeWorldId);
        }
    }


    /// <summary>Ticked tags for a data center, created on first use. Persisted in the config.</summary>
    private HashSet<string> SelectedTagsFor(string dataCenterName)
    {
        if (!this.Configuration.SelectedTagsByDc.TryGetValue(dataCenterName, out var selected))
            this.Configuration.SelectedTagsByDc[dataCenterName] = selected = [];
        return selected;
    }

    public override void Draw()
    {
        attachmentImageCache.BeginFrame();

        bool isLoading;
        string loadingStatus;
        string? localError;
        int evCount;
        HashSet<string> dcsWithEvents;
        lock (_dataLock) { localError = displayError; evCount = partyVerseEvents.Count; isLoading = _isLoading; loadingStatus = _loadingStatus; dcsWithEvents = eventsByDc.Keys.ToHashSet(); }

        if (isLoading) ImGui.BeginDisabled();
        if (ImGui.Button("Reload Events"))
            Task.Run(() => UpdateEvents(_cts.Token));
        if (isLoading) ImGui.EndDisabled();
        ImGui.SameLine();

        ImGui.Text(eventStringCache.GetLastUpdateString(lastUpdate));
        if (isLoading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(loadingStatus);
        }

        ImGui.Spacing();

        if (localError != null)
        {
            ImGui.Text(localError);
        }
        else if (evCount == 0)
        {
            ImGui.Text(isLoading ? loadingStatus : "No events found.");
        }
        else
        {
            if (ImGui.BeginTabBar("region_tab_bar"))
            {
                for (var location = 1; location < PartyVerseApi.RegionList.Count; location++)
                {
                    var regionName = PartyVerseApi.RegionList[location];

                    var regionHasEvents = partyVerseApi.DataCenters.Values
                        .Any(dc => dc.Region == location && dcsWithEvents.Contains(dc.Name));
                    if (!regionHasEvents) continue;

                    if (this.Configuration.SelectedRegion.IsNullOrEmpty())
                    {
                        this.Configuration.SelectedRegion = regionName;
                    }

                    var open = this.Configuration.SelectedRegion.Equals(regionName);
                    var true_val = true;

                    var flags = ImGuiTabItemFlags.None;

                    if (!this.Configuration.SelectedRegionSet && open)
                    {
                        flags |= ImGuiTabItemFlags.SetSelected;
                        this.Configuration.SelectedRegionSet = true;
                    }

                    if (ImGui.BeginTabItem(regionName, ref true_val, flags))
                    {
                        if (this.Configuration.SelectedRegion != regionName)
                        {
                            this.Configuration.SelectedRegion = regionName;
                            this.Configuration.Save();
                        }
                        if (ImGui.BeginTabBar("datacenters_tab_bar"))
                        {
                            foreach (var dataCenter in this.partyVerseApi.DataCenters)
                            {
                                if (dataCenter.Value.Region == location && dcsWithEvents.Contains(dataCenter.Value.Name))
                                {
                                    DrawDataCenter(dataCenter.Value);
                                }
                            }
                            ImGui.EndTabBar();
                        }
                        ImGui.EndTabItem();
                    }
                }
                ImGui.EndTabBar();
            }
        }
    }

    public void DrawDataCenter(Models.DataCenterType dataCenter)
    {
        if (this.Configuration.SelectedDataCenter.IsNullOrEmpty())
        {
            this.Configuration.SelectedDataCenter = dataCenter.Name;
        }

        var open = this.Configuration.SelectedDataCenter == dataCenter.Name;
        var true_val = true;

        var flags = ImGuiTabItemFlags.None;

        if (!this.Configuration.SelectedDataCenterSet && open)
        {
            flags |= ImGuiTabItemFlags.SetSelected;
            this.Configuration.SelectedDataCenterSet = true;
        }

        if (ImGui.BeginTabItem(dataCenter.Name, ref true_val, flags))
        {
            if (this.Configuration.SelectedDataCenter != dataCenter.Name)
            {
                this.Configuration.SelectedDataCenter = dataCenter.Name;
                this.Configuration.Save();
            }

            List<Models.EventType> events;
            List<string> tags;
            lock (_dataLock)
            {
                var rawEvents = eventsByDc.GetValueOrDefault(dataCenter.Name);
                events = rawEvents != null ? new List<Models.EventType>(rawEvents) : [];
                var rawTags = tagsByDc.GetValueOrDefault(dataCenter.Name);
                tags = rawTags != null ? [.. rawTags] : [];
            }

            if (!_searchByDc.ContainsKey(dataCenter.Name))
                _searchByDc[dataCenter.Name] = string.Empty;
            var searchText = _searchByDc[dataCenter.Name];
            ImGui.SetNextItemWidth(300);
            if (ImGui.InputText("Search##" + dataCenter.Name, ref searchText, 256))
                _searchByDc[dataCenter.Name] = searchText;

            ImGui.SameLine();
            var sortMode = (int)this.Configuration.CurrentSortMode;
            ImGui.SetNextItemWidth(160);
            if (ImGui.Combo("Sort##" + dataCenter.Name, ref sortMode, SortLabels, SortLabels.Length))
            {
                this.Configuration.CurrentSortMode = (SortMode)sortMode;
                this.Configuration.Save();
                eventFilterCache.Clear();
            }

            var selectedTags = SelectedTagsFor(dataCenter.Name);

            for (var i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (i % 8 != 0)
                    ImGui.SameLine();

                var isSelected = selectedTags.Contains(tag);
                if (ImGui.Checkbox(tag, ref isSelected))
                {
                    if (isSelected)
                        selectedTags.Add(tag);
                    else
                        selectedTags.Remove(tag);
                    this.Configuration.Save();
                }
            }

            // Filter by the ticked tags that this data center still offers, so a stale selection
            // from another data center can't silently empty the list.
            var activeTags = tags.Where(selectedTags.Contains).ToList();
            var filteredEvents = eventFilterCache.GetFiltered(dataCenter.Name, events, activeTags, searchText, this.Configuration.CurrentSortMode);

            foreach (var ev in filteredEvents)
            {
                ImGui.Separator();
                ImGui.PushID(ev.Id);
                EventRenderer.DrawEventRow(ev, eventStringCache.GetOrCompute(ev), attachmentImageCache);
                ImGui.PopID();
            }

            if (filteredEvents.Count == 0)
                ImGui.Text("No events found.");

            ImGui.EndTabItem();
        }
    }

}
