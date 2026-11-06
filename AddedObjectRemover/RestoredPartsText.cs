using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AddedObjectRemover.Run.RunAllSteps.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover;

/// <summary>
/// The text of the restored parts file: the first line, the check line, then one part per line as cleaned mod, record key, part
/// and source plugin, separated by tabs. Every line ends with a line feed. The check value is the first digits of the SHA-256 of
/// the part lines as written, so a torn or edited file is never used.
/// </summary>
internal static class RestoredPartsText
{
    /// <summary>Names the kind of file and the version of its layout.</summary>
    internal const string FirstLine = "Added Object Remover restored parts 1";

    private const string CheckLinePrefix = "check\t";
    private const char LineEnd = '\n';
    private const char FieldSeparator = '\t';
    private const char KeySeparator = ':';
    private const int CheckValueDigits = 16;
    private const int FieldsPerPart = 4;
    private const int CleanedModField = 0;
    private const int RecordField = 1;
    private const int WordField = 2;
    private const int SourceField = 3;
    private const string RegionPartPrefix = "cell region ";

    /// <summary>Followed by the grid points, as ascending runs such as <c>0-40,200,210-220</c>.</summary>
    private const string LandHeightsPrefix = "land heights ";

    private const char RunSeparator = ',';
    private const char RunEndSeparator = '-';

    /// <summary>
    /// The fixed word of every part but a cell's region, which names its region after <see cref="RegionPartPrefix"/>, and a LAND
    /// record's heights, which name their points after <see cref="LandHeightsPrefix"/>. A new word needs
    /// a new <see cref="FirstLine"/> version: an older copy reads a file with a word it does not know as not usable.
    /// </summary>
    private static readonly ImmutableDictionary<RestoredPartName, string> Words = new Dictionary<RestoredPartName, string>
    {
        [new RestoredPartName.Placed(PlacedParts.Position)] = "placed position",
        [new RestoredPartName.Placed(PlacedParts.Rotation)] = "placed rotation",
        [new RestoredPartName.Placed(PlacedParts.Scale)] = "placed scale",
        [new RestoredPartName.Placed(PlacedParts.Presence)] = "placed presence",
        [new RestoredPartName.Placed(PlacedParts.NavDoorLink)] = "placed door link",
        [new RestoredPartName.Placed(PlacedParts.BaseAndData)] = "placed base",
        [CellPartOf(CellPart.Lighting)] = "cell lighting",
        [CellPartOf(CellPart.ImageSpace)] = "cell image space",
        [CellPartOf(CellPart.AcousticSpace)] = "cell acoustic space",
        [CellPartOf(CellPart.Music)] = "cell music",
        [CellPartOf(CellPart.WaterLevel)] = "cell water level",
        [CellPartOf(CellPart.WaterLook)] = "cell water look",
        [CellPartOf(CellPart.SkyAndWeatherFromRegion)] = "cell sky and weather from region",
        [CellPartOf(CellPart.Name)] = "cell name",
        [CellPartOf(CellPart.Ownership)] = "cell ownership",
        [CellPartOf(CellPart.EncounterZone)] = "cell encounter zone",
        [CellPartOf(CellPart.Location)] = "cell location",
        [CellPartOf(CellPart.CantTravelFromHere)] = "cell cant travel from here",
        [CellPartOf(CellPart.NoLodWater)] = "cell no lod water",
        [CellPartOf(CellPart.PublicArea)] = "cell public area",
        [CellPartOf(CellPart.HandChanged)] = "cell hand changed",
        [CellPartOf(CellPart.ShowSky)] = "cell show sky",
        [CellPartOf(CellPart.UseSkyLighting)] = "cell use sky lighting",
        [CellPartOf(CellPart.OffLimits)] = "cell off limits",
        [CellPartOf(CellPart.CantWait)] = "cell cant wait",
        [CellPartOf(CellPart.RestOfCell)] = "cell rest",
        [new RestoredPartName.LandTextureQuarter(0)] = "land texture quarter 0",
        [new RestoredPartName.LandTextureQuarter(1)] = "land texture quarter 1",
        [new RestoredPartName.LandTextureQuarter(2)] = "land texture quarter 2",
        [new RestoredPartName.LandTextureQuarter(3)] = "land texture quarter 3",
    }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<string, RestoredPartName> PartsByWord = Words.ToImmutableDictionary(entry => entry.Value, entry => entry.Key);

    public static string Write(ImmutableArray<RestoredPart> parts)
    {
        var partLines = string.Concat(parts.Select(part => WritePart(part) + LineEnd));
        return $"{FirstLine}{LineEnd}{CheckLinePrefix}{CheckValueOf(partLines)}{LineEnd}{partLines}";
    }

    /// <param name="file">The file the text was read from, which a problem names.</param>
    public static RestoredPartsRead Read(string text, string file)
    {
        var (firstLine, afterFirst) = SplitFirstLine(text);
        if (firstLine != FirstLine) return Unused(new RestoredPartsFileProblem.UnknownFirstLine(file, firstLine));

        var (checkLine, partLines) = SplitFirstLine(afterFirst);
        if (checkLine != CheckLinePrefix + CheckValueOf(partLines)) return Unused(new RestoredPartsFileProblem.CheckValueDiffers(file));

        var lines = partLines.Split(LineEnd)[..^1].Select(line => SplitFields(line, file)).ToList();
        var unknownWord = lines.Select(fields => fields[WordField]).FirstOrDefault(word => ReadWord(word) is null);
        if (unknownWord is not null) return Unused(new RestoredPartsFileProblem.UnknownPart(file, unknownWord));

        return new RestoredPartsRead([.. lines.Select(ReadPart)], Problem: null);
    }
    private static RestoredPartsRead Unused(RestoredPartsFileProblem problem) => new([], problem);

    /// <returns>The text up to the first line end, and the text after it; the whole text and nothing when it holds no line end.</returns>
    private static (string Line, string After) SplitFirstLine(string text)
    {
        var end = text.IndexOf(LineEnd);
        return end < 0 ? (text, string.Empty) : (text[..end], text[(end + 1)..]);
    }

    private static string CheckValueOf(string partLines) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(partLines)))[..CheckValueDigits];

    private static string WritePart(RestoredPart part) =>
        string.Join(FieldSeparator, part.CleanedMod.FileName, part.Record.ToString(), WordOf(part.Part), part.Source.FileName);

    private static string WordOf(RestoredPartName part) => part switch
    {
        RestoredPartName.Cell { Part: { Part: CellPart.Region, Region: { } region } } => RegionPartPrefix + region,
        RestoredPartName.LandHeights heights => LandHeightsPrefix + WritePoints(heights.Points),
        _ => Words[part],
    };

    private static string WritePoints(PointSet points) =>
        string.Join(RunSeparator, RunsOf(points.Points).Select(run => run.First == run.Last ? Number(run.First) : $"{Number(run.First)}{RunEndSeparator}{Number(run.Last)}"));

    private static string Number(int point) => point.ToString(CultureInfo.InvariantCulture);

    /// <param name="ascending">Distinct points in ascending order.</param>
    private static List<(int First, int Last)> RunsOf(IEnumerable<int> ascending)
    {
        List<(int First, int Last)> runs = [];
        foreach (var point in ascending)
        {
            if (runs.Count > 0 && runs[^1].Last == point - 1) runs[^1] = (runs[^1].First, point);
            else runs.Add((point, point));
        }
        return runs;
    }

    private static PointSet ReadPoints(string text) =>
        PointSet.Of(text.Split(RunSeparator).SelectMany(run =>
        {
            var bounds = run.Split(RunEndSeparator).Select(bound => int.Parse(bound, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray();
            return Enumerable.Range(bounds[0], bounds[^1] - bounds[0] + 1);
        }));

    /// <remarks>
    /// The check value matched, so a copy of this patcher wrote the line: a layout it cannot read is its own fault. A part word it
    /// does not know can come from a copy of another version, which Synthesis lets the user pin, so that word is no fault.
    /// </remarks>
    private static string[] SplitFields(string line, string file)
    {
        var fields = line.Split(FieldSeparator);
        return fields.Length == FieldsPerPart ? fields : throw new InvalidDataException($"A line of {file} holds {fields.Length} fields, not {FieldsPerPart}: {line}");
    }

    private static RestoredPart ReadPart(string[] fields) =>
        new(new PluginName(fields[CleanedModField]), ReadKey(fields[RecordField]), ReadWord(fields[WordField])!, new PluginName(fields[SourceField]));

    /// <returns>Null for a word this version does not know.</returns>
    private static RestoredPartName? ReadWord(string word)
    {
        if (word.StartsWith(RegionPartPrefix, StringComparison.Ordinal)) return CellPartOf(CellPart.Region, ReadKey(word[RegionPartPrefix.Length..]));
        if (word.StartsWith(LandHeightsPrefix, StringComparison.Ordinal)) return new RestoredPartName.LandHeights(ReadPoints(word[LandHeightsPrefix.Length..]));
        return PartsByWord.GetValueOrDefault(word);
    }

    /// <summary>Reads a key as it prints: the FormID in hexadecimal digits, then the plugin.</summary>
    private static RecordKey ReadKey(string text)
    {
        var separator = text.IndexOf(KeySeparator);
        return new RecordKey(new PluginName(text[(separator + 1)..]), uint.Parse(text[..separator], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
    }

    private static RestoredPartName CellPartOf(CellPart part, RecordKey? region = null) => new RestoredPartName.Cell(new CellPartName(part, region));
}
