namespace AddedObjectRemover;

/// <summary>A NiflySharp call failed on malformed NIF data.</summary>
internal sealed class MalformedNifException(Exception libraryException) : UserDataException(libraryException)
{
    public string LibraryExceptionType => InnerException!.GetType().Name;
}
