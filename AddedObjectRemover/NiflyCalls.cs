namespace AddedObjectRemover;

/// <summary>
/// Boundary around NiflySharp calls: the exceptions the library raises for malformed data become
/// <see cref="MalformedNifException"/>, so the same exception types thrown by this project's own
/// code still surface as bugs.
/// </summary>
internal static class NiflyCalls
{
    public static T Call<T>(Func<T> libraryCall)
    {
        try
        {
            return libraryCall();
        }
        catch (Exception ex) when (Failures.IsMalformedNif(ex))
        {
            throw new MalformedNifException(ex);
        }
    }
}
