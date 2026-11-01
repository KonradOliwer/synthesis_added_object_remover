namespace AddedObjectRemover;

/// <summary>Mutagen found a plugin record's data malformed while a read enumerated its subrecords.</summary>
internal sealed class MalformedRecordException(Exception libraryException) : UserDataException(libraryException);
