namespace Infidex.Coverage;

internal readonly struct CoverageResult(byte coverageScore, int termsCount, int firstMatchIndex, float sumCi)
{
    public readonly byte CoverageScore = coverageScore;
    public readonly int TermsCount = termsCount;
    public readonly int FirstMatchIndex = firstMatchIndex;
    public readonly float SumCi = sumCi;
}
