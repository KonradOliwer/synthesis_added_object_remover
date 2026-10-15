using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests;

/// <summary>Record keys for test data, spelled as the FormKey's own plugin name; the patcher spells them from the load order.</summary>
internal static class TestRecordKeys
{
    public static RecordKey ToRecordKey(this FormKey key) => new(key.ModKey.ToPluginName(), key.ID);
}
