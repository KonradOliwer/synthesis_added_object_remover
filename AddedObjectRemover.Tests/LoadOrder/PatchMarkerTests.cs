using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using AddedObjectRemover.Tests.EndToEnd;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// The patch marker: the patch writes it once into its header description, and the plugin reader flags every plugin whose header
/// description holds it.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public class PatchMarkerTests
{
    private const string MarkerText = "Added Object Remover patch";
    private const string OtherDescription = "Hand-made notes";

    [Fact]
    public void TheMarkerTextIsTheDecidedWording() => Assert.Equal(MarkerText, PatchMarker.Text);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void APatchWithoutADescriptionGetsTheMarker(string? description)
    {
        var state = CreateState(description);

        WritingInto(state).WritePatchMarker();

        Assert.Equal(MarkerText, state.PatchMod.ModHeader.Description);
    }

    [Fact]
    public void WritingTheMarkerAgainLeavesTheDescriptionAsItIs()
    {
        var state = CreateState(description: null);
        var writing = WritingInto(state);

        writing.WritePatchMarker();
        writing.WritePatchMarker();

        Assert.Equal(MarkerText, state.PatchMod.ModHeader.Description);
    }

    [Fact]
    public void ADescriptionWithoutTheMarkerIsKeptAndTheMarkerGoesOnANewLineAfterIt()
    {
        var state = CreateState(OtherDescription);

        WritingInto(state).WritePatchMarker();

        Assert.Equal($"{OtherDescription}\r\n{MarkerText}", state.PatchMod.ModHeader.Description);
    }

    [Fact]
    public void WritingTheMarkerTwiceAfterAnotherDescriptionAppendsItOnce()
    {
        var state = CreateState(OtherDescription);
        var writing = WritingInto(state);

        writing.WritePatchMarker();
        writing.WritePatchMarker();

        Assert.Equal($"{OtherDescription}\r\n{MarkerText}", state.PatchMod.ModHeader.Description);
    }

    [Fact]
    public void ADescriptionThatAlreadyHoldsTheMarkerIsLeftAlone()
    {
        var description = $"Made for a group. {MarkerText}";
        var state = CreateState(description);

        WritingInto(state).WritePatchMarker();

        Assert.Equal(description, state.PatchMod.ModHeader.Description);
    }

    [Fact]
    public void ThePluginReaderFlagsEveryPluginWhoseHeaderDescriptionHoldsTheMarker()
    {
        var state = FixturePatcherState.Create(
            [
                Plugin("Marked.esp", MarkerText),
                Plugin("Longer.esp", $"Made for a group. {MarkerText}"),
                Plugin("Other.esp", OtherDescription),
                Plugin("None.esp", description: null),
            ],
            Path.GetTempPath(),
            Path.GetTempPath());
        using var world = FixturePatcherState.CreateFinalWorld(state);

        var flags = new[] { "Marked.esp", "Longer.esp", "Other.esp", "None.esp" }
            .Select(name => world.Plugins.Find(new PluginName(name))!.CarriesPatchMarker);

        Assert.Equal([true, true, false, false], flags);
    }

    [Fact]
    public void TheMarkerThePatchWritesIsTheOneThePluginReaderFlags()
    {
        var state = CreateState(description: null);
        WritingInto(state).WritePatchMarker();

        using var world = FixturePatcherState.CreateFinalWorld(state);

        Assert.True(world.Plugins.Find(FixturePatcherState.PatchModKey.ToPluginName())!.CarriesPatchMarker);
    }

    /// <summary>A run that stops before any processing leaves the patch as Synthesis handed it over.</summary>
    [Fact]
    public void ARunThatStopsOnItsSettingsWritesNoMarker()
    {
        var folder = Directory.CreateTempSubdirectory("aor-patch-marker-");
        try
        {
            var state = FixturePatcherState.Create(
                [Plugin("Skyrim.esm", description: null)], folder.FullName, Path.Combine(folder.FullName, FixturePatcherState.PatchModKey.FileName));

            PipelineRun.CaptureConsole(() => Program.RunPatch(state, () => new Settings(), workers: 1, FixturePatcherState.WritePluginsFile(state)));

            Assert.Null(state.PatchMod.ModHeader.Description);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static SkyrimMod Plugin(string fileName, string? description)
    {
        var plugin = new SkyrimMod(ModKey.FromNameAndExtension(fileName), SkyrimRelease.SkyrimSE);
        plugin.ModHeader.Description = description;
        return plugin;
    }

    private static IPatcherState<ISkyrimMod, ISkyrimModGetter> CreateState(string? description)
    {
        var state = FixturePatcherState.Create([], Path.GetTempPath(), Path.GetTempPath());
        state.PatchMod.ModHeader.Description = description;
        return state;
    }

    private static PatchWriting WritingInto(IPatcherState<ISkyrimMod, ISkyrimModGetter> state) =>
        new(new SynthesisWriteCache(state.LinkCache), state.PatchMod);
}
