using Infidex.Coverage;
using Infidex.Metrics;

namespace Infidex.Scoring;

/// <summary>
/// Pure functions for calculating fusion scores combining coverage and relevancy signals.
/// </summary>
internal static class FusionScorer
{
    private const float IntentBonusPerSignal = 0.15f;
    private const int AnchorStemLength = 3;
    private const int MaxTrailingTermLengthForBonus = 2;

    /// <summary>
    /// Calculates the fusion score for a (query, document) pair using precomputed signals.
    /// Returns (score, tiebreaker) where score encodes precedence (high byte) and semantic (low byte).
    /// </summary>
    /// <remarks>
    /// This is the Lucene-style approach: all string operations happen in the coverage layer,
    /// fusion scoring only performs numeric operations on precomputed flags and bytes.
    /// </remarks>
    public static (ushort score, byte tiebreaker) Calculate(
        string queryText,
        string documentText,
        CoverageFeatures features,
        float bm25Score,
        int minStemLength,
        char[] delimiters)
    {
        // Use unfiltered query token count for fusion logic (not coverage's filtered count)
        int n = features.FusionSignals.UnfilteredQueryTokenCount > 0 
            ? features.FusionSignals.UnfilteredQueryTokenCount 
            : features.TermsCount;
        bool isSingleTerm = n <= 1;

        bool isComplete = features.TermsCount > 0 && features.TermsWithAnyMatch == features.TermsCount;
        bool isClean = features.TermsCount > 0 && features.TermsPrefixMatched == features.TermsCount;
        bool isExact = features.TermsCount > 0 && features.TermsStrictMatched == features.TermsCount;
        bool startsAtBeginning = features.FirstMatchIndex == 0;

        // Use precomputed lexical prefix-last signal
        bool lexicalPrefixLast = features.FusionSignals.LexicalPrefixLast;

        int precedingTerms = Math.Max(0, features.TermsCount - 1);
        bool coveragePrefixLast = features.TermsCount >= 1 &&
                                  features.PrecedingStrictCount == precedingTerms &&
                                  features.LastTokenHasPrefix;

        bool isPrefixLastStrong = lexicalPrefixLast && coveragePrefixLast;
        
        // Use precomputed perfect-doc signal
        bool isPerfectDoc = features.FusionSignals.IsPerfectDocLexical;

        int precedence = 0;

        // PRECEDENCE BIT STRUCTURE (highest to lowest priority):
        // Bit 7 (128): isComplete - all query terms matched
        // Bit 6 (64):  isClean - all query terms are prefix matches (not fuzzy)
        // Bit 5 (32):  EXACT PREFIX BOOST (multi-term only)
        // Bits 0-4:    Quality signals (phrase runs, tier, etc.)
        
        if (isComplete) precedence |= 128;  // Bit 7
        if (isClean && features.TermsCount > 0) precedence |= 64;  // Bit 6
        
        // EXACT PREFIX BOOST (bit 5): 
        // Guarantees for multi-term queries with typeahead:
        // - "two fo" → "Two for Joy" beats "Tea for Two" (exact prefix vs partial match)
        // - "two fo" → "Two for Joy" beats "Two Faced Killer" (clean prefix-last vs exact only)
        // 
        // Conditions (ALL must be true):
        // 1. Multi-term query (!isSingleTerm) - single-term uses different precedence rules
        // 2. Clean match (isClean) - all terms are prefix matches, not fuzzy
        // 3. Starts at beginning (startsAtBeginning) - first match at position 0
        // 4. Lexical prefix-last (lexicalPrefixLast) - all preceding exact + last is prefix
        // 5. Complete coverage (isComplete) - all query terms found
        bool isExactPrefix = !isSingleTerm && isClean && startsAtBeginning && lexicalPrefixLast && isComplete;
        
        if (isExactPrefix)
        {
            precedence |= 32;  // Bit 5: EXACT PREFIX BOOST
        }

        if (isSingleTerm)
        {
            precedence |= ComputeSingleTermPrecedence(isExact, isClean, startsAtBeginning, isComplete);
        }
        else
        {
            precedence |= ComputeMultiTermPrecedence(
                isPrefixLastStrong, lexicalPrefixLast, isPerfectDoc, features, n, startsAtBeginning, isClean);
        }

        float coverageRatio = features.TermsCount > 0
            ? (float)features.TermsWithAnyMatch / features.TermsCount
            : 0f;

        bool hasPartialCoverage = coverageRatio > 0f && coverageRatio < 1f;

        if (hasPartialCoverage && n >= 2)
        {
            // Use precomputed stem evidence signal
            bool hasStemEvidence = features.FusionSignals.HasStemEvidence;
            
            if (hasStemEvidence)
            {
                precedence |= 128;
            }
            else
            {
                // When exactly one term is unmatched, we compare how much *information*
                // is missing versus how many terms are missing. If the information loss
                // is smaller than what raw coordinate coverage suggests, we allow a
                // precedence boost to compensate.
                int unmatchedTerms = features.TermsCount - features.TermsWithAnyMatch;
                bool lastTermMatched = features.LastTokenHasPrefix ||
                                      (features.TermsCount > 0 && features.TermsWithAnyMatch == features.TermsCount);
                
                bool canBoost = (lastTermMatched || !features.LastTermIsTypeAhead) &&
                                features.TotalIdf > 0f;
                
                if (unmatchedTerms == 1 && canBoost)
                {
                    float missingInfoRatio = features.MissingIdf / features.TotalIdf;
                    float termGap = 1f - coverageRatio; // fraction of unmatched terms
                    
                    if (missingInfoRatio < termGap)
                    {
                        precedence |= 8;  // Boost to overcome phrase-run bonuses
                    }
                }
            }
        }

        float semantic = ComputeSemanticScore(
            queryText, features, isSingleTerm, bm25Score, coverageRatio);

        byte semanticByte = (byte)Math.Clamp(semantic * 255f, 0, 255);

        // Tiebreaker: prefer shorter documents (more focused matches)
        byte tiebreaker = 0;
        if (n >= 2 && documentText.Length > 0)
        {
            float focus = MathF.Min(1f, (float)queryText.Length / documentText.Length);
            tiebreaker = (byte)(focus * 255f);
        }

        return ((ushort)((precedence << 8) | semanticByte), tiebreaker);
    }

    private static int ComputeSingleTermTier(bool isExact, bool isClean, bool startsAtBeginning, bool isComplete)
    {
        if (!isComplete) return 0;

        if (startsAtBeginning)
        {
            if (isExact) return 4;
            if (isClean) return 3;
        }
        else
        {
            if (isExact) return 2;
            if (isClean) return 1;
        }

        return 0;
    }

    private static int ComputeSingleTermPrecedence(bool isExact, bool isClean, bool startsAtBeginning, bool isComplete)
    {
        int tier = ComputeSingleTermTier(isExact, isClean, startsAtBeginning, isComplete);
        return tier << 3;
    }

    private static int ComputeMultiTermTier(
        bool isPrefixLastStrong,
        bool lexicalPrefixLast,
        bool isPerfectDoc,
        bool hasAnchorWithRun)
    {
        if (isPrefixLastStrong) return 3;
        if (lexicalPrefixLast) return 2;
        if (isPerfectDoc || hasAnchorWithRun) return 1;
        return 0;
    }

    private static int ComputePhraseQualityBits(
        int suffixRun,
        int longestRun,
        int span,
        int queryTermCount,
        int coverageTermCount,
        int termsWithMatch)
    {
        int bits = 0;

        int minSuffixForStrong = Math.Max(2, Math.Min(coverageTermCount, queryTermCount) - 1);
        if (suffixRun >= minSuffixForStrong)
        {
            bits |= 8;
        }
        else if (suffixRun >= 2)
        {
            bits |= 4;
        }

        if (longestRun >= 3) bits |= 2;
        if (termsWithMatch >= 2 && span == 2) bits |= 1;

        return bits;
    }

    private static int ComputeMultiTermPrecedence(
        bool isPrefixLastStrong,
        bool lexicalPrefixLast,
        bool isPerfectDoc,
        CoverageFeatures features,
        int queryTermCount,
        bool startsAtBeginning,
        bool isClean)
    {
        // Use precomputed anchor stem signal
        bool hasAnchorWithRun = features.FusionSignals.HasAnchorStem && features.LongestPrefixRun >= 2;

        // Multi-term precedence uses bits 0-4 (values 0-31)
        // Bits 5-7 are reserved: bit 7=isComplete, bit 6=isClean, bit 5=exactPrefix
        int tier = ComputeMultiTermTier(isPrefixLastStrong, lexicalPrefixLast, isPerfectDoc, hasAnchorWithRun);
        int tierBits = tier << 2;  // Use bits 2-3 (tier is 0-3, so this gives 0, 4, 8, 12)

        int phraseBits = ComputePhraseQualityBits(
            features.SuffixPrefixRun,
            features.LongestPrefixRun,
            features.PhraseSpan,
            queryTermCount,
            features.TermsCount,
            features.TermsWithAnyMatch);

        // Phrase bits use bits 0-1 only
        phraseBits = Math.Min(phraseBits, 3);  // Clamp to 0-3 (bits 0-1)

        return tierBits | phraseBits;
    }

    // All token-level helper methods (ComputePerfectDoc, CheckStemEvidence, CheckPrefixLastMatch,
    // ComputeSingleTermLexicalSimilarity) have been moved to FusionSignalComputer in the coverage layer.
    // FusionScorer now only performs numeric operations on precomputed signals.

    private static float ComputeSemanticScore(
        string queryText,
        CoverageFeatures features,
        bool isSingleTerm,
        float bm25Score,
        float coverageRatio)
    {
        float avgCi = features.TermsCount > 0 ? features.SumCi / features.TermsCount : 0f;
        float semantic;
        
        bool hasPartialCoverage = coverageRatio is > 0f and < 1f;

        if (isSingleTerm)
        {
            // Use precomputed single-term lexical similarity
            float lexicalSim = features.FusionSignals.SingleTermLexicalSim / 255f;
            semantic = (avgCi + lexicalSim) / 2f;
        }
        else if (features.DocTokenCount == 0)
        {
            semantic = avgCi;
        }
        else
        {
            // Use IDF-weighted coverage when available and informative.
            // For partial coverage where exactly one term is unmatched, prefer IDF coverage
            // if it's higher (indicating matched terms are more informative than missing ones).
            int unmatchedTerms = features.TermsCount - features.TermsWithAnyMatch;
            bool lastTermMatched = features.LastTokenHasPrefix || 
                                  (features.TermsCount > 0 && features.TermsWithAnyMatch == features.TermsCount);
            bool canUseIdf = (lastTermMatched || !features.LastTermIsTypeAhead) && 
                            features.TotalIdf > 0f;
            
            bool useIdfCoverage = hasPartialCoverage && unmatchedTerms == 1 && canUseIdf &&
                                 features.IdfCoverage > coverageRatio;
            
            float baseCoverage = useIdfCoverage ? features.IdfCoverage : avgCi;
                
            float density = (float)features.WordHits / features.DocTokenCount;
            semantic = baseCoverage * density;
            semantic = ApplyIntentBonus(semantic, features);
            semantic = ApplyTrailingTermBonus(semantic, features);
        }

        float coverageGap = 1f - coverageRatio;

        if (hasPartialCoverage && bm25Score >= coverageGap)
        {
            semantic = coverageRatio * semantic + coverageGap * bm25Score;
        }

        return semantic;
    }

    private static float ApplyIntentBonus(
        float semantic,
        CoverageFeatures features)
    {
        if (features.TermsCount < 3)
            return semantic;

        bool hasSuffixPhrase = features.SuffixPrefixRun >= 2;

        // Use precomputed anchor stem signal
        bool hasAnchorStem = features.FusionSignals.HasAnchorStem;

        int signalCount = (hasAnchorStem ? 1 : 0) + (hasSuffixPhrase ? 1 : 0);
        if (signalCount > 0)
        {
            float bonus = IntentBonusPerSignal * signalCount;
            semantic = MathF.Min(1f, semantic + bonus);
        }

        return semantic;
    }

    private static float ApplyTrailingTermBonus(float semantic, CoverageFeatures features)
    {
        if (features.TermsCount < 2)
            return semantic;

        // Use precomputed trailing match density
        float matchDensity = features.FusionSignals.TrailingMatchDensity / 255f;
        if (matchDensity > 0f)
        {
            float headroom = 1f - semantic;
            semantic += headroom * matchDensity;
        }

        return semantic;
    }
}

