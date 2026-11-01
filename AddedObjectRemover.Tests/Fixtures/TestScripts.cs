using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Script data for in-memory records.</summary>
internal static class TestScripts
{
    public const string ScriptName = "SomeScript";

    /// <summary>One script with one object property per given record, in that order.</summary>
    public static VirtualMachineAdapter Naming(params FormKey[] objects)
    {
        var script = new ScriptEntry { Name = ScriptName };
        foreach (var named in objects)
        {
            script.Properties.Add(new ScriptObjectProperty { Name = PropertyName(script.Properties.Count), Object = new FormLink<ISkyrimMajorRecordGetter>(named) });
        }
        var adapter = new VirtualMachineAdapter();
        adapter.Scripts.Add(script);
        return adapter;
    }

    public static string PropertyName(int index) => $"Property{index}";
}
