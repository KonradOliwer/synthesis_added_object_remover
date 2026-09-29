namespace AddedObjectRemover.Tests.Meshes;

public class AssetProblemLogTests
{
    private static AssetProblem Problem(string mesh, AssetProblemKind kind = AssetProblemKind.Unreadable, string? message = null) =>
        new(mesh, kind, message ?? $"{kind} {mesh}");

    [Fact]
    public void FirstRecordOfAMeshAndKindWins()
    {
        var log = new AssetProblemLog();
        var mark = log.Mark();
        var first = Problem(@"meshes\a.nif", message: "first");
        log.Add(first);
        log.Add(Problem(@"meshes\a.nif", message: "second"));

        Assert.Equal(first, Assert.Single(log.Since(mark)));
    }

    [Fact]
    public void TheSameMeshWithDifferentKindsKeepsBoth()
    {
        var log = new AssetProblemLog();
        var mark = log.Mark();
        log.Add(Problem(@"meshes\a.nif", AssetProblemKind.NotFound));
        log.Add(Problem(@"meshes\a.nif", AssetProblemKind.ReadWarning));

        Assert.Equal(2, log.Since(mark).Length);
    }

    [Fact]
    public void MeshPathsDifferingInCaseAreOneProblem()
    {
        var log = new AssetProblemLog();
        var mark = log.Mark();
        var first = Problem(@"Meshes\A.nif", message: "first");
        log.Add(first);
        log.Add(Problem(@"meshes\a.nif", message: "second"));

        Assert.Equal(first, Assert.Single(log.Since(mark)));
    }

    [Fact]
    public void SinceReturnsOnlyProblemsRecordedAfterTheMark()
    {
        var log = new AssetProblemLog();
        log.Add(Problem(@"meshes\before.nif"));
        var mark = log.Mark();
        var after = Problem(@"meshes\after.nif");
        log.Add(after);

        Assert.Equal(after, Assert.Single(log.Since(mark)));
    }

    [Fact]
    public void ARepeatedProblemAfterTheMarkIsNotReturnedAgain()
    {
        var log = new AssetProblemLog();
        log.Add(Problem(@"meshes\a.nif"));
        var mark = log.Mark();
        log.Add(Problem(@"MESHES\A.NIF"));

        Assert.Empty(log.Since(mark));
    }

    [Fact]
    public void NestedMarksEachSeeTheirOwnLaterProblems()
    {
        var log = new AssetProblemLog();
        var outer = log.Mark();
        log.Add(Problem(@"meshes\one.nif"));
        var inner = log.Mark();
        log.Add(Problem(@"meshes\two.nif"));

        Assert.Equal(2, log.Since(outer).Length);
        Assert.Equal([@"meshes\two.nif"], log.Since(inner).Select(problem => problem.Mesh));
    }

    [Fact]
    public void SinceSortsByMeshIgnoringCaseThenByOrdinalMessage()
    {
        var log = new AssetProblemLog();
        var mark = log.Mark();
        log.Add(Problem(@"meshes\b.nif", AssetProblemKind.NotFound, "m"));
        log.Add(Problem(@"meshes\A.nif", AssetProblemKind.Unreadable, "z"));
        log.Add(Problem(@"meshes\a.NIF", AssetProblemKind.ReadWarning, "b"));

        Assert.Equal(["b", "z", "m"], log.Since(mark).Select(problem => problem.Message));
    }

    [Fact]
    public void ArchiveProblemsAreKeyedByKindAndSubject()
    {
        var log = new AssetProblemLog();
        var mark = log.Mark();
        var first = new ArchiveProblem(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "first");
        var otherKind = new ArchiveProblem(ArchiveProblemKind.IniArchiveListUnreadable, "a.bsa", "other kind");
        log.Add(first);
        log.Add(new ArchiveProblem(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "second"));
        log.Add(otherKind);

        Assert.Equal([first, otherKind], log.ArchiveProblemsSince(mark).ToArray());
    }

    [Fact]
    public void ArchiveProblemsComeInRecordOrderAndOnlyThoseAfterTheMark()
    {
        var log = new AssetProblemLog();
        log.Add(new ArchiveProblem(ArchiveProblemKind.ArchiveUnreadable, "old.bsa", "old"));
        var mark = log.Mark();
        log.Add(new ArchiveProblem(ArchiveProblemKind.ArchiveUnreadable, "z.bsa", "z"));
        log.Add(new ArchiveProblem(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "a"));
        log.Add(new ArchiveProblem(ArchiveProblemKind.ArchiveUnreadable, "old.bsa", "old again"));

        Assert.Equal(["z", "a"], log.ArchiveProblemsSince(mark).Select(problem => problem.Message));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void ConcurrentAddsOfTheSameProblemsGiveOneIdenticalList(int workers)
    {
        var problems = Enumerable.Range(0, 200)
            .Select(i => Problem($@"meshes\m{i % 50}.nif", (AssetProblemKind)(i % 3)))
            .ToList();
        var log = new AssetProblemLog();
        var mark = log.Mark();

        Parallel.ForEach(
            problems.Concat(problems).OrderBy(problem => problem.Message.Length).ThenByDescending(problem => problem.Mesh),
            new ParallelOptions { MaxDegreeOfParallelism = workers },
            log.Add);

        var expected = problems
            .DistinctBy(problem => (problem.Mesh, problem.Kind))
            .OrderBy(problem => problem.Mesh, StringComparer.OrdinalIgnoreCase)
            .ThenBy(problem => problem.Message, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expected, log.Since(mark));
    }
}
