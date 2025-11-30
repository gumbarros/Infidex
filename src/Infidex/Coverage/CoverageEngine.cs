using System.Buffers;
using System.Collections.Concurrent;
using Infidex.Tokenization;
using Infidex.Core;
using Infidex.Indexing;

namespace Infidex.Coverage;

public sealed class CoverageQueryContext : IDisposable
{
    public string Query { get; }
    internal StringSlice[] QueryTokenArray { get; }
    public int QueryTokenCount { get; }
    public float[] TermIdf { get; }
    public int[] TermMaxChars { get; }
    public float[]? WordLevelIdf { get; }

    internal CoverageQueryContext(
        string query,
        StringSlice[] queryTokenArray,
        int queryTokenCount,
        float[] termIdf,
        int[] termMaxChars,
        float[]? wordLevelIdf)
    {
        Query = query;
        QueryTokenArray = queryTokenArray;
        QueryTokenCount = queryTokenCount;
        TermIdf = termIdf;
        TermMaxChars = termMaxChars;
        WordLevelIdf = wordLevelIdf;
    }

    public void Dispose()
    {
        if (QueryTokenArray.Length > 0)
            ArrayPool<StringSlice>.Shared.Return(QueryTokenArray);
        if (TermIdf.Length > 0)
            ArrayPool<float>.Shared.Return(TermIdf);
        if (TermMaxChars.Length > 0)
            ArrayPool<int>.Shared.Return(TermMaxChars);
    }
}

public class CoverageEngine
{
    private readonly Tokenizer _tokenizer;
    private readonly CoverageSetup _setup;
    private TermCollection? _termCollection;
    private int _totalDocuments;
    private readonly ConcurrentDictionary<string, float[]> _queryIdfCache = new();
    private DocumentMetadataCache? _documentMetadataCache;
    private Dictionary<string, float>? _wordIdfCache;
    
    public CoverageEngine(Tokenizer tokenizer, CoverageSetup? setup = null)
    {
        _tokenizer = tokenizer;
        _setup = setup ?? CoverageSetup.CreateDefault();
    }

    public CoverageQueryContext PrepareQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
            return new CoverageQueryContext(query, [], 0, [], [], null);

        ReadOnlySpan<char> delimiters = _tokenizer.TokenizerSetup?.Delimiters ?? [' '];
        int queryLen = query.Length;
        int maxQueryTokens = queryLen / 2 + 1;
        
        StringSlice[] queryTokenArray = ArrayPool<StringSlice>.Shared.Rent(maxQueryTokens);
        int qCountRaw = CoverageTokenizer.TokenizeToSpan(query, queryTokenArray, _setup.MinWordSize, delimiters);

        if (qCountRaw == 0)
        {
            ArrayPool<StringSlice>.Shared.Return(queryTokenArray);
            return new CoverageQueryContext(query, [], 0, [], [], null);
        }

        ReadOnlySpan<char> querySpan = query.AsSpan();
        int qCount = CoverageTokenizer.DeduplicateQueryTokens(queryTokenArray, qCountRaw, querySpan);

        int[] termMaxChars = ArrayPool<int>.Shared.Rent(qCount);
        float[] termIdf = ArrayPool<float>.Shared.Rent(qCount);

        // Precompute per-query term IDF
        if (_termCollection != null && _totalDocuments > 0)
        {
            if (!_queryIdfCache.TryGetValue(query, out float[]? cached) || cached.Length < qCount)
            {
                cached = new float[qCount];
                for (int i = 0; i < qCount; i++)
                {
                    cached[i] = ComputeTermIdf(queryTokenArray[i], querySpan);
                }
                _queryIdfCache[query] = cached;
            }

            for (int i = 0; i < qCount; i++)
            {
                termMaxChars[i] = queryTokenArray[i].Length;
                termIdf[i] = cached[i];
            }
        }
        else
        {
            for (int i = 0; i < qCount; i++)
            {
                termMaxChars[i] = queryTokenArray[i].Length;
                termIdf[i] = MathF.Log2(termMaxChars[i] + 1);
            }
        }

        float[]? wordLevelIdfArray = null;
        if (_wordIdfCache != null && qCount > 0)
        {
            wordLevelIdfArray = new float[qCount];
            for (int i = 0; i < qCount; i++)
            {
                StringSlice tokenSlice = queryTokenArray[i];
                string token = query.Substring(tokenSlice.Offset, tokenSlice.Length);
                wordLevelIdfArray[i] = _wordIdfCache.TryGetValue(token, out float idf) ? idf : 0f;
            }
        }

        return new CoverageQueryContext(query, queryTokenArray, qCount, termIdf, termMaxChars, wordLevelIdfArray);
    }
    
    /// <summary>
    /// Sets the term collection and document count for IDF computation.
    /// Should be called once after indexing is complete.
    /// </summary>
    public void SetCorpusStatistics(TermCollection termCollection, int totalDocuments)
    {
        _termCollection = termCollection;
        _totalDocuments = totalDocuments;
    }
    
    /// <summary>
    /// Sets the document metadata cache for fusion signal optimization.
    /// Should be called once after indexing is complete.
    /// </summary>
    internal void SetDocumentMetadataCache(DocumentMetadataCache? metadataCache)
    {
        _documentMetadataCache = metadataCache;
    }
    
    /// <summary>
    /// Sets the word-level IDF cache for token-level discriminative power.
    /// Should be called once after indexing is complete.
    /// </summary>
    internal void SetWordIdfCache(Dictionary<string, float>? wordIdfCache)
    {
        _wordIdfCache = wordIdfCache;
    }
    
    public byte CalculateCoverageScore(string query, string documentText, double lcsSum, out int wordHits, int documentId = -1)
    {
        using var context = PrepareQuery(query);
        // Create a temporary buffer for single usage
        using var buffer = new CoverageBuffer();
        CoverageResult result = CalculateCoverageInternal(context, documentText, lcsSum, documentId, buffer,
            out wordHits, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
        return result.CoverageScore;
    }

    public CoverageFeatures CalculateFeatures(string query, string documentText, double lcsSum, int documentId = -1)
    {
        using var context = PrepareQuery(query);
        using var buffer = new CoverageBuffer();
        return CalculateFeatures(context, documentText, lcsSum, buffer, documentId);
    }

    // Overload used by optimized pipeline
    internal CoverageFeatures CalculateFeatures(CoverageQueryContext context, string documentText, double lcsSum, CoverageBuffer buffer, int documentId = -1)
    {
        CoverageResult result = CalculateCoverageInternal(
            context,
            documentText,
            lcsSum,
            documentId,
            buffer,
            out int wordHits,
            out int docTokenCount,
            out int termsWithAnyMatch,
            out int termsFullyMatched,
            out int termsStrictMatched,
            out int termsPrefixMatched,
            out int longestPrefixRun,
            out int suffixPrefixRun,
            out int phraseSpan,
            out int precedingStrictCount,
            out bool lastTokenHasPrefix,
            out var fusionSignals);

        return new CoverageFeatures(
            result.CoverageScore,
            result.TermsCount,
            termsWithAnyMatch,
            termsFullyMatched,
            termsStrictMatched,
            termsPrefixMatched,
            result.FirstMatchIndex,
            result.SumCi,
            wordHits,
            docTokenCount,
            longestPrefixRun,
            suffixPrefixRun,
            phraseSpan,
            precedingStrictCount,
            lastTokenHasPrefix,
            result.LastTermCi,
            result.WeightedCoverage,
            result.LastTermIsTypeAhead,
            result.IdfCoverage,
            result.TotalIdf,
            result.MissingIdf,
            result.TermIdf,
            result.TermCi,
            fusionSignals);
    }

    private CoverageResult CalculateCoverageInternal(CoverageQueryContext context, string documentText, double lcsSum, int documentId, CoverageBuffer buffer,
        out int wordHits,
        out int docTokenCount,
        out int termsWithAnyMatch,
        out int termsFullyMatched,
        out int termsStrictMatched,
        out int termsPrefixMatched,
        out int longestPrefixRun,
        out int suffixPrefixRun,
        out int phraseSpan,
        out int precedingStrictCount,
        out bool lastTokenHasPrefix,
        out FusionSignals fusionSignals)
    {
        wordHits = 0;
        docTokenCount = 0;
        termsWithAnyMatch = 0;
        termsFullyMatched = 0;
        termsStrictMatched = 0;
        termsPrefixMatched = 0;
        longestPrefixRun = 0;
        suffixPrefixRun = 0;
        phraseSpan = 0;
        precedingStrictCount = 0;
        lastTokenHasPrefix = false;
        fusionSignals = default;
        
        if (context.QueryTokenCount == 0) 
            return new CoverageResult(0, 0, -1, 0);

        ReadOnlySpan<char> delimiters = _tokenizer.TokenizerSetup?.Delimiters ?? [' '];
        int docLen = documentText.Length;
        int qCount = context.QueryTokenCount;

        // Ensure buffer capacity for query (should be done once per query really, but cheap here)
        buffer.EnsureQueryCapacity(qCount);

        // Use pre-tokenized query data
        Span<StringSlice> queryTokens = context.QueryTokenArray.AsSpan(0, qCount);
        ReadOnlySpan<char> querySpan = context.Query.AsSpan();

        int maxDocTokens = docLen / 2 + 1;
        buffer.EnsureDocCapacity(maxDocTokens);
        
        Span<StringSlice> docTokens = buffer.DocTokenArray.AsSpan(0, maxDocTokens);

        ReadOnlySpan<char> docSpan = documentText.AsSpan();
        int dCountRaw = CoverageTokenizer.TokenizeToSpan(documentText, docTokens, _setup.MinWordSize, delimiters);
        docTokenCount = dCountRaw;

        Span<StringSlice> uniqueDocTokens = buffer.UniqueDocTokenArray.AsSpan(0, dCountRaw);

        int dCount = CoverageTokenizer.DeduplicateDocTokens(docTokens.Slice(0, dCountRaw), dCountRaw, uniqueDocTokens, docSpan);

        // Tracking arrays from buffer
        Span<bool> qActive = buffer.QActiveArray.AsSpan(0, qCount);
        qActive.Fill(true);

        Span<bool> dActive = buffer.DActiveArray.AsSpan(0, dCount);
        dActive.Fill(true);

        Span<float> termMatchedChars = buffer.TermMatchedCharsArray.AsSpan(0, qCount);
        termMatchedChars.Clear();

        // Use precomputed TermMaxChars/TermIdf
        Span<int> termMaxChars = context.TermMaxChars.AsSpan(0, qCount);
        Span<float> termIdf = context.TermIdf.AsSpan(0, qCount);
        
        Span<bool> termHasWhole = buffer.TermHasWholeArray.AsSpan(0, qCount);
        termHasWhole.Clear();

        Span<bool> termHasJoined = buffer.TermHasJoinedArray.AsSpan(0, qCount);
        termHasJoined.Clear();

        Span<bool> termHasPrefix = buffer.TermHasPrefixArray.AsSpan(0, qCount);
        termHasPrefix.Clear();

        Span<int> termFirstPos = buffer.TermFirstPosArray.AsSpan(0, qCount);
        termFirstPos.Fill(-1);

        // Build MatchState
        MatchState state = new MatchState
        {
            QueryTokens = queryTokens,
            UniqueDocTokens = uniqueDocTokens.Slice(0, dCount),
            QActive = qActive,
            DActive = dActive,
            TermMatchedChars = termMatchedChars,
            TermMaxChars = termMaxChars,
            TermHasWhole = termHasWhole,
            TermHasJoined = termHasJoined,
            TermHasPrefix = termHasPrefix,
            TermFirstPos = termFirstPos,
            TermIdf = termIdf,
            QuerySpan = querySpan,
            DocSpan = docSpan,
            QCount = qCount,
            DCount = dCount,
            DocTokenCount = docTokenCount
        };

        if (_setup.CoverWholeWords)
            WholeWordMatcher.Match(ref state);

        if (_setup.CoverJoinedWords && qCount > 0)
            JoinedWordMatcher.Match(ref state);

        if (_setup.CoverPrefixSuffix && qCount > 0)
            PrefixSuffixMatcher.Match(ref state);

        if (_setup.CoverFuzzyWords && qCount > 0 && !FuzzyWordMatcher.AllTermsFullyMatched(ref state))
            FuzzyWordMatcher.Match(ref state, _setup);

        wordHits = state.WordHits;
        
        CoverageResult coverageResult = CoverageScorer.CalculateFinalScore(
            ref state,
            context.Query.Length,
            lcsSum,
            _setup.CoverWholeQuery,
            context.WordLevelIdf,
            out termsWithAnyMatch,
            out termsFullyMatched,
            out termsStrictMatched,
            out termsPrefixMatched,
            out longestPrefixRun,
            out suffixPrefixRun,
            out phraseSpan,
            out precedingStrictCount,
            out lastTokenHasPrefix);
        
        // Fusion signals need all tokens (no MinWordSize filtering)
        int maxFusionQueryTokens = context.Query.Length / 2 + 1;
        buffer.EnsureQueryCapacity(maxFusionQueryTokens);
        Span<StringSlice> fusionQueryTokens = buffer.FusionQueryTokenArray.AsSpan(0, maxFusionQueryTokens);

        int fusionQCount = CoverageTokenizer.TokenizeToSpan(context.Query, fusionQueryTokens, minWordSize: 0, delimiters);

        int maxFusionDocTokens = docLen / 2 + 1;
        buffer.EnsureDocCapacity(maxFusionDocTokens);
        Span<StringSlice> fusionDocTokens = buffer.FusionDocTokenArray.AsSpan(0, maxFusionDocTokens);

        int fusionDCount = CoverageTokenizer.TokenizeToSpan(documentText, fusionDocTokens, minWordSize: 0, delimiters);

        // Get precomputed document metadata for optimization
        DocumentMetadata docMetadata = _documentMetadataCache != null && documentId >= 0
            ? _documentMetadataCache.Get(documentId)
            : DocumentMetadata.Empty;

        fusionSignals = FusionSignalComputer.ComputeSignals(
            querySpan,
            docSpan,
            fusionQueryTokens.Slice(0, fusionQCount),
            fusionDocTokens.Slice(0, fusionDCount),
            fusionQCount,
            fusionDCount,
            _setup.MinWordSize,
            docMetadata);

        return coverageResult;
    }
    
    /// <summary>
    /// Computes IDF for a query term by averaging IDF over its constituent n-grams.
    /// Returns a default value if term collection is not available.
    /// </summary>
    private float ComputeTermIdf(StringSlice termSlice, ReadOnlySpan<char> querySpan)
    {
        if (_termCollection == null || _totalDocuments == 0)
        {
            // Fallback: use term length as a proxy for information content
            return MathF.Log2(termSlice.Length + 1);
        }
        
        ReadOnlySpan<char> termSpan = querySpan.Slice(termSlice.Offset, termSlice.Length);
        
        // Generate n-grams for this term and compute average IDF
        int[] ngramSizes = _tokenizer.IndexSizes;
        float idfSum = 0f;
        int ngramCount = 0;
        
        foreach (int size in ngramSizes)
        {
            if (termSpan.Length < size)
                continue;
                
            for (int i = 0; i <= termSpan.Length - size; i++)
            {
                ReadOnlySpan<char> ngram = termSpan.Slice(i, size);
                string ngramText = new string(ngram);
                
                Term? term = _termCollection.GetTerm(ngramText);
                if (term != null && term.DocumentFrequency > 0)
                {
                    float idf = Bm25Scorer.ComputeIdf(_totalDocuments, term.DocumentFrequency);
                    idfSum += idf;
                    ngramCount++;
                }
            }
        }
        
        // Return average IDF, or a default based on term length if no n-grams found
        return ngramCount > 0 
            ? idfSum / ngramCount 
            : MathF.Log2(termSpan.Length + 1);
    }
}
