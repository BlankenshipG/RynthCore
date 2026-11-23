# Engine 2026.10.19.3: Items panel Prismatic damage type (2026-10-19)

Previous release: 2026.10.19.2 (the merged test branch over rynth's 2026.10.19.1). Goes with
RynthAi 0.5.93, which treats Prismatic as universal damage.

## Changed

- **Prismatic damage type.** Missile weapons and ammo on the Items panel can be set to
  "Prismatic", universal damage. RynthAi resolves it per target to the weakest element, or the
  Monsters rule's damage type. Other kinds keep the eight elements.
  - It works in both the ImGui and Avalonia faces.
  - The list is `ItemsCommands.MissileElements`, chosen by `ItemsCommands.ElementsFor(kind)`.
- **Its colour** is a light rose (`DamageTypeColors.Prismatic`).
