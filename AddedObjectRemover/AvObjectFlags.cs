using NiflySharp;
using NiflySharp.Blocks;

namespace AddedObjectRemover;

/// <summary>
/// Reads the NIF "hidden" bit (0x1) of NiAVObject flags. NiflySharp splits the Flags field by
/// Bethesda stream version (nif.xml: uint when BSVER > 26, ushort otherwise; LE is 83, SSE 100);
/// only the field matching the file's stream version is populated.
/// </summary>
internal readonly record struct AvObjectFlags(bool UsesUIntFlags)
{
    private const int LastBsVersionWithUShortFlags = 26;
    private const uint HiddenBit = 0x1;

    public static AvObjectFlags For(NifFile nif) => new(nif.Header.Version?.StreamVersion > LastBsVersionWithUShortFlags);

    /// <summary>
    /// Objects with a time controller never count as hidden: animated NIFs (furniture, carts,
    /// doors, ...) often store parts with the hidden bit set and show them through a visibility
    /// controller at runtime.
    /// </summary>
    public bool IsHiddenWithoutController(uint flagsUi, ushort flagsUs, NiBlockRef<NiTimeController>? controller) =>
        ((UsesUIntFlags ? flagsUi : flagsUs) & HiddenBit) != 0
        && (controller == null || controller.IsEmpty());
}
