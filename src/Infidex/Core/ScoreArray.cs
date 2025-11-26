namespace Infidex.Core;

/// <summary>
/// Bucket-based score storage that provides O(1) insertion and O(n) top-K retrieval.
/// Supports scores 0-65535 (ushort) with an additional 8-bit tiebreaker for ordering within buckets.
/// Optimized for sparse data using range tracking.
/// </summary>
public class ScoreArray
{
    private readonly List<(long DocId, byte Tiebreaker)>?[] _buckets;
    // Optimization: Bitmap to track active buckets. 
    // 65536 bits = 8192 bytes = 1024 ulongs. Fits in L1 cache.
    // Allows GetTopK to skip empty buckets without accessing the large pointer array.
    private readonly ulong[] _activeBucketsBitmap; 
    private int _maxScore = -1;
    private int _minScore = 65536;

    public ScoreArray()
    {
        // Create 65536 buckets (one for each possible ushort value)
        // Lazy initialization: array of nulls
        _buckets = new List<(long, byte)>?[65536];
        _activeBucketsBitmap = new ulong[1024]; // 65536 / 64
        Count = 0;
    }
    
    /// <summary>
    /// Adds a document with its score. O(1) operation.
    /// </summary>
    public void Add(long documentId, ushort score, byte tiebreaker = 0)
    {
        if (_buckets[score] == null)
        {
            _buckets[score] = [];
            // Set bit in bitmap
            _activeBucketsBitmap[score >> 6] |= (1UL << (score & 63));
        }
        
        _buckets[score]!.Add((documentId, tiebreaker));
        Count++;
        
        if (score > _maxScore) _maxScore = score;
        if (score < _minScore) _minScore = score;
    }
    
    /// <summary>
    /// Updates a document's score. If the document doesn't exist, adds it.
    /// Note: This is expensive if we don't know the old score, so we scan.
    /// For ScoreArray usage pattern (re-scoring), we often just Add.
    /// Update logic assumes we might need to remove from old bucket.
    /// </summary>
    public void Update(long documentId, ushort score, byte tiebreaker = 0)
    {
        // Remove any existing occurrences of this document from all active buckets
        // Optimization: scan only within known range
        if (Count > 0 && _maxScore >= 0)
        {
            // Iterate only buckets that might have data
            for (int s = _minScore; s <= _maxScore; s++)
            {
                var bucket = _buckets[s];
                if (bucket != null && bucket.Count > 0)
                {
                    for (int i = bucket.Count - 1; i >= 0; i--)
                    {
                        if (bucket[i].DocId == documentId)
                        {
                            bucket.RemoveAt(i);
                            Count--;
                        }
                    }
                }
            }
        }

        Add(documentId, score, tiebreaker);
    }
    
    /// <summary>
    /// Gets the top K results by iterating from highest score to lowest.
    /// Within each score bucket, entries are sorted by tiebreaker (higher first).
    /// O(n) operation but extremely fast due to bucket structure.
    /// </summary>
    public ScoreEntry[] GetTopK(int k)
    {
        List<ScoreEntry> results = [];
        
        if (Count == 0 || _maxScore < 0)
            return [];
            
        int maxChunkIndex = _maxScore >> 6;
        int minChunkIndex = _minScore >> 6;

        // Iterate through 64-bit chunks from high to low
        for (int i = maxChunkIndex; i >= minChunkIndex && results.Count < k; i--)
        {
            ulong chunk = _activeBucketsBitmap[i];
            if (chunk == 0) continue;

            // Iterate bits in the chunk from high (63) to low (0)
            for (int bit = 63; bit >= 0; bit--)
            {
                if ((chunk & (1UL << bit)) != 0)
                {
                    int score = (i << 6) | bit;
                    
                    if (score > _maxScore) continue; 
                    if (score < _minScore) break;

                    var bucket = _buckets[score];
                    if (bucket != null && bucket.Count > 0)
                    {
                        // Sort bucket by tiebreaker descending (higher = better)
                        // This is typically a small list, so sorting is fast
                        if (bucket.Count > 1)
                        {
                            bucket.Sort((a, b) => b.Tiebreaker.CompareTo(a.Tiebreaker));
                        }
                        
                        foreach (var (docId, tiebreaker) in bucket)
                        {
                            results.Add(new ScoreEntry((ushort)score, docId, tiebreaker));
                            if (results.Count >= k)
                                goto Done;
                        }
                    }
                }
            }
        }
        
        Done:
        return results.ToArray();
    }
    
    /// <summary>
    /// Gets all results sorted by score (highest first)
    /// </summary>
    public ScoreEntry[] GetAll()
    {
        return GetTopK(Count);
    }
    
    /// <summary>
    /// Clears all scores
    /// </summary>
    public void Clear()
    {
        if (_maxScore >= 0)
        {
            // Only clear used buckets
            for (int i = _minScore; i <= _maxScore; i++)
            {
                if (_buckets[i] != null)
                    _buckets[i]!.Clear();
            }
            
            // Clear bitmap (only need to clear range we touched)
            int minChunk = _minScore >> 6;
            int maxChunk = _maxScore >> 6;
            Array.Clear(_activeBucketsBitmap, minChunk, maxChunk - minChunk + 1);
        }
        Count = 0;
        _maxScore = -1;
        _minScore = 65536;
    }
    
    /// <summary>
    /// Gets the total number of entries
    /// </summary>
    public int Count { get; private set; }
}
