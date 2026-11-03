using System.Reflection;
using Xunit.Sdk;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Marks a class whose tests compare expected-output files: while the write switch is on, each test writes every file it
/// compares and fails only after it ends, instead of at the first file it writes.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class ExpectedOutputTestAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest) => ExpectedOutputFiles.StartTest();

    public override void After(MethodInfo methodUnderTest) => ExpectedOutputFiles.EndTest();
}
