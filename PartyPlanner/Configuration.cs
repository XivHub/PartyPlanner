using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace PartyPlanner
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        public string SelectedRegion { get; set; } = string.Empty;
        public string SelectedDataCenter { get; set; } = string.Empty;
        public SortMode CurrentSortMode { get; set; } = SortMode.StartsAtAsc;
        public bool HomeWorldAutoSelected { get; set; } = false;

        /// <summary>Checked tag filters per data center, so they survive a reload and a restart.</summary>
        public Dictionary<string, HashSet<string>> SelectedTagsByDc { get; set; } = [];

        public TimeFilter CurrentTimeFilter { get; set; } = TimeFilter.All;

        /// <summary>Minutes between automatic refreshes while the window is open. 0 disables it.</summary>
        public int AutoRefreshMinutes { get; set; } = 5;

        /// <summary>How far ahead events are fetched and shown.</summary>
        public int EventHorizonDays { get; set; } = 30;

        /// <summary>Hide events with fewer attendees than this.</summary>
        public int MinAttendees { get; set; } = 0;

        public TimeFormat TimeFormat { get; set; } = TimeFormat.Culture;

        /// <summary>Show the "starts in 2 hours" line next to the absolute times.</summary>
        public bool ShowRelativeTimes { get; set; } = true;

        public bool ShowAttachmentImages { get; set; } = true;

        public bool ShowTravelButton { get; set; } = true;

        [NonSerialized]
        public bool SelectedRegionSet = false;
        [NonSerialized]
        public bool SelectedDataCenterSet = false;

        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface!.SavePluginConfig(this);
        }
    }
}
