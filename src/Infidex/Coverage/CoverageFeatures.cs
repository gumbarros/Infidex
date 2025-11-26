namespace Infidex.Coverage;

public readonly struct CoverageFeatures
{
    public readonly byte CoverageScore;
    public readonly int TermsCount;
    public readonly int TermsWithAnyMatch;
    public readonly int TermsFullyMatched;
    public readonly int TermsStrictMatched;
    public readonly int TermsPrefixMatched;
    public readonly int FirstMatchIndex;
    public readonly float SumCi;
    public readonly int WordHits;
    public readonly int DocTokenCount;
    public readonly int LongestPrefixRun;
    public readonly int SuffixPrefixRun;
    public readonly int PhraseSpan;
    public readonly int PrecedingStrictCount;
    public readonly bool LastTokenHasPrefix;

    public CoverageFeatures(
        byte coverageScore,
        int termsCount,
        int termsWithAnyMatch,
        int termsFullyMatched,
        int termsStrictMatched,
        int termsPrefixMatched,
        int firstMatchIndex,
        float sumCi,
        int wordHits,
        int docTokenCount,
        int longestPrefixRun,
        int suffixPrefixRun,
        int phraseSpan,
        int precedingStrictCount = 0,
        bool lastTokenHasPrefix = false)
    {
        CoverageScore = coverageScore;
        TermsCount = termsCount;
        TermsWithAnyMatch = termsWithAnyMatch;
        TermsFullyMatched = termsFullyMatched;
        TermsStrictMatched = termsStrictMatched;
        TermsPrefixMatched = termsPrefixMatched;
        FirstMatchIndex = firstMatchIndex;
        SumCi = sumCi;
        WordHits = wordHits;
        DocTokenCount = docTokenCount;
        LongestPrefixRun = longestPrefixRun;
        SuffixPrefixRun = suffixPrefixRun;
        PhraseSpan = phraseSpan;
        PrecedingStrictCount = precedingStrictCount;
        LastTokenHasPrefix = lastTokenHasPrefix;
    }
}
