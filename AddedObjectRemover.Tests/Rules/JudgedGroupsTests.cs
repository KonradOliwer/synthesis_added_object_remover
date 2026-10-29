using System.Diagnostics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The groups of objects that a step judges: equal settings make one group, and an object is in one group.</summary>
public class JudgedGroupsTests
{
    [Fact]
    public void GroupsWithEqualSettingsBecomeOneGroupInTheOrderTheirSettingsFirstAppear()
    {
        var merged = JudgedGroups.MergeEqualSettings([TestJudgedGroups.Of([3], 2), TestJudgedGroups.Of([2, 0], 1), TestJudgedGroups.Of([1], 2)]);

        Assert.Equal([(2, "1,3"), (1, "0,2")], merged.Select(group => (group.Settings, string.Join(",", group.Objects.Select(id => id.Index)))));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    public void AnObjectInTwoGroupsIsRefusedWhateverTheirSettings(int firstSettings, int secondSettings)
    {
        var exception = Assert.Throws<UnreachableException>(
            () => JudgedGroups.MergeEqualSettings([TestJudgedGroups.Of([0, 4], firstSettings), TestJudgedGroups.Of([4], secondSettings)]));

        Assert.Contains("4", exception.Message);
    }
}
