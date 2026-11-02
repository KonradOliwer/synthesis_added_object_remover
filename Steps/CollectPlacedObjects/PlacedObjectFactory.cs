using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>Creates the target and other-mod objects of the placed records, with the ids left for the result to give.</summary>
internal sealed class PlacedObjectFactory(
    WhatToCollect plan, PlacedRecordFacts facts, TargetAreaNames areaNames, ChangedAreaRecordCollector changedAreaRecords)
{
    public TargetObject CreateTarget(PlacedRecordFact record, TargetOrigin origin, PlacedVersionFact? earlierVersion)
    {
        var (position, rotation) = PlacementOf(record);
        return new TargetObject(
            Id: default,
            Key: record.Key,
            EditorId: record.EditorId,
            SpaceKey: record.Space.Key,
            Cell: record.Cell.Key != record.Space.Key ? record.Cell : null,
            Transform: new PlacedTransform(position, Mat3.FromEuler(rotation), ReferenceScale.Normalize(record.Scale)),
            Rotation: rotation,
            Base: record.Base,
            IsTeleportDoor: record.IsTeleportDoor,
            IsPrimitive: record.IsPrimitive,
            HasMapMarker: record.HasMapMarker,
            IsPersistent: record.IsPersistent,
            HasEnableParent: record.Placement.Presence.EnableParent is not null,
            NamedByScript: record.NamedByScript,
            HasLocationRefType: record.HasLocationRefType,
            ReferenceRadius: record.ReferenceRadius,
            PrimitiveBounds: record.PrimitiveBounds,
            CollisionLayer: record.Winner.Reference.CollisionLayer,
            IsCullingVolume: record.Winner.Reference.IsCullingVolume,
            Area: areaNames.Name(record.Space, record.Cell.Key, position),
            Origin: origin,
            EarlierVersion: earlierVersion is { } earlier ? changedAreaRecords.CreateEarlierVersion(record, earlier) : null)
        {
            CellDeleted = changedAreaRecords.DeletedInFinalWorld(record.Cell),
        };
    }

    public OtherObject CreateOtherObject(PlacedRecordFact record, ObjectChange? change)
    {
        var (position, rotation) = PlacementOf(record);
        return new OtherObject(
            default,
            record.Key,
            record.Space.Key,
            record.WinningMod,
            plan.Needs.OtherModDetails ? record.EditorId : null,
            record.Base,
            position,
            rotation,
            ReferenceScale.Normalize(record.Scale),
            record.IsPrimitive,
            record.HasMapMarker,
            change,
            RolesOf(record),
            ChangeWithoutCleanedMod: null)
        {
            CellDeleted = changedAreaRecords.DeletedInFinalWorld(record.Cell),
        };
    }

    private ReferenceRoles RolesOf(PlacedRecordFact record) =>
        (record.IsPersistent ? ReferenceRoles.Persistent : ReferenceRoles.None)
        | (record.IsTeleportDoor ? ReferenceRoles.TeleportDoor : ReferenceRoles.None)
        | (record.Placement.Presence.EnableParent is not null ? ReferenceRoles.HasEnableParent : ReferenceRoles.None)
        | (record.NamedByScript is not null ? ReferenceRoles.ScriptNamesQuestOrScene : ReferenceRoles.None)
        | (record.HasLocationRefType ? ReferenceRoles.LocationRefType : ReferenceRoles.None)
        | (facts.LinkedFromPlacedRecords.Contains(record.Key) ? ReferenceRoles.LinkedFromPlacedRecord : ReferenceRoles.None)
        | (record.HasMapMarker ? ReferenceRoles.MapMarker : ReferenceRoles.None);

    /// <remarks>Only for a record whose presence is <see cref="ShownInGame.Present"/>, which has a position and a rotation.</remarks>
    private static (Vector3 Position, Vector3 Rotation) PlacementOf(PlacedRecordFact record) =>
        (record.Placement.Position!.Value, record.Placement.EulerRotation!.Value);
}
