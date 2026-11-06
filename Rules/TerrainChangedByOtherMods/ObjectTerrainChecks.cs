using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

/// <summary>The checks over one view; each set of settings gets its own check, whose cell comparisons it alone reads. Thread-safe.</summary>
internal sealed class ObjectTerrainChecks(ITerrainView view, IBaseObjectShapes shapes, TerrainPlugins plugins) : IObjectTerrainChecks
{
    private readonly ComputedOncePerKey<ChangedTerrainSettings, ObjectTerrainCheck> _checks = new(Publication.BuiltOnce, EqualityComparer<ChangedTerrainSettings>.Default);

    public TerrainCheckOutcome Check(TargetObject target, ChangedTerrainSettings settings)
    {
        if (!settings.Runs) return default;
        var check = _checks.Get(settings, () => new ObjectTerrainCheck(view, shapes, plugins, settings)).Check(target);
        return new TerrainCheckOutcome(check.Hit, check.Failure);
    }
}
