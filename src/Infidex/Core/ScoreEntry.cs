namespace Infidex.Core;

/// <summary>
/// Represents a search result entry with a score and document identifier.
/// </summary>
/// <param name="Score">Primary 16-bit score (precedence in high byte, semantic in low byte)</param>
/// <param name="DocumentId">Document identifier</param>
/// <param name="Tiebreaker">Secondary sort key for ordering within same Score (higher = better)</param>
/// <param name="SegmentNumber">Optional segment number for multi-segment documents</param>
public record ScoreEntry(ushort Score, long DocumentId, byte Tiebreaker = 0, int? SegmentNumber = null)
{
    /// <summary>
    /// Combined 24-bit sort key: Score (16 bits) + Tiebreaker (8 bits).
    /// Higher values sort first.
    /// </summary>
    public int SortKey => (Score << 8) | Tiebreaker;
    
    public override string ToString()
    {
        if (SegmentNumber.HasValue)
            return $"Score: {Score}, DocId: {DocumentId}, Tie: {Tiebreaker}, Segment: {SegmentNumber.Value}";
        return $"Score: {Score}, DocId: {DocumentId}, Tie: {Tiebreaker}";
    }
}

