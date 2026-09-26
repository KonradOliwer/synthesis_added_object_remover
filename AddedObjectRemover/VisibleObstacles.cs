using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// The visible placed objects that remain after this run: every non-target object of any plugin
/// and the target's visible objects that are not removed. Thread-safe.
/// </summary>
internal sealed class VisibleObstacles(SupporterIndex nonTargetObjects, VisibleTargetIndex remainingTargets, ObjectContainment containment)
{
    public bool IsInsideAny(FormKey spaceKey, Vector3 point) =>
        containment.FindContainingVisible(nonTargetObjects.GetSpace(spaceKey), point, skipReplaced: false) >= 0
        || remainingTargets.AnyContains(spaceKey, point, containment);
}
