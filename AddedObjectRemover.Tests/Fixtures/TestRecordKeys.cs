using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests;

/// <summary>Record keys for test data, spelled as the FormKey's own plugin name; the patcher spells them from the load order.</summary>
internal static class TestRecordKeys
{
    /// <summary>The vanilla keyword the patcher passes to the load-order read as the mark of a hold Location.</summary>
    public static readonly RecordKey HoldKeyword = new(new PluginName("Skyrim.esm"), 0x016771);

    public static RecordKey ToRecordKey(this FormKey key) => new(key.ModKey.ToPluginName(), key.ID);
}
