using PartyPlanner.Helpers;

namespace PartyPlanner.Tests;

public class TextSanitizerTests
{
    [Theory]
    // Mathematical alphanumerics — the sets Discord/partake "font" generators use.
    [InlineData("𝐌𝐚𝐢𝐝 𝐂𝐚𝐟𝐞", "Maid Cafe")]                    // bold
    [InlineData("𝑀𝑎𝑖𝑑 𝐶𝑎𝑓𝑒", "Maid Cafe")]                    // italic
    [InlineData("𝓜𝓪𝓲𝓭 𝓒𝓪𝓯𝓮", "Maid Cafe")]                    // bold script
    [InlineData("𝕄𝕒𝕚𝕕 ℂ𝕒𝕗𝕖", "Maid Cafe")]                    // double-struck
    [InlineData("𝔐𝔞𝔦𝔡 ℭ𝔞𝔣𝔢", "Maid Cafe")]                    // fraktur
    [InlineData("𝙼𝚊𝚒𝚍 𝙲𝚊𝚏𝚎", "Maid Cafe")]                    // monospace
    [InlineData("𝗠𝗮𝗶𝗱 𝗖𝗮𝗳𝗲", "Maid Cafe")]                    // sans-serif bold
    [InlineData("𝟭𝟬𝟬 𝟴 𝗽𝗺", "100 8 pm")]                       // sans-serif bold digits
    // Other decorative sets.
    [InlineData("Ｍａｉｄ　Ｃａｆｅ", "Maid Cafe")]                    // fullwidth
    [InlineData("Ⓜⓐⓘⓓ Ⓒⓐⓕⓔ", "Maid Cafe")]                    // circled
    [InlineData("🅼🅰🅸🅳 🅲🅰🅵🅴", "MAID CAFE")]                   // negative squared
    [InlineData("🄼🄰🄸🄳", "MAID")]                                // squared
    [InlineData("ᴍᴀɪᴅ ᴄᴀꜰᴇ", "maid cafe")]                        // small caps
    [InlineData("Мaid Сafe", "Maid Cafe")]                          // Cyrillic homoglyphs
    [InlineData("Μαid Ϲafe", "Maid Cafe")]                          // Greek homoglyphs
    [InlineData("Ｍａｉｄ Ł Øøđ", "Maid L Ood")]                     // stroked letters
    public void Sanitize_FoldsFancyFontsToAscii(string input, string expected)
    {
        Assert.Equal(expected, TextSanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData("Café Münchën", "Cafe Munchen")]      // accents drop to base letters
    [InlineData("Straße", "Strasse")]                 // ß expands
    [InlineData("Æther Œuvre", "AEther OEuvre")]      // ligatures expand
    [InlineData("½ price", "1/2 price")]              // vulgar fractions
    [InlineData("Ⅷ Levinstrike", "VIII Levinstrike")] // roman numerals
    [InlineData("2ⁿᵈ floor", "2nd floor")]            // superscripts
    public void Sanitize_ExpandsCompatibilityForms(string input, string expected)
    {
        Assert.Equal(expected, TextSanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData("Don’t miss it", "Don't miss it")]
    [InlineData("“Gala” night", "\"Gala\" night")]
    [InlineData("Mist — Ward 6", "Mist - Ward 6")]
    [InlineData("Mist • Ward 6", "Mist - Ward 6")]
    [InlineData("And more…", "And more...")]
    public void Sanitize_FoldsTypographicPunctuation(string input, string expected)
    {
        Assert.Equal(expected, TextSanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData("🎉 Party Night 🎉", "Party Night")]
    [InlineData("✧･ﾟ Club ･✧", "Club")]
    [InlineData("A ✦ B", "A B")]                   // no double space where a symbol was dropped
    [InlineData("  spaced   out  ", "spaced out")] // trimmed and collapsed
    [InlineData("Zalgo p̸̢͖a̸r̸t̸y̸", "Zalgo party")]     // combining marks dropped
    public void Sanitize_DropsUnrenderableCharacters(string input, string expected)
    {
        Assert.Equal(expected, TextSanitizer.Sanitize(input));
    }

    [Fact]
    public void Sanitize_KeepsFlagEmojiOutOfTheAlphabet()
    {
        // Regional indicators are dropped, not folded — otherwise 🇯🇵 would read as "JP".
        Assert.Equal("Tokyo Meetup", TextSanitizer.Sanitize("🇯🇵 Tokyo Meetup"));
    }

    [Fact]
    public void Sanitize_KeepsLineBreaksInDescriptions()
    {
        Assert.Equal("Line one\nLine two", TextSanitizer.Sanitize("Line one\nLine two 🎉"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("日本語のみ", "")] // nothing renderable survives; caller shows "(No title)"
    [InlineData("plain ascii title", "plain ascii title")]
    public void Sanitize_HandlesEdgeCases(string? input, string expected)
    {
        Assert.Equal(expected, TextSanitizer.Sanitize(input));
    }

    [Fact]
    public void Sanitize_HandlesUnpairedSurrogates()
    {
        Assert.Equal("ab", TextSanitizer.Sanitize("a\ud800b"));
    }
}
