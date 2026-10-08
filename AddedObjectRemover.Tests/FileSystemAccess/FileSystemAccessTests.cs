namespace AddedObjectRemover.Tests.FileSystemAccess;

public sealed class FileSystemAccessTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "aor-files-" + Guid.NewGuid().ToString("N"));

    public FileSystemAccessTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void GuardReturnsNullWhenTheActionSucceeds() =>
        Assert.Null(FileFailures.Guard(() => { }, _ => "unused"));

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(FileNotFoundException))]
    public void GuardCatchesAnExpectedFailureAndReturnsTheCallersMessage(Type failure) =>
        Assert.Equal(
            "failed: boom",
            FileFailures.Guard(() => throw (Exception)Activator.CreateInstance(failure, "boom")!, exception => $"failed: {exception.Message}"));

    [Fact]
    public void GuardLetsOtherFailuresThrough() =>
        Assert.Throws<InvalidOperationException>(
            () => FileFailures.Guard(() => throw new InvalidOperationException(), _ => "unused"));

    [Fact]
    public void DeleteListedRemovesOnlyTheListedFilesThatExist()
    {
        var listed = Path.Combine(_folder, "a.csv");
        var other = Path.Combine(_folder, "notes.txt");
        File.WriteAllText(listed, "x");
        File.WriteAllText(other, "y");

        Files.DeleteListed(_folder, ["a.csv", "missing.csv"]);

        Assert.False(File.Exists(listed));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void ResolveAgainstKeepsARootedPathAndAnchorsARelativeOne()
    {
        var rooted = Path.Combine(_folder, "rooted");

        Assert.Equal(rooted, FolderPaths.ResolveAgainst("elsewhere", rooted));
        Assert.Equal(Path.Combine(_folder, "reports"), FolderPaths.ResolveAgainst(_folder, "reports"));
    }
}
