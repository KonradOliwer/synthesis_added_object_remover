namespace AddedObjectRemover.Tests.Architecture;

public class ConsoleUseTests
{
    /// <summary>The entry, the run log's console implementation, and the tee that copies the console to a file.</summary>
    private static readonly string[] ConsoleFiles = ["Program.cs", "RunLog.cs", "ConsoleLogFile.cs"];

    [Fact]
    public void OnlyTheEntryAndTheRunLogUseTheConsole()
    {
        var offenders = ProductionSources.Files()
            .Where(file => !ConsoleFiles.Contains(Path.GetFileName(file)))
            .Where(file => File.ReadAllText(file).Contains("Console.", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }
}
