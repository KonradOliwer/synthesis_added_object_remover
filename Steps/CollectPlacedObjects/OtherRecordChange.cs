using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// Whether the game shows another mod's placed record differently from its accepted values (<see cref="AcceptedVersions"/>), and
/// which plugins the log names for it. Each object property is decided on its own; the object is changed when any of them is.
/// </summary>
internal static class OtherRecordChange
{
    // Plugins that re-save a record round its floats, so a difference this small is no change. Base and presence are exact.
    private const float PositionTolerance = 0.01f;
    private const float RotationTolerance = 0.0001f;
    private const float ScaleTolerance = 0.0001f;

    /// <summary>Ranks a change only the patch holds below every version index, so any real mod's change is named first.</summary>
    private const int PatchOnlyRank = -1;

    /// <summary>The object properties, each as "the two versions differ": position, rotation, scale, base and presence.</summary>
    private static readonly Func<PlacedVersionFact, PlacedVersionFact, bool>[] ObjectProperties =
    [
        static (first, second) => DiffersBeyond(
            first.Placement.Position, second.Placement.Position, static (one, other) => Vector3.Abs(one - other), PositionTolerance),
        static (first, second) => DiffersBeyond(first.Placement.EulerRotation, second.Placement.EulerRotation, Vectors.ShortestAngles, RotationTolerance),
        static (first, second) => MathF.Abs(ReferenceScale.Normalize(first.Scale) - ReferenceScale.Normalize(second.Scale)) > ScaleTolerance,
        static (first, second) => first.Base != second.Base,
        static (first, second) => first.Placement.Presence != second.Placement.Presence,
    ];

    /// <returns>Null when the game shows every object property as accepted.</returns>
    public static ObjectChange? Decide(PlacedRecordFact record, AcceptedWorld world)
    {
        var versions = record.Versions;
        VersionTrust[] trusts = [.. versions.Select(version => world.TrustOf(version.Plugin))];
        Func<int, int, bool>[] differs =
            [.. ObjectProperties.Select(property => (Func<int, int, bool>)((first, second) => property(versions[first], versions[second])))];
        var acceptances = AcceptedVersions.Decide(trusts, differs);
        return acceptances.Any(acceptance => acceptance.Changed) ? NameTheChange(versions, acceptances) : null;
    }

    /// <param name="gapOf">Per axis, how far apart two values are.</param>
    private static bool DiffersBeyond(Vector3? first, Vector3? second, Func<Vector3, Vector3, Vector3> gapOf, float tolerance) =>
        (first, second) switch
        {
            ({ } one, { } other) => gapOf(one, other) is var gap && (gap.X > tolerance || gap.Y > tolerance || gap.Z > tolerance),
            _ => first.HasValue != second.HasValue,
        };

    /// <summary>
    /// Of the changed properties, the one whose changing version loads last names the change; on a tie, the first in property
    /// order. A property whose shown value only the patch holds has no changing version and names the change only when every
    /// changed property is such, so a real mod's change is never hidden behind an earlier patcher's.
    /// </summary>
    private static ObjectChange NameTheChange(ImmutableArray<PlacedVersionFact> versions, PropertyAcceptance[] acceptances)
    {
        var naming = acceptances.Where(acceptance => acceptance.Changed).MaxBy(acceptance => acceptance.Changing ?? PatchOnlyRank);
        return new ObjectChange(
            naming.Changing is { } changing ? versions[changing].Plugin : null,
            naming.Accepted is { } accepted ? versions[accepted].Plugin : null);
    }
}
