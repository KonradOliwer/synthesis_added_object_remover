namespace AddedObjectRemover;

/// <summary>A NiflySharp call failed on malformed NIF data; the library's own exception is the inner exception.</summary>
internal sealed class MalformedNifException(Exception libraryException)
    : Exception(libraryException.Message, libraryException)
{
    public string LibraryExceptionType => InnerException!.GetType().Name;
}
