using System;
using System.Collections.Generic;
using System.Text;

namespace PartyPlanner.Helpers;

/// <summary>
/// Folds "fancy font" text down to plain ASCII before non-ASCII characters get dropped.
/// <para>
/// Event titles are often written with Unicode look-alikes (𝓯𝓪𝓷𝓬𝔶 𝓼𝓬𝓻𝓲𝓹𝓽, ｆｕｌｌｗｉｄｔｈ,
/// Ⓒⓘⓡⓒⓛⓔⓓ, sᴍᴀʟʟ ᴄᴀᴘs, Cyrillic/Greek homoglyphs). The game font cannot render those, so they
/// used to be stripped outright, which left an empty title and a "(No title)" row.
/// </para>
/// <para>
/// Most decorative sets are compatibility-equivalent to ASCII, so NFKD does the heavy lifting.
/// The tables below cover the confusables NFKD does not decompose.
/// </para>
/// </summary>
public static class TextSanitizer
{
    /// <summary>Codepoint-to-ASCII map for confusables without an NFKD decomposition.</summary>
    private static readonly Dictionary<int, string> Confusables = BuildConfusables();

    /// <summary>
    /// Returns an ASCII-only version of <paramref name="input"/>, trimmed and with runs of
    /// spaces collapsed. Characters with no ASCII look-alike are dropped.
    /// </summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Fold before NFKD: normalization would rewrite some confusables into another
        // non-ASCII character (Ϲ becomes Σ) and hide them from the table.
        return StripToAscii(Decompose(FoldConfusables(input)));
    }

    /// <summary>Replaces confusables with their ASCII look-alike, leaving everything else as-is.</summary>
    private static string FoldConfusables(string input)
    {
        var result = new StringBuilder(input.Length);

        foreach (var rune in input.EnumerateRunes())
        {
            if (Confusables.TryGetValue(rune.Value, out var ascii))
                result.Append(ascii);
            else
                result.Append(rune);
        }

        return result.ToString();
    }

    /// <summary>
    /// Folds any confusable NFKD produced (½ becomes "1⁄2" with a fraction slash), drops what is
    /// left of the non-ASCII — combining marks, emojis, other scripts — then collapses runs of
    /// spaces and trims.
    /// </summary>
    private static string StripToAscii(string input)
    {
        var result = new StringBuilder(input.Length);

        foreach (var rune in input.EnumerateRunes())
        {
            if (rune.Value < 0x80)
            {
                // Keep printable ASCII plus the line breaks and tabs descriptions rely on.
                if (rune.Value >= 0x20 || rune.Value is '\n' or '\r' or '\t')
                    Append(result, (char)rune.Value);
            }
            else if (Confusables.TryGetValue(rune.Value, out var ascii))
            {
                foreach (var c in ascii)
                    Append(result, c);
            }
        }

        return result.ToString().Trim();
    }

    /// <summary>Appends <paramref name="c"/>, collapsing runs of spaces into one.</summary>
    private static void Append(StringBuilder sb, char c)
    {
        if (c == ' ' && (sb.Length == 0 || sb[^1] == ' ')) return;
        sb.Append(c);
    }

    /// <summary>
    /// NFKD-normalizes <paramref name="input"/>, mapping compatibility forms (math alphanumerics,
    /// fullwidth, circled, superscripts, ligatures) onto their ASCII base characters.
    /// Malformed input (unpaired surrogates) is passed through untouched.
    /// </summary>
    private static string Decompose(string input)
    {
        try
        {
            return input.IsNormalized(NormalizationForm.FormKD)
                ? input
                : input.Normalize(NormalizationForm.FormKD);
        }
        catch (ArgumentException)
        {
            return input;
        }
    }

    private static Dictionary<int, string> BuildConfusables()
    {
        var map = new Dictionary<int, string>(256);

        // Enclosed Alphanumeric Supplement: parenthesized, squared, negative circled and
        // negative squared capitals (🄐 🄰 🅐 🅰), each a contiguous A-Z run.
        foreach (var start in new[] { 0x1F110, 0x1F130, 0x1F150, 0x1F170 })
        {
            for (var i = 0; i < 26; i++)
                map[start + i] = ((char)('A' + i)).ToString();
        }

        // Regional indicators (U+1F1E6..U+1F1FF) are deliberately left out: folding them would
        // turn real flag emoji into letter pairs. Upside-down text is left out too, since the
        // characters are also reversed and folding produces backwards words.

        // Small capitals. Unicode has no small capital X, hence the gap.
        AddPairs(map, "ᴀʙᴄᴅᴇꜰɢʜɪᴊᴋʟᴍɴᴏᴘꞯʀꜱᴛᴜᴠᴡʏᴢ", "abcdefghijklmnopqrstuvwyz");

        // Stroked and hooked Latin letters (Ł ø đ ƭ), a staple of decorative name generators.
        AddPairs(map, "ĐĦŁØŦƁƇƊƘƤƬƵƑƗɆɄɎ", "DHLOTBCDKPTZFIEUY");
        AddPairs(map, "đħłøŧɓƈɗƙƥƭƶƒɨɇʉɏıȷƚɫɬʈʋƴſ", "dhlotbcdkptzfieuyijllltvys");

        // Cyrillic homoglyphs.
        AddPairs(map, "АВЕКМНОРСТУХЅІЈԚҮЗ", "ABEKMHOPCTYXSIJQY3");
        AddPairs(map, "аеорсухѕіјԛь", "aeopcyxsijqb");

        // Greek homoglyphs.
        AddPairs(map, "ΑΒΕΖΗΙΚΜΝΟΡΤΥΧϹ", "ABEZHIKMNOPTYXC");
        AddPairs(map, "αβεικνορτυχγϲ", "abeikvoptuxyc");

        // Multi-character expansions.
        map[0x00DF] = "ss";   // ß
        map[0x00C6] = "AE";   // Æ
        map[0x00E6] = "ae";   // æ
        map[0x0152] = "OE";   // Œ
        map[0x0153] = "oe";   // œ
        map[0x00DE] = "Th";   // Þ
        map[0x00FE] = "th";   // þ
        map[0x00D0] = "D";    // Ð
        map[0x00F0] = "d";    // ð
        map[0x2026] = "...";  // …
        map[0x00A9] = "(C)";
        map[0x00AE] = "(R)";

        // Typographic punctuation, so quotes and dashes survive instead of vanishing mid-word.
        AddPairs(map, "‘’‛′ʼ´", "''''''");
        AddPairs(map, "“”„‟″«»", "\"\"\"\"\"\"\"");
        AddPairs(map, "‐‑‒–—―−⁃•●▪·‧", "-------------");
        map[0x201A] = ",";
        map[0x2044] = "/";    // ⁄
        map[0x2215] = "/";    // ∕
        map[0x00D7] = "x";    // ×

        return map;
    }

    /// <summary>Maps each character of <paramref name="from"/> to the one at the same index in <paramref name="to"/>.</summary>
    private static void AddPairs(Dictionary<int, string> map, string from, string to)
    {
        if (from.Length != to.Length)
            throw new ArgumentException($"confusable table length mismatch: {from.Length} vs {to.Length}");

        for (var i = 0; i < from.Length; i++)
            map[from[i]] = to[i].ToString();
    }
}
