namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>A run with the expected-output write switch on is never green, even when every file already matches.</summary>
public class ExpectedOutputWriteSwitchTests
{
    [Fact]
    public void TheWriteSwitchIsOff() =>
        Assert.False(ExpectedOutputFiles.IsWriteSwitchOn(), $"The expected-output write switch is on; empty {ExpectedOutputFiles.WriteSwitchPath()}.");
}
