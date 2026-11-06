using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>One of the cleaned mod's objects an area counts.</summary>
/// <param name="Volume">What it adds to the area's size: its box volume; nothing for an object whose mesh holds only effect shapes.</param>
/// <param name="IsOverriddenLater">One of the cleaned mod's objects a later plugin holds, which no decision removes.</param>
internal readonly record struct CountedObject(TargetId Target, double Volume, bool IsOverriddenLater);

/// <summary>
/// The cleaned mod's own objects the game draws in a column an area is measured in: those whose box centre lies within the radius
/// horizontally, at any height. Thread-safe.
/// </summary>
internal sealed class AreaContents
{
    private readonly IVisibleTargetObjects _shownTargets;

    /// <summary>The number of targets; the cleaned mod's objects later plugins hold have the ids after theirs.</summary>
    private readonly int _targetCount;

    /// <summary>How each target weighs in an area, by target index.</summary>
    private readonly Weight[] _weights;

    /// <summary>The box centre of each shown target, by target index; null for a target the index does not hold.</summary>
    private readonly Vector3?[] _boxCentres;

    /// <param name="targets">Every target, by id.</param>
    /// <param name="overriddenLater">The cleaned mod's objects later plugins hold, by id, after the targets.</param>
    /// <param name="shownTargets">The shown targets where the rounds measure them, then the shown objects of <paramref name="overriddenLater"/>.</param>
    public AreaContents(ImmutableArray<TargetObject> targets, ImmutableArray<TargetObject> overriddenLater, IVisibleTargetObjects shownTargets, IBaseObjectShapes shapes)
    {
        _shownTargets = shownTargets;
        _targetCount = targets.Length;
        _weights = [.. targets.Concat(overriddenLater).Select(target => WeightOf(target, shapes))];
        _boxCentres = BoxCentresOf(_weights.Length, shownTargets);
    }

    /// <returns>The cleaned mod's own drawn objects in the column, in ascending id.</returns>
    public IEnumerable<CountedObject> In(RecordKey space, Vector2 centre, float radius)
    {
        var inColumn = new List<VisibleTargetNear>();
        _shownTargets.InColumn(space, centre, radius, inColumn);
        foreach (var near in inColumn)
        {
            var weight = _weights[near.Target.Index];
            if (weight == Weight.NotCounted) continue;
            yield return new CountedObject(near.Target, weight == Weight.ByCountOnly ? 0d : near.Box.Volume, IsOverriddenLater: near.Target.Index >= _targetCount);
        }
    }

    /// <summary>Whether <see cref="In"/> lists the target for the column.</summary>
    public bool Counts(TargetId target, Vector2 centre, float radius) =>
        _weights[target.Index] != Weight.NotCounted && _boxCentres[target.Index] is { } boxCentre && Vectors.IsWithinHorizontally(boxCentre, centre, radius);

    private static Vector3?[] BoxCentresOf(int targetCount, IVisibleTargetObjects shownTargets)
    {
        var centres = new Vector3?[targetCount];
        var inSpace = new List<VisibleTargetNear>();
        foreach (var space in shownTargets.Spaces)
        {
            shownTargets.InColumn(space, Vector2.Zero, float.PositiveInfinity, inSpace);
            foreach (var near in inSpace) centres[near.Target.Index] = near.Box.Center;
        }
        return centres;
    }

    /// <remarks>An effect volume (fog, light rays) can be far larger than the solid pieces around it, so only its count weighs.</remarks>
    private static Weight WeightOf(TargetObject target, IBaseObjectShapes shapes)
    {
        if (target.Origin != TargetOrigin.CleanedMod) return Weight.NotCounted;
        return ClearedAreaKinds.Of(shapes, target) switch
        {
            ClearedAreaObjectKind.Drawn => Weight.BySize,
            ClearedAreaObjectKind.EffectOnly => Weight.ByCountOnly,
            _ => Weight.NotCounted,
        };
    }

    private enum Weight : byte
    {
        /// <summary>Another mod's object the cleaned mod changed.</summary>
        NotCounted,

        /// <summary>By number and by box volume.</summary>
        BySize,

        /// <summary>By number alone.</summary>
        ByCountOnly,
    }
}
