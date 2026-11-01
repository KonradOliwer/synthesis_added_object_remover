using Mutagen.Bethesda.Plugins.Exceptions;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Throws the game library's own malformed-data failure through the conversion the plugin readers use, as a broken record would.</summary>
internal static class PlantedMalformedRecord
{
    public static T Throw<T>(string message) => MutagenCalls.Read<T>(() => throw new MalformedDataException(message));
}
