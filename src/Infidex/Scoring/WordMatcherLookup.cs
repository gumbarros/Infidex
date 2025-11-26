using Infidex.Coverage;

namespace Infidex.Scoring;

/// <summary>
/// Performs fuzzy word lookups via WordMatcher (exact, LD1, affix).
/// </summary>
internal static class WordMatcherLookup
{
    public static HashSet<int> Execute(
        string queryText,
        WordMatcher.WordMatcher? wordMatcher,
        CoverageSetup? coverageSetup,
        char[] delimiters,
        bool enableDebugLogging)
    {
        HashSet<int> result = [];

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

            HashSet<int> ids = wordMatcher.Lookup(word, filter: null);
            if (enableDebugLogging)
                Console.WriteLine($"[DEBUG] WordMatcher Lookup('{word}'): {ids.Count} exact/LD1 matches");

            foreach (int id in ids)
                result.Add(id);

            if (coverageSetup != null && coverageSetup.CoverPrefixSuffix)
            {
                HashSet<int> affixIds = wordMatcher.LookupAffix(word, filter: null);
                if (enableDebugLogging)
                    Console.WriteLine($"[DEBUG] WordMatcher LookupAffix('{word}'): {affixIds.Count} affix matches");

                foreach (int id in affixIds)
                    result.Add(id);
            }
        }

        if (enableDebugLogging)
            Console.WriteLine($"[DEBUG] WordMatcher total: {result.Count} matches");

        return result;
    }
}

