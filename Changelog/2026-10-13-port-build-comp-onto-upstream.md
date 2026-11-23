# Port: local build-comp-10-11 onto upstream 2026.10.13.2 (plugin API v84)

Branch `port/build-comp-on-2026.10.13.2`, based on `aelrynth/main`. Upstream's version is kept for
everything rynth already took; only what upstream lacks is re-applied. Nothing is pushed.

## Plugin API

Upstream used v80-v83 (GetContainerOrder / PutItemInContainer, GetMergeStackResult, SalvageDirect,
GetObjectServerAge / RemoveClientObject). The local ArmConfirmation / GetLastConfirmation slots
move to **v84**, appended after RemoveClientObjectFn in `PluginContract.cs` and
`RynthCoreApiNative.cs`. `PluginContractVersion.Current` and the SDK targets are 84;
`RynthCoreHost.HasConfirmationAnswers` checks `Version >= 84`. PluginManifestTests updated
for the new slot order.

## Kept from the local branch

- Installer: local component layout, `overwritereadonly` on every ignoreversion file,
  `Permissions: users-modify` on the install folders, and the read-only clearing in
  `Build-Installer.ps1`.
- Items panel weapon rows tinted by damage type; Skills panel Progression header wording.
- SafeImGui coverage (`%` in ImGui text): ChatFace, MetaFace.Editor and the Inventory Give/Drop
  zone tooltip (it carries the target's name). Strings that are already escaped are not wrapped
  twice.

## Taken from upstream

- `native/SehTrampoline` v6.
- Server Features, Progression and Guardian live in RynthILT; the engine's ILT server menu and
  chat commands follow upstream.

## Tests

- Engine builds in Release.
- `tools/PluginManifestTests`: PASS (195 assertions).
