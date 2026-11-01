namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>A failure of the user's data planted in a test, which a run records and carries on from, unlike a bug.</summary>
public sealed class PlantedUserDataException(string message = "planted") : UserDataException(new FormatException(message));
