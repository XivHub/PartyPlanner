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
