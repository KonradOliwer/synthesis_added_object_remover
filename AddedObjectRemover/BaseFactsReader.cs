using System.Collections.Immutable;
using System.Numerics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Reads base records from the load order into <see cref="BaseFacts"/>, once per base. Thread-safe:
/// Mutagen's load-order link cache is documented as multithread safe.
/// </summary>
internal sealed class BaseFactsReader(ILinkCache linkCache) : IBaseFacts
{
    private readonly ComputedOncePerKey<RecordKey, BaseFacts> _byBase = new(Publication.FirstWriteWins, EqualityComparer<RecordKey>.Default);

    public BaseFacts Of(BaseKey baseKey) => _byBase.Get(baseKey.Record, () => Read(baseKey));

    public static Box ToBox(IObjectBoundsGetter bounds) => Box.FromCorners(
        new Vector3(bounds.First.X, bounds.First.Y, bounds.First.Z),
        new Vector3(bounds.Second.X, bounds.Second.Y, bounds.Second.Z));

    /// <summary>
    /// Resolves by the base link's own type; <see cref="IMajorRecordGetter"/> would make the link
    /// cache enumerate every record of every mod.
    /// </summary>
    private BaseFacts Read(BaseKey baseKey)
    {
        if (!linkCache.TryResolve(baseKey.Record.ToFormKey(), LinkTypeOf(baseKey.Kind), out var record)) return BaseFacts.Unresolved(baseKey.Record);
        return new BaseFacts(
            record.FormKey.ToRecordKey(),
            Resolved: true,
            KindOf(record),
            record.Registration.Name,
            record.EditorID,
            ModelPathOf(record),
            record is IObjectBoundedOptionalGetter { ObjectBounds: { } bounds } ? ToBox(bounds) : null,
            MarkerFlagOf(record),
            ScriptNamesOf(record),
            record is ILightGetter { Radius: > 0 } light ? light.Radius : null,
            record is ISoundMarkerGetter marker ? MaxHearingDistanceOf(marker) : null);
    }

    private static Type LinkTypeOf(BaseLinkKind kind) => kind switch
    {
        BaseLinkKind.PlaceableObject => typeof(IPlaceableObjectGetter),
        BaseLinkKind.Npc => typeof(INpcGetter),
        BaseLinkKind.Hazard => typeof(IHazardGetter),
        BaseLinkKind.Projectile => typeof(IProjectileGetter),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static BaseRecordKind KindOf(IMajorRecordGetter record) => record switch
    {
        INpcGetter => BaseRecordKind.Npc,
        ILightGetter => BaseRecordKind.Light,
        ISoundMarkerGetter => BaseRecordKind.SoundMarker,
        IAcousticSpaceGetter => BaseRecordKind.AcousticSpace,
        ITextureSetGetter => BaseRecordKind.TextureSet,
        IIdleMarkerGetter => BaseRecordKind.IdleMarker,
        IStaticGetter => BaseRecordKind.Static,
        IFurnitureGetter => BaseRecordKind.Furniture,
        IActivatorGetter => BaseRecordKind.Activator,
        IDoorGetter => BaseRecordKind.Door,
        _ => BaseRecordKind.Other,
    };

    private static string? ModelPathOf(IMajorRecordGetter record) =>
        record is IModeledGetter { Model: { } model } && !string.IsNullOrWhiteSpace(model.File.GivenPath)
            ? model.File.GivenPath
            : null;

    /// <summary>
    /// The base's own major record flags carry the engine's IsMarker bit (map, XMarkerHeading and
    /// similar marker bases). Only these four base types define that bit with this meaning; other
    /// types reuse the same bit value for unrelated flags.
    /// </summary>
    private static MarkerFlagKind? MarkerFlagOf(IMajorRecordGetter record) => record switch
    {
        IStaticGetter { MajorFlags: var flags } when flags.HasFlag(Static.MajorFlag.IsMarker) => MarkerFlagKind.XMarker,
        IFurnitureGetter { MajorFlags: var flags } when flags.HasFlag(Furniture.MajorFlag.IsMarker) => MarkerFlagKind.FurnitureMarker,
        IActivatorGetter { MajorFlags: var flags } when flags.HasFlag(Mutagen.Bethesda.Skyrim.Activator.MajorFlag.IsMarker) => MarkerFlagKind.OtherMarker,
        IDoorGetter { MajorFlags: var flags } when flags.HasFlag(Door.MajorFlag.IsMarker) => MarkerFlagKind.DoorMarker,
        _ => null,
    };

    private static ImmutableArray<string> ScriptNamesOf(IMajorRecordGetter record) =>
        record is IActivatorGetter { VirtualMachineAdapter: { } adapter }
            ? [.. adapter.Scripts.Select(script => script.Name)]
            : ImmutableArray<string>.Empty;

    private float? MaxHearingDistanceOf(ISoundMarkerGetter marker) =>
        marker.SoundDescriptor.TryResolve(linkCache, out var descriptor)
        && descriptor.OutputModel.TryResolve(linkCache, out var outputModel)
        && outputModel.Attenuation is { MaxDistance: > 0 } attenuation
            ? attenuation.MaxDistance
            : null;
}
