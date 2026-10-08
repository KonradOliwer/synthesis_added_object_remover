using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace AddedObjectRemover.Tests.CsvFileWriting;

public sealed class CsvFileWritingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "aor-csv-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static CsvTable Table(string fileName, params string[][] rows) =>
        new(fileName, ["a", "b"], [.. rows.Select(row => row.ToImmutableArray())]);

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    [InlineData("line\rbreak", "\"line\rbreak\"")]
    [InlineData("", "")]
    public void QuoteWrapsOnlyFieldsThatNeedIt(string value, string expected) =>
        Assert.Equal(expected, CsvFormat.Quote(value));

    [Fact]
    public void OptionalTextTurnsMissingIntoEmptyAndQuotesTheRest()
    {
        Assert.Equal(string.Empty, CsvFormat.OptionalText(null));
        Assert.Equal("\"x,y\"", CsvFormat.OptionalText("x,y"));
    }

    [Fact]
    public void NumbersAndBoolsIgnoreTheCurrentCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            Assert.Equal("1.5", CsvFormat.Number(1.5f));
            Assert.Equal(["1.5", "-2", "0.25"], CsvFormat.Numbers(new System.Numerics.Vector3(1.5f, -2, 0.25f)));
            Assert.Equal("-12", CsvFormat.Int(-12));
            Assert.Equal("true", CsvFormat.Bool(true));
            Assert.Equal("false", CsvFormat.Bool(false));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void EmptyOrValuesGivesEmptyFieldsForNothingAndTheValuesOtherwise()
    {
        Assert.Equal(["", "", ""], CsvRow.EmptyOrValues<string>(null, 3, _ => ["x"]));
        Assert.Equal(["x", "y"], CsvRow.EmptyOrValues("source", 3, source => ["x", "y"]));
    }

    [Fact]
    public void WriteCreatesTheFolderAndWritesUtf8WithHeaderAndRows()
    {
        var written = CsvWriter.Write(_folder, Table("x.csv", ["1", "2"], ["3", "4"]));

        Assert.Equal(Path.Combine(_folder, "x.csv"), written.Path);
        Assert.Equal(2, written.Rows);
        Assert.Equal(["a,b", "1,2", "3,4"], File.ReadAllLines(written.Path));
        Assert.Equal(Encoding.UTF8.GetPreamble(), File.ReadAllBytes(written.Path).Take(3));
    }

    [Fact]
    public void WriteAllContinuesPastAFileThatCannotBeWritten()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "x.csv"));

        var result = CsvWriter.WriteAll(
            _folder, [Table("x.csv"), Table("y.csv")], (table, exception) => $"{table.FileName} failed");

        Assert.Equal("y.csv", Assert.Single(result.Written).FileName);
        Assert.Equal("x.csv failed", Assert.Single(result.Failures));
    }

    [Fact]
    public void FindReturnsTheNamedFileOrNull()
    {
        ImmutableArray<WrittenTable> tables = [new("x.csv", 1, "p", TimeSpan.Zero)];

        Assert.Equal("x.csv", WrittenTables.Find(tables, "x.csv")?.FileName);
        Assert.Null(WrittenTables.Find(tables, "y.csv"));
    }
}
