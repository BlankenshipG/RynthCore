#!/usr/bin/env python3
"""
Offline check of the UI hooks (Compatibility/UiFlowHooks.cs, Compatibility/UiElementHooks.cs)
against an acclient.exe. Read-only: maps the file, runs nothing.

pe_pattern.py CHECK already proves every embedded pattern is unique and lands on its VA.
This goes further and checks the facts the detours rely on, so a different client build
fails here instead of in game:

  * each target starts a function (padding or a ret right before it) and returns the way
    its detour's signature says (ret / ret 4 / ret 0Ch / ret 14h, or a tail jump);
  * UseNewMode copies UIFlow+0x90 into +0x8C (the offsets the detour reads);
  * StartTooltip / ResetTooltip use UIElementManager +0x2F4 / +0x2F8, ResetTooltip
    tail-jumps into CheckTooltip;
  * StartDragandDrop stores the drag icon at +0x31C and its owner at +0x320;
  * StopDragandDrop calls the catcher's vtable +0xD8, and every UIElement vtable in the
    client has CatchDroppedItem there (directly, or UIElement_Field's, which calls it);
  * every UIElement vtable has GetUIElementType at +0x98 (mov eax,imm32 / ret, or xor/ret),
    UIElement_ItemList's returns 0x10000031, GetParent is the vtable +0xA0 call that
    GetAncestorByID walks, and the element id is at +0x2E4.

Usage:  python tools/ui_hooks_check.py ["<path-to-acclient.exe>"]
Exit 0 = every check passed.
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pe_pattern import read_window, matches, parse_engine_patterns  # noqa: E402

DEFAULT_CLIENT = r"C:\Turbine\Asheron's Call\acclient.exe"
N = None

fails = 0


def check(ok, msg):
    global fails
    print(("ok   " if ok else "FAIL ") + msg)
    if not ok:
        fails += 1


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_CLIENT
    if not os.path.exists(path):
        print(f"acclient.exe not found: {path}")
        return 2
    W, BASE = read_window(path)

    def at(va, n):
        o = va - BASE
        return W[o:o + n]

    def u32(va):
        return struct.unpack_from("<I", W, va - BASE)[0]

    def find(pat):
        ms = matches(W, pat, limit=4)
        return BASE + ms[0] if len(ms) == 1 else None

    def body_has(va, needle, span=0x200):
        return W.find(bytes(needle), va - BASE, va - BASE + span) >= 0

    def rel_callers(target, op=0xE8):
        out, i = [], 0
        while True:
            i = W.find(bytes([op]), i)
            if i < 0 or i + 5 > len(W):
                break
            if BASE + i + 5 + struct.unpack_from("<i", W, i + 1)[0] == target:
                out.append(BASE + i)
            i += 1
        return out

    def abs_refs(value):
        needle, out, i = struct.pack("<I", value), [], 0
        while True:
            i = W.find(needle, i)
            if i < 0:
                break
            out.append(BASE + i)
            i += 1
        return out

    # 1. the engine's own patterns for these two files, resolved here
    want = {"UiFlow.UseNewMode": 0x00479AA0, "UiFlow.ClientCleanup": 0x004118D0,
            "UiElement.StartTooltip": 0x0045DF70, "UiElement.ResetTooltip": 0x0045C440,
            "UiElement.CheckTooltip": 0x0045B7C0, "UiElement.StartDragandDrop": 0x0045E120,
            "UiElement.CatchDroppedItem": 0x00461860, "UiElement.InqDropIconInfo": 0x004E3380}
    found = {}
    for it in parse_engine_patterns():
        if it["file"] in ("UiFlowHooks.cs", "UiElementHooks.cs") and it["kind"] == "fn":
            va = find(it["pattern"])
            found[it["name"]] = va
            check(va == it["va"], f"{it['name']}: pattern unique at 0x{(va or 0):08X} (fallback 0x{it['va']:08X})")
    for name in want:
        check(name in found, f"{name}: pattern present in the engine source")
    va = {k: (found.get(k) or v) for k, v in want.items()}

    # 2. function starts and returns
    for name, v in va.items():
        prev = at(v - 4, 4)
        boundary = prev[-1] in (0x90, 0xCC, 0xC3) or (prev[-3] == 0xC2 and prev[-1] == 0x00)
        check(boundary, f"{name}: starts a function (bytes before: {prev.hex(' ')})")
    check(body_has(va["UiElement.StartTooltip"], [0xC2, 0x14, 0x00], 0x1B0), "StartTooltip: ret 14h (5 stack args: StringInfo*, owner, 3 ids)")
    check(body_has(va["UiElement.StartDragandDrop"], [0x32, 0xC0, 0x5B, 0x83, 0xC4, 0x34, 0xC2, 0x0C, 0x00]), "StartDragandDrop: ret 0Ch (element, x, y), returns al")
    check(at(va["UiElement.CatchDroppedItem"] + 14, 5) == bytes([0xB0, 0x01, 0xC2, 0x04, 0x00]), "CatchDroppedItem: mov al,1 / ret 4 (one arg: DragDropInfo*)")
    check(at(va["UiElement.CatchDroppedItem"] + 7, 2) == bytes([0x6A, 0x15]), "CatchDroppedItem: broadcasts element message 0x15 (drop release)")
    check(body_has(va["UiFlow.UseNewMode"], [0x81, 0xC4, 0x90, 0x00, 0x00, 0x00, 0xC3]), "UseNewMode: plain ret (no args)")
    check(body_has(va["UiFlow.ClientCleanup"], [0x5E, 0xE9], 0xB0), "Client::Cleanup: ends in a tail jump (no args)")
    callers = rel_callers(va["UiElement.InqDropIconInfo"])
    check(len(callers) >= 10, f"InqDropIconInfo: {len(callers)} callers in the client (AC uses it itself)")
    popping = [c for c in callers if body_has(c + 5, [0x83, 0xC4, 0x10], 0x0C)]
    check(len(popping) >= len(callers) - 1,
          f"InqDropIconInfo: cdecl with 4 args - {len(popping)}/{len(callers)} callers pop 16 bytes right after (one batches it later)")
    check(at(va["UiElement.InqDropIconInfo"], 4) == bytes([0x8B, 0x44, 0x24, 0x10]) and body_has(va["UiElement.InqDropIconInfo"], [0x3B, 0xF3], 0x20),
          "InqDropIconInfo: reads its 4th arg first and null-checks the element")

    # 3. UseNewMode: +0x90 -> +0x8C
    check(body_has(va["UiFlow.UseNewMode"], [0x8B, 0x8E, 0x90, 0x00, 0x00, 0x00, 0x57, 0x89, 0x8E, 0x8C, 0x00, 0x00, 0x00]),
          "UseNewMode: mov ecx,[esi+90h] / mov [esi+8Ch],ecx (pending -> current mode)")
    check(len(rel_callers(va["UiFlow.UseNewMode"])) == 1, "UseNewMode: one call site")
    check(len(rel_callers(va["UiFlow.ClientCleanup"])) == 1 and len(abs_refs(va["UiFlow.ClientCleanup"])) >= 1,
          "Client::Cleanup: one direct call (gmClient::Cleanup) and a vtable entry")

    # 4. tooltip offsets
    check(body_has(va["UiElement.StartTooltip"], [0x89, 0xBE, 0xF4, 0x02, 0x00, 0x00], 0x60), "StartTooltip: owner stored at +0x2F4")
    check(body_has(va["UiElement.ResetTooltip"], [0xC7, 0x86, 0xF8, 0x02, 0x00, 0x00, 0, 0, 0, 0], 0x20), "ResetTooltip: clears the tooltip element at +0x2F8")
    jmp = W.find(bytes([0x8B, 0xCE, 0x5E, 0xE9]), va["UiElement.ResetTooltip"] - BASE, va["UiElement.ResetTooltip"] - BASE + 0x80)
    tail = BASE + jmp + 3 + 5 + struct.unpack_from("<i", W, jmp + 4)[0] if jmp >= 0 else 0
    check(tail == va["UiElement.CheckTooltip"], f"ResetTooltip: tail-jumps into CheckTooltip (0x{tail:08X})")
    inner = W.find(bytes([0x89, 0xB3, 0xF8, 0x02, 0x00, 0x00]), va["UiElement.StartTooltip"] - BASE - 0x6000, va["UiElement.StartTooltip"] - BASE)
    check(inner >= 0, "the inner StartTooltip stores the new tooltip element at +0x2F8")

    # 5. drag: icon +0x31C, owner +0x320
    check(body_has(va["UiElement.StartDragandDrop"], [0x89, 0x9E, 0x1C, 0x03, 0x00, 0x00], 0x260), "StartDragandDrop: drag icon stored at +0x31C")
    check(body_has(va["UiElement.StartDragandDrop"], [0x89, 0x8E, 0x20, 0x03, 0x00, 0x00], 0x260), "StartDragandDrop: drag owner stored at +0x320")
    check(len(rel_callers(va["UiElement.StartDragandDrop"])) == 5, "StartDragandDrop: 5 call sites (one is itself: the drag proxy)")

    # 6. StopDragandDrop builds DragDropInfo and calls the catcher's +0xD8
    stop = find([0x53, 0x57, 0x8B, 0xF9, 0x8B, 0x87, 0x1C, 0x03, 0x00, 0x00, 0x33, 0xDB, 0x3B, 0xC3, 0x0F, 0x84])
    check(stop is not None, f"StopDragandDrop found (0x{(stop or 0):08X})")
    if stop:
        check(body_has(stop, [0x89, 0x46, 0x08], 0x50) and body_has(stop, [0x89, 0x56, 0x0C], 0x60) and body_has(stop, [0x89, 0x4E, 0x10], 0x60),
              "StopDragandDrop: DragDropInfo +0x08 icon, +0x0C owner, +0x10 catcher")
        check(body_has(stop, [0xFF, 0x90, 0xD8, 0x00, 0x00, 0x00], 0x80), "StopDragandDrop: calls catcher->vtable[+0xD8] (CatchDroppedItem)")

    # 7. every UIElement vtable: +0xD8, +0x98, +0xA0
    ancestor = find([0x8B, 0x01, 0xFF, 0x90, 0xA0, 0x00, 0x00, 0x00, 0x85, 0xC0, 0x74, 0x1C, 0x56, 0x8B, 0x74, 0x24, 0x08,
                     0x39, 0xB0, 0xE4, 0x02, 0x00, 0x00])
    check(ancestor is not None, f"UIElement::GetAncestorByID found (walks vtable +0xA0, compares +0x2E4) at 0x{(ancestor or 0):08X}")
    field = find([0x56, 0x8B, 0xF1, 0x8A, 0x86, 0xF0, 0x05, 0x00, 0x00, 0x84, 0xC0, 0x74, 0x18])
    if field:
        call = W.find(bytes([0xE8]), field - BASE + 0x2C, field - BASE + 0x2D)
        target = BASE + call + 5 + struct.unpack_from("<i", W, call + 1)[0] if call >= 0 else 0
        check(target == va["UiElement.CatchDroppedItem"], "UIElement_Field::CatchDroppedItem calls UIElement::CatchDroppedItem")
    if ancestor:
        tables = [r - 0xA4 for r in abs_refs(ancestor)]
        check(len(tables) >= 100, f"{len(tables)} UIElement vtables (GetAncestorByID at +0xA4)")
        bad_drop = [t for t in tables if u32(t + 0xD8) not in (va["UiElement.CatchDroppedItem"], field)]
        check(not bad_drop, f"every UIElement vtable's +0xD8 reaches CatchDroppedItem ({len(tables) - len(bad_drop)}/{len(tables)})")
        bad_type = [t for t in tables if not (at(u32(t + 0x98), 1) == b"\xB8" and at(u32(t + 0x98) + 5, 1) == b"\xC3" or at(u32(t + 0x98), 3) == b"\x33\xC0\xC3")]
        check(not bad_type, f"every UIElement vtable's +0x98 is GetUIElementType ({len(tables) - len(bad_type)}/{len(tables)})")
        itemlist = [t for t in tables if at(u32(t + 0x98), 6) == bytes([0xB8, 0x31, 0x00, 0x00, 0x10, 0xC3])]
        check(len(itemlist) >= 1, f"UIElement_ItemList's GetUIElementType returns 0x10000031 ({len(itemlist)} vtable(s))")

    print("=" * 60)
    print("PASS - the UI hooks' facts hold on this client." if fails == 0 else f"FAIL - {fails} check(s) failed.")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
