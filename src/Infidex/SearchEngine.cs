using System.Collections.Concurrent;
using System.Diagnostics;
using Infidex.Api;
using Infidex.Core;
using Infidex.Coverage;
using Infidex.Filtering;
using Infidex.Indexing;
using Infidex.Scoring;
using Infidex.Synonyms;
using Infidex.Tokenization;
using System.Text;

namespace Infidex;

public enum SearchEngineStatus
{
    Ready,
    Indexing,
    Loading
}

/// <summary>
/// Public search engine facade over the vector model and search pipeline.
/// </summary>
public class SearchEngine : ISearchEngine
{
    private readonly VectorModel _vectorModel;
    private readonly CoverageEngine? _coverageEngine;
    private readonly CoverageSetup? _coverageSetup;
    private readonly WordMatcher.WordMatcher? _wordMatcher;
    private readonly ThreadLocal<FilterCompiler> _filterCompiler = new(() => new FilterCompiler());
    private readonly ThreadLocal<FilterVM> _filterVM = new(() => new FilterVM());
    private readonly ConcurrentDictionary<Filter, CompiledFilter> _compiledFilterCache = new();
    private readonly ReaderWriterLockSlim _rwLock = new();
    private readonly ResultProcessor _resultProcessor;

    private volatile SearchEngineStatus _status = SearchEngineStatus.Ready;
    private bool _isIndexed;

    public bool EnableDebugLogging { get; set; }
    public SynonymMap? SynonymMap { get; }
    public DocumentFields? DocumentFieldSchema { get; set; }
    public event EventHandler<int>? ProgressChanged;

    public SearchEngineStatus Status
    {
        get => _status;
        private set => _status = value;
    }

    public SearchEngine(
        int[] indexSizes,
        int startPadSize = 2,
        int stopPadSize = 0,
        bool enableCoverage = true,
        TextNormalizer? textNormalizer = null,
        TokenizerSetup? tokenizerSetup = null,
        CoverageSetup? coverageSetup = null,
        int stopTermLimit = 1_250_000,
        WordMatcherSetup? wordMatcherSetup = null,
        float[]? fieldWeights = null,
        SynonymMap? synonymMap = null)
    {
        textNormalizer ??= TextNormalizer.CreateDefault();
        tokenizerSetup ??= TokenizerSetup.CreateDefault();

        Tokenizer tokenizer = new(indexSizes, startPadSize, stopPadSize, textNormalizer, tokenizerSetup);
        SynonymMap = synonymMap;
        _vectorModel = new VectorModel(tokenizer, stopTermLimit, fieldWeights, synonymMap);
        _vectorModel.ProgressChanged += (_, progress) => ProgressChanged?.Invoke(this, 50 + progress / 2);

        if (enableCoverage)
        {
            _coverageSetup = coverageSetup ?? CoverageSetup.CreateDefault();
            _coverageEngine = new CoverageEngine(tokenizer, _coverageSetup);
        }

        if (wordMatcherSetup != null)
        {
            _wordMatcher = new WordMatcher.WordMatcher(wordMatcherSetup, tokenizerSetup.Delimiters, textNormalizer);
        }

        _resultProcessor = new ResultProcessor(_vectorModel.Documents, _filterCompiler, _filterVM, _compiledFilterCache);
    }

    public static SearchEngine CreateDefault()
    {
        ConfigurationParameters config = ConfigurationParameters.GetConfig(400);
        return new SearchEngine(
            indexSizes: config.IndexSizes,
            startPadSize: config.StartPadSize,
            stopPadSize: config.StopPadSize,
            enableCoverage: true,
            textNormalizer: config.TextNormalizer,
            tokenizerSetup: config.TokenizerSetup,
            coverageSetup: null,
            stopTermLimit: config.StopTermLimit,
            wordMatcherSetup: config.WordMatcherSetup,
            fieldWeights: config.FieldWeights);
    }

    public static SearchEngine CreateMinimal()
    {
        return new SearchEngine(indexSizes: [3], startPadSize: 2, stopPadSize: 0, enableCoverage: false);
    }

    public void IndexDocuments(IEnumerable<Document> documents, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        _rwLock.EnterWriteLock();
        try
        {
            Status = SearchEngineStatus.Indexing;
            List<Document> docList = documents.ToList();

            for (int i = 0; i < docList.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Document document = docList[i];

                DocumentFieldSchema ??= document.Fields;

                Document stored = _vectorModel.IndexDocument(document);
                if (_wordMatcher != null)
                {
                    _wordMatcher.Load(CanonicalizeForWordMatcher(stored.IndexedText), stored.Id);
                }

                if (docList.Count > 0)
                {
                    int percent = (int)((i + 1) * 50.0 / docList.Count);
                    ProgressChanged?.Invoke(this, percent);
                    progress?.Report(percent);
                }
            }

            BuildIndexInternal(cancellationToken);
            Status = SearchEngineStatus.Ready;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public async Task IndexDocumentsAsync(IEnumerable<Document> documents, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        => await Task.Run(() => IndexDocuments(documents, progress, cancellationToken), cancellationToken);

    public void IndexDocument(Document document)
    {
        IndexDocuments([document]);
    }

    public void BuildIndex(CancellationToken cancellationToken = default)
    {
        _rwLock.EnterWriteLock();
        try
        {
            Status = SearchEngineStatus.Indexing;
            BuildIndexInternal(cancellationToken);
            Status = SearchEngineStatus.Ready;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public Result Search(string text, int maxResults = 10) => Search(new Query(text, maxResults));

    public Result Search(Query query)
    {
        _rwLock.EnterReadLock();
        try
        {
            if (!_isIndexed)
                return Result.MakeEmptyResult();

            Stopwatch stopwatch = Stopwatch.StartNew();
            Query effectiveQuery = new Query(query);
            effectiveQuery.Text = CanonicalizeQueryText(effectiveQuery.Text);
            effectiveQuery.TimeOutLimitMilliseconds = Math.Clamp(effectiveQuery.TimeOutLimitMilliseconds, 0, 10_000);

            if (string.IsNullOrWhiteSpace(effectiveQuery.Text))
            {
                return SearchEmptyWithFacets(effectiveQuery, stopwatch);
            }

            _vectorModel.EnableDebugLogging = EnableDebugLogging;

            SearchPipeline pipeline = new(
                _vectorModel,
                effectiveQuery.EnableCoverage ? _coverageEngine : null,
                effectiveQuery.CoverageSetup ?? _coverageSetup,
                _wordMatcher,
                SynonymMap);

            ScoreEntry[] results = pipeline.Execute(
                effectiveQuery.Text,
                effectiveQuery.EnableCoverage ? (effectiveQuery.CoverageSetup ?? _coverageSetup) : null,
                effectiveQuery.CoverageDepth,
                effectiveQuery.MaxNumberOfRecordsToReturn);

            results = ApplyQueryPostProcessing(results, effectiveQuery);

            Dictionary<string, KeyValuePair<string, int>[]>? facets = effectiveQuery.EnableFacets
                ? FacetBuilder.BuildFacets(results, _vectorModel.Documents, DocumentFieldSchema)
                : null;

            ScoreEntry[] topResults = results.Take(effectiveQuery.MaxNumberOfRecordsToReturn).ToArray();
            return new Result(
                topResults,
                facets,
                topResults.Length > 0 ? topResults.Length - 1 : 0,
                topResults.Length > 0 ? topResults[^1].Score : 0f,
                false)
            {
                TotalCandidates = results.Length,
                ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public Document? GetDocument(long documentKey)
    {
        _rwLock.EnterReadLock();
        try
        {
            return _vectorModel.Documents.GetDocumentByPublicKey(documentKey);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public List<Document> GetDocuments(long documentKey)
    {
        _rwLock.EnterReadLock();
        try
        {
            return _vectorModel.Documents.GetDocumentsForPublicKey(documentKey);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public SearchStatistics GetStatistics()
    {
        _rwLock.EnterReadLock();
        try
        {
            return new SearchStatistics
            {
                DocumentCount = _vectorModel.Documents.Count,
                VocabularySize = _vectorModel.TermCollection.Count
            };
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public SystemStatus GetStatus()
    {
        _rwLock.EnterReadLock();
        try
        {
            return new SystemStatus
            {
                DocumentCount = _vectorModel.Documents.Count,
                ReIndexRequired = !_isIndexed,
                IndexProgress = Status == SearchEngineStatus.Ready ? 100 : 0
            };
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public void Save(string filePath)
    {
        _rwLock.EnterWriteLock();
        try
        {
            using FileStream stream = File.Create(filePath);
            using BinaryWriter writer = new(stream);
            _vectorModel.SaveToStream(writer);
            _wordMatcher?.Save(writer);
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public async Task SaveAsync(string filePath) => await Task.Run(() => Save(filePath));

    public static SearchEngine Load(
        string filePath,
        int[] indexSizes,
        int startPadSize = 2,
        int stopPadSize = 0,
        bool enableCoverage = true,
        TextNormalizer? textNormalizer = null,
        TokenizerSetup? tokenizerSetup = null,
        CoverageSetup? coverageSetup = null,
        int stopTermLimit = 1_250_000,
        WordMatcherSetup? wordMatcherSetup = null,
        float[]? fieldWeights = null,
        SynonymMap? synonymMap = null)
    {
        SearchEngine engine = new(
            indexSizes,
            startPadSize,
            stopPadSize,
            enableCoverage,
            textNormalizer,
            tokenizerSetup,
            coverageSetup,
            stopTermLimit,
            wordMatcherSetup,
            fieldWeights,
            synonymMap);

        engine._rwLock.EnterWriteLock();
        try
        {
            engine.Status = SearchEngineStatus.Loading;
            using FileStream stream = File.OpenRead(filePath);
            long? wordMatcherOffset = engine._wordMatcher != null ? GetWordMatcherOffset(stream) : null;
            stream.Position = 0;
            using BinaryReader reader = new(stream);

            engine._vectorModel.LoadFromStream(reader);
            if (engine._wordMatcher != null && wordMatcherOffset is long offset && offset < stream.Length)
            {
                stream.Position = offset;
                using BinaryReader matcherReader = new(stream, Encoding.UTF8, leaveOpen: true);
                engine._wordMatcher.Load(matcherReader);
            }

            engine.DocumentFieldSchema = engine._vectorModel.Documents.GetAllDocuments().FirstOrDefault()?.Fields;
            engine.BuildIndexInternal(CancellationToken.None);
            engine.Status = SearchEngineStatus.Ready;
            return engine;
        }
        finally
        {
            engine._rwLock.ExitWriteLock();
        }
    }

    public static async Task<SearchEngine> LoadAsync(
        string filePath,
        int[] indexSizes,
        int startPadSize = 2,
        int stopPadSize = 0,
        bool enableCoverage = true,
        TextNormalizer? textNormalizer = null,
        TokenizerSetup? tokenizerSetup = null,
        CoverageSetup? coverageSetup = null,
        int stopTermLimit = 1_250_000,
        WordMatcherSetup? wordMatcherSetup = null,
        float[]? fieldWeights = null,
        SynonymMap? synonymMap = null)
        => await Task.Run(() => Load(
            filePath,
            indexSizes,
            startPadSize,
            stopPadSize,
            enableCoverage,
            textNormalizer,
            tokenizerSetup,
            coverageSetup,
            stopTermLimit,
            wordMatcherSetup,
            fieldWeights,
            synonymMap));

    public void Dispose()
    {
        _rwLock.Dispose();
        _vectorModel.Dispose();
        _wordMatcher?.Dispose();
        _filterCompiler.Dispose();
        _filterVM.Dispose();
    }

    private void BuildIndexInternal(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _vectorModel.BuildInvertedLists(cancellationToken: cancellationToken);
        _vectorModel.BuildOptimizedIndexes();
        _wordMatcher?.FinalizeIndex();
        _isIndexed = true;
    }

    private Result SearchEmptyWithFacets(Query query, Stopwatch stopwatch)
    {
        if (!query.EnableFacets)
            return Result.MakeEmptyResult();

        ScoreEntry[] allResults = _vectorModel.Documents
            .GetAllDocuments()
            .Where(doc => !doc.Deleted)
            .Select(doc => new ScoreEntry(float.MaxValue, doc.DocumentKey))
            .ToArray();

        allResults = ApplyQueryPostProcessing(allResults, query);

        Dictionary<string, KeyValuePair<string, int>[]> facets =
            FacetBuilder.BuildFacets(allResults, _vectorModel.Documents, DocumentFieldSchema);

        ScoreEntry[] topResults = allResults.Take(query.MaxNumberOfRecordsToReturn).ToArray();
        return new Result(
            topResults,
            facets,
            topResults.Length > 0 ? topResults.Length - 1 : 0,
            topResults.Length > 0 ? topResults[^1].Score : 0f,
            false)
        {
            TotalCandidates = allResults.Length,
            ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds
        };
    }

    private ScoreEntry[] ApplyQueryPostProcessing(ScoreEntry[] results, Query query)
    {
        ScoreEntry[] processed = results;

        if (query.Filter != null)
            processed = _resultProcessor.ApplyFilter(processed, query.Filter);

        if (query.EnableBoost && query.Boosts is { Length: > 0 })
            processed = _resultProcessor.ApplyBoosts(processed, query.Boosts);

        if (query.SortBy != null)
            processed = _resultProcessor.ApplySort(processed, query.SortBy, query.SortAscending);

        return processed;
    }

    private string CanonicalizeQueryText(string text)
    {
        string normalized = text?.Trim() ?? string.Empty;
        if (_vectorModel.Tokenizer.TextNormalizer != null)
            normalized = _vectorModel.Tokenizer.TextNormalizer.Normalize(normalized);

        normalized = normalized.ToLowerInvariant();
        if (SynonymMap != null && SynonymMap.HasCanonicalMappings && _vectorModel.Tokenizer.TokenizerSetup != null)
            normalized = SynonymMap.CanonicalizeText(normalized, _vectorModel.Tokenizer.TokenizerSetup.Delimiters);

        return normalized;
    }

    private string CanonicalizeForWordMatcher(string text)
    {
        string normalized = _vectorModel.Tokenizer.TextNormalizer?.Normalize(text) ?? text;
        normalized = normalized.ToLowerInvariant();

        if (SynonymMap != null && SynonymMap.HasCanonicalMappings && _vectorModel.Tokenizer.TokenizerSetup != null)
            normalized = SynonymMap.CanonicalizeText(normalized, _vectorModel.Tokenizer.TokenizerSetup.Delimiters);

        return normalized;
    }

    private static long? GetWordMatcherOffset(Stream stream)
    {
        const int headerSize = 6 + (sizeof(uint) * 5);
        const int trailerSize = sizeof(uint);

        long originalPosition = stream.Position;
        try
        {
            if (stream.Length < headerSize + sizeof(uint) + trailerSize)
                return null;

            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            stream.Position = 0;

            _ = reader.ReadBytes(6);          // magic
            _ = reader.ReadUInt32();          // version
            _ = reader.ReadUInt32();          // flags
            _ = reader.ReadUInt32();          // doc count
            _ = reader.ReadUInt32();          // term count
            _ = reader.ReadUInt32();          // header checksum
            uint dataLength = reader.ReadUInt32();

            long offset = headerSize + sizeof(uint) + dataLength + trailerSize;
            return offset <= stream.Length ? offset : null;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}
