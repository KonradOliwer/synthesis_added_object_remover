using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using static AddedObjectRemover.SettingCorrections;

namespace AddedObjectRemover;

/// <summary>Builds the settings of the area method of "Objects left behind", the step for objects in cleared areas.</summary>
internal static class ClearedAreasSettingsBuilder
{
    private static readonly ClearedAreaSettingsReader.Names AreaNames = new(
        SettingPaths.AreaMethodClearedAreaGroup + SettingNames.SearchRadius,
        SettingPaths.AreaMethodClearedAreaGroup + SettingNames.MaxStandingSizePercent,
        SettingPaths.AreaMethodClearedAreaGroup + SettingNames.MaxStandingCountPercent);

    /// <param name="Settings">Null when the step is switched off.</param>
    /// <param name="Off">Why the step is switched off; null when it is on.</param>
    public sealed record Built(ObjectsInClearedAreasSettings? Settings, ObjectsInClearedAreasOff? Off);

    /// <summary>
    /// Validated even when the step is off, so invalid values are always reported. A limit of 100% or more switches the step off. That
    /// is the method's design: both shares must pass, so one limit at 100% would only make its share always pass.
    /// </summary>
    /// <param name="minObjectsAround">Validated by <see cref="LeftBehindSettingsBuilder.ValidateMinObjectsAround"/>.</param>
    public static Built Create(ILeftoverObjectsSettingsValues values, int minObjectsAround, ICollection<SettingWarning> warnings)
    {
        var area = ClearedAreaSettingsReader.Read(
            values.ClearedArea, AreaNames, (percent, defaultValue, name) => AtLeast(percent, 0, defaultValue, name, warnings), warnings);
        var off = OffBy(
            values.RemoveObjectsInClearedAreas,
            wholeSize: area.MaxStandingSizePercent >= Percent.PerWhole,
            wholeCount: area.MaxStandingCountPercent >= Percent.PerWhole);
        return off is null
            ? new Built(new ObjectsInClearedAreasSettings(area.ToClearedArea(), minObjectsAround), Off: null)
            : new Built(Settings: null, off);
    }

    private static ObjectsInClearedAreasOff? OffBy(bool switchedOn, bool wholeSize, bool wholeCount) => (switchedOn, wholeSize, wholeCount) switch
    {
        (false, _, _) => ObjectsInClearedAreasOff.Switch,
        (true, true, true) => ObjectsInClearedAreasOff.BothLimits,
        (true, true, false) => ObjectsInClearedAreasOff.SizeLimit,
        (true, false, true) => ObjectsInClearedAreasOff.CountLimit,
        (true, false, false) => null,
    };
}
