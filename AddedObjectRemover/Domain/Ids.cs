using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>A target object's position in <see cref="World.Targets"/>, which is in <see cref="FormKeyOrder"/>.</summary>
internal readonly record struct TargetId(int Index) : IComparable<TargetId>
{
    public int CompareTo(TargetId other) => Index.CompareTo(other.Index);
}

/// <summary>
/// Another mod's object, dense over <see cref="World.Rivals"/> and then <see cref="World.Backdrop"/>,
/// each list in <see cref="FormKeyOrder"/>.
/// </summary>
internal readonly record struct OtherId(int Index) : IComparable<OtherId>
{
    public int CompareTo(OtherId other) => Index.CompareTo(other.Index);
}

/// <summary>
/// The order every "first" and "lowest" choice follows: origin plugin name, then local FormID. It
/// does not depend on the load order, so the choices stay the same when the load order changes.
/// </summary>
internal static class FormKeyOrder
{
    public static IComparer<FormKey> Comparer { get; } = Comparer<FormKey>.Create(Compare);

    private static int Compare(FormKey first, FormKey second)
    {
        var byPlugin = StringComparer.OrdinalIgnoreCase.Compare(first.ModKey.FileName.String, second.ModKey.FileName.String);
        return byPlugin != 0 ? byPlugin : first.ID.CompareTo(second.ID);
    }
}
