# Settings and Damage panel: environment-hit throttle, per-monster attack power

Engine: the next build after 2026.10.5.38. The version is set when the installer is built. Pairs
with RynthAi 0.5.47-legacy-ui.

## What changed

- `UI/Data/SettingsData.cs`:
  - New properties:
    - `VariablePowerMin` (50) and `VariablePowerMax` (100);
    - `MissileEnvironmentResponse` and `MagicEnvironmentResponse` (0 Off, 1 Move closer,
      2 New target, 3 Move closer then new target);
    - `EnvironmentImpactLimit` (2), `EnvironmentImpactWindowSec` (20),
      `EnvironmentApproachMeters` (6), `EnvironmentMinDistance` (3) and `EnvironmentSkipSec` (30).
  - Melee Combat tab:
    - a "Variable Power (monster rules)" section with Variable Min % and Max %;
    - a "Hitting the Environment" section with Missile Response and the shared numbers (Hits to
      trip, Window, Move closer by, Closest, Skip target).
  - Spell Combat tab: a "Hitting the Environment" section with Magic Response.
- `UI/Panels/MonstersPanel.cs`:
  - `Rule.AttackPower` (default "Default");
  - change detection (`RulesEqual`) includes it.
- `UI/Data/DamageData.cs`:
  - `DamageCommands.AttackPowerModes` (Default, High, Reckless, Variable, Reckless + Variable).
  - A new rule copied from the DEFAULT line follows it ("Default"), the same as the off hand and pet.
  - A rule copied from a name rule keeps that rule's power.
- `ImGui/Panels/DamageDetailFace.cs`: a "Power" combo on every rule, the DEFAULT line included,
  next to Damage and Extra vuln.

What RynthAi does with these settings is described in RynthSuite
`Changelog/2026-10-12-environment-impact-throttle-attack-power.md`.

## Compatibility

- An older RynthAi ignores the new settings and the AttackPower field.
- RynthAi 0.5.47 with an older engine keeps its own values, and keeps each rule's power when the
  engine sends rules without it.

The engine builds in Release. Not yet tested in game.
