# 2026-10-05 - ILT Guardian window, Mini Remote slot assignment

RynthAi 0.5.11-legacy-ui (previous 0.5.10-legacy-ui) with the RynthCore engine changes below.
Ships in installer 2026.10.5.10.

## Guardian window (ported from UB-IT's Guardian tab)

Open it from the Mini Remote's Options, the dashboard Char button's right-click menu, or
`/ra hub open guardian` (`/ra guardian window`).

- **Riddle translator.** Translates incoming "Guardian of the Temple of Enlightenment" and
  "Guardian of Attribute Enlightenment" tells and says (all 18 Temple spell words and the full
  attribute-riddle table, typo-tolerant), plus a paste box for manual translation. The answer
  shows in the window and as a "Guardian:" row on the Mini Remote for 10 minutes, with a
  Give button.
- **Hand-in.** Gives one of the answer item to the guardian (whole item when it isn't a stack).
  For the Temple guardian, when you carry none and "Buy if missing" is on, it first buys one
  from the open or nearest vendor through the engine's vendor trade API. It waits out the busy
  hourglass, has per-step timeouts, and reports every failure in chat. "Auto hand-in" runs it
  after each chat answer (off by default).
- **Attribute turn-ins.** One row per Fiun attribute NPC plus the riddle. Counts and cooldowns
  come from /myquests when the server reports the flag, otherwise from "/qb list" wait stamps
  (a stamp appearing counts a turn-in and starts an estimated cooldown; "Assumed cooldown"
  sets its length). Rows can be overridden with `infi attribute.csv` in the RynthAi folder or
  UB's `Documents\Asheron's Call` location.
- **Commands:** `/ra guardian [window|translate <text>|handin|stop|chat on|off|auto on|off|buy on|off|refresh|status]`.
- **Not ported:** UB's `/fillcomps` + "buy all" spell-component restock during the Temple
  hand-in. It is unrelated to the guardian quest, and the engine buys specific items rather
  than the client's buy list.

New files: `IltHub/IltGuardian.cs`, `IltHub/IltGuardianPhrasebook.cs`, `IltHub/IltTempleAttributes.cs`.
Settings persist per character in `ilt-hub.json` (`Guardian` block, `GuardianWindowOpen`).

## Mini Remote slot assignment

Previously, items could not be put into Mini Remote slots from the game inventory or from a
popped-out RynthCore Inventory.

- **Game inventory drag-and-drop (engine `ItemDragBridge`).** A left drag that starts on a
  carried item in AC's own inventory is now published to ImGui. Releasing it on an **empty**
  Mini Remote slot assigns it. A game drag released over any RynthCore window is handed back
  to AC at its starting point, so the item is never dropped on the ground.
- **Popped-out RynthCore Inventory.** Drags from a popped-out Inventory window now reach the
  Mini Remote (the pop-out runs in its own ImGui context).
- **Click to assign.** Clicking an item in the RynthCore Inventory also selects it in the game.
  The Mini Remote remembers the last carried item you selected for 2 minutes, so the combat
  bot re-selecting monsters no longer loses it before you click an empty slot.
- **Inventory right-click: "Add to Mini Remote".** Adds the item to the first empty slot or a
  chosen slot (remote command `remoteslot <slot|first> <objectId>`).
- Empty-slot tooltips explain all three ways to assign.

### Known limits

- The engine infers a game-inventory drag from AC's selection. Dragging an AC panel while a
  pack item is selected looks the same, which is why game drags only fill empty slots.
  Dragging from the RynthCore Inventory can replace a filled slot.
- If an AC panel is dragged and released over a RynthCore window while a pack item is selected,
  the panel returns to where the drag started.
