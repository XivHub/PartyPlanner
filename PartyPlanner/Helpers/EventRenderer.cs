using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using ECommons.GameHelpers;
using PartyPlanner.IPC;
using PartyPlanner.Models;
using System;
using System.Numerics;
using XivHubPluginKit.UI;

namespace PartyPlanner.Helpers;

/// <summary>
/// Handles rendering of individual event rows in the UI.
/// </summary>
public static class EventRenderer
{
    private const string AttachmentBaseUrl = "https://cdn.partake.gg/assets/";

    private const float MaxImageWidth = 500f;

    /// <summary>
    /// Renders a single event row with all details and interactive elements.
    /// </summary>
    public static void DrawEventRow(EventType ev, CachedEventStrings cached, AttachmentImageCache imageCache)
    {
        ImGui.Spacing();

        // Event title (clickable, opens partake.gg) — rendered at 1.3× size
        var hasTitle = !string.IsNullOrEmpty(ev.Title);
        using (Plugin.TitleFontHandle.Push())
            ImGui.TextColored(hasTitle ? HubStyle.Info : HubStyle.Faint, hasTitle ? ev.Title : "(No title)");
        if (ImGui.IsItemClicked())
        {
            Util.OpenLink("https://www.partake.gg/events/{0}".Format(ev.Id));
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Click to open the partake.gg website.");
            ImGui.EndTooltip();
        }

        // Location (selectable, copies to clipboard)
        ImGui.Text("Location:");
        ImGui.SameLine();
        if (ImGui.Selectable(cached.Location))
        {
            ImGui.SetClipboardText(cached.Location);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextColored(HubStyle.Info, cached.Location);
            ImGui.Text("Click to copy");
            ImGui.EndTooltip();
        }

        if (Plugin.Config.ShowTravelButton)
            DrawTravelButton(ev);

        ImGui.Text(string.Format("Attendees: {0}", ev.AttendeeCount));

        // Live / starting-soon badge
        if (cached.IsLive)
            ImGui.TextColored(HubStyle.Good, "Happening now");
        else if (cached.StartsSoonLabel.Length > 0)
            ImGui.TextColored(HubStyle.Warn, cached.StartsSoonLabel);

        if (Plugin.Config.ShowRelativeTimes)
        {
            // Start time (humanized with tooltip showing exact time)
            ImGui.Text(string.Format("Starts {0}", cached.StartsAtHumanized));
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(cached.StartsAtLocal);
            }
            ImGui.SameLine();

            // End time (humanized with tooltip showing exact time)
            ImGui.Text(string.Format("|  Ends {0}", cached.EndsAtHumanized));
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(cached.EndsAtLocal);
            }
        }

        // Full time range
        ImGui.TextColored(HubStyle.Muted,
           string.Format("From {0} to {1}", cached.StartsAtLocal, cached.EndsAtLocal));

        // Tags
        ImGui.TextColored(HubStyle.Muted,
            string.Format("Tags: {0}", cached.FormattedTags));

        // Description (collapsible)
        if (ImGui.CollapsingHeader("More details"))
        {
            DrawDescription(ev.Description);

            if (ev.Attachments.Length > 0)
            {
                ImGui.Spacing();
                for (var i = 0; i < ev.Attachments.Length; i++)
                {
                    var attachment = ev.Attachments[i];
                    var url = AttachmentBaseUrl + attachment;
                    var ext = System.IO.Path.GetExtension(attachment).ToLowerInvariant();
                    var isImageFile = ext is ".webp" or ".png" or ".jpg" or ".jpeg" or ".gif";
                    var isImage = isImageFile && Plugin.Config.ShowAttachmentImages;

                    if (isImage)
                    {
                        var tex = imageCache.TryGet(url);
                        if (tex != null)
                        {
                            var texW = (float)tex.Width;
                            var texH = (float)tex.Height;
                            var avail = ImGui.GetContentRegionAvail().X;
                            var maxW = Math.Min(MaxImageWidth, avail);
                            var scale = texW > maxW ? maxW / texW : 1f;
                            var displaySize = new Vector2(texW * scale, texH * scale);

                            ImGui.Image(tex.Handle, displaySize);
                            if (ImGui.IsItemClicked())
                                Util.OpenLink(url);
                            if (ImGui.IsItemHovered())
                            {
                                ImGui.BeginTooltip();
                                ImGui.Text("Click to open in browser");
                                ImGui.EndTooltip();
                            }
                        }
                        else
                        {
                            ImGui.TextDisabled("Loading image...");
                        }
                    }
                    else
                    {
                        var kind = ext is ".mp4" or ".webm" or ".mov" ? "Video" : isImageFile ? "Image" : "File";
                        var label = ev.Attachments.Length == 1
                            ? $"{kind} ({ext})##attach{i}"
                            : $"{kind} {i + 1} ({ext})##attach{i}";
                        if (ImGui.SmallButton(label))
                            Util.OpenLink(url);
                    }
                }
            }
        }
    }

    private enum TravelTier { None, DisabledNoLifestream, WorldOnly, DoorStep }

    private static TravelTier ResolveTier(EventType ev, out HousingAddress? addr)
    {
        addr = null;
        var server = ev.LocationData?.Server;
        if (server == null || server.Id == 0)
            return TravelTier.None;
        if (!Player.Available || server.Id == Player.Object!.CurrentWorld.RowId)
            return TravelTier.None;
        if (!LifestreamIPC.Installed)
            return TravelTier.DisabledNoLifestream;
        addr = HousingLocationParser.Parse(ev.Location);
        if (addr != null && NavmeshIPC.Installed)
            return TravelTier.DoorStep;
        return TravelTier.WorldOnly;
    }

    /// <summary>
    /// Renders an optional travel button when the event's venue is on a different world than
    /// the player. Offers door-step navigation (Lifestream + vnavmesh) when the location
    /// parses as a housing address and vnavmesh is present, world-only travel otherwise,
    /// and a disabled hint when Lifestream isn't installed at all.
    /// </summary>
    private static void DrawTravelButton(EventType ev)
    {
        var tier = ResolveTier(ev, out var addr);
        if (tier == TravelTier.None)
            return;

        var server = ev.LocationData!.Server;

        switch (tier)
        {
            case TravelTier.DoorStep:
            {
                var a = addr!.Value;
                var slotLabel = a.IsApartment ? $"Apt{a.ApartmentNumber}" : $"P{a.Plot}";
                var label = $"Travel to {a.District} W{a.Ward} {slotLabel}";
                if (ImGui.SmallButton(label))
                {
                    if (!Plugin.Lifestream.IsBusy())
                    {
                        var args = HousingLocationParser.ToBuildArgs(a, server.Name);
                        var entry = Plugin.Lifestream.BuildAddressBookEntry(
                            args.worldStr, args.cityStr, args.wardNum,
                            args.plotApartmentNum, args.isApartment, args.isSubdivision);
                        if (entry.World != 0)
                            Plugin.Lifestream.GoToHousingAddress(entry);
                        else
                            Plugin.Lifestream.ChangeWorldById((uint)server.Id);
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    var slotDesc = a.IsApartment ? $"Apartment {a.ApartmentNumber}" : $"Plot {a.Plot}";
                    ImGui.SetTooltip(
                        $"Walk to {server.Name} — {a.District}, Ward {a.Ward}, {slotDesc} (via Lifestream + vnavmesh)");
                }
                break;
            }

            case TravelTier.WorldOnly:
            {
                if (ImGui.SmallButton($"Travel to {server.Name}"))
                {
                    if (!Plugin.Lifestream.IsBusy())
                        Plugin.Lifestream.ChangeWorldById((uint)server.Id);
                }
                if (addr != null && !NavmeshIPC.Installed && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        $"Install vnavmesh to walk to the plot door. Traveling to {server.Name} only.");
                }
                break;
            }

            case TravelTier.DisabledNoLifestream:
            {
                using (ImRaii.Disabled(true))
                {
                    ImGui.SmallButton($"Travel to {server.Name}");
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Install the Lifestream plugin to travel to this world.");
                break;
            }
        }
    }

    /// <summary>
    /// Renders a plain-text description with heuristic section header detection.
    /// Lines that are short and end with ':' are rendered as colored headers.
    /// Blank lines add vertical spacing.
    /// </summary>
    private static void DrawDescription(string description)
    {
        if (string.IsNullOrEmpty(description)) return;

        var lines = description.Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                ImGui.Spacing();
            }
            else if (line.Length < 60 && line.EndsWith(':'))
            {
                ImGui.Spacing();
                ImGui.TextColored(HubStyle.Accent, line);
            }
            else
            {
                ImGui.TextWrapped(line);
            }
        }
    }
}
