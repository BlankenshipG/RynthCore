# Inventory: the Vault button shows again (engine 2026.10.18.14)

On Aelrynth, the Classic Inventory's title bar has a **Vault** button: the account vault, from the
Aeshnidae.Mule mod. rynth's main shows it; the test branch didn't.

**Why:** the test branch added four text buttons to the Classic title bar: Remote, Sort, Count and
GInv, about 185 px together. Vault went to the left of them. The bar is 300 px at 100 %, so Vault
started about 44 px left of the panel's edge and was clipped away. The "Inventory of <name>" title
had no room left either.

**Now:**
- **Vault** is back next to the size buttons, where rynth's main has it.
- **The four related panels** (Mini Remote, RynthSort, Item Count, Global Inventory) share one "more" button (three dots). It opens a menu with a check against each panel that is open.
- **The title** has room again.

The Modern look is unchanged: its header keeps its four icon buttons. Like rynth's main, it has
no Vault button; the Vault is in the Classic look.
