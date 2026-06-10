using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using ECommons.GameHelpers;
using PartyPlanner.IPC;
using PartyPlanner.Models;
using System;
using System.Numerics;

namespace PartyPlanner.Helpers;

/// <summary>
/// Handles rendering of individual event rows in the UI.
/// </summary>
public static class EventRenderer
{
    // Static color constants to avoid allocations
    private static readonly Vector4 ColorGreen = new(0.0742f, 0.530f, 0.150f, 1.0f);
    private static readonly Vector4 ColorPurple = new(0.668f, 0.146f, 0.910f, 1.0f);
    private static readonly Vector4 ColorBlue = new(0.156f, 0.665f, 0.920f, 1.0f);
    private static readonly Vector4 ColorLightGreen = new(0.0888f, 0.740f, 0.176f, 1.0f);
    private static readonly Vector4 ColorGray = new(0.55f, 0.55f, 0.55f, 1.0f);
    private static readonly Vector4 ColorSectionHeader = new(0.90f, 0.75f, 0.35f, 1.0f);

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
            ImGui.TextColored(hasTitle ? ColorPurple : ColorGray, hasTitle ? ev.Title : "(No title)");
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
            ImGui.TextColored(ColorGreen, cached.Location);
            ImGui.Text("Click to copy");
            ImGui.EndTooltip();
        }

        DrawTravelButton(ev);

        ImGui.Text(string.Format("Attendees: {0}", ev.AttendeeCount));

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

        // Full time range
        ImGui.TextColored(ColorBlue,
           string.Format("From {0} to {1}", cached.StartsAtLocal, cached.EndsAtLocal));

        // Tags
        ImGui.TextColored(ColorLightGreen,
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
                    var isImage = ext is ".webp" or ".png" or ".jpg" or ".jpeg" or ".gif";

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
                        var kind = ext is ".mp4" or ".webm" or ".mov" ? "Video" : "File";
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

    /// <summary>
    /// Renders an optional "Travel to &lt;world&gt;" button when the event's venue is on a
    /// different world than the player. Uses the Lifestream plugin via IPC; the button is
    /// disabled (with a hint) when Lifestream isn't installed, so it is never a hard dependency.
    /// </summary>
    private static void DrawTravelButton(EventType ev)
    {
        var server = ev.LocationData?.Server;
        if (server == null || server.Id == 0)
            return;

        if (!Player.Available || server.Id == Player.Object!.CurrentWorld.RowId)
            return;

        var available = LifestreamIPC.Installed;
        using (ImRaii.Disabled(!available))
        {
            if (ImGui.SmallButton($"Travel to {server.Name}"))
            {
                if (!Plugin.Lifestream.IsBusy())
                    Plugin.Lifestream.ChangeWorldById((uint)server.Id);
            }
        }

        if (!available && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Install the Lifestream plugin to travel to this world.");
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
                ImGui.TextColored(ColorSectionHeader, line);
            }
            else
            {
                ImGui.TextWrapped(line);
            }
        }
    }
}
