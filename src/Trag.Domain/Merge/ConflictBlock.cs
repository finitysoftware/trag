namespace Trag.Domain.Merge;

// Where one conflict sits in its file, as line indexes: the opening <<<<<<< marker, the ||||||| marker of the base when
// Git wrote one (-1 otherwise), the ======= separator and the closing >>>>>>> marker.
public sealed record ConflictBlock(int Start, int Base, int Separator, int End)
{
    // Our side runs from after the opening marker to the base or the separator; theirs from after the separator to the end.
    public int MineEnd => Base >= 0 ? Base : Separator;
}
