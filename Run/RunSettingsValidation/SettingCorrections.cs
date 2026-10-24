using AddedObjectRemover.Run.RunSettingsValidation.Contracts;

namespace AddedObjectRemover;

/// <summary>Replaces an invalid setting value with a valid one and adds one warning saying so.</summary>
internal static class SettingCorrections
{
    private const int PercentStep = 10;

    public static T ValidateDefined<T>(T value, T fallback, string label, ICollection<SettingWarning> warnings) where T : struct, Enum
    {
        if (Enum.IsDefined(value)) return value;
        warnings.Add(new SettingWarning(label, $"Warning: {label} {value} is invalid; using {fallback}."));
        return fallback;
    }

    /// <summary>A percentage limited to 10-100 and rounded to the nearest ten.</summary>
    public static int WholeTens(int percent, string name, ICollection<SettingWarning> warnings)
    {
        var withinRange = Math.Clamp(percent, PercentStep, Percent.PerWhole);
        if (withinRange != percent)
        {
            warnings.Add(new SettingWarning(name, $"Warning: {name} {percent} is outside {PercentStep}-{Percent.PerWhole}; using {withinRange}."));
        }
        var rounded = (int)Math.Round(withinRange / (double)PercentStep, MidpointRounding.AwayFromZero) * PercentStep;
        if (rounded != withinRange)
        {
            warnings.Add(new SettingWarning(name, $"Warning: {name} {withinRange} is not a multiple of {PercentStep}; using {rounded}."));
        }
        return rounded;
    }

    public static int Clamp(int value, int minimum, int maximum, string name, ICollection<SettingWarning> warnings)
    {
        var clamped = Math.Clamp(value, minimum, maximum);
        if (clamped != value)
        {
            warnings.Add(new SettingWarning(name, $"Warning: {name} {value} is outside {minimum}-{maximum}; using {clamped}."));
        }
        return clamped;
    }

    /// <summary>For a setting with a lower limit only.</summary>
    public static int AtLeast(int value, int minimum, string name, ICollection<SettingWarning> warnings)
    {
        if (value >= minimum) return value;
        warnings.Add(new SettingWarning(name, $"Warning: {name} {value} is below {minimum}; using {minimum}."));
        return minimum;
    }

    /// <summary>For a distance with no upper limit: a negative value, or one that is not a number, becomes the default.</summary>
    public static float NotNegative(float value, float defaultValue, string name, ICollection<SettingWarning> warnings)
    {
        if (value >= 0) return value;
        warnings.Add(new SettingWarning(name, float.IsNaN(value)
            ? $"Warning: {name} is not a valid number; using {defaultValue}."
            : $"Warning: {name} {value} is below 0; using {defaultValue}."));
        return defaultValue;
    }

    /// <summary>Out of range values become the nearest valid value; a value that is not a number becomes the default.</summary>
    public static float Clamp(float value, float minimum, float maximum, float defaultValue, string name, ICollection<SettingWarning> warnings)
    {
        if (float.IsNaN(value))
        {
            warnings.Add(new SettingWarning(name, $"Warning: {name} is not a valid number; using {defaultValue}."));
            return defaultValue;
        }
        if (value >= minimum && value <= maximum) return value;
        var clamped = Math.Clamp(value, minimum, maximum);
        warnings.Add(new SettingWarning(name, $"Warning: {name} {value} is outside {minimum}-{maximum}; using {clamped}."));
        return clamped;
    }
}
