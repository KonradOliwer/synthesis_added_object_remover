namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>
/// Trait name of tests that pin current, known-wrong behaviour; the trait value says what is wrong.
/// Such a test asserts today's output, so a fix must flip it deliberately.
/// </summary>
internal static class KnownBug
{
    public const string Trait = "KnownBug";
}
