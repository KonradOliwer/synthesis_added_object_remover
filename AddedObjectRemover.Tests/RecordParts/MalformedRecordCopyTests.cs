using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.RecordParts;

/// <summary>
/// A placed record whose script data the game library cannot parse fails as malformed user data when the patch writing copies it,
/// into the patch or into the trial plugin of the restore step, so that step can stop instead of the run.
/// </summary>
public sealed class MalformedRecordCopyTests : IDisposable
{
    private const float PositionTolerance = 0.01f;
    private const float RotationTolerance = 0.0001f;
    private const float ScaleTolerance = 0.0001f;

    private static readonly PlacedRecordParts Parts = new(new PlacedPartTolerances(PositionTolerance, RotationTolerance, ScaleTolerance));

    private readonly string _folder = Directory.CreateTempSubdirectory().FullName;
    private readonly FormKey _cell;
    private readonly ISkyrimModDisposableGetter _plugin;
    private readonly IPlacedObjectGetter _crate;

    public MalformedRecordCopyTests()
    {
        var mod = new FixtureMod("Broken.esp");
        var quest = mod.Mod.Quests.AddNew("NamedQuest");
        var cell = new Cell(mod.NextKey(), FixtureMod.Release) { EditorID = "BrokenInterior", Flags = Cell.Flag.IsInteriorCell };
        mod.Mod.Cells.AddInteriorCell(cell);
        var crate = new PlacedObject(mod.NextKey(), FixtureMod.Release) { Placement = new Placement(), VirtualMachineAdapter = TestScripts.Naming(quest.FormKey) };
        crate.Base.SetTo(mod.AddMarkerStatic("CrateBase"));
        cell.Temporary.Add(crate);
        _cell = cell.FormKey;
        _plugin = BrokenScriptData.WriteAndOpen(mod.Mod, _folder, BrokenScriptData.Fault.UnknownPropertyType);
        _crate = _plugin.EnumerateMajorRecords<IPlacedObjectGetter>().Single();
    }

    public void Dispose()
    {
        _plugin.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void CopyingTheRestOfAPlacedRecordFailsAsMalformedUserData()
    {
        var rest = Parts.Parts.Single(entry => entry.Part == PlacedParts.BaseAndData).Compare;
        var target = new PlacedObject(_crate.FormKey, FixtureMod.Release);

        var failure = Assert.Throws<MalformedRecordException>(() => rest.CopyInto(target, _crate));

        Assert.True(Failures.IsRecoverable(failure));
    }

    [Fact]
    public void GivingAPlacedRecordAsAnOverrideFailsAsMalformedUserData()
    {
        var patch = new SkyrimMod(FixturePatcherState.PatchModKey, FixtureMod.Release);
        var loadOrder = new LoadOrder<IModListing<ISkyrimModGetter>>(new[] { _plugin }.Select(mod => new ModListing<ISkyrimModGetter>(mod, enabled: true)));
        var writer = new PlacedOverrideWriter(new CellOverrides(new SynthesisWriteCache(loadOrder.ToMutableLinkCache<ISkyrimMod, ISkyrimModGetter>(patch)), patch), Parts);

        var failure = Assert.Throws<MalformedRecordException>(() => writer.GetOrAddOverride(_crate, _cell, inPersistentList: false));

        Assert.True(Failures.IsRecoverable(failure));
    }
}
