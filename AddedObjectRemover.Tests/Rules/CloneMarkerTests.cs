using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Rules;

public class CloneMarkerTests
{
    private const int LongestEditorId = 255;

    /// <summary>The length of a marker without its two names: the prefix, six hex digits and two separators.</summary>
    private const int MarkerWithoutNames = 17;

    private static readonly RecordKey Original = new(new PluginName("Skyrim.esm"), 0x01FE52);
    private static readonly PluginName CleanedMod = new("Camp Mod.esp");
    private static readonly PluginName Longest = new(new string('a', 251) + ".esp");
    private static readonly PluginName OtherLongest = new(new string('b', 251) + ".esp");

    [Fact]
    public void AMarkerReadsBackAsTheOriginalAndTheCleanedMod()
    {
        Assert.Equal(new CloneMarkerReading(true, new CloneMark(Original, CleanedMod)), Read(CloneMarker.Of(Original, CleanedMod)));
    }

    [Fact]
    public void AMarkerHoldsOnlyLettersDigitsAndUnderscores()
    {
        Assert.Matches("^[A-Za-z0-9_]+$", CloneMarker.Of(Original, new PluginName("Ünïcode mod (v2).esp")));
        Assert.Matches("^[A-Za-z0-9_]+$", CloneMarker.Of(Original with { Plugin = Longest }, OtherLongest));
    }

    [Fact]
    public void TheMarkerIsTheSameHoweverTheLoadOrderSpellsTheNames()
    {
        Assert.Equal(
            CloneMarker.Of(Original, CleanedMod),
            CloneMarker.Of(Original with { Plugin = new PluginName("SKYRIM.ESM") }, new PluginName("camp mod.ESP")));
        Assert.Equal(
            CloneMarker.Of(Original with { Plugin = Longest }, OtherLongest),
            CloneMarker.Of(Original with { Plugin = new PluginName(Longest.FileName.ToUpperInvariant()) }, OtherLongest));
    }

    /// <summary>The names spelled out fill exactly the longest EditorID; one character more and both names are hashed.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void AMarkerSpellsTheNamesOutUpToTheLongestEditorIdAndHashesThemBeyond(int beyond, bool hashed)
    {
        var cleaned = new PluginName(new string('c', (LongestEditorId - MarkerWithoutNames) / 2 - Original.Plugin.FileName.Length - ".esp".Length + beyond) + ".esp");

        var marker = CloneMarker.Of(Original, cleaned);

        Assert.True(marker.Length <= LongestEditorId);
        Assert.Equal(hashed, marker.EndsWith(CloneMarker.HashedNameOf(cleaned), StringComparison.Ordinal));
        Assert.Equal(hashed ? LongestEditorId - 204 : LongestEditorId, marker.Length);
        Assert.Equal(new CloneMark(Original, cleaned), CloneMarker.Read(marker, PluginOfHashedName(Original.Plugin, cleaned)).Mark);
    }

    [Fact]
    public void AMarkerWithTheLongestPluginNamesStaysShortReadsBackAgainstTheLoadOrderAndSurvivesAPluginFile()
    {
        var marker = CloneMarker.Of(Original with { Plugin = Longest }, OtherLongest);
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var mod = new SkyrimMod(ModKey.FromNameAndExtension("Marked.esp"), SkyrimRelease.SkyrimSE);
            mod.Statics.AddNew(marker);
            var path = Path.Combine(folder, mod.ModKey.FileName);
            mod.WriteToBinary(path);
            using var read = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
            var editorId = read.Statics.Single().EditorID;

            Assert.True(marker.Length <= LongestEditorId);
            Assert.Equal(new CloneMark(Original with { Plugin = Longest }, OtherLongest), CloneMarker.Read(editorId, PluginOfHashedName(Longest, OtherLongest)).Mark);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A hashed name no plugin of the load order matches, or one written although the names fit spelled out, is no marker as written.</summary>
    [Fact]
    public void AHashedMarkerTheLoadOrderCannotMatchOrThatShouldBeSpelledOutIsBroken()
    {
        var hashed = CloneMarker.Of(Original with { Plugin = Longest }, OtherLongest);
        var hashedThoughItFits = $"AORClone_01FE52_{CloneMarker.HashedNameOf(Original.Plugin)}_{CloneMarker.HashedNameOf(CleanedMod)}";

        Assert.True(CloneMarker.Read(hashed, PluginOfHashedName(Longest)).IsBroken);
        Assert.True(CloneMarker.Read(hashedThoughItFits, PluginOfHashedName(Original.Plugin, CleanedMod)).IsBroken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("RockRef")]
    [InlineData("AORClone")]
    public void AnEditorIdWithoutThePrefixIsNoMarker(string? editorId)
    {
        Assert.Equal(new CloneMarkerReading(false, null), Read(editorId));
        Assert.False(Read(editorId).IsBroken);
    }

    [Theory]
    [InlineData("AORClone_")]
    [InlineData("AORClone_01FE52_736B7972696D2E65736D")]
    [InlineData("AORClone_01FE52_736B7972696D2E65736D_")]
    [InlineData("AORClone_1FE52_736B7972696D2E65736D_63616D70206D6F642E657370")]
    [InlineData("AORClone_01FE52_736B7972696D2E65736D_63616D70206D6F642E65737")]
    [InlineData("AORClone_01FE52_736b7972696d2e65736d_63616D70206D6F642E657370")]
    [InlineData("AORClone_01FE52_736B7972696D2E65736D_63616D70206D6F642E657370_00")]
    [InlineData("AORClone_0XFE52_736B7972696D2E65736D_63616D70206D6F642E657370")]
    public void AnEditorIdWithThePrefixButNotExactlyAsAMarkerIsWrittenIsABrokenMarker(string editorId)
    {
        Assert.Equal(new CloneMarkerReading(true, null), Read(editorId));
        Assert.True(Read(editorId).IsBroken);
    }

    [Fact]
    public void TheWrittenMarkerOfTheTestValuesIsTheOneTheBrokenRowsChange()
    {
        Assert.Equal("AORClone_01FE52_736B7972696D2E65736D_63616D70206D6F642E657370", CloneMarker.Of(Original, CleanedMod));
    }

    private static CloneMarkerReading Read(string? editorId) => CloneMarker.Read(editorId, PluginOfHashedName(Original.Plugin, CleanedMod, Longest, OtherLongest));

    /// <summary>Matches a hashed name against the given plugins, as the scan matches it against the load order.</summary>
    private static Func<string, PluginName?> PluginOfHashedName(params PluginName[] loadOrder) =>
        hashed => loadOrder.Where(plugin => CloneMarker.HashedNameOf(plugin) == hashed).Cast<PluginName?>().SingleOrDefault();
}
