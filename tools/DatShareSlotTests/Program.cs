using System.Text;
using RynthCore.Engine.Compatibility;

namespace DatShareSlotTests;

/// <summary>
/// LazyImportSlotScanner: finds the private CreateFileW slot the 2025 ACClient.exe fills on first
/// use. Without it the DAT share hook missed every DAT open and a second client failed with
/// "Can't open the data files" (2026-10-15).
/// </summary>
internal static class Program
{
    private static int _checks, _failed;
    private static string _case = "";

    // Synthetic image layout: headers, .text at 0x1000 (code), .rdata at 0x2000 (strings + slots).
    private const uint ImageBase = 0x00400000;
    private const uint TextRva = 0x1000;
    private const uint DataRva = 0x2000;
    private const int ImageSize = 0x3000;
    private const uint DllRva = DataRva + 0x10;
    private const uint NameRva = DataRva + 0x40;
    private const uint SlotRva = DataRva + 0x100;

    private static int Main()
    {
        ReadsSections();
        FindsTheStub();
        UnresolvedOrResolvedSlotIsFoundTheSame();
        RejectsJmpThroughAnotherSlot();
        RejectsAnotherDll();
        RejectsNameAsTailOfLongerName();
        RejectsBrokenStubShape();
        OtherFunctionHasNoSlot();
        Real2025Client();

        Console.WriteLine($"{_checks} check(s), {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string what)
    {
        _checks++;
        if (ok) return;
        _failed++;
        Console.WriteLine($"[FAIL] {_case}: {what}");
    }

    // ── synthetic image ──────────────────────────────────────────────────────

    /// <summary>A minimal PE32 header with .text and .rdata, plus "kernel32.dll" and the name.</summary>
    private static byte[] NewImage(string functionName = "CreateFileW", string dllName = "kernel32.dll")
    {
        var img = new byte[ImageSize];
        const int pe = 0x80;
        BitConverter.TryWriteBytes(img.AsSpan(0x3C), pe);
        Encoding.ASCII.GetBytes("PE\0\0").CopyTo(img, pe);
        BitConverter.TryWriteBytes(img.AsSpan(pe + 6), (ushort)2);      // NumberOfSections
        BitConverter.TryWriteBytes(img.AsSpan(pe + 20), (ushort)0xE0);  // SizeOfOptionalHeader
        BitConverter.TryWriteBytes(img.AsSpan(pe + 24), (ushort)0x10B); // PE32
        int table = pe + 24 + 0xE0;
        WriteSection(img, table, ".text", TextRva, 0x1000, 0x60000020);
        WriteSection(img, table + 40, ".rdata", DataRva, 0x1000, 0x40000040);
        Encoding.ASCII.GetBytes(dllName + "\0").CopyTo(img, DllRva);
        Encoding.ASCII.GetBytes(functionName + "\0").CopyTo(img, NameRva);
        return img;
    }

    private static void WriteSection(byte[] img, int at, string name, uint rva, uint size, uint flags)
    {
        Encoding.ASCII.GetBytes(name.PadRight(8, '\0')).CopyTo(img, at);
        BitConverter.TryWriteBytes(img.AsSpan(at + 8), size);
        BitConverter.TryWriteBytes(img.AsSpan(at + 12), rva);
        BitConverter.TryWriteBytes(img.AsSpan(at + 16), size);
        BitConverter.TryWriteBytes(img.AsSpan(at + 20), rva);
        BitConverter.TryWriteBytes(img.AsSpan(at + 36), flags);
    }

    /// <summary>Writes the stub at <paramref name="stubRva"/>, the same bytes the 2025 client has.</summary>
    private static void WriteStub(byte[] img, uint stubRva, uint slotVa, uint nameVa, uint dllVa, uint jmpSlotVa)
    {
        var code = new List<byte>();
        code.Add(0x68); code.AddRange(BitConverter.GetBytes(0x00405BB3u));             // push fallback
        code.Add(0xFF); code.Add(0x35); code.AddRange(BitConverter.GetBytes(0x00906788u)); // push [handle cache]
        code.Add(0x68); code.AddRange(BitConverter.GetBytes(slotVa));
        code.Add(0x68); code.AddRange(BitConverter.GetBytes(nameVa));
        code.Add(0x68); code.AddRange(BitConverter.GetBytes(dllVa));
        code.Add(0xE8); code.AddRange(BitConverter.GetBytes(0xFFD4CD41u));             // call resolver
        code.Add(0xFF); code.Add(0x25); code.AddRange(BitConverter.GetBytes(jmpSlotVa)); // jmp [slot]
        code.CopyTo(img, (int)stubRva);
    }

    private static List<LazyImportSlot> Scan(byte[] img, string function = "CreateFileW") =>
        LazyImportSlotScanner.FindSlots(img, ImageBase, LazyImportSlotScanner.ReadSections(img), "kernel32.dll", function);

    // ── cases ────────────────────────────────────────────────────────────────

    private static void ReadsSections()
    {
        _case = "sections";
        var s = LazyImportSlotScanner.ReadSections(NewImage());
        Check(s.Count == 2, $"two sections, got {s.Count}");
        Check(s.Count == 2 && s[0].Name == ".text" && s[0].Executable && s[0].Rva == TextRva, ".text is code at 0x1000");
        Check(s.Count == 2 && s[1].Name == ".rdata" && !s[1].Executable && s[1].Rva == DataRva, ".rdata is data at 0x2000");
        Check(LazyImportSlotScanner.ReadSections(new byte[0x100]).Count == 0, "no PE signature: nothing");
    }

    private static void FindsTheStub()
    {
        _case = "stub";
        var img = NewImage();
        WriteStub(img, TextRva + 0x20, ImageBase + SlotRva, ImageBase + NameRva, ImageBase + DllRva, ImageBase + SlotRva);
        var found = Scan(img);
        Check(found.Count == 1, $"one slot, got {found.Count}");
        Check(found.Count == 1 && found[0].SlotVa == ImageBase + SlotRva, $"slot 0x{ImageBase + SlotRva:X8}");
        Check(found.Count == 1 && found[0].StubVa == ImageBase + TextRva + 0x20, $"stub at 0x{ImageBase + TextRva + 0x20:X8}, got 0x{(found.Count == 1 ? found[0].StubVa : 0):X8}");
    }

    private static void UnresolvedOrResolvedSlotIsFoundTheSame()
    {
        _case = "slot contents";
        var img = NewImage();
        WriteStub(img, TextRva + 0x20, ImageBase + SlotRva, ImageBase + NameRva, ImageBase + DllRva, ImageBase + SlotRva);
        BitConverter.TryWriteBytes(img.AsSpan((int)SlotRva), ImageBase + TextRva + 0x20); // still points at its stub
        Check(Scan(img).Count == 1, "unresolved (slot points at the stub)");
        BitConverter.TryWriteBytes(img.AsSpan((int)SlotRva), 0x76ACEA10u);                // resolved to kernel32
        Check(Scan(img).Count == 1, "resolved (slot points into kernel32)");
    }

    private static void RejectsJmpThroughAnotherSlot()
    {
        _case = "jmp slot";
        var img = NewImage();
        WriteStub(img, TextRva + 0x20, ImageBase + SlotRva, ImageBase + NameRva, ImageBase + DllRva, ImageBase + SlotRva + 8);
        Check(Scan(img).Count == 0, "the jmp must go through the slot the stub fills");
    }

    private static void RejectsAnotherDll()
    {
        _case = "dll";
        var img = NewImage(dllName: "user32.dll");
        WriteStub(img, TextRva + 0x20, ImageBase + SlotRva, ImageBase + NameRva, ImageBase + DllRva, ImageBase + SlotRva);
        Check(Scan(img).Count == 0, "a stub resolving from user32.dll is not kernel32's CreateFileW");
        var upper = NewImage(dllName: "KERNEL32.DLL");
        WriteStub(upper, TextRva + 0x20, ImageBase + SlotRva, ImageBase + NameRva, ImageBase + DllRva, ImageBase + SlotRva);
        Check(Scan(upper).Count == 1, "the DLL name matches in any case");
    }

    private static void RejectsNameAsTailOfLongerName()
    {
        _case = "longer name";
        var img = NewImage(functionName: "MyCreateFileW");
        // Points at the "CreateFileW" tail of "MyCreateFileW".
        WriteStub(img, TextRva + 0x20, ImageBase + SlotRva, ImageBase + NameRva + 2, ImageBase + DllRva, ImageBase + SlotRva);
        Check(Scan(img).Count == 0, "\"MyCreateFileW\" is not CreateFileW");
    }

    private static void RejectsBrokenStubShape()
    {
        _case = "shape";
        var img = NewImage();
        WriteStub(img, TextRva + 0x20, ImageBase + SlotRva, ImageBase + NameRva, ImageBase + DllRva, ImageBase + SlotRva);
        img[TextRva + 0x20 + 26] = 0x90; // the call becomes a nop
        Check(Scan(img).Count == 0, "no call to the resolver: not a stub");

        var outside = NewImage();
        WriteStub(outside, TextRva + 0x20, 0x12345678, ImageBase + NameRva, ImageBase + DllRva, 0x12345678);
        Check(Scan(outside).Count == 0, "a slot outside the image is never written");

        var atStart = NewImage();
        WriteStub(atStart, TextRva + 0x20, ImageBase + SlotRva, ImageBase + NameRva, ImageBase + DllRva, ImageBase + SlotRva);
        // A lone push of the name at the very start of .text has no stub around it.
        atStart[TextRva] = 0x68;
        BitConverter.TryWriteBytes(atStart.AsSpan((int)TextRva + 1), ImageBase + NameRva);
        Check(Scan(atStart).Count == 1, "a lone push of the name is skipped, the real stub still found");
    }

    private static void OtherFunctionHasNoSlot()
    {
        _case = "other function";
        var img = NewImage();
        WriteStub(img, TextRva + 0x20, ImageBase + SlotRva, ImageBase + NameRva, ImageBase + DllRva, ImageBase + SlotRva);
        Check(Scan(img, "CreateFileA").Count == 0, "only the named function's slot is returned");
    }

    // ── the real client ──────────────────────────────────────────────────────

    private static void Real2025Client()
    {
        _case = "ACClient.exe 0.0.11.6096";
        string? path = Environment.GetEnvironmentVariable("RYNTH_ACCLIENT");
        if (string.IsNullOrEmpty(path))
            path = @"C:\Turbine\Asheron's Call-ryn\ACClient.exe";
        if (!File.Exists(path))
        {
            Console.WriteLine($"  skip: {path} not found (set RYNTH_ACCLIENT to check a client)");
            return;
        }

        byte[] mapped = MapImage(File.ReadAllBytes(path), out uint imageBase);
        var sections = LazyImportSlotScanner.ReadSections(mapped);
        var w = LazyImportSlotScanner.FindSlots(mapped, imageBase, sections, "kernel32.dll", "CreateFileW");
        var a = LazyImportSlotScanner.FindSlots(mapped, imageBase, sections, "kernel32.dll", "CreateFileA");
        Console.WriteLine($"  {path}: CreateFileW slots {string.Join(", ", w.Select(s => $"0x{s.SlotVa:X8} (stub 0x{s.StubVa:X8})"))}; CreateFileA slots {a.Count}");

        // Read from the running client on 2026-10-15: slot 0x00837374 held kernel32!CreateFileW.
        if (imageBase == 0x00400000 && FileVersion(path) == "00.00.11.6096")
        {
            Check(w.Count == 1 && w[0].SlotVa == 0x00837374, "the CreateFileW slot is 0x00837374");
            Check(w.Count == 1 && w[0].StubVa == 0x006B8D00, "filled by the stub at 0x006B8D00");
            Check(a.Count == 0, "CreateFileA is a normal import, no private slot");
        }
    }

    private static string FileVersion(string path) =>
        System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion ?? "";

    /// <summary>Lays a PE file out the way the loader maps it (headers, then each section at its RVA).</summary>
    private static byte[] MapImage(byte[] file, out uint imageBase)
    {
        int pe = BitConverter.ToInt32(file, 0x3C);
        int opt = pe + 24;
        imageBase = BitConverter.ToUInt32(file, opt + 28);
        uint sizeOfImage = BitConverter.ToUInt32(file, opt + 56);
        uint sizeOfHeaders = BitConverter.ToUInt32(file, opt + 60);
        var img = new byte[sizeOfImage];
        Array.Copy(file, img, Math.Min(sizeOfHeaders, (uint)file.Length));
        int count = BitConverter.ToUInt16(file, pe + 6);
        int table = opt + BitConverter.ToUInt16(file, pe + 20);
        for (int i = 0; i < count; i++)
        {
            int h = table + 40 * i;
            uint rva = BitConverter.ToUInt32(file, h + 12);
            uint rawSize = BitConverter.ToUInt32(file, h + 16);
            uint raw = BitConverter.ToUInt32(file, h + 20);
            uint n = Math.Min(rawSize, Math.Min(sizeOfImage - rva, (uint)file.Length - raw));
            Array.Copy(file, raw, img, rva, n);
        }
        return img;
    }
}
