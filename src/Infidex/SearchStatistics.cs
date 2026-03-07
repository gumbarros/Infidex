namespace Infidex;

/// <summary>
/// Basic index statistics exposed by the public API.
/// </summary>
public sealed class SearchStatistics
{
    public int DocumentCount { get; set; }
    public int VocabularySize { get; set; }

    public override string ToString() => $"{DocumentCount} documents, {VocabularySize} unique terms";
}
