using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Entry;

public sealed class CsvReportOutputTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "aor-report-folder-" + Guid.NewGuid().ToString("N"));

    private readonly CsvReportOutput _output = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static CsvTable Table(string fileName, params string[][] rows) =>
        new(fileName, ["a", "b"], [.. rows.Select(row => row.ToImmutableArray())]);

    [Fact]
    public void PrepareDeletesEarlierKnownFilesOnly()
    {
        Directory.CreateDirectory(_folder);
        var known = Path.Combine(_folder, ReportFileNames.EdgesFileName);
        var other = Path.Combine(_folder, "notes.txt");
        File.WriteAllText(known, "old");
        File.WriteAllText(other, "mine");

        Assert.Null(_output.Prepare(new ReportOptions(true, _folder)));

        Assert.False(File.Exists(known));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void PrepareLeavesFilesAloneWhenWritingIsOff()
    {
        Directory.CreateDirectory(_folder);
        var known = Path.Combine(_folder, ReportFileNames.EdgesFileName);
        File.WriteAllText(known, "old");

        Assert.Null(_output.Prepare(new ReportOptions(false, _folder)));

        Assert.True(File.Exists(known));
    }

    [Fact]
    public void WriteCreatesTheFolderAndReportsEachFile()
    {
        var result = _output.Write(new ReportOptions(true, _folder), [Table("x.csv", ["1", "2"], ["3", "4"]), Table("y.csv")]);

        Assert.Empty(result.Warnings);
        Assert.Equal(["x.csv", "y.csv"], result.Written.Select(table => table.FileName));
        Assert.Equal([2, 0], result.Written.Select(table => table.Rows));
        Assert.Equal(Path.Combine(_folder, "x.csv"), result.Written[0].Path);
        Assert.Equal(["a,b", "1,2", "3,4"], File.ReadAllLines(result.Written[0].Path));
    }

    [Fact]
    public void WriteDoesNothingWhenWritingIsOff()
    {
        var result = _output.Write(new ReportOptions(false, _folder), [Table("x.csv")]);

        Assert.Empty(result.Written);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void AFileThatCannotBeWrittenBecomesAWarningAndTheRestAreWritten()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "x.csv"));

        var result = _output.Write(new ReportOptions(true, _folder), [Table("x.csv"),Table("y.csv")]);

        Assert.Equal("y.csv", Assert.Single(result.Written).FileName);
        Assert.StartsWith($"  Warning: writing x.csv in {_folder} failed: ", Assert.Single(result.Warnings));
    }
}
