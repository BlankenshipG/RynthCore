"""Check that every Dear ImGui function the engine calls exists in our cimgui.dll.

ImGui.NET binds native functions by name and lazily, so a call to a function
the DLL doesn't export (our build compiles out obsolete APIs, e.g.
GetContentRegionMax in 1.91) compiles fine and only fails - on AC's render
thread - the first time that line runs. Run after adding ImGui code:

    python tools/check_imgui_calls.py

Exit code 1 lists the calls with no matching export.
"""
import re
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DLL = ROOT / "src" / "RynthCore.Engine" / "Native" / "cimgui.dll"
SOURCES = ROOT / "src" / "RynthCore.Engine" / "ImGui"
# Old diagnostic shell, FORCE-gated and never run: not held to this check.
SKIP = {"RynthCoreShell.cs"}

# Wrapper-object calls: variable/field name -> cimgui prefix.
MEMBER_PREFIX = {
    "dl": "ImDrawList_",
    "clipper": "ImGuiListClipper_",
    "io.Fonts": "ImFontAtlas_",
    "style": "ImGuiStyle_",
}


def pe_exports(path: Path) -> set[str]:
    data = path.read_bytes()
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    nsec = struct.unpack_from("<H", data, pe + 6)[0]
    opt = pe + 24
    magic = struct.unpack_from("<H", data, opt)[0]
    dd = opt + (96 if magic == 0x10B else 112)
    exp_rva = struct.unpack_from("<I", data, dd)[0]
    sections = []
    sec = opt + struct.unpack_from("<H", data, pe + 20)[0]
    for i in range(nsec):
        vsize, va, raw_size, raw_ptr = struct.unpack_from("<IIII", data, sec + i * 40 + 8)
        sections.append((va, max(vsize, raw_size), raw_ptr))

    def off(rva: int) -> int:
        for va, size, raw in sections:
            if va <= rva < va + size:
                return rva - va + raw
        raise ValueError(hex(rva))

    e = off(exp_rva)
    count, names_rva = struct.unpack_from("<I", data, e + 24)[0], struct.unpack_from("<I", data, e + 32)[0]
    names = set()
    for i in range(count):
        n = off(struct.unpack_from("<I", data, off(names_rva) + i * 4)[0])
        names.add(data[n:data.index(b"\0", n)].decode())
    return names


def main() -> int:
    exports = pe_exports(DLL)

    def found(prefix: str, name: str) -> bool:
        full = prefix + name
        return full in exports or any(x.startswith(full + "_") for x in exports)

    missing = []
    for f in sorted(SOURCES.rglob("*.cs")):
        if f.name in SKIP:
            continue
        text = f.read_text(encoding="utf-8")
        for m in re.finditer(r"ImGui(?:NET\.ImGui)?\.(\w+)\(", text):
            if not found("ig", m.group(1)):
                missing.append(f"{f.relative_to(ROOT)}:{text.count(chr(10), 0, m.start()) + 1}: ImGui.{m.group(1)}")
        # Direct native calls name the exact export (overload suffix included).
        for m in re.finditer(r"\bImGuiNative\.(\w+)\(", text):
            if m.group(1) not in exports:
                missing.append(f"{f.relative_to(ROOT)}:{text.count(chr(10), 0, m.start()) + 1}: ImGuiNative.{m.group(1)}")
        for m in re.finditer(r"\b(dl|clipper|io\.Fonts|style)\.(\w+)\(", text):
            if not found(MEMBER_PREFIX[m.group(1)], m.group(2)):
                missing.append(f"{f.relative_to(ROOT)}:{text.count(chr(10), 0, m.start()) + 1}: {m.group(1)}.{m.group(2)}")

    if missing:
        print("ImGui calls with no export in cimgui.dll:")
        print("\n".join("  " + m for m in missing))
        return 1
    print(f"OK: every ImGui call resolves ({len(exports)} exports).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
