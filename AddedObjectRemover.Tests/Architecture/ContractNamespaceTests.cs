using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>The reference rule "no step contracts reference RunCaches.Contracts", checked by namespace while everything is one project.</summary>
public partial class ContractNamespaceTests
{
    private const string RunCachesContracts = "AddedObjectRemover.Caches.RunCaches.Contracts";

    [Fact]
    public void NoStepContractsReferenceTheRunCachesContracts()
    {
        var offenders = StepContractFiles()
            .Where(file => file.Code.Contains(RunCachesContracts, StringComparison.Ordinal))
            .Select(file => file.Path)
            .Order(StringComparer.Ordinal);

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheCheckFindsStepContractsNamespaces() =>
        Assert.NotEmpty(StepContractFiles());

    private static List<SourceFile> StepContractFiles() =>
        SourceFile.All().Where(file => StepContractsNamespace().IsMatch(file.Code)).ToList();

    [GeneratedRegex(@"^\s*namespace\s+AddedObjectRemover\.Steps\.\w+\.Contracts\b", RegexOptions.Multiline)]
    private static partial Regex StepContractsNamespace();
}
