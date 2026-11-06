using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Entry;

public sealed class RestoredPartsFileTests : IDisposable
{
    private static readonly PluginName Camp = new("CampMod.esp");
    private static readonly PluginName Base = new("Skyrim.esm");
    private static readonly PluginName Pond = new("PondMod.esp");

    private static readonly ImmutableArray<RestoredPart> EveryKindOfPart =
    [
        new(Camp, new RecordKey(Base, 0xD62), new RestoredPartName.Placed(PlacedParts.Position), Base),
        new(Camp, new RecordKey(Base, 0xD62), new RestoredPartName.Placed(PlacedParts.BaseAndData), Base),
        new(Camp, new RecordKey(Base, 0x1A2B3), new RestoredPartName.Cell(new CellPartName(CellPart.WaterLevel, Region: null)), Pond),
        new(Camp, new RecordKey(Base, 0x1A2B3), new RestoredPartName.Cell(new CellPartName(CellPart.Region, new RecordKey(Pond, 0x812))), Pond),
        new(Camp, new RecordKey(Base, 0x1A2B4), new RestoredPartName.LandHeights(PointSet.Of([0, 1, 2, 3, 40, 200, 210, 211, 212])), Base),
        new(Camp, new RecordKey(Base, 0x1A2B4), new RestoredPartName.LandTextureQuarter(2), Base),
    ];

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("aor-restored-parts-");

    public void Dispose() => _folder.Delete(recursive: true);

    private string SourcePatch => Path.Combine(_folder.FullName, "source", "Synthesis.esp");

    private string OutputPatch => Path.Combine(_folder.FullName, "output", "Synthesis.esp");

    [Fact]
    public void TheTextNamesItsFormatAndCheckValueThenOnePartPerLineWithTabsAndLineFeeds()
    {
        var lines = RestoredPartsText.Write(EveryKindOfPart).Split('\n');

        Assert.Equal(RestoredPartsText.FirstLine, lines[0]);
        Assert.Matches("^check\t[0-9A-F]{16}$", lines[1]);
        Assert.Equal(
            [
                "CampMod.esp\t000D62:Skyrim.esm\tplaced position\tSkyrim.esm",
                "CampMod.esp\t000D62:Skyrim.esm\tplaced base\tSkyrim.esm",
                "CampMod.esp\t01A2B3:Skyrim.esm\tcell water level\tPondMod.esp",
                "CampMod.esp\t01A2B3:Skyrim.esm\tcell region 000812:PondMod.esp\tPondMod.esp",
                "CampMod.esp\t01A2B4:Skyrim.esm\tland heights 0-3,40,200,210-212\tSkyrim.esm",
                "CampMod.esp\t01A2B4:Skyrim.esm\tland texture quarter 2\tSkyrim.esm",
                "",
            ],
            lines[2..]);
    }

    [Fact]
    public void ReadingTheWrittenTextGivesThePartsInTheirOrder()
    {
        var read = RestoredPartsText.Read(RestoredPartsText.Write(EveryKindOfPart), "file");

        Assert.Null(read.Problem);
        Assert.Equal<RestoredPart>(EveryKindOfPart, read.Parts);
    }

    [Fact]
    public void ATextWithoutPartsReadsAsNoParts()
    {
        var read = RestoredPartsText.Read(RestoredPartsText.Write([]), "file");

        Assert.Null(read.Problem);
        Assert.Empty(read.Parts);
    }

    [Fact]
    public void ATornTextIsNotUsed()
    {
        var text = RestoredPartsText.Write(EveryKindOfPart);

        var read = RestoredPartsText.Read(text[..^10], "file");

        Assert.Equal(new RestoredPartsFileProblem.CheckValueDiffers("file"), read.Problem);
        Assert.Empty(read.Parts);
    }

    [Fact]
    public void AnEditedPartIsNotUsed()
    {
        var text = RestoredPartsText.Write(EveryKindOfPart).Replace("placed position", "placed rotation", StringComparison.Ordinal);

        Assert.Equal(new RestoredPartsFileProblem.CheckValueDiffers("file"), RestoredPartsText.Read(text, "file").Problem);
    }

    [Fact]
    public void ATextOfAnotherVersionIsNotUsed()
    {
        var text = "Added Object Remover restored parts 2" + RestoredPartsText.Write(EveryKindOfPart)[RestoredPartsText.FirstLine.Length..];

        var read = RestoredPartsText.Read(text, "file");

        Assert.Equal(new RestoredPartsFileProblem.UnknownFirstLine("file", "Added Object Remover restored parts 2"), read.Problem);
        Assert.Empty(read.Parts);
    }

    /// <summary>
    /// Pins every part word. A copy pinned to another version of the patcher reads a word it does not know as a file not used, so a
    /// changed list needs a new version in the first line.
    /// </summary>
    [Fact]
    public void EveryPartHasItsPinnedWordAndReadsBackAsItself()
    {
        var region = new RecordKey(Pond, 0x812);
        RestoredPartName[] everyName =
        [
            .. Enum.GetValues<PlacedParts>().Where(part => part != PlacedParts.None).Select(part => new RestoredPartName.Placed(part)),
            .. Enum.GetValues<CellPart>().Select(part => new RestoredPartName.Cell(new CellPartName(part, part == CellPart.Region ? region : null))),
            new RestoredPartName.LandHeights(PointSet.Of([5])),
            .. Enumerable.Range(0, TerrainQuadrants.Count).Select(quarter => new RestoredPartName.LandTextureQuarter(quarter)),
        ];
        ImmutableArray<RestoredPart> everyPart = [.. everyName.Select(part => new RestoredPart(Camp, new RecordKey(Base, 0xD62), part, Base))];

        var text = RestoredPartsText.Write(everyPart);

        Assert.Equal("Added Object Remover restored parts 1", RestoredPartsText.FirstLine);
        Assert.Equal(
            [
                "placed position", "placed rotation", "placed scale", "placed presence", "placed door link", "placed base",
                "cell lighting", "cell image space", "cell acoustic space", "cell music", "cell water level", "cell water look",
                "cell region 000812:PondMod.esp", "cell sky and weather from region", "cell name", "cell ownership", "cell encounter zone",
                "cell location", "cell cant travel from here", "cell no lod water", "cell public area", "cell hand changed", "cell show sky",
                "cell use sky lighting", "cell off limits", "cell cant wait", "cell rest",
                "land heights 5", "land texture quarter 0", "land texture quarter 1", "land texture quarter 2", "land texture quarter 3",
            ],
            text.Split('\n')[2..^1].Select(line => line.Split('\t')[2]));
        Assert.Equal<RestoredPart>(everyPart, RestoredPartsText.Read(text, "file").Parts);
    }

    [Fact]
    public void AFileWithAPartWordThisVersionDoesNotKnowIsNotUsed()
    {
        const string partLines = "CampMod.esp\t000D62:Skyrim.esm\tplaced glow\tSkyrim.esm\n";
        var checkValue = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(partLines)))[..16];
        var text = $"{RestoredPartsText.FirstLine}\ncheck\t{checkValue}\n{partLines}";

        var read = RestoredPartsText.Read(text, "file");

        Assert.Equal(new RestoredPartsFileProblem.UnknownPart("file", "placed glow"), read.Problem);
        Assert.Empty(read.Parts);
    }

    [Fact]
    public void TheFileLiesBesideThePatchUnderThePatchsFullFileName()
    {
        Assert.Equal(Path.Combine("folder", "Synthesis.esp.restored-parts.txt"), RestoredPartsFile.PathBeside(Path.Combine("folder", "Synthesis.esp")));
    }

    [Fact]
    public void TheFileIsWrittenBesideTheOutputPatchAsUtf8WithoutAByteOrderMarkAndLeavesNoTemporaryFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPatch)!);

        new RestoredPartsFile(SourcePatch, OutputPatch, earlierCopyWroteThePatch: false).Write(EveryKindOfPart);

        var written = RestoredPartsFile.PathBeside(OutputPatch);
        Assert.Equal(RestoredPartsText.Write(EveryKindOfPart), File.ReadAllText(written));
        Assert.NotEqual(0xEF, File.ReadAllBytes(written)[0]);
        Assert.Equal([written], Directory.GetFiles(Path.GetDirectoryName(OutputPatch)!));
    }

    [Fact]
    public void TheFileBesideTheSourcePatchIsReadWhenAnEarlierCopyWroteThePatch()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SourcePatch)!);
        File.WriteAllText(RestoredPartsFile.PathBeside(SourcePatch), RestoredPartsText.Write(EveryKindOfPart));

        var read = new RestoredPartsFile(SourcePatch, OutputPatch, earlierCopyWroteThePatch: true).Read();

        Assert.Equal<RestoredPart>(EveryKindOfPart, read.Parts);
        Assert.Null(read.Problem);
    }

    [Fact]
    public void AMissingFileIsAProblemOnlyWhenAnEarlierCopyWroteThePatch()
    {
        var afterEarlierCopy = new RestoredPartsFile(SourcePatch, OutputPatch, earlierCopyWroteThePatch: true).Read();
        var afterOtherPatcher = new RestoredPartsFile(SourcePatch, OutputPatch, earlierCopyWroteThePatch: false).Read();

        Assert.Equal(new RestoredPartsFileProblem.NotFound(SourcePatch), afterEarlierCopy.Problem);
        Assert.Empty(afterEarlierCopy.Parts);
        Assert.Null(afterOtherPatcher.Problem);
        Assert.Empty(afterOtherPatcher.Parts);
    }

    [Fact]
    public void AFileBesideAPatchNoEarlierCopyWroteIsNotRead()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SourcePatch)!);
        File.WriteAllText(RestoredPartsFile.PathBeside(SourcePatch), RestoredPartsText.Write(EveryKindOfPart));

        Assert.Empty(new RestoredPartsFile(SourcePatch, OutputPatch, earlierCopyWroteThePatch: false).Read().Parts);
    }

    [Fact]
    public void EachProblemIsOneLogLineNamingTheFileAndWhatIsWrong()
    {
        Assert.Empty(LogAndReports.BuildRestoredPartsFileSections(null));
        Assert.Equal(
            [
                "Restored parts file not found beside p.esp.",
                "Restored parts file not read: f.txt. denied",
                "Restored parts file not used: f.txt. Its first line \"Something else\" is not known.",
                "Restored parts file not used: f.txt. Its part \"placed glow\" is not known.",
                "Restored parts file not used: f.txt. Its check value does not match its content.",
            ],
            new RestoredPartsFileProblem[]
            {
                new RestoredPartsFileProblem.NotFound("p.esp"),
                new RestoredPartsFileProblem.NotRead("f.txt", "denied"),
                new RestoredPartsFileProblem.UnknownFirstLine("f.txt", "Something else"),
                new RestoredPartsFileProblem.UnknownPart("f.txt", "placed glow"),
                new RestoredPartsFileProblem.CheckValueDiffers("f.txt"),
            }.Select(problem => Assert.Single(Assert.Single(LogAndReports.BuildRestoredPartsFileSections(problem)).Lines)));
    }
}
