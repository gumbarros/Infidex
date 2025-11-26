namespace Infidex.Coverage;

internal readonly struct StringSlice(int offset, int length, int position, int hash)
{
    public readonly int Offset = offset;
    public readonly int Length = length;
    public readonly int Position = position;
    public readonly int Hash = hash;
}
