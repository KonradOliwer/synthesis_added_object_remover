namespace AddedObjectRemover;

/// <summary>Builds Mutagen translation masks from field names, so one list of names drives both a compare and a copy.</summary>
internal static class TranslationMasks
{
    /// <summary>A mask with only the named fields on; a named sub-record is on whole.</summary>
    public static TMask Only<TMask>(IEnumerable<string> fields) => Build<TMask>(defaultOn: false, fields);

    /// <summary>A mask with every field on except the named ones, including any field a later Mutagen version adds; a named sub-record is off whole.</summary>
    public static TMask AllBut<TMask>(IEnumerable<string> fields) => Build<TMask>(defaultOn: true, fields);

    /// <remarks>
    /// A sub-record's mask left null follows the default, so a named sub-record gets a mask of its own whose overall switch is
    /// the opposite of the default.
    /// </remarks>
    private static TMask Build<TMask>(bool defaultOn, IEnumerable<string> fields)
    {
        var mask = (TMask)Activator.CreateInstance(typeof(TMask), defaultOn, true)!;
        foreach (var name in fields)
        {
            var field = typeof(TMask).GetField(name) ?? throw new ArgumentException($"{typeof(TMask)} has no field {name}.", nameof(fields));
            field.SetValue(mask, field.FieldType == typeof(bool) ? !defaultOn : Activator.CreateInstance(field.FieldType, !defaultOn, !defaultOn));
        }
        return mask;
    }
}
