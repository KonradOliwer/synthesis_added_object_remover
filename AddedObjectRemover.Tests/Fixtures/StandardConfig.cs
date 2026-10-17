namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The settings of Standard mode, wrapped the way the entry hands them to validation.</summary>
public static class StandardConfig
{
    public static ConfiguredSettings Of(ISettingsValues settings) => new(new SettingsSource.Standard(), settings);
}
