using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>
/// Gives test targets a chosen visibility without building real base records: a target takes the
/// <see cref="TaggedBase"/> of the visibility it should have, and the shapes from <see cref="Over"/> answer for it.
/// Any other base is answered by the wrapped shapes.
/// </summary>
internal static class TestVisibility
{
    private const uint FirstTaggedId = 0xA00;

    private static readonly ObjectVisibility[] Tagged =
    [
        ObjectVisibility.Visible,
        ObjectVisibility.MissingBase,
        ObjectVisibility.EffectOnly,
        .. Enum.GetValues<InvisibleObjectKind>().Select(ObjectVisibility.Invisible),
    ];

    private static readonly Dictionary<BaseKey, ObjectVisibility> ByTaggedBase =
        Tagged.Select((visibility, index) => (Base: TaggedBaseAt(index), Visibility: visibility)).ToDictionary(entry => entry.Base, entry => entry.Visibility);

    public static BaseKey TaggedBase(ObjectVisibility visibility) => TaggedBaseAt(Array.IndexOf(Tagged, visibility));

    /// <summary>Shapes that know only the tagged bases, for tests whose targets have no base records.</summary>
    public static IBaseObjectShapes OfTaggedBasesOnly() => Over(TestShapes.Create(TestTargets.TargetMod, "TaggedVisibilityData"));

    public static IBaseObjectShapes Over(IBaseObjectShapes inner)
    {
        var shapes = new TaggedShapes(inner);
        TestShapes.ShareMeshFiles(inner, shapes);
        return shapes;
    }

    private static BaseKey TaggedBaseAt(int index) =>
        new(new FormKey(TestTargets.TargetMod, FirstTaggedId + (uint)index).ToRecordKey(), BaseLinkKind.PlaceableObject);

    private sealed class TaggedShapes(IBaseObjectShapes inner) : IBaseObjectShapes
    {
        public BaseShape Of(BaseKey? baseKey) => inner.Of(baseKey);

        public ObjectVisibility VisibilityOf(BaseKey? baseKey, bool isPrimitive, bool hasMapMarker) =>
            baseKey is { } key && ByTaggedBase.TryGetValue(key, out var visibility) ? visibility : inner.VisibilityOf(baseKey, isPrimitive, hasMapMarker);

        public IReadOnlyList<MeasuredBase> Computed() => inner.Computed();
    }
}
