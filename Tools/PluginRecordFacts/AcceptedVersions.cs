namespace AddedObjectRemover;

/// <summary>How far the values of one version of a record are believed.</summary>
public enum VersionTrust
{
    /// <summary>
    /// Never accepted and never the changing version. It is also skipped when looking for the version just below another
    /// one, so a copy it holds changes no other version's standing.
    /// </summary>
    Transparent,

    /// <summary>Every value is accepted, also one only carried over from a lower version.</summary>
    Full,

    /// <summary>
    /// OwnEdits owns a property when it differs from the version just below it (the one just below in the list of
    /// versions, not a master of its plugin), or when no version lies below it. A value it only carries over is not owned.
    /// </summary>
    OwnEdits,

    /// <summary>Never accepted; it can be the changing version.</summary>
    None,
}

/// <param name="Accepted">The index of the highest version trusted for the property; null when no version is.</param>
/// <param name="Changed">The final version's value differs from the accepted one, or there is no accepted version.</param>
/// <param name="Changing">
/// The index of the lowest version above the accepted one (the lowest at all when there is none) that differs from the
/// accepted value and holds the final value, skipping transparent versions; null when nothing changed, or when only a
/// transparent version holds the final value.
/// </param>
public readonly record struct PropertyAcceptance(int? Accepted, bool Changed, int? Changing);

/// <summary>
/// Decides which version of a record holds the value to compare the final value with. Versions are indices in load
/// order, the last one being the final winner.
/// </summary>
public static class AcceptedVersions
{
    /// <param name="trusts">The trust of each version, lowest first; the last entry is the final version.</param>
    /// <param name="differsByProperty">
    /// Per property, whether the values of two versions differ beyond the property's tolerance. It is false for two equal
    /// indices.
    /// </param>
    /// <returns>One result per property, in the order of <paramref name="differsByProperty"/>.</returns>
    public static PropertyAcceptance[] Decide(
        IReadOnlyList<VersionTrust> trusts, IReadOnlyList<Func<int, int, bool>> differsByProperty) =>
        [.. differsByProperty.Select(differs => DecideProperty(trusts, differs))];

    private static PropertyAcceptance DecideProperty(IReadOnlyList<VersionTrust> trusts, Func<int, int, bool> differs)
    {
        var finalIndex = trusts.Count - 1;
        var accepted = FindHighestAccepted(trusts, differs);
        var changed = accepted is not { } acceptedIndex || differs(acceptedIndex, finalIndex);
        var changing = changed ? FindChanging(trusts, differs, accepted, finalIndex) : null;
        return new PropertyAcceptance(accepted, changed, changing);
    }

    private static int? FindHighestAccepted(IReadOnlyList<VersionTrust> trusts, Func<int, int, bool> differs)
    {
        for (var index = trusts.Count - 1; index >= 0; index--)
        {
            if (IsAccepted(trusts, differs, index)) return index;
        }
        return null;
    }

    private static bool IsAccepted(IReadOnlyList<VersionTrust> trusts, Func<int, int, bool> differs, int index) =>
        trusts[index] switch
        {
            VersionTrust.Full => true,
            VersionTrust.OwnEdits => OwnsProperty(trusts, differs, index),
            _ => false,
        };

    private static bool OwnsProperty(IReadOnlyList<VersionTrust> trusts, Func<int, int, bool> differs, int index) =>
        FindVersionJustBelow(trusts, index) is not { } below || differs(below, index);

    private static int? FindVersionJustBelow(IReadOnlyList<VersionTrust> trusts, int index)
    {
        for (var below = index - 1; below >= 0; below--)
        {
            if (trusts[below] != VersionTrust.Transparent) return below;
        }
        return null;
    }

    private static int? FindChanging(
        IReadOnlyList<VersionTrust> trusts, Func<int, int, bool> differs, int? accepted, int finalIndex)
    {
        var firstAbove = accepted is { } acceptedIndex ? acceptedIndex + 1 : 0;
        for (var index = firstAbove; index <= finalIndex; index++)
        {
            if (trusts[index] != VersionTrust.Transparent
                && (accepted is not { } other || differs(other, index))
                && !differs(index, finalIndex))
            {
                return index;
            }
        }
        return null;
    }
}
