using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Determinism;

public class RecordKeyOrderParityTests
{
    private static readonly string[] PluginFiles =
    [
        "Alpha.esp", "alpha.esm", "ALPHA.esl", "Beta.esp", "beta2.esp", "Skyrim.esm", "zeta.esl", "Zeta.esp", "A_b.esp", "A-b.esp",
    ];

    private static readonly uint[] Ids = [0, 1, 0xF, 0x10, 0xABC, 0x800, 0xFFFFFF, 0xFFFFFE, 0x0A0800];

    private static List<FormKey> AllFormKeys() =>
        PluginFiles
            .SelectMany(file => Ids.Select(id => new FormKey(ModKey.FromNameAndExtension(file), id)))
            .Distinct()
            .ToList();

    /// <summary>The order the patcher used before it had its own record keys: plugin name ignoring case, then local FormID.</summary>
    private static class FormKeyOrder
    {
        public static IComparer<FormKey> Comparer { get; } = Comparer<FormKey>.Create((first, second) =>
        {
            var byPlugin = StringComparer.OrdinalIgnoreCase.Compare(first.ModKey.FileName.String, second.ModKey.FileName.String);
            return byPlugin != 0 ? byPlugin : first.ID.CompareTo(second.ID);
        });
    }

    private static List<FormKey> Shuffled(List<FormKey> keys, int seed)
    {
        var random = new Random(seed);
        return keys.OrderBy(_ => random.Next()).ToList();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RecordKeyOrderSortsLikeFormKeyOrder(int seed)
    {
        var keys = Shuffled(AllFormKeys(), seed);

        var expected = keys.OrderBy(key => key, FormKeyOrder.Comparer).Select(key => key.ToRecordKey()).ToList();
        var actual = keys.Select(key => key.ToRecordKey()).OrderBy(key => key, RecordKeyOrder.Comparer).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RecordKeyOrderComparesLikeFormKeyOrderForEveryPair()
    {
        var keys = AllFormKeys();

        foreach (var first in keys)
        {
            foreach (var second in keys)
            {
                Assert.Equal(
                    Math.Sign(FormKeyOrder.Comparer.Compare(first, second)),
                    Math.Sign(RecordKeyOrder.Comparer.Compare(first.ToRecordKey(), second.ToRecordKey())));
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RecordKeyTextOrderSortsLikeOrdinalFormKeyText(int seed)
    {
        var keys = Shuffled(AllFormKeys(), seed);

        var expected = keys.OrderBy(key => key.ToString(), StringComparer.Ordinal).Select(key => key.ToRecordKey()).ToList();
        var actual = keys.Select(key => key.ToRecordKey()).OrderBy(key => key, RecordKeyTextOrder.Comparer).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RecordKeyTextOrderComparesLikeOrdinalTextForEveryPairIncludingIdsAboveSixDigits()
    {
        uint[] ids = [0, 0xF, 0xABCDEF, 0xFFFFFF, 0x1000000, 0x1ABCDEF, 0xABCDEF1, 0xFFFFFFFF];
        var keys = PluginFiles
            .SelectMany(file => ids.Select(id => new RecordKey(new PluginName(file), id)))
            .ToList();

        foreach (var first in keys)
        {
            foreach (var second in keys)
            {
                Assert.Equal(
                    Math.Sign(string.CompareOrdinal(first.ToString(), second.ToString())),
                    Math.Sign(RecordKeyTextOrder.Comparer.Compare(first, second)));
            }
        }
    }

    [Fact]
    public void RecordKeyPrintsLikeFormKey()
    {
        foreach (var formKey in AllFormKeys())
        {
            Assert.Equal(formKey.ToString(), formKey.ToRecordKey().ToString());
        }
    }

    [Fact]
    public void DefaultPluginNameAndRecordKeyCanBeHashed()
    {
        Assert.Equal(default(PluginName).GetHashCode(), new PluginName(string.Empty).GetHashCode());
        _ = default(RecordKey).GetHashCode();
    }

    [Fact]
    public void PluginNamesDifferingOnlyInCaseAreEqual()
    {
        var lower = new PluginName("example.esp");
        var upper = new PluginName("EXAMPLE.ESP");

        Assert.Equal(lower, upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
        Assert.Equal(new RecordKey(lower, 5), new RecordKey(upper, 5));
    }

    [Fact]
    public void TheTwoOrdersDifferOnPurpose()
    {
        var firstPluginHighId = new RecordKey(new PluginName("A.esp"), 0x2);
        var secondPluginLowId = new RecordKey(new PluginName("B.esp"), 0x1);

        Assert.True(RecordKeyOrder.Comparer.Compare(firstPluginHighId, secondPluginLowId) < 0);
        Assert.True(RecordKeyTextOrder.Comparer.Compare(firstPluginHighId, secondPluginLowId) > 0);
    }
}
