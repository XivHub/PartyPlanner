using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PartyPlanner.Helpers;

/// <summary>
/// Parsed housing address extracted from a free-text location string.
/// </summary>
public readonly record struct HousingAddress(
    string District,
    int Ward,
    int? Plot,
    int? ApartmentNumber,
    bool IsApartment,
    bool IsSubdivision);

/// <summary>
/// Parses free-text event location strings (from partake.gg) into structured housing addresses.
/// This file uses only BCL types and has no Dalamud/ECommons/game dependencies.
/// When in any doubt, Parse returns null rather than risk sending a player to the wrong plot.
/// </summary>
public static class HousingLocationParser
{
    // Alias entries: (alias, canonical). Longer/more-specific aliases first so they match
    // before shorter substrings when we do token-boundary matching.
    private static readonly (string Alias, string Canonical)[] DistrictAliases =
    [
        ("Lavender Beds", "Lavender Beds"),
        ("Lavender Bed",  "Lavender Beds"),
        ("Lavender",      "Lavender Beds"),
        ("The Goblet",    "The Goblet"),
        ("Empyreum",      "Empyreum"),
        ("Shirogane",     "Shirogane"),
        ("Goblet",        "The Goblet"),
        ("Shiro",         "Shirogane"),
        ("Empy",          "Empyreum"),
        ("Emp",           "Empyreum"),
        ("Mists",         "Mist"),
        ("Mist",          "Mist"),
        ("Lav",           "Lavender Beds"),
        ("Gob",           "The Goblet"),
        ("LB",            "Lavender Beds"),
    ];

    // Pre-compiled regex patterns for each alias (word-boundary anchored).
    private static readonly (Regex Pattern, string Canonical)[] DistrictPatterns =
        DistrictAliases
            .Select(entry => (
                new Regex(
                    @"(?<![a-zA-Z0-9])" + Regex.Escape(entry.Alias) + @"(?![a-zA-Z0-9])",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled),
                entry.Canonical))
            .ToArray();

    // Ward: "ward 6", "w6", "W 6", "W-6", "W/6", "w.6", "W06"
    // The bare-W form is anchored with \b so it doesn't match inside longer words.
    // The second alternative handles the compound W<n>P<n> form (e.g. "W8P34") where the
    // plot letter directly follows the ward digits, so the trailing \b would otherwise fail.
    private static readonly Regex WardRegex2 = new(
        @"\b(?:ward|w)\s*[-/:.=]?\s*0*(\d{1,2})\b|\bw\s*[-/:.=]?\s*0*(\d{1,2})\s*p\s*[-/:.=]?\s*0*\d{1,2}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Plot: "plot 5", "p5", "P 5", "P-5", "P/5", "p.5", "P05"
    // The bare-P form is anchored with \b. An additional alternative handles the compound
    // W<n>P<n> form where P directly follows the ward digits with no separator.
    private static readonly Regex PlotRegex = new(
        @"\b(?:plot|p)\s*[-/:.=]?\s*0*(\d{1,2})\b|\bw\s*[-/:.=]?\s*0*\d{1,2}\s*p\s*[-/:.=]?\s*0*(\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Apartment indicator (also captures the number following the keyword, if present).
    // "room" is deliberately NOT an alias — it appears in non-housing event text and would
    // mis-flag a plot as an apartment. Only "apartment"/"apt" count.
    private static readonly Regex ApartmentRegex = new(
        @"\b(?:apartment|apt)\b\s*[-/:.=]?\s*0*(\d{1,2})?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Subdivision indicator. Requires the full word; bare "sub" is too loose and the flag
    // changes the travel destination (subdivision plots differ from main-ward plots).
    private static readonly Regex SubdivisionRegex = new(
        @"\bsubdivision\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses a free-text location string into a <see cref="HousingAddress"/>.
    /// Returns null when the location is not a housing address, is ambiguous, or has
    /// out-of-range values. Never returns a result that could send a player to the wrong plot.
    /// </summary>
    public static HousingAddress? Parse(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;

        // --- District detection ---
        // Patterns are ordered most-specific-first; the first match wins. Array order is the
        // only disambiguation (real location strings contain a single district).
        string? district = null;
        foreach (var (pattern, canonical) in DistrictPatterns)
        {
            if (pattern.IsMatch(location))
            {
                district = canonical;
                break;
            }
        }

        if (district is null)
            return null;

        // --- Ward detection ---
        int? ward = null;
        var wardMatch = WardRegex2.Match(location);
        if (wardMatch.Success)
        {
            var rawWard = wardMatch.Groups[1].Success ? wardMatch.Groups[1].Value : wardMatch.Groups[2].Value;
            if (int.TryParse(rawWard, out int wardVal))
                ward = wardVal;
        }

        if (ward is null)
            return null;

        if (ward < 1 || ward > 30)
            return null;

        // --- Apartment / subdivision flags ---
        var apartmentMatch = ApartmentRegex.Match(location);
        bool isApartment = apartmentMatch.Success;
        bool isSubdivision = SubdivisionRegex.IsMatch(location);

        // --- Plot / apartment number detection ---
        int? plotOrApt = null;
        var plotMatch = PlotRegex.Match(location);
        if (plotMatch.Success)
        {
            // Group 1 = standalone "plot/p" form; group 2 = compound W<n>P<n> form.
            string rawPlot = plotMatch.Groups[1].Success
                ? plotMatch.Groups[1].Value
                : plotMatch.Groups[2].Value;
            if (int.TryParse(rawPlot, out int plotVal))
                plotOrApt = plotVal;
        }

        if (isApartment)
        {
            // The apartment number must come from the "Apartment/apt" keyword itself — never
            // from a "Plot N" token, which would send the player to the wrong place.
            int? aptNum = null;
            if (apartmentMatch.Groups[1].Success
                && int.TryParse(apartmentMatch.Groups[1].Value, out int aptNumFromKeyword))
            {
                aptNum = aptNumFromKeyword;
            }

            // Require an apartment number in valid range
            if (aptNum is null || aptNum < 1 || aptNum > 90)
                return null;

            return new HousingAddress(
                District: district,
                Ward: ward.Value,
                Plot: null,
                ApartmentNumber: aptNum,
                IsApartment: true,
                IsSubdivision: isSubdivision);
        }
        else
        {
            // Require a plot number
            if (plotOrApt is null)
                return null;
            if (plotOrApt < 1 || plotOrApt > 60)
                return null;

            return new HousingAddress(
                District: district,
                Ward: ward.Value,
                Plot: plotOrApt,
                ApartmentNumber: null,
                IsApartment: false,
                IsSubdivision: isSubdivision);
        }
    }

    /// <summary>
    /// Maps a <see cref="HousingAddress"/> and world name to the arguments expected by
    /// Lifestream's <c>BuildAddressBookEntry</c> IPC method.
    /// </summary>
    public static (
        string worldStr,
        string cityStr,
        string wardNum,
        string plotApartmentNum,
        bool isApartment,
        bool isSubdivision) ToBuildArgs(HousingAddress addr, string worldName)
    {
        string plotApartmentNum = (addr.IsApartment ? addr.ApartmentNumber : addr.Plot)
            !.Value
            .ToString();

        return (
            worldStr: worldName,
            cityStr: addr.District,
            wardNum: addr.Ward.ToString(),
            plotApartmentNum: plotApartmentNum,
            isApartment: addr.IsApartment,
            isSubdivision: addr.IsSubdivision);
    }
}
