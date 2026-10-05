// ============================================================================
//  RynthCore.Engine - UI/ScriptWindows/AcIconDecoder.cs
//  Script windows: decodes Asheron's Call icons (0x06 Texture files in
//  client_portal.dat) to RGBA, and stacks an item's underlay, icon and overlay
//  into one picture. Pure code: no AC memory, no D3D, no engine state, so the
//  RynthSuite offline tests compile it in and decode real icons.
//
//  The texture formats and their byte layouts are the ones RynthCore.StatusAgent
//  (Dat/IconDecoder.cs, the phone remote's icons) decodes, ported from
//  ACEmulator's DatLoader; DXT1/3/5 use the StatusAgent's DxtUtil.cs (MonoGame,
//  Ms-PL), linked into the engine project. The dat reader is the on-demand
//  B-tree walk of ImGui/MonsterHud/PortalSpellTable.cs over one open stream:
//  header at 0x140, a B-tree searched by file id, block chains whose first
//  dword is the next block's offset.
//
//  Threads: whatever thread owns the AcDatFile (the icon worker). Every read is
//  bounds-checked; a bad file returns null, never throws out of Decode/Compose.
// ============================================================================

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using ACE.DatLoader;

namespace RynthCore.Engine.UI.ScriptWindows;

/// <summary>A read-only portal.dat opened for a batch of reads. Not thread-safe.</summary>
internal sealed class AcDatFile : IDisposable
{
    private const int HeaderOffset = 0x140;
    private const int Branches = 62, MaxEntries = 61, EntrySize = 24;
    private const int NodeSize = 4 * Branches + 4 + EntrySize * MaxEntries;   // 1716
    private const int MaxDepth = 16;

    private readonly FileStream _fs;
    private readonly uint _blockSize;
    private readonly uint _root;

    private AcDatFile(FileStream fs, uint blockSize, uint root)
    {
        _fs = fs;
        _blockSize = blockSize;
        _root = root;
    }

    /// <summary>Opens <paramref name="path"/> read-only, sharing everything (AC holds its own handles).</summary>
    public static AcDatFile? Open(string path, out string why)
    {
        FileStream? fs = null;
        try
        {
            fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
            Span<byte> hdr = stackalloc byte[36];
            fs.Seek(HeaderOffset, SeekOrigin.Begin);
            if (!ReadFully(fs, hdr)) { why = "short header"; fs.Dispose(); return null; }
            uint blockSize = BinaryPrimitives.ReadUInt32LittleEndian(hdr.Slice(4, 4));
            uint root = BinaryPrimitives.ReadUInt32LittleEndian(hdr.Slice(32, 4));
            if (blockSize <= 4 || blockSize > 65536 || root == 0 || root >= fs.Length)
            {
                why = "bad header";
                fs.Dispose();
                return null;
            }
            why = string.Empty;
            return new AcDatFile(fs, blockSize, root);
        }
        catch (Exception ex)
        {
            fs?.Dispose();
            why = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    /// <summary>client_portal.dat (or portal.dat) next to the running exe or in the current directory; null when none.</summary>
    public static string? FindPortalDat()
    {
        var dirs = new List<string>(2);
        try
        {
            string? exe = Environment.ProcessPath;
            string? dir = string.IsNullOrEmpty(exe) ? null : Path.GetDirectoryName(exe);
            if (!string.IsNullOrEmpty(dir)) dirs.Add(dir);
        }
        catch { }
        try { dirs.Add(Environment.CurrentDirectory); } catch { }
        foreach (string dir in dirs)
            foreach (string name in new[] { "client_portal.dat", "portal.dat" })
            {
                try
                {
                    string full = Path.Combine(dir, name);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
        return null;
    }

    /// <summary>A file's bytes by id, or null (not in the dat, over <paramref name="maxSize"/>, or unreadable).</summary>
    public byte[]? Read(uint fileId, int maxSize)
    {
        try
        {
            uint offset = _root;
            for (int depth = 0; depth < MaxDepth && offset != 0; depth++)
            {
                byte[]? node = ReadChain(offset, NodeSize);
                if (node == null) return null;
                uint count = BinaryPrimitives.ReadUInt32LittleEndian(node.AsSpan(4 * Branches));
                if (count > MaxEntries) return null;
                int i = 0;
                while (i < count && EntryId(node, i) < fileId) i++;
                if (i < count && EntryId(node, i) == fileId)
                {
                    int e = 4 * Branches + 4 + i * EntrySize;
                    uint fileOffset = BinaryPrimitives.ReadUInt32LittleEndian(node.AsSpan(e + 8));
                    uint fileSize = BinaryPrimitives.ReadUInt32LittleEndian(node.AsSpan(e + 12));
                    if (fileSize == 0 || fileSize > (uint)maxSize) return null;
                    return ReadChain(fileOffset, (int)fileSize);
                }
                bool leaf = BinaryPrimitives.ReadUInt32LittleEndian(node) == 0;
                if (leaf || i >= Branches) return null;
                offset = BinaryPrimitives.ReadUInt32LittleEndian(node.AsSpan(i * 4));
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static uint EntryId(byte[] node, int i) =>
        BinaryPrimitives.ReadUInt32LittleEndian(node.AsSpan(4 * Branches + 4 + i * EntrySize + 4));

    /// <summary>A block chain: each block starts with the next block's offset (0 = last).</summary>
    private byte[]? ReadChain(uint offset, int size)
    {
        if ((long)offset + 4 > _fs.Length) return null;
        var buffer = new byte[size];
        Span<byte> next = stackalloc byte[4];
        _fs.Seek(offset, SeekOrigin.Begin);
        if (!ReadFully(_fs, next)) return null;
        uint nextAddr = BinaryPrimitives.ReadUInt32LittleEndian(next);
        int done = 0;
        int guard = 0;
        while (done < size)
        {
            int take = Math.Min((int)_blockSize - 4, size - done);
            if (!ReadFully(_fs, buffer.AsSpan(done, take))) return null;
            done += take;
            if (done >= size) break;
            if (nextAddr == 0 || nextAddr >= _fs.Length || ++guard > 1_000_000) return null;
            _fs.Seek(nextAddr, SeekOrigin.Begin);
            if (!ReadFully(_fs, next)) return null;
            nextAddr = BinaryPrimitives.ReadUInt32LittleEndian(next);
        }
        return buffer;
    }

    private static bool ReadFully(FileStream fs, Span<byte> dest)
    {
        while (dest.Length > 0)
        {
            int n = fs.Read(dest);
            if (n <= 0) return false;
            dest = dest.Slice(n);
        }
        return true;
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>An RGBA picture (Pixels = Width * Height * 4, bytes R, G, B, A).</summary>
internal sealed class AcIconImage
{
    public int Width;
    public int Height;
    public byte[] Pixels = Array.Empty<byte>();
}

internal static class AcIconDecoder
{
    /// <summary>Icons are 32 x 32; anything bigger than this (a UI picture, not an icon) is refused.</summary>
    public const int MaxSide = 128;
    /// <summary>The largest side a retail UI picture (a window background, a title bar) may have.</summary>
    public const int MaxUiSide = 512;
    private const int MaxTextureFile = 1024 * 1024;
    private const int MaxPaletteFile = 256 * 1024;

    /// <summary>UtilityBelt's rule for an icon id: below 0x06000000, 0x06000000 is added.</summary>
    public static uint NormalizeIconId(uint id) => id < 0x06000000u ? id + 0x06000000u : id;

    /// <summary>0x06xxxxxx: a Texture file (icons live there).</summary>
    public static bool IsTextureId(uint id) => (id >> 24) == 0x06;

    /// <summary>
    /// The item picture AC shows: the underlay, then the icon, then the overlay, each drawn over
    /// the last (alpha blended) at the icon's size. 0 = no underlay / overlay. Null when the icon
    /// itself can't be decoded; an underlay or overlay that can't be is left out.
    /// </summary>
    public static AcIconImage? Compose(AcDatFile dat, uint icon, uint underlay, uint overlay)
    {
        AcIconImage? baseImage = Decode(dat, icon);
        if (baseImage == null) return null;
        AcIconImage? under = underlay != 0 ? Decode(dat, underlay) : null;
        AcIconImage? over = overlay != 0 ? Decode(dat, overlay) : null;
        if (under == null && over == null) return baseImage;

        int w = baseImage.Width, h = baseImage.Height;
        var outPx = new byte[w * h * 4];
        if (under != null) Over(outPx, w, h, under);
        Over(outPx, w, h, baseImage);
        if (over != null) Over(outPx, w, h, over);
        return new AcIconImage { Width = w, Height = h, Pixels = outPx };
    }

    /// <summary>Draws <paramref name="src"/> over <paramref name="dst"/> (w x h), scaled nearest-neighbour when sizes differ.</summary>
    private static void Over(byte[] dst, int w, int h, AcIconImage src)
    {
        for (int y = 0; y < h; y++)
        {
            int sy = src.Height == h ? y : y * src.Height / h;
            for (int x = 0; x < w; x++)
            {
                int sx = src.Width == w ? x : x * src.Width / w;
                int s = (sy * src.Width + sx) * 4, d = (y * w + x) * 4;
                int sa = src.Pixels[s + 3];
                if (sa == 0) continue;
                if (sa == 255)
                {
                    dst[d] = src.Pixels[s];
                    dst[d + 1] = src.Pixels[s + 1];
                    dst[d + 2] = src.Pixels[s + 2];
                    dst[d + 3] = 255;
                    continue;
                }
                int da = dst[d + 3];
                int outA = sa + da * (255 - sa) / 255;
                if (outA <= 0) continue;
                for (int c = 0; c < 3; c++)
                    dst[d + c] = (byte)((src.Pixels[s + c] * sa + dst[d + c] * da * (255 - sa) / 255) / outA);
                dst[d + 3] = (byte)outA;
            }
        }
    }

    /// <summary>
    /// A 0x06 Texture as RGBA, or null (not a texture id, missing, an unsupported format, larger
    /// than <see cref="MaxSide"/>, or short data). Layout: id u32, unknown i32, width i32,
    /// height i32, format u32, length i32, data[length], then for INDEX16/P8 a palette id u32.
    /// </summary>
    public static AcIconImage? Decode(AcDatFile dat, uint textureId) => Decode(dat, textureId, MaxSide);

    /// <summary>
    /// As <see cref="Decode(AcDatFile, uint)"/>, allowing sides up to <paramref name="maxSide"/>
    /// (at most <see cref="MaxUiSide"/>): the retail UI pictures (ImGui/RetailSprites).
    /// </summary>
    public static AcIconImage? Decode(AcDatFile dat, uint textureId, int maxSide)
    {
        if (!IsTextureId(textureId)) return null;
        try
        {
            byte[]? data = dat.Read(textureId, MaxTextureFile);
            return data == null ? null : DecodeTextureFile(data, id => dat.Read(id, MaxPaletteFile), maxSide);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A Texture file's bytes as RGBA (see <see cref="Decode(AcDatFile, uint)"/>); <paramref name="readPalette"/> loads a 0x04 palette.</summary>
    internal static AcIconImage? DecodeTextureFile(byte[] data, Func<uint, byte[]?> readPalette, int maxSide = MaxSide)
    {
        if (data.Length < 24) return null;
        maxSide = Math.Clamp(maxSide, 1, MaxUiSide);
        ReadOnlySpan<byte> d = data;
        int w = BinaryPrimitives.ReadInt32LittleEndian(d[8..]);
        int h = BinaryPrimitives.ReadInt32LittleEndian(d[12..]);
        uint fmt = BinaryPrimitives.ReadUInt32LittleEndian(d[16..]);
        int len = BinaryPrimitives.ReadInt32LittleEndian(d[20..]);
        if (w <= 0 || h <= 0 || w > maxSide || h > maxSide || len < 0 || len > data.Length - 24) return null;
        byte[] src = d.Slice(24, len).ToArray();
        int n = w * h;

        switch (fmt)
        {
            case 827611204: return Dxt(src, w, h, 8, DxtUtil.DecompressDxt1);    // DXT1
            case 861165636: return Dxt(src, w, h, 16, DxtUtil.DecompressDxt3);   // DXT3
            case 894720068: return Dxt(src, w, h, 16, DxtUtil.DecompressDxt5);   // DXT5
        }

        var rgba = new byte[n * 4];
        void Px(int i, int r, int g, int b, int a)
        {
            int o = i * 4;
            rgba[o] = (byte)r; rgba[o + 1] = (byte)g; rgba[o + 2] = (byte)b; rgba[o + 3] = (byte)a;
        }

        switch (fmt)
        {
            case 20:  // R8G8B8, stored B, G, R
                for (int i = 0; i < n && i * 3 + 2 < src.Length; i++) Px(i, src[i * 3 + 2], src[i * 3 + 1], src[i * 3], 255);
                break;
            case 243: // CUSTOM_LSCAPE_R8G8B8, stored R, G, B
                for (int i = 0; i < n && i * 3 + 2 < src.Length; i++) Px(i, src[i * 3], src[i * 3 + 1], src[i * 3 + 2], 255);
                break;
            case 21:  // A8R8G8B8, stored B, G, R, A
            case 22:  // X8R8G8B8
                for (int i = 0; i < n && i * 4 + 3 < src.Length; i++)
                    Px(i, src[i * 4 + 2], src[i * 4 + 1], src[i * 4], fmt == 22 ? 255 : src[i * 4 + 3]);
                break;
            case 23:  // R5G6B5
                for (int i = 0; i < n && i * 2 + 1 < src.Length; i++)
                {
                    int v = src[i * 2] | (src[i * 2 + 1] << 8);
                    Px(i, ((v >> 11) & 0x1F) << 3, ((v >> 5) & 0x3F) << 2, (v & 0x1F) << 3, 255);
                }
                break;
            case 26:  // A4R4G4B4
                for (int i = 0; i < n && i * 2 + 1 < src.Length; i++)
                {
                    int v = src[i * 2] | (src[i * 2 + 1] << 8);
                    Px(i, ((v >> 8) & 0xF) * 17, ((v >> 4) & 0xF) * 17, (v & 0xF) * 17, ((v >> 12) & 0xF) * 17);
                }
                break;
            case 28:  // A8 (greyscale)
            case 244: // LSCAPE_ALPHA
                for (int i = 0; i < n && i < src.Length; i++) Px(i, src[i], src[i], src[i], 255);
                break;
            case 101: // INDEX16
            case 41:  // P8
            {
                if (data.Length < 24 + len + 4) return null;
                uint paletteId = BinaryPrimitives.ReadUInt32LittleEndian(d[(24 + len)..]);
                uint[]? pal = Palette(readPalette(paletteId));
                if (pal == null || pal.Length == 0) return null;
                bool p8 = fmt == 41;
                for (int i = 0; i < n; i++)
                {
                    int idx;
                    if (p8) { if (i >= src.Length) break; idx = src[i]; }
                    else { if (i * 2 + 1 >= src.Length) break; idx = src[i * 2] | (src[i * 2 + 1] << 8); }
                    uint c = pal[idx % pal.Length];
                    Px(i, (int)((c >> 16) & 0xFF), (int)((c >> 8) & 0xFF), (int)(c & 0xFF), (int)((c >> 24) & 0xFF));
                }
                break;
            }
            default:
                return null;   // a format icons don't use
        }
        return new AcIconImage { Width = w, Height = h, Pixels = rgba };
    }

    private static AcIconImage? Dxt(byte[] src, int w, int h, int blockBytes, Func<byte[], int, int, byte[]> decompress)
    {
        long need = (long)((w + 3) / 4) * ((h + 3) / 4) * blockBytes;
        if (src.Length < need) return null;   // DxtUtil would read past the end
        byte[] rgba = decompress(src, w, h);
        return rgba.Length == w * h * 4 ? new AcIconImage { Width = w, Height = h, Pixels = rgba } : null;
    }

    /// <summary>Palette (0x04): id u32, count i32, count x u32 ARGB.</summary>
    private static uint[]? Palette(byte[]? data)
    {
        if (data == null || data.Length < 8) return null;
        int n = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        if (n <= 0 || n > 65536) return null;
        n = Math.Min(n, (data.Length - 8) / 4);
        var pal = new uint[n];
        for (int i = 0; i < n; i++) pal[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8 + i * 4));
        return pal;
    }
}
