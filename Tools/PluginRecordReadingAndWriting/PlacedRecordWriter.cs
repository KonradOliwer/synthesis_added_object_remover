using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>Runs write orders as overrides in the patch; records are addressed by key. Not thread-safe.</summary>
internal sealed class PlacedRecordWriter(PlacedOverrideWriter overrides, RecordHandles handles)
{
    public void Write(IReadOnlyList<WriteOrder> orders)
    {
        foreach (var order in orders)
        {
            var location = handles.LocationOf(order.Record);
            Apply(overrides.GetOrAddOverride(handles.RecordOf(order.Record), location.WinningCell.FormKey, location.InPersistentList), order);
        }
    }

    private static void Apply(IPlaced placed, WriteOrder order)
    {
        switch (order)
        {
            case SetInitiallyDisabled:
                placed.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;
                break;
            case SetPosition set:
                placed.Placement!.Position = ToP3Float(set.Position);
                break;
            case SetPlacement set:
                placed.Placement!.Position = ToP3Float(set.Placement.Position);
                placed.Placement.Rotation = ToP3Float(set.Placement.Rotation);
                placed.Scale = set.Placement.Scale;
                break;
            case SetEnableParent set:
                placed.EnableParent = new EnableParent
                {
                    Reference = new FormLink<IPlacedGetter>(set.Parent.ToFormKey()),
                    Flags = set.Opposite ? EnableParent.Flag.SetEnableStateToOppositeOfParent : default,
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown write order.");
        }
    }

    private static P3Float ToP3Float(Vector3 vector) => new(vector.X, vector.Y, vector.Z);
}
