using Infidex.Core;
using Infidex.Coverage;
using Infidex.Indexing;
using Infidex.Tokenization;

namespace Infidex.Scoring;

/// <summary>
/// Lexical pre-screening to drop documents that don't contain any query tokens.
/// </summary>
internal static class LexicalPrescreen
{
    /// <summary>
    /// Filters TF-IDF candidates that don't contain any query tokens as substrings.
    /// Conservative implementation to avoid impacting fuzzy/typo behavior.
    /// </summary>
    public static ScoreEntry[] Apply(
        string searchText,
        ScoreEntry[] candidates,
        Tokenizer tokenizer,
        TermCollection termCollection,
        DocumentCollection documents,
        CoverageSetup? coverageSetup)
    {
        string[] queryTokens = tokenizer
            .GetWordTokensForCoverage(searchText, coverageSetup?.MinWordSize ?? 2)
            .ToArray();

        if (queryTokens.Length == 0)
            return candidates;

        // If any token is not in index (df == 0), skip pre-screening (possible typo/fuzzy case)
        foreach (string token in queryTokens)
        {
            Term? term = termCollection.GetTerm(token);
            if (term == null || term.DocumentFrequency == 0)
            {
                return candidates;
            }
        }

        List<ScoreEntry> filtered = new List<ScoreEntry>(candidates.Length);

        foreach (ScoreEntry candidate in candidates)
        {
            Document? doc = documents.GetDocumentByPublicKey(candidate.DocumentId);
            if (doc == null || doc.Deleted)
                continue;

            string text = doc.IndexedText;
            if (tokenizer.TextNormalizer != null)
            {
                text = tokenizer.TextNormalizer.Normalize(text);
            }

            bool hasAnyToken = false;

            foreach (string token in queryTokens)
            {
                if (token.Length == 0)
                    continue;

                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasAnyToken = true;
                    break;
                }
            }

            if (hasAnyToken)
            {
                filtered.Add(candidate);
            }
        }

        return filtered.Count == 0 ? candidates : filtered.ToArray();
    }
}

