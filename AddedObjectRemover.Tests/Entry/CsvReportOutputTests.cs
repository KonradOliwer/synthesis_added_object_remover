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

        Assert.Equal(new ReportFolderPrepared(StepOutcome.Performed, Warning: null), _output.Prepare(new ReportFileSettings(true, _folder)));

        Assert.False(File.Exists(known));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void PrepareLeavesFilesAloneWhenWritingIsOff()
    {
        Directory.CreateDirectory(_folder);
        var known = Path.Combine(_folder, ReportFileNames.EdgesFileName);
        File.WriteAllText(known, "old");

        Assert.Equal(new ReportFolderPrepared(StepOutcome.SwitchedOff, Warning: null), _output.Prepare(new ReportFileSettings(false, _folder)));

        Assert.True(File.Exists(known));
    }

    private static ReportTables Performed(params CsvTable[] tables) => new(StepOutcome.Performed, [.. tables], Failure: null);

    [Fact]
    public void WriteCreatesTheFolderAndReportsEachFile()
    {
        var result = _output.Write(new ReportFileSettings(true, _folder), Performed(Table("x.csv", ["1", "2"], ["3", "4"]), Table("y.csv")));

        Assert.Empty(result.Warnings);
        Assert.Equal(["x.csv", "y.csv"], result.Written.Select(table => table.FileName));
        Assert.Equal([2, 0], result.Written.Select(table => table.Rows));
        Assert.Equal(Path.Combine(_folder, "x.csv"), result.Written[0].Path);
        Assert.Equal(["a,b", "1,2", "3,4"], File.ReadAllLines(result.Written[0].Path));
    }

    [Fact]
    public void WriteGivenSwitchedOffTablesWritesNothingAndReturnsSwitchedOff()
    {
        var result = _output.Write(new ReportFileSettings(true, _folder), ReportTables.SwitchedOff);

        Assert.Equal(StepOutcome.SwitchedOff, result.Outcome);
        Assert.Empty(result.Written);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void TablesThatWereNotBuiltAreNotWrittenAndTheirOutcomeIsPassedOn()
    {
        var stopped = new ReportTables(
            StepOutcome.StoppedByUnexpectedError, [], new PartFailure(ReportFileParts.ReportFiles, "InvalidOperationException: it broke", Subject: null));

        var result = _output.Write(new ReportFileSettings(true, _folder), stopped);

        Assert.Equal(StepOutcome.StoppedByUnexpectedError, result.Outcome);
        Assert.Empty(result.Written);
        Assert.Null(result.Failure);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void AFailureOfTheWritingAsAWholeIsReturnedNotThrown()
    {
        var brokenTable = new CsvTable("x.csv", ["a", "b"], default);

        var result = _output.Write(new ReportFileSettings(true, _folder), Performed(brokenTable));

        Assert.Equal(StepOutcome.StoppedByUnexpectedError, result.Outcome);
        Assert.Empty(result.Written);
        Assert.Empty(result.Warnings);
        Assert.Equal(ReportFileParts.ReportFiles, result.Failure?.Part);
        Assert.StartsWith("NullReferenceException", result.Failure?.Failure);
    }

    [Fact]
    public void AFileThatCannotBeWrittenBecomesAWarningAndTheRestAreWritten()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "x.csv"));

        var result = _output.Write(new ReportFileSettings(true, _folder), Performed(Table("x.csv"), Table("y.csv")));

        Assert.Equal("y.csv", Assert.Single(result.Written).FileName);
        var problem = Assert.Single(result.Warnings);
        Assert.Equal(ReportFileAction.WritingFile, problem.Action);
        Assert.Equal(_folder, problem.Folder);
        Assert.Equal("x.csv", problem.FileName);
        Assert.NotEmpty(problem.ExceptionMessage);
    }

    [Fact]
    public void EarlierFilesThatCannotBeDeletedBecomeAProblemAndTheOutcomeIsStillPerformed()
    {
        Directory.CreateDirectory(_folder);
        var readOnly = Path.Combine(_folder, ReportFileNames.EdgesFileName);
        File.WriteAllText(readOnly, "old");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        ReportFolderPrepared prepared;
        try
        {
            prepared = _output.Prepare(new ReportFileSettings(true, _folder));
        }
        finally
        {
            File.SetAttributes(readOnly, FileAttributes.Normal);
        }

        Assert.Equal(StepOutcome.Performed, prepared.Outcome);
        var problem = Assert.IsType<ReportFileProblem>(prepared.Warning);
        Assert.Equal(ReportFileAction.DeletingEarlierFiles, problem.Action);
        Assert.Equal(_folder, problem.Folder);
        Assert.Null(problem.FileName);
        Assert.NotEmpty(problem.ExceptionMessage);
    }
}
