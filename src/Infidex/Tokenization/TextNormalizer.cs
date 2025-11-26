using System.Runtime.CompilerServices;

namespace Infidex.Tokenization;

/// <summary>
/// Handles text normalization including character and string replacements.
/// </summary>
public class TextNormalizer
{
    private readonly Dictionary<string, string> stringReplacements;
    private readonly Dictionary<char, char> charReplacements;
    private readonly char[] charMap;
    private readonly bool useStandardWhitespaceNormalization;

    private static readonly Lazy<TextNormalizer> _default = new Lazy<TextNormalizer>(CreateDefaultInternal, LazyThreadSafetyMode.ExecutionAndPublication);
    
    /// <summary>
    /// When true, replacements are only applied during indexing (one-way mode)
    /// </summary>
    public bool OneWayMode { get; }
    
    public TextNormalizer(
        Dictionary<string, string>? stringReplacements = null,
        Dictionary<char, char>? charReplacements = null,
        bool oneWayMode = false)
    {
        this.stringReplacements = stringReplacements ?? new Dictionary<string, string>();
        this.charReplacements = charReplacements ?? new Dictionary<char, char>();
        OneWayMode = oneWayMode;

        // Precompute a char->char mapping table for fast replacement.
        // By default this is an identity map, with specific entries overridden
        // for any configured replacements.
        charMap = new char[char.MaxValue + 1];
        for (int i = 0; i < charMap.Length; i++)
        {
            charMap[i] = (char)i;
        }

        foreach (KeyValuePair<char, char> kvp in this.charReplacements)
        {
            charMap[kvp.Key] = kvp.Value;
        }

        // Detect the "standard" whitespace normalization pattern used by defaults:
        //  - "  " -> " "
        //  - "\t" -> " "
        //  - "\n" -> " "
        //  - "\r" -> " "
        if (this.stringReplacements.Count == 4 &&
            this.stringReplacements.TryGetValue("  ", out string? v1) && v1 == " " &&
            this.stringReplacements.TryGetValue("\t", out string? v2) && v2 == " " &&
            this.stringReplacements.TryGetValue("\n", out string? v3) && v3 == " " &&
            this.stringReplacements.TryGetValue("\r", out string? v4) && v4 == " ")
        {
            useStandardWhitespaceNormalization = true;
        }
    }
    
    /// <summary>
    /// Applies string replacements to the text
    /// </summary>
    private string ReplaceStrings(string text)
    {
        foreach (KeyValuePair<string, string> kvp in stringReplacements)
        {
            text = text.Replace(kvp.Key, kvp.Value);
        }
        return text;
    }
    
    /// <summary>
    /// Applies character replacements to the text
    /// </summary>
    private string ReplaceChars(string text)
    {
        if (string.IsNullOrEmpty(text) || charReplacements.Count == 0)
            return text;

        // Fast path: scan for the first character that would actually change.
        // If none change, we can return the original string without allocating.
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char mapped = charMap[c];
            if (mapped != c)
            {
                // At least one character changes; allocate the result string once
                // and copy/map characters into it.
                return string.Create(
                    text.Length,
                    (text, _charMap: charMap, i, mapped),
                    static (span, state) =>
                    {
                        (string src, char[] map, int index, char firstMapped) = state;

                        // Copy the unchanged prefix
                        src.AsSpan(0, index).CopyTo(span);

                        // Apply the first mapped character
                        span[index] = firstMapped;

                        // Map the remaining characters
                        for (int j = index + 1; j < span.Length; j++)
                        {
                            char ch = src[j];
                            span[j] = map[ch];
                        }
                    });
            }
        }

        // No characters needed changing
        return text;
    }
    
    /// <summary>
    /// Applies all normalization (string then char replacements)
    /// </summary>
    public string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Fast-path for the default configuration: normalize whitespace and
        // apply character replacements in a single linear scan.
        if (useStandardWhitespaceNormalization)
        {
            return NormalizeWithStandardWhitespace(text);
        }

        text = ReplaceStrings(text);
        text = ReplaceChars(text);
        return text;
    }

    private string NormalizeWithStandardWhitespace(string text)
    {
        // First pass: determine if any changes are needed and compute final length.
        bool changed = false;
        bool previousIsSpace = false;
        int outputLength = 0;

        foreach (char original in text)
        {
            char mapped = MapCharWithWhitespace(original);
            bool isSpace = mapped == ' ';

            // Collapse sequences of spaces into a single space.
            if (isSpace && previousIsSpace)
            {
                changed = true;
                continue;
            }

            // Detect any change in content:
            //  - character mapping changed (diacritics or whitespace)
            //  - character became a space from a non-space original
            if (!changed && (mapped != original || (isSpace && original != ' ')))
            {
                changed = true;
            }

            previousIsSpace = isSpace;
            outputLength++;
        }

        if (!changed)
            return text;

        // Second pass: materialize the normalized string.
        return string.Create(
            outputLength,
            (text, this),
            static (span, state) =>
            {
                (string src, TextNormalizer self) = state;

                bool previousIsSpace = false;
                int pos = 0;

                foreach (char original in src)
                {
                    char mapped = self.MapCharWithWhitespace(original);
                    bool isSpace = mapped == ' ';

                    if (isSpace && previousIsSpace)
                        continue;

                    span[pos++] = mapped;
                    previousIsSpace = isSpace;
                }
            });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char MapCharWithWhitespace(char c)
    {
        return c is '\t' or '\n' or '\r' ? ' ' : charMap[c];
    }
    
    /// <summary>
    /// Creates a default normalizer with common replacements.
    /// Includes comprehensive Latin diacritic removal for cross-language search.
    /// </summary>
    public static TextNormalizer CreateDefault()
    {
        return _default.Value;
    }

    private static TextNormalizer CreateDefaultInternal()
    {
        // Comprehensive diacritic/accent removal for Latin-based scripts
        // Covers: Czech, Polish, Slovak, Hungarian, Romanian, Turkish, Vietnamese, 
        //         Nordic, German, Spanish, French, Portuguese, Italian, etc.
        Dictionary<char, char> charReplacements = new Dictionary<char, char>
        {
            // Nordic/German (existing)
            { 'Æ', 'E' }, { 'æ', 'e' },
            { 'Ø', 'O' }, { 'ø', 'o' },
            { 'Å', 'A' }, { 'å', 'a' },
            { 'Ä', 'A' }, { 'ä', 'a' },
            { 'Ö', 'O' }, { 'ö', 'o' },
            { 'Ü', 'U' }, { 'ü', 'u' },
            { 'ß', 's' },
            
            // Czech/Slovak háčky (caron)
            { 'Š', 'S' }, { 'š', 's' },
            { 'Č', 'C' }, { 'č', 'c' },
            { 'Ř', 'R' }, { 'ř', 'r' },
            { 'Ž', 'Z' }, { 'ž', 'z' },
            { 'Ň', 'N' }, { 'ň', 'n' },
            { 'Ť', 'T' }, { 'ť', 't' },
            { 'Ď', 'D' }, { 'ď', 'd' },
            { 'Ě', 'E' }, { 'ě', 'e' },
            
            // Czech/Slovak/Polish čárky (acute accents)
            { 'Á', 'A' }, { 'á', 'a' },
            { 'É', 'E' }, { 'é', 'e' },
            { 'Í', 'I' }, { 'í', 'i' },
            { 'Ó', 'O' }, { 'ó', 'o' },
            { 'Ú', 'U' }, { 'ú', 'u' },
            { 'Ý', 'Y' }, { 'ý', 'y' },
            { 'Ů', 'U' }, { 'ů', 'u' },  // Czech kroužek
            
            // Polish specific
            { 'Ą', 'A' }, { 'ą', 'a' },
            { 'Ć', 'C' }, { 'ć', 'c' },
            { 'Ę', 'E' }, { 'ę', 'e' },
            { 'Ł', 'L' }, { 'ł', 'l' },
            { 'Ń', 'N' }, { 'ń', 'n' },
            { 'Ś', 'S' }, { 'ś', 's' },
            { 'Ź', 'Z' }, { 'ź', 'z' },
            { 'Ż', 'Z' }, { 'ż', 'z' },
            
            // Hungarian
            { 'Ő', 'O' }, { 'ő', 'o' },
            { 'Ű', 'U' }, { 'ű', 'u' },
            
            // Romanian
            { 'Ă', 'A' }, { 'ă', 'a' },
            { 'Â', 'A' }, { 'â', 'a' },
            { 'Î', 'I' }, { 'î', 'i' },
            { 'Ș', 'S' }, { 'ș', 's' },
            { 'Ț', 'T' }, { 'ț', 't' },
            
            // Turkish
            { 'Ğ', 'G' }, { 'ğ', 'g' },
            { 'İ', 'I' }, { 'ı', 'i' },
            { 'Ş', 'S' }, { 'ş', 's' },
            
            // French/Spanish/Portuguese
            { 'À', 'A' }, { 'à', 'a' },
            { 'Ç', 'C' }, { 'ç', 'c' },
            { 'È', 'E' }, { 'è', 'e' },
            { 'Ê', 'E' }, { 'ê', 'e' },
            { 'Ë', 'E' }, { 'ë', 'e' },
            { 'Ì', 'I' }, { 'ì', 'i' },
            { 'Ï', 'I' }, { 'ï', 'i' },
            { 'Ñ', 'N' }, { 'ñ', 'n' },
            { 'Ò', 'O' }, { 'ò', 'o' },
            { 'Ô', 'O' }, { 'ô', 'o' },
            { 'Õ', 'O' }, { 'õ', 'o' },
            { 'Ù', 'U' }, { 'ù', 'u' },
            { 'Û', 'U' }, { 'û', 'u' },
            { 'Ÿ', 'Y' }, { 'ÿ', 'y' },
            
            // Icelandic
            { 'Ð', 'D' }, { 'ð', 'd' },
            { 'Þ', 'T' }, { 'þ', 't' },
        };
        
        Dictionary<string, string> stringReplacements = new Dictionary<string, string>
        {
            { "  ", " " }, // Double space to single
            { "\t", " " }, // Tab to space
            { "\n", " " }, // Newline to space
            { "\r", " " }  // Carriage return to space
        };
        
        return new TextNormalizer(stringReplacements, charReplacements, oneWayMode: true);
    }
}


