using System.Collections.Immutable;
using System.Numerics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// Reads base records from the load order into <see cref="BaseFacts"/>, once per base. Thread-safe:
/// Mutagen's load-order link cache is documented as multithread safe.
/// </summary>
/// <param name="knownPlugins">The run's known plugin names, which spell the keys of the records the bases' scripts name.</param>
internal sealed class BaseFactsReader(ILinkCache linkCache, KnownPluginNames knownPlugins) : IBaseFacts
{
    /// <summary>Keyed by record and link kind, as a record is resolved by the link's type.</summary>
    private readonly ComputedOncePerKey<BaseKey, BaseFacts> _byBase = new(Publication.FirstWriteWins, EqualityComparer<BaseKey>.Default);
    private readonly PluginNames _names = new(knownPlugins);

    public BaseFacts Of(BaseKey baseKey) => _byBase.Get(baseKey, () => Read(baseKey));

    public static Box ToBox(P3Int16 first, P3Int16 second) => Box.FromCorners(new Vector3(first.X, first.Y, first.Z), new Vector3(second.X, second.Y, second.Z));

    /// <summary>
    /// Resolves by the base link's own type; <see cref="IMajorRecordGetter"/> would make the link
    /// cache enumerate every record of every mod.
    /// </summary>
    private BaseFacts Read(BaseKey baseKey)
    {
        var formKey = baseKey.Record.ToFormKey();
        var linkType = LinkTypeOf(baseKey.Kind);
        if (MutagenCalls.Read(() => linkCache.TryResolve(formKey, linkType, out var resolved) ? resolved : null) is not { } record)
        {
            return BaseFacts.Unresolved(baseKey.Record);
        }
        var stored = MutagenCalls.Read(() => (
            record.EditorID,
            Model: (record as IModeledGetter)?.Model?.File.GivenPath,
            Bounds: (record as IObjectBoundedOptionalGetter)?.ObjectBounds is { } bounds ? (bounds.First, bounds.Second) : ((P3Int16, P3Int16)?)null,
            Scripts: (record as IActivatorGetter)?.VirtualMachineAdapter?.Scripts.Select(script => script.Name).ToArray(),
            Adapter: (record as IHaveVirtualMachineAdapterGetter)?.VirtualMachineAdapter,
            LightRadius: (record as ILightGetter)?.Radius,
            HearingDistance: record is ISoundMarkerGetter marker
                && marker.SoundDescriptor.TryResolve(linkCache, out var descriptor)
                && descriptor.OutputModel.TryResolve(linkCache, out var outputModel)
                && outputModel.Attenuation is { MaxDistance: > 0 } attenuation
                    ? attenuation.MaxDistance
                    : (float?)null));
        return new BaseFacts(
            baseKey.Record,
            Resolved: true,
            KindOf(record),
            record.Registration.Name,
            stored.EditorID,
            string.IsNullOrWhiteSpace(stored.Model) ? null : stored.Model,
            stored.Bounds is (var first, var second) ? ToBox(first, second) : null,
            MarkerFlagOf(record),
            stored.Scripts is { } scripts ? [.. scripts] : ImmutableArray<string>.Empty,
            ScriptPropertyReader.FindQuestOrScene(stored.Adapter, linkCache, _names),
            stored.LightRadius is > 0 ? stored.LightRadius : null,
            stored.HearingDistance);
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
        IContainerGetter => BaseRecordKind.Container,
        ITreeGetter or IFloraGetter => BaseRecordKind.TreeOrPlant,
        IMiscItemGetter or IWeaponGetter or IArmorGetter or IBookGetter or IIngredientGetter or IIngestibleGetter
            or IAmmunitionGetter or IScrollGetter or ISoulGemGetter or IKeyGetter => BaseRecordKind.LooseItem,
        _ => BaseRecordKind.Other,
    };

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
}
