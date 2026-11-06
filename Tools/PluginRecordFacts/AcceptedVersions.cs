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

/// <param name="Accepted">
/// The index of the highest version trusted for the property; when only a transparent version holds the final value, the highest
/// trusted version holding it, if one does; the final version when it still holds a value it was given back and some version is
/// trusted. Null when no version is trusted.
/// </param>
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
    /// <param name="givenBackFrom">
    /// Per property, in the order of <paramref name="differsByProperty"/>, the versions whose values the final version was given back
    /// (<see cref="GivenBackFrom"/>); null when the final version was given back nothing.
    /// </param>
    /// <returns>One result per property, in the order of <paramref name="differsByProperty"/>.</returns>
    public static PropertyAcceptance[] Decide(
        IReadOnlyList<VersionTrust> trusts,
        IReadOnlyList<Func<int, int, bool>> differsByProperty,
        IReadOnlyList<IReadOnlyCollection<int>>? givenBackFrom) =>
        [.. differsByProperty.Select((differs, property) => DecideProperty(trusts, differs, givenBackFrom?[property] ?? []))];

    /// <summary>
    /// The versions below the final one whose plugins are <paramref name="sources"/>, when the final version is the patch's: an earlier
    /// run gave the patch their values back.
    /// </summary>
    /// <param name="plugins">The plugin of each version, lowest first; the last entry is the final version.</param>
    /// <returns>Empty when the final version is not the patch's.</returns>
    public static int[] GivenBackFrom(IReadOnlyList<PluginName> plugins, PluginName patch, IReadOnlyCollection<PluginName> sources) =>
        plugins.Count > 0 && plugins[^1] == patch
            ? [.. Enumerable.Range(0, plugins.Count - 1).Where(index => sources.Contains(plugins[index]))]
            : [];

    /// <remarks>
    /// A final value only a transparent version holds is what an earlier patcher of the group wrote into the patch. When it equals
    /// a trusted version's value, the base game's included, that patcher put a trusted value back, so it is accepted from that
    /// version. A real plugin that sets a lower trusted version's value back still changes the property. A final value that still
    /// equals a value it was given back is accepted as the final version's own, whatever the trust of the version it came from: it is
    /// part of the world without the mod whose change it replaced. A property no version is trusted for stays unchecked.
    /// </remarks>
    private static PropertyAcceptance DecideProperty(IReadOnlyList<VersionTrust> trusts, Func<int, int, bool> differs, IReadOnlyCollection<int> givenBackFrom)
    {
        var acceptance = CompareWithHighestAccepted(trusts, differs);
        if (IsHeldOnlyByTransparentVersion(acceptance) && FindHighestAcceptedHoldingFinalValue(trusts, differs) is { } holding)
        {
            return new PropertyAcceptance(holding, Changed: false, Changing: null);
        }

        var finalIndex = trusts.Count - 1;
        return acceptance is { Changed: true, Accepted: not null } && givenBackFrom.Any(index => !differs(index, finalIndex))
            ? new PropertyAcceptance(finalIndex, Changed: false, Changing: null)
            : acceptance;
    }

    private static PropertyAcceptance CompareWithHighestAccepted(IReadOnlyList<VersionTrust> trusts, Func<int, int, bool> differs)
    {
        var finalIndex = trusts.Count - 1;
        var accepted = FindHighestAccepted(trusts, differs, static _ => true);
        var changed = accepted is not { } acceptedIndex || differs(acceptedIndex, finalIndex);
        var changing = changed ? FindChanging(trusts, differs, accepted, finalIndex) : null;
        return new PropertyAcceptance(accepted, changed, changing);
    }

    private static bool IsHeldOnlyByTransparentVersion(PropertyAcceptance acceptance) => acceptance is { Changed: true, Changing: null };

    private static int? FindHighestAcceptedHoldingFinalValue(IReadOnlyList<VersionTrust> trusts, Func<int, int, bool> differs)
    {
        var finalIndex = trusts.Count - 1;
        return FindHighestAccepted(trusts, differs, index => !differs(index, finalIndex));
    }

    /// <param name="qualifies">Whether a version may be the one found, besides being accepted.</param>
    private static int? FindHighestAccepted(IReadOnlyList<VersionTrust> trusts, Func<int, int, bool> differs, Func<int, bool> qualifies)
    {
        for (var index = trusts.Count - 1; index >= 0; index--)
        {
            if (qualifies(index) && IsAccepted(trusts, differs, index)) return index;
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
