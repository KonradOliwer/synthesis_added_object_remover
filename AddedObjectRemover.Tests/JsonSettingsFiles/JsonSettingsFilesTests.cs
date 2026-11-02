using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AddedObjectRemover.Tests.JsonSettingsFiles;

public class JsonSettingsFilesTests
{
    [Fact]
    public void ACommentAheadOfTheFirstValueIsNotTheValue() =>
        Assert.Equal(JTokenType.Array, JsonFiles.ParseIgnoringComments("// note\n[1, 2]").Type);

    [Fact]
    public void ALineOfATokenIsTheLineItStartsOn() =>
        Assert.Equal(2, JsonFiles.LineOf(JsonFiles.ParseIgnoringComments("{\n  \"a\": 1\n}")["a"]!));

    [Fact]
    public void AReaderFailureGivesItsPathAndPosition()
    {
        var failure = Assert.Throws<JsonReaderException>(() => JsonFiles.ParseIgnoringComments("{\n  \"a\": @\n}"));

        Assert.NotNull(JsonFiles.PathOf(failure));
        Assert.Equal(2, JsonFiles.PositionOf(failure)!.Value.Line);
    }

    [Fact]
    public void AFailureWithoutAPositionGivesNone() =>
        Assert.Null(JsonFiles.PositionOf(new JsonException("no position")));

    [Theory]
    [InlineData(typeof(string), typeof(string))]
    [InlineData(typeof(int), typeof(int))]
    [InlineData(typeof(List<int>), typeof(int))]
    [InlineData(typeof(string[]), typeof(string))]
    public void TheElementTypeOfAListIsItsItemTypeAndOtherTypesStayAsTheyAre(Type type, Type expected) =>
        Assert.Equal(expected, PropertyValues.ElementTypeOrSelf(type));

    [Fact]
    public void ListsDifferOnlyWhenTheirItemsDifferInOrderOrValue()
    {
        Assert.False(PropertyValues.DifferByValue(new List<int> { 1, 2 }, new[] { 1, 2 }, typeof(List<int>)));
        Assert.True(PropertyValues.DifferByValue(new List<int> { 2, 1 }, new[] { 1, 2 }, typeof(List<int>)));
    }

    [Fact]
    public void ANullListIsAnEmptyList() =>
        Assert.False(PropertyValues.DifferByValue(null, new List<int>(), typeof(List<int>)));

    [Fact]
    public void SingleValuesAreComparedByValueAndTextIsNotAList()
    {
        Assert.False(PropertyValues.DifferByValue(32d, 32.0, typeof(double)));
        Assert.True(PropertyValues.DifferByValue("a", "b", typeof(string)));
    }
}
