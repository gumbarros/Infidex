using Infidex.Tokenization;

namespace Infidex.Core;

/// <summary>
/// Configuration parameters for the search engine.
/// </summary>
public class ConfigurationParameters
{
    private static readonly Dictionary<int, ConfigurationParameters> PredefinedConfigs = [];

    public static readonly float[] DefaultFieldWeights = [1.5f, 1.25f, 1.0f];

    public int[] IndexSizes { get; set; }
    public int StartPadSize { get; set; }
    public int StopPadSize { get; set; }
    public int StopTermLimit { get; set; }
    public bool CaseSensitive { get; set; }
    public int MaxIndexTextLength { get; set; }
    public int MaxClientTextLength { get; set; }
    public int MaxDocuments { get; set; }
    public TextNormalizer? TextNormalizer { get; set; }
    public TokenizerSetup? TokenizerSetup { get; set; }
    public bool DeleteTextAfterIndexing { get; set; }
    public AutoSegmentationSetup? AutoSegmentationSetup { get; set; }
    public int FilterCacheSize { get; set; }
    public float[] FieldWeights { get; set; }
    public WordMatcherSetup? WordMatcherSetup { get; set; }

    public ConfigurationParameters()
    {
        IndexSizes = [2, 3];
        StartPadSize = 2;
        StopPadSize = 0;
        StopTermLimit = 1_250_000;
        MaxIndexTextLength = 300;
        MaxClientTextLength = 1000;
        MaxDocuments = 5_000_000;
        FieldWeights = DefaultFieldWeights;
    }

    static ConfigurationParameters()
    {
        SetupPredefinedConfigs();
    }

    public static ConfigurationParameters GetConfig(int configNumber)
    {
        if (PredefinedConfigs.TryGetValue(configNumber, out ConfigurationParameters? config))
            return config;

        throw new ArgumentException($"Configuration {configNumber} not found");
    }

    public static bool HasConfig(int configNumber) => PredefinedConfigs.ContainsKey(configNumber);

    private static void SetupPredefinedConfigs()
    {
        TextNormalizer textNormalizer = TextNormalizer.CreateDefault();
        char[] delimiters = TokenizerSetup.GetDefaultDelimiters();

        PredefinedConfigs[100] = new ConfigurationParameters
        {
            IndexSizes = [2, 3],
            StartPadSize = 2,
            StopPadSize = 0,
            StopTermLimit = 1_250_000,
            TextNormalizer = textNormalizer,
            TokenizerSetup = new TokenizerSetup(delimiters, highResolutionMode: false, removeDuplicateTokens: true),
            FieldWeights = DefaultFieldWeights
        };

        PredefinedConfigs[103] = new ConfigurationParameters
        {
            IndexSizes = [3],
            StartPadSize = 2,
            StopPadSize = 0,
            StopTermLimit = 1_250_000,
            TextNormalizer = textNormalizer,
            TokenizerSetup = new TokenizerSetup(delimiters, highResolutionMode: false, removeDuplicateTokens: true),
            FieldWeights = DefaultFieldWeights
        };

        PredefinedConfigs[400] = new ConfigurationParameters
        {
            IndexSizes = [3],
            StartPadSize = 2,
            StopPadSize = 0,
            StopTermLimit = 1_250_000,
            TextNormalizer = textNormalizer,
            TokenizerSetup = new TokenizerSetup(delimiters, highResolutionMode: false, removeDuplicateTokens: false),
            DeleteTextAfterIndexing = true,
            AutoSegmentationSetup = new AutoSegmentationSetup(200, 0.2),
            FilterCacheSize = 200_000,
            FieldWeights = DefaultFieldWeights,
            WordMatcherSetup = new WordMatcherSetup(
                MaximumWordSizeExact: 8,
                MaximumWordSizeLD1: 8,
                MinimumWordSizeExact: 2,
                MinimumWordSizeLD1: 3,
                SupportLD1: true,
                SupportAffix: true)
        };

        PredefinedConfigs[401] = new ConfigurationParameters
        {
            IndexSizes = [3],
            StartPadSize = 2,
            StopPadSize = 0,
            StopTermLimit = 1_250_000,
            TextNormalizer = textNormalizer,
            TokenizerSetup = new TokenizerSetup(delimiters, highResolutionMode: false, removeDuplicateTokens: false),
            DeleteTextAfterIndexing = true,
            AutoSegmentationSetup = new AutoSegmentationSetup(200, 0.2),
            FilterCacheSize = 200_000,
            FieldWeights = DefaultFieldWeights,
            WordMatcherSetup = new WordMatcherSetup(
                MaximumWordSizeExact: 8,
                MaximumWordSizeLD1: 8,
                MinimumWordSizeExact: 2,
                MinimumWordSizeLD1: 3,
                SupportLD1: true,
                SupportAffix: true)
        };
    }
}

public class AutoSegmentationSetup
{
    public int TargetSegmentSize { get; set; }
    public double OverlapRatio { get; set; }

    public AutoSegmentationSetup(int targetSegmentSize, double overlapRatio)
    {
        TargetSegmentSize = targetSegmentSize;
        OverlapRatio = overlapRatio;
    }
}

public class WordMatcherSetup
{
    public int MaximumWordSizeExact { get; set; }
    public int MaximumWordSizeLD1 { get; set; }
    public int MinimumWordSizeExact { get; set; }
    public int MinimumWordSizeLD1 { get; set; }
    public bool SupportLD1 { get; set; }
    public bool SupportAffix { get; set; }

    public WordMatcherSetup(
        int MaximumWordSizeExact = 8,
        int MaximumWordSizeLD1 = 8,
        int MinimumWordSizeExact = 2,
        int MinimumWordSizeLD1 = 3,
        bool SupportLD1 = false,
        bool SupportAffix = false)
    {
        this.MaximumWordSizeExact = MaximumWordSizeExact;
        this.MaximumWordSizeLD1 = MaximumWordSizeLD1;
        this.MinimumWordSizeExact = MinimumWordSizeExact;
        this.MinimumWordSizeLD1 = MinimumWordSizeLD1;
        this.SupportLD1 = SupportLD1;
        this.SupportAffix = SupportAffix;
    }
}
