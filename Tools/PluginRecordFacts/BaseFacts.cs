using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>The base record types that the shape rules and the visible left-behind kinds tell apart.</summary>
public enum BaseRecordKind { Npc, Light, SoundMarker, AcousticSpace, TextureSet, IdleMarker, Static, Furniture, Activator, Door, Container, TreeOrPlant, LooseItem, Other }

/// <summary>The engine's IsMarker record flag, by the base types that define it with that meaning.</summary>
public enum MarkerFlagKind { XMarker, FurnitureMarker, DoorMarker, OtherMarker }

/// <summary>What the run needs to know about one base record; read once per base.</summary>
/// <param name="Resolved">False when the base is not in the load order; every other value is then empty.</param>
/// <param name="RecordTypeName">The record type, as reports name it (e.g. "Static").</param>
/// <param name="ModelPath">The model path as the record gives it; null without a model.</param>
/// <param name="ObjectBounds">The record's Object Bounds; null when the record type has none or they are missing.</param>
/// <param name="ScriptNames">The names of the scripts an activator runs; empty for other record types.</param>
/// <param name="NamedByScript">The first quest or scene that a script on the base names, for any record type; null when none does.</param>
/// <param name="LightRadius">A light's radius when above zero.</param>
/// <param name="SoundMaxDistance">A sound marker's maximum hearing distance when above zero.</param>
public sealed record BaseFacts(
    RecordKey Record,
    bool Resolved,
    BaseRecordKind Kind,
    string? RecordTypeName,
    string? EditorId,
    string? ModelPath,
    Box? ObjectBounds,
    MarkerFlagKind? MarkerFlag,
    ImmutableArray<string> ScriptNames,
    ScriptNamedRecord? NamedByScript,
    float? LightRadius,
    float? SoundMaxDistance)
{
    public static BaseFacts Unresolved(RecordKey record) =>
        new(record, false, BaseRecordKind.Other, null, null, null, null, null, ImmutableArray<string>.Empty, null, null, null);
}

/// <summary>Opens the base-facts reader of one run, so no run reuses what another run read.</summary>
public interface IBaseFactsFactory
{
    /// <param name="records">The index of the run's plugin records.</param>
    IBaseFacts Open(IRecordIndex records);
}

/// <summary>The base records of the load order, by base; each value depends only on its key.</summary>
public interface IBaseFacts
{
    BaseFacts Of(BaseKey baseKey);
}
