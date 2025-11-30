using Infidex.Coverage;
using Infidex.Internalized.Roaring;

namespace Infidex.Scoring;

/// <summary>
/// Performs fuzzy word lookups via WordMatcher (exact, LD1, affix).
/// </summary>
internal static class WordMatcherLookup
{
    public static RoaringBitmap Execute(
        string queryText,
        WordMatcher.WordMatcher? wordMatcher,
        CoverageSetup? coverageSetup,
        char[] delimiters,
        bool enableDebugLogging)
    {
        RoaringBitmap result = RoaringBitmap.Create([]); // Empty bitmap

        if (wordMatcher == null)
        {
            if (enableDebugLogging)
                Console.WriteLine("[DEBUG] WordMatcher is null!");
            return result;
        }

        string[] queryWords = queryText.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);

        if (enableDebugLogging)
            Console.WriteLine($"[DEBUG] LookupFuzzyWords tokenized '{queryText}' into {queryWords.Length} words");

        foreach (string word in queryWords)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
                continue;

            RoaringBitmap? ids = wordMatcher.Lookup(word, filter: null);
            if (ids != null)
            {
                if (enableDebugLogging)
                    Console.WriteLine($"[DEBUG] WordMatcher Lookup('{word}'): {ids.Cardinality} exact/LD1 matches");
                
                result |= ids;
            }
            else
            {
                if (enableDebugLogging)
                    Console.WriteLine($"[DEBUG] WordMatcher Lookup('{word}'): 0 exact/LD1 matches");
            }

            if (coverageSetup != null && coverageSetup.CoverPrefixSuffix)
            {
                RoaringBitmap? affixIds = wordMatcher.LookupAffix(word, filter: null);
                if (affixIds != null)
                {
                    if (enableDebugLogging)
                        Console.WriteLine($"[DEBUG] WordMatcher LookupAffix('{word}'): {affixIds.Cardinality} affix matches");
                    
                    result |= affixIds;
                }
            }
        }

        if (enableDebugLogging)
            Console.WriteLine($"[DEBUG] WordMatcher total: {result.Cardinality} matches");

        return result;
    }
}
