# 2026.10.16.1 - Skills under Oracle; buffing prots/banes UI removed

Previous: 2026.10.15.9. Major feature (new Oracle surface).

## Skills → Oracle

The standalone Skills panel body (raise / train / attributes / ILT progression)
now embeds inside RynthOracle as three tabs:

- **Skills** — raise and train (`/ro skills`, `/rc skills`)
- **Attributes** — attributes and vitals (`/ro attrs`)
- **Progress** — ILT progression planners (`/ro progress`, `/rc progression`)

Engine bridge: `OracleSkillsEmbed` + `ScriptWindowFace` draws `SkillsFace` after
the Oracle tab strip when `/rc oracle-skills <0|1|2|off>` is set. The Skills bar
button is removed; Character / Mini Remote menus open Oracle instead. Standalone
Skills remains as a fallback when Oracle is not loaded.

## Files (Core)

- `ImGui/Panels/OracleSkillsEmbed.cs` (new)
- `ImGui/Panels/SkillsFace.cs` — `DrawEmbedded` / hosted body
- `ImGui/Panels/ScriptWindowFace.cs` — Oracle embed draw + clear on hide
- `ImGui/ImGuiBar.cs` — drop Skills bar button
- `ImGui/Panels/RynthAiFace.cs` — Character menu → `/ro skills|attrs|progress`
- `Compatibility/RynthCoreChatCommands.cs` — `/rc oracle-skills`, retarget skills/progression

## Deploy

`Publish-EngineCoreClr.ps1 -EngineOnly -AllowBehind -MsBuildArgs @('-p:Version=2026.10.16.1')`
→ `C:\Games\RynthCore\Runtime\`. Matching Suite plugins stamp the same calver.
