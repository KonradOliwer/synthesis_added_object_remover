using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using static AddedObjectRemover.SettingCorrections;

namespace AddedObjectRemover;

/// <summary>Reads the cleared-area values that the restore and the area method of "Objects left behind" both judge an area by.</summary>
internal static class ClearedAreaSettingsReader
{
    internal const float MinRadius = 1024f;
    internal const float MaxRadius = 16384f;

    /// <summary>Where each value sits on the settings screen, as its warnings name it.</summary>
    public sealed record Names(string Radius, string MaxStandingSizePercent, string MaxStandingCountPercent);

    /// <summary>The validated values, the percents still as floats so the caller can judge their limits.</summary>
    public readonly record struct Values(float Radius, float MaxStandingSizePercent, float MaxStandingCountPercent)
    {
        /// <summary>The percents are the decimals the user typed: the float setting 0.7 would otherwise widen to 0.69999998.</summary>
        public ClearedArea ToClearedArea() => new(Radius, (double)(decimal)MaxStandingSizePercent, (double)(decimal)MaxStandingCountPercent);
    }

    /// <param name="validatePercent">Keeps a standing percent within the limits of the caller: the value, its default and its name.</param>
    public static Values Read(IClearedAreaSettingsValues area, Names names, Func<float, float, string, float> validatePercent, ICollection<SettingWarning> warnings) =>
        new(
            Clamp(area.SearchRadius, MinRadius, MaxRadius, SettingDefaults.ClearedAreaSearchRadius, names.Radius, warnings),
            validatePercent(area.MaxStandingSizePercent, SettingDefaults.ClearedAreaMaxStandingSizePercent, names.MaxStandingSizePercent),
            validatePercent(area.MaxStandingCountPercent, SettingDefaults.ClearedAreaMaxStandingCountPercent, names.MaxStandingCountPercent));
}
