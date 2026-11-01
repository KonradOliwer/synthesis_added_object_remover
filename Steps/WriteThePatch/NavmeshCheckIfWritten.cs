using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

internal sealed class NavmeshCheckIfWritten(IPluginRecords plugin, IPatchWriting writing) : INavmeshCheckIfWritten
{
    public NavmeshFault? FindFaultIfWritten(IReadOnlyList<WriteOrder> orders) => writing.FindNavmeshFaultIfWritten(plugin.ReadWritableRecords(), orders);
}
