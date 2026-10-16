# 2026.10.5.71 - SK port: Display chrome + Vaulted Dungeons Options gate

Previous release: 2026.10.5.70.

## Summary

Standing `sk` branch now carries tonight's local engine work for upstream release prep:

- **Settings > Display**: Borderless Windowed, Remember Open Windows and Positions, Save Layout Now (`AcWindowChrome`, EngineSettings, PanelStateStore).
- **Settings > Navigation > Vaulted Dungeons**: Options checkbox (default off) that gates the Mini Remote ILT "Vaulted Dungeons" tab (paired with RynthSuite `sk` Ai/ILT).
- Chat type **0x1C** recognized for ILT channel lines (ChatData).
- Loot picker filter and related FaceKit / Mini Remote face polish from 2026.10.5.65–.70.

## Deploy note

Live clients load CoreCLR from `C:\Games\RynthCore\Runtime\`. Publish with
`scripts\Publish-EngineCoreClr.ps1 -MsBuildArgs @('-p:Version=2026.10.5.71')`.
