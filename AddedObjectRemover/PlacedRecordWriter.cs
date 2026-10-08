using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>Runs write orders as overrides in the patch and answers the one read the orders depend on; records are addressed by key. Not thread-safe.</summary>
internal sealed class PlacedRecordWriter(PlacedOverrideWriter overrides, RecordHandles handles)
{
    public bool HasEnableParent(RecordKey record) => handles.RecordOf(record).EnableParent != null;

    public void Write(IReadOnlyList<WriteOrder> orders)
    {
        foreach (var order in orders)
        {
            var placed = overrides.GetOrAddOverride(handles.RecordOf(order.Record), handles.LocationOf(order.Record));
            Apply(placed, order);
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
                placed.Placement!.Position = new P3Float(set.Position.X, set.Position.Y, set.Position.Z);
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
}
