# 2026.10.5.70 - CoreCLR Runtime deploy: Settings > Display borderless / layout

Previous release: 2026.10.5.69.

## Why you could not see them

Live clients load the engine from `C:\Games\RynthCore\Runtime\` (CoreCLR host),
not the NativeAOT `C:\Games\RynthCore\RynthCore.Engine.dll`. Earlier hotbuilds
only updated the AOT copy. Runtime was still **2026.10.15.7**, which had no
**Borderless Windowed**, **Remember Open Windows and Positions**, or **Save Layout Now**.

## Fix

Published the local engine as CoreCLR (`-p:EngineHost=coreclr`) and copied it to
`Runtime\RynthCore.Engine.dll` as **2026.10.5.70**, including the Display rows from
2026.10.5.65–.67.

## Where to look

**Settings > Display** (Advanced Settings > Display):

- Borderless Windowed  
- Remember Open Windows and Positions  
- Save Layout Now  

Hot reload should pick up the new Runtime DLL; otherwise reinject.
