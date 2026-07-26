using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using System;
using System.Numerics;

namespace PartyPlanner.Windows
{
    public sealed class ConfigWindow : Window, IDisposable
    {
        private const string RepoUrl = "https://github.com/XivHub/PartyPlanner";

        private static readonly string[] TimeFormatLabels = ["System default", "24-hour", "12-hour"];

        private readonly Configuration configuration;

        /// <summary>Called after a setting changes so cached display strings get rebuilt.</summary>
        private readonly Action onChanged;

        public ConfigWindow(Configuration configuration, Action onChanged)
            : base("PartyPlanner Config", ImGuiWindowFlags.NoCollapse)
        {
            this.configuration = configuration;
            this.onChanged = onChanged;

            SizeCondition = ImGuiCond.FirstUseEver;
            Size = new Vector2(420, 380);
        }

        public void Dispose()
        {
        }

        public override void Draw()
        {
            var dirty = false;

            ImGui.TextDisabled("Events");

            var horizon = this.configuration.EventHorizonDays;
            if (ImGui.SliderInt("Show events up to (days)", ref horizon, 1, 90))
            {
                this.configuration.EventHorizonDays = horizon;
            }
            // Commit once the drag ends, not on every intermediate value.
            if (ImGui.IsItemDeactivatedAfterEdit())
                dirty = true;
            Hint("Events starting further ahead than this are not fetched.");

            var minAttendees = this.configuration.MinAttendees;
            if (ImGui.SliderInt("Minimum attendees", ref minAttendees, 0, 100))
            {
                this.configuration.MinAttendees = minAttendees;
            }
            // Commit once the drag ends, not on every intermediate value.
            if (ImGui.IsItemDeactivatedAfterEdit())
                dirty = true;
            Hint("Hides smaller events. 0 shows everything.");

            var refresh = this.configuration.AutoRefreshMinutes;
            if (ImGui.SliderInt("Auto-refresh (minutes)", ref refresh, 0, 60))
            {
                this.configuration.AutoRefreshMinutes = refresh;
            }
            // Commit once the drag ends, not on every intermediate value.
            if (ImGui.IsItemDeactivatedAfterEdit())
                dirty = true;
            Hint("Refreshes while the window is open. 0 disables it.");

            ImGui.Spacing();
            ImGui.TextDisabled("Display");

            var timeFormat = (int)this.configuration.TimeFormat;
            if (ImGui.Combo("Time format", ref timeFormat, TimeFormatLabels, TimeFormatLabels.Length))
            {
                this.configuration.TimeFormat = (TimeFormat)timeFormat;
                dirty = true;
            }

            var relative = this.configuration.ShowRelativeTimes;
            if (ImGui.Checkbox("Show relative times", ref relative))
            {
                this.configuration.ShowRelativeTimes = relative;
                dirty = true;
            }
            Hint("The \"starts in 2 hours\" line.");

            var images = this.configuration.ShowAttachmentImages;
            if (ImGui.Checkbox("Show attachment images", ref images))
            {
                this.configuration.ShowAttachmentImages = images;
                dirty = true;
            }
            Hint("Off shows a link button instead of downloading the image.");

            var travel = this.configuration.ShowTravelButton;
            if (ImGui.Checkbox("Show travel button", ref travel))
            {
                this.configuration.ShowTravelButton = travel;
                dirty = true;
            }
            Hint("Needs Lifestream, plus vnavmesh to walk to the plot door.");

            ImGui.Spacing();
            ImGui.TextDisabled("Filters");

            if (ImGui.Button("Clear all tag filters"))
            {
                this.configuration.SelectedTagsByDc.Clear();
                dirty = true;
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Source code");
            if (ImGui.SmallButton(RepoUrl))
            {
                Util.OpenLink(RepoUrl);
            }

            if (dirty)
            {
                this.configuration.Save();
                this.onChanged();
            }
        }

        /// <summary>Small grey explanation under the setting it belongs to.</summary>
        private static void Hint(string text)
        {
            ImGui.Indent();
            ImGui.TextDisabled(text);
            ImGui.Unindent();
        }
    }
}
