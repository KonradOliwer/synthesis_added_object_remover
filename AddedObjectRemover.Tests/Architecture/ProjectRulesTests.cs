namespace AddedObjectRemover.Tests.Architecture;

public class ProjectRulesTests
{
    private static readonly ProjectRules Rules = ProjectRules.Load();

    [Fact]
    public void TheTableHasNoDuplicateNamesAndOnlyKnownReferences()
    {
        var names = Rules.Projects.Select(project => project.Name).ToList();

        Assert.Equal(names.Distinct(), names);
        Assert.Empty(Rules.Projects.SelectMany(project => project.References.Except(names).Select(reference => $"{project.Name} -> {reference}")));
    }

    [Fact]
    public void LogicProjectsReferenceNoLogic() =>
        Assert.Empty(Rules.FindLogicReferencingLogic());

    [Fact]
    public void OnlyTheRunnerAndTheTestsReferenceLogic() =>
        Assert.Empty(Rules.FindLogicReferencedByNonRunners());

    [Fact]
    public void OnlyTheEntryAndTheTestsReferenceTheEntryOnlyProjects() =>
        Assert.Empty(Rules.FindEntryOnlyReferencedByOthers());

    [Fact]
    public void TheReferencesHaveNoCycle() =>
        Assert.Empty(Rules.FindCycles());

    [Fact]
    public void ALogicProjectReferencingLogicIsReported()
    {
        var bad = Table(Project("A", "L", "B"), Project("B", "L"));

        Assert.Equal(["A -> B"], bad.FindLogicReferencingLogic());
    }

    [Fact]
    public void ALogicProjectReferencedByAnotherLogicProjectIsReported()
    {
        var bad = Table(Project("A", "L", "B"), Project("B", "B"), Project("Run", "R", "B"));

        Assert.Equal(["A -> B"], bad.FindLogicReferencedByNonRunners());
    }

    [Fact]
    public void AnEntryOnlyProjectReferencedBySomeoneElseIsReported()
    {
        var bad = Table(Project("Entry", "E", "Checks"), Project("Test", "X", "Checks"), Project("Run", "R", "Checks"), Project("Checks", "V"));

        Assert.Equal(["Run -> Checks"], bad.FindEntryOnlyReferencedByOthers());
    }

    [Fact]
    public void ACycleIsReportedOnce()
    {
        var bad = Table(Project("A", "R", "B"), Project("B", "R", "A"));

        Assert.Equal(["A -> B -> A"], bad.FindCycles());
    }

    private static ProjectRules Table(params ProjectRule[] projects) => new([], projects);

    private static ProjectRule Project(string name, string kind, params string[] references) => new(name, kind, name, references, []);
}
