using System.Buffers;
using Infidex.Tokenization;

namespace Infidex.Coverage;

/// <summary>
/// Reusable buffer for coverage calculations to avoid repeated array allocations.
/// Not thread-safe; should be created per query execution.
/// </summary>
internal sealed class CoverageBuffer : IDisposable
{
    // Arrays sized for max document tokens
    public StringSlice[] DocTokenArray;
    public StringSlice[] UniqueDocTokenArray;
    public bool[] QActiveArray;
    public bool[] DActiveArray;
    public float[] TermMatchedCharsArray;
    public bool[] TermHasWholeArray;
    public bool[] TermHasJoinedArray;
    public bool[] TermHasPrefixArray;
    public int[] TermFirstPosArray;
    public float[] TermIdfArray;
    
    // Fusion arrays
    public StringSlice[] FusionQueryTokenArray;
    public StringSlice[] FusionDocTokenArray;

    private const int DefaultCapacity = 1024; // Reasonable start for doc tokens
    private const int DefaultQueryCapacity = 64;

    public CoverageBuffer()
    {
        DocTokenArray = ArrayPool<StringSlice>.Shared.Rent(DefaultCapacity);
        UniqueDocTokenArray = ArrayPool<StringSlice>.Shared.Rent(DefaultCapacity);
        QActiveArray = ArrayPool<bool>.Shared.Rent(DefaultQueryCapacity);
        DActiveArray = ArrayPool<bool>.Shared.Rent(DefaultCapacity);
        TermMatchedCharsArray = ArrayPool<float>.Shared.Rent(DefaultQueryCapacity);
        TermHasWholeArray = ArrayPool<bool>.Shared.Rent(DefaultQueryCapacity);
        TermHasJoinedArray = ArrayPool<bool>.Shared.Rent(DefaultQueryCapacity);
        TermHasPrefixArray = ArrayPool<bool>.Shared.Rent(DefaultQueryCapacity);
        TermFirstPosArray = ArrayPool<int>.Shared.Rent(DefaultQueryCapacity);
        TermIdfArray = ArrayPool<float>.Shared.Rent(DefaultQueryCapacity);
        
        FusionQueryTokenArray = ArrayPool<StringSlice>.Shared.Rent(DefaultQueryCapacity);
        FusionDocTokenArray = ArrayPool<StringSlice>.Shared.Rent(DefaultCapacity);
    }

    public void EnsureDocCapacity(int required)
    {
        if (DocTokenArray.Length < required)
        {
            Resize(ref DocTokenArray, required);
            Resize(ref UniqueDocTokenArray, required);
            Resize(ref DActiveArray, required);
            Resize(ref FusionDocTokenArray, required);
        }
    }

    public void EnsureQueryCapacity(int required)
    {
        if (QActiveArray.Length < required)
        {
            Resize(ref QActiveArray, required);
            Resize(ref TermMatchedCharsArray, required);
            Resize(ref TermHasWholeArray, required);
            Resize(ref TermHasJoinedArray, required);
            Resize(ref TermHasPrefixArray, required);
            Resize(ref TermFirstPosArray, required);
            Resize(ref TermIdfArray, required);
            Resize(ref FusionQueryTokenArray, required);
        }
    }

    private void Resize<T>(ref T[] array, int newSize)
    {
        ArrayPool<T>.Shared.Return(array);
        array = ArrayPool<T>.Shared.Rent(newSize);
    }

    public void Dispose()
    {
        if (DocTokenArray != null) ArrayPool<StringSlice>.Shared.Return(DocTokenArray);
        if (UniqueDocTokenArray != null) ArrayPool<StringSlice>.Shared.Return(UniqueDocTokenArray);
        if (QActiveArray != null) ArrayPool<bool>.Shared.Return(QActiveArray);
        if (DActiveArray != null) ArrayPool<bool>.Shared.Return(DActiveArray);
        if (TermMatchedCharsArray != null) ArrayPool<float>.Shared.Return(TermMatchedCharsArray);
        if (TermHasWholeArray != null) ArrayPool<bool>.Shared.Return(TermHasWholeArray);
        if (TermHasJoinedArray != null) ArrayPool<bool>.Shared.Return(TermHasJoinedArray);
        if (TermHasPrefixArray != null) ArrayPool<bool>.Shared.Return(TermHasPrefixArray);
        if (TermFirstPosArray != null) ArrayPool<int>.Shared.Return(TermFirstPosArray);
        if (TermIdfArray != null) ArrayPool<float>.Shared.Return(TermIdfArray);
        if (FusionQueryTokenArray != null) ArrayPool<StringSlice>.Shared.Return(FusionQueryTokenArray);
        if (FusionDocTokenArray != null) ArrayPool<StringSlice>.Shared.Return(FusionDocTokenArray);
    }
}
