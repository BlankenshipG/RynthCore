# 2026.10.5.64 - engine: second client can open the DAT files again (2025 client)

Previous release: 2026.10.5.62.

## Problem

With "Allow multiple clients" on, only the first client started. Every client launched after it
(Silentstorm, SK, ...) showed "Can't open the data files. Check that they exist and that you
have permission to write to them. The program will now exit." about half a second after the
engine loaded.

The 2025 client (`C:\Turbine\Asheron's Call-ryn\ACClient.exe`, 0.0.11.6096) does not import
`kernel32!CreateFileW`. A Layer-for-Unicode style stub at `0x006B8D00` looks it up on first use
and stores it in a private slot at `0x00837374`, and AC opens every DAT file through that slot.

- Up to 2026.10.5.60 the DAT share hook detoured the kernel32 export, so it caught those opens.
  The log showed them as `Compat: DAT share - W [filtered]: client_portal.dat ... share=0x1->0x7`.
- 2026.10.5.61 moved the hook to the import tables of `acclient.exe` and `MSVCR70.dll`, to keep
  Direct3D and drivers out of it. The private slot is in no import table, so from .61 on no DAT
  open was rewritten. The `W [filtered]` lines stopped appearing.
- The first client then opened the DATs read/write and denied writes to everyone else
  (`share=0x1`). A second client's own read/write open failed with a sharing violation.

Confirmed on the running client: the three import slots pointed at the engine's detours, but the
private slot `0x00837374` still held `kernel32!CreateFileW`.

## Change

- The DAT share hook also finds acclient.exe's private CreateFileW/CreateFileA slots, then points
  them at the same detours. Uninstall restores them like the import slots.
- The slots are found by the stub's full byte pattern, not a fixed address:
  - push slot, push the function name, push "kernel32.dll", call the resolver, then
    `jmp [slot]` through the same slot;
  - the name must stand alone ("MyCreateFileW" does not count);
  - the DLL operand must name kernel32.dll.
- If the slot hasn't been resolved yet (it still points at its stub), the detour calls
  `kernel32!CreateFileW` directly, because calling the stub would overwrite the slot.
- Direct3D, drivers, the runtime and plugins still call CreateFile without the engine in the way.
- The startup log lists the slot, for example
  `Compat: DAT share - hooked acclient.exe!CreateFileW private slot (stub 0x006B8D00) (slot 0x00837374, was 0x76ACEA10).`
  With multi-client on, the `W [filtered]: client_portal.dat` lines come back.

## Code

- `src/RynthCore.Engine/Compatibility/LazyImportSlotScanner.cs` (new): reads the section table
  of a mapped image and finds the stubs (`ReadSections`, `FindSlots`).
- `src/RynthCore.Engine/Compatibility/DatFileShareHooks.cs`:
  - `HookLazySlots` hooks the private slots;
  - `HookSlot` builds the A/W detour for import slots and private slots alike;
  - the log line names the kind of slot.
- `tools/DatShareSlotTests` (new), 20 checks:
  - synthetic stubs;
  - near misses that must not match (another jmp slot, another DLL, a longer name, no call, a
    slot outside the image, a lone push of the name);
  - the real 2025 client: slot `0x00837374`, stub `0x006B8D00`, no CreateFileA slot.

## Tests

- `dotnet run -c Release --project tools\DatShareSlotTests`: 20 checks, 0 failed.
- `dotnet build src\RynthCore.Engine -c Release`: 0 errors, no new warnings.

## Before updating

Close the other clients first. A client already running on .61/.62 holds the DATs deny-write,
so a new client can't start alongside it until it is closed, even on .64.
