namespace AddedObjectRemover;

/// <summary>A group file or config file the user supplied cannot be used; the message names the file and what is wrong.</summary>
internal sealed class ConfigFilesProblem(string message) : Exception(message);
