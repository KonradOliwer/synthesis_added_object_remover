namespace AddedObjectRemover;

/// <summary>A library call failed on malformed data of the user's files; the library's own exception is the inner exception.</summary>
public abstract class UserDataException(Exception libraryException) : Exception(libraryException.Message, libraryException);
