using System;
using System.Collections.Generic;
using System.Text;

namespace RynthCore.Engine.Compatibility;

/// <summary>One section of a PE image as it is mapped in memory (RVA layout, not file layout).</summary>
internal readonly record struct ImageSection(string Name, uint Rva, uint Size, bool Executable);

/// <summary>A private function-pointer slot filled by a lazy-resolve stub, and the stub that fills it.</summary>
internal readonly record struct LazyImportSlot(uint SlotVa, uint StubVa);

/// <summary>
/// Finds the private pointer slots that Layer-for-Unicode style stubs fill on first use.
///
/// The 2025 ACClient.exe (0.0.11.6096) does not import kernel32!CreateFileW. It resolves it the
/// first time it is called, with a stub of this exact shape, and every call goes through the slot:
/// <code>
///   68 xx xx xx xx      push fallback
///   FF 35 xx xx xx xx   push [module handle cache]
///   68 SS SS SS SS      push slot             (the pointer the stub fills)
///   68 NN NN NN NN      push "CreateFileW"
///   68 DD DD DD DD      push "kernel32.dll"
///   E8 xx xx xx xx      call resolver
///   FF 25 SS SS SS SS   jmp [slot]
/// </code>
/// AC opens its DAT files through that slot, so a hook on the import table alone never sees them.
/// The pattern is matched in full (both slot operands must agree, and the DLL operand must name
/// <c>dll</c>), so a stray push of the name string is never taken for a stub.
/// </summary>
internal static class LazyImportSlotScanner
{
    private const byte PushImm32 = 0x68;
    private const byte CallRel32 = 0xE8;
    private const int IMAGE_SCN_CNT_CODE = 0x00000020;
    private const int IMAGE_SCN_MEM_EXECUTE = 0x20000000;

    // Offsets inside the stub, from the "push name" instruction.
    private const int SlotPushBack = 5;  // push slot sits right before push name
    private const int DllPushAhead = 5;  // push dll sits right after it
    private const int CallAhead = 10;
    private const int JmpAhead = 15;     // FF 25 <slot>, after the 5-byte call
    private const int StubHeadBytes = 11; // push fallback (5) + push [handle cache] (6), before push slot

    /// <summary>
    /// Reads the section table of a PE32 image mapped at RVA layout (a module's memory, or a test
    /// buffer laid out the same way). Returns an empty list when the headers don't parse.
    /// </summary>
    public static List<ImageSection> ReadSections(ReadOnlySpan<byte> image)
    {
        var sections = new List<ImageSection>();
        if (image.Length < 0x40)
            return sections;

        int pe = BitConverter.ToInt32(image.Slice(0x3C, 4));
        if (pe <= 0 || pe + 24 > image.Length || BitConverter.ToUInt32(image.Slice(pe, 4)) != 0x00004550) // "PE\0\0"
            return sections;

        int count = BitConverter.ToUInt16(image.Slice(pe + 6, 2));
        int optionalSize = BitConverter.ToUInt16(image.Slice(pe + 20, 2));
        int table = pe + 24 + optionalSize;
        for (int i = 0; i < count; i++)
        {
            int h = table + 40 * i;
            if (h + 40 > image.Length)
                break;
            string name = Encoding.ASCII.GetString(image.Slice(h, 8)).TrimEnd('\0');
            uint virtualSize = BitConverter.ToUInt32(image.Slice(h + 8, 4));
            uint rva = BitConverter.ToUInt32(image.Slice(h + 12, 4));
            uint rawSize = BitConverter.ToUInt32(image.Slice(h + 16, 4));
            int flags = BitConverter.ToInt32(image.Slice(h + 36, 4));
            uint size = virtualSize != 0 ? virtualSize : rawSize;
            // Clamp to the buffer: a section can't be scanned past the mapped image.
            if (rva >= image.Length)
                continue;
            size = (uint)Math.Min(size, image.Length - rva);
            bool executable = (flags & (IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_CNT_CODE)) != 0;
            sections.Add(new ImageSection(name, rva, size, executable));
        }
        return sections;
    }

    /// <summary>
    /// Every slot a stub fills with <paramref name="dll"/>!<paramref name="function"/>, as absolute
    /// addresses (the stubs push absolute addresses, already relocated to <paramref name="imageBase"/>).
    /// </summary>
    public static List<LazyImportSlot> FindSlots(ReadOnlySpan<byte> image, uint imageBase,
        IReadOnlyList<ImageSection> sections, string dll, string function)
    {
        var found = new List<LazyImportSlot>();
        Span<byte> needle = stackalloc byte[5];
        foreach (uint nameVa in FindNameStrings(image, imageBase, sections, function))
        {
            needle[0] = PushImm32;
            BitConverter.TryWriteBytes(needle.Slice(1), nameVa);

            foreach (ImageSection code in sections)
            {
                if (!code.Executable)
                    continue;
                ReadOnlySpan<byte> text = image.Slice((int)code.Rva, (int)code.Size);
                int from = 0;
                while (true)
                {
                    int hit = text.Slice(from).IndexOf(needle);
                    if (hit < 0)
                        break;
                    int at = from + hit;
                    from = at + 1;
                    if (TryReadStub(text, at, image, imageBase, dll, out uint slotVa))
                        found.Add(new LazyImportSlot(slotVa, imageBase + code.Rva + (uint)Math.Max(0, at - SlotPushBack - StubHeadBytes)));
                }
            }
        }
        return found;
    }

    // The NUL-terminated name, not as the tail of a longer identifier (e.g. "MyCreateFileW").
    private static IEnumerable<uint> FindNameStrings(ReadOnlySpan<byte> image, uint imageBase,
        IReadOnlyList<ImageSection> sections, string function)
    {
        byte[] name = Encoding.ASCII.GetBytes(function + "\0");
        var hits = new List<uint>();
        foreach (ImageSection s in sections)
        {
            if (s.Executable)
                continue;
            ReadOnlySpan<byte> data = image.Slice((int)s.Rva, (int)s.Size);
            int from = 0;
            while (true)
            {
                int hit = data.Slice(from).IndexOf(name);
                if (hit < 0)
                    break;
                int at = from + hit;
                from = at + 1;
                if (at > 0 && IsIdentifierByte(data[at - 1]))
                    continue;
                hits.Add(imageBase + s.Rva + (uint)at);
            }
        }
        return hits;
    }

    private static bool TryReadStub(ReadOnlySpan<byte> text, int namePush, ReadOnlySpan<byte> image,
        uint imageBase, string dll, out uint slotVa)
    {
        slotVa = 0;
        int slotPush = namePush - SlotPushBack;
        if (slotPush < 0 || namePush + JmpAhead + 6 > text.Length)
            return false;
        if (text[slotPush] != PushImm32
            || text[namePush + DllPushAhead] != PushImm32
            || text[namePush + CallAhead] != CallRel32
            || text[namePush + JmpAhead] != 0xFF || text[namePush + JmpAhead + 1] != 0x25)
            return false;

        uint slot = BitConverter.ToUInt32(text.Slice(slotPush + 1, 4));
        uint jmpSlot = BitConverter.ToUInt32(text.Slice(namePush + JmpAhead + 2, 4));
        if (slot != jmpSlot || !InImage(slot, 4, imageBase, image.Length))
            return false;

        uint dllVa = BitConverter.ToUInt32(text.Slice(namePush + DllPushAhead + 1, 4));
        if (!string.Equals(ReadAsciiz(image, imageBase, dllVa), dll, StringComparison.OrdinalIgnoreCase))
            return false;

        slotVa = slot;
        return true;
    }

    private static bool InImage(uint va, int size, uint imageBase, int imageLength) =>
        va >= imageBase && (long)va - imageBase + size <= imageLength;

    private static string? ReadAsciiz(ReadOnlySpan<byte> image, uint imageBase, uint va)
    {
        if (!InImage(va, 1, imageBase, image.Length))
            return null;
        ReadOnlySpan<byte> rest = image.Slice((int)(va - imageBase));
        int end = rest.IndexOf((byte)0);
        if (end < 0 || end > 260)
            return null;
        return Encoding.ASCII.GetString(rest.Slice(0, end));
    }

    private static bool IsIdentifierByte(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'_';
}
