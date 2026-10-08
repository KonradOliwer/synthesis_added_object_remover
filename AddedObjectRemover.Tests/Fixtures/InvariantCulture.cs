using System.Globalization;
using System.Runtime.CompilerServices;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Report text is compared with saved expected text, so every test formats numbers the same way on any machine.</summary>
internal static class InvariantCulture
{
    [ModuleInitializer]
    internal static void UseForEveryThread()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }
}
