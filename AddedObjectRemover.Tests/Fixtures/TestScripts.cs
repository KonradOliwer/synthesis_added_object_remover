using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Script data for in-memory records.</summary>
internal static class TestScripts
{
    /// <summary>One script with one object property per given record, in that order.</summary>
    public static VirtualMachineAdapter Naming(params FormKey[] objects)
    {
        var script = new ScriptEntry { Name = "SomeScript" };
        foreach (var named in objects)
        {
            script.Properties.Add(new ScriptObjectProperty { Name = $"Property{script.Properties.Count}", Object = new FormLink<ISkyrimMajorRecordGetter>(named) });
        }
        var adapter = new VirtualMachineAdapter();
        adapter.Scripts.Add(script);
        return adapter;
    }
}
