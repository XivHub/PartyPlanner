using System;
using System.IO;
using System.Linq;
using PartyPlanner.Helpers;

namespace PartyPlanner.Tests;

public class HousingLocationParserTests
{
    // ── Task 3.2 — Format-variant theory tests ──────────────────────────────

    [Theory]
    [InlineData("EU- Light - Alpha - Mist - Ward 6 - Plot 2",     "Mist",         6,  2)]
    [InlineData("Goblet / Ward 29 / Plot 35",                      "The Goblet",  29, 35)]
    [InlineData("Mist W10-P5",                                     "Mist",        10,  5)]
    [InlineData("Marilith Mist W8P34",                             "Mist",         8, 34)]
    [InlineData("Light|Odin|LB|W04|P06",                           "Lavender Beds", 4,  6)]
    [InlineData("Empyreum, W30 P52",                               "Empyreum",    30, 52)]
    [InlineData("Mist • Ward 26 • Plot 45",                        "Mist",        26, 45)]
    [InlineData("☆Goblet☆ ☆Ward 7☆ ☆Plot 5☆",                    "The Goblet",   7,  5)]
    [InlineData("Lavender Beds W29 P06",                           "Lavender Beds", 29, 6)]
    [InlineData("Emp W6 P60",                                      "Empyreum",     6, 60)]
    [InlineData("Shiro - Ward 5 - Plot 3",                         "Shirogane",    5,  3)]
    public void FormatVariants_ParseCorrectly(string location, string expectedDistrict, int expectedWard, int expectedPlot)
    {
        var result = HousingLocationParser.Parse(location);
        Assert.NotNull(result);
        Assert.Equal(expectedDistrict, result!.Value.District);
        Assert.Equal(expectedWard,     result!.Value.Ward);
        Assert.Equal(expectedPlot,     result!.Value.Plot);
    }

    // ── Task 3.3 — Apartment/subdivision test ───────────────────────────────

    [Fact]
    public void ApartmentSubdivision_ParsesAllFields()
    {
        const string location = "Shirogane - Ward 12 Subdivision Kobai Goten Apartments - Apartment 24";
        var result = HousingLocationParser.Parse(location);
        Assert.NotNull(result);
        var addr = result!.Value;
        Assert.Equal("Shirogane", addr.District);
        Assert.Equal(12,          addr.Ward);
        Assert.True(addr.IsApartment);
        Assert.True(addr.IsSubdivision);
        Assert.Equal(24,          addr.ApartmentNumber);
        Assert.Null(addr.Plot);
    }

    // ── Wrong-destination guards (regression) ───────────────────────────────

    [Fact]
    public void PlotWithRoomWord_StaysPlot_NotApartment()
    {
        // "room" must NOT flag this as an apartment, else it would travel to "apartment 30"
        // instead of plot 30 — a wrong-destination bug.
        var result = HousingLocationParser.Parse("Mist - Ward 5 - Plot 30 — hang out in the main room");
        Assert.NotNull(result);
        var addr = result!.Value;
        Assert.False(addr.IsApartment);
        Assert.Equal(30, addr.Plot);
        Assert.Null(addr.ApartmentNumber);
    }

    [Fact]
    public void ApartmentNumberComesFromKeyword_NotPlotToken()
    {
        // If both a Plot token and an Apartment keyword appear, the apartment number must come
        // from the Apartment keyword (5), never the Plot token (30).
        var result = HousingLocationParser.Parse("Mist - Ward 5 - Plot 30 - Apartment 5");
        Assert.NotNull(result);
        var addr = result!.Value;
        Assert.True(addr.IsApartment);
        Assert.Equal(5, addr.ApartmentNumber);
        Assert.Null(addr.Plot);
    }

    // ── Task 3.4 — Null-return theory tests ─────────────────────────────────

    [Theory]
    [InlineData("Costa Del Sol - Event Area")]
    [InlineData("Adress Soon !")]
    [InlineData("New Gridania | Aetheryte Plaza")]
    [InlineData("W14/P60")]
    [InlineData("Shirogane W2 Beach")]
    [InlineData("LB 3/3")]
    [InlineData("Mist - W2 - Mist Beach (X:9.9 Y:13.5)")]
    [InlineData("Mist, Ward 11, Mist Beach")]
    public void NonHousingInputs_ReturnNull(string location)
    {
        Assert.Null(HousingLocationParser.Parse(location));
    }

    // ── Task 3.5 — Round-trip field-order tests ──────────────────────────────

    [Fact]
    public void ToBuildArgs_PlotAddress_MapsCorrectSlots()
    {
        var addr = new HousingAddress(
            District: "Mist",
            Ward: 6,
            Plot: 2,
            ApartmentNumber: null,
            IsApartment: false,
            IsSubdivision: false);

        var (worldStr, cityStr, wardNum, plotApartmentNum, isApartment, isSubdivision) =
            HousingLocationParser.ToBuildArgs(addr, "Gilgamesh");

        Assert.Equal("Gilgamesh", worldStr);
        Assert.Equal("Mist",      cityStr);
        Assert.Equal("6",         wardNum);
        Assert.Equal("2",         plotApartmentNum);
        Assert.False(isApartment);
        Assert.False(isSubdivision);
    }

    [Fact]
    public void ToBuildArgs_ApartmentAddress_UsesApartmentNumberInPlotSlot()
    {
        var addr = new HousingAddress(
            District: "Shirogane",
            Ward: 12,
            Plot: null,
            ApartmentNumber: 24,
            IsApartment: true,
            IsSubdivision: true);

        var (worldStr, cityStr, wardNum, plotApartmentNum, isApartment, isSubdivision) =
            HousingLocationParser.ToBuildArgs(addr, "Gilgamesh");

        Assert.Equal("Gilgamesh", worldStr);
        Assert.Equal("Shirogane", cityStr);
        Assert.Equal("12",        wardNum);
        Assert.Equal("24",        plotApartmentNum);
        Assert.True(isApartment);
        Assert.True(isSubdivision);
    }

    // ── Task 3.6 — Fixture coverage test ────────────────────────────────────

    [Fact]
    public void FixtureParseRateMeetsThreshold()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "partake_locations_sample.txt");
        var lines = File.ReadAllLines(fixturePath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        int total   = lines.Count;
        int success = lines.Count(l => HousingLocationParser.Parse(l) is not null);
        double rate = (double)success / total;

        Console.WriteLine($"[FixtureParseRate] {success}/{total} = {rate:P1}");

        Assert.True(rate >= 0.90, $"parse rate {rate:P1} ({success}/{total} lines) is below the 90 % threshold");
    }
}
