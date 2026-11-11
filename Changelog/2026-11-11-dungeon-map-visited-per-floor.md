# 2026-11-11 — Dungeon Map: each shown floor draws its own walked cells

The Dungeon Map drew the walked (visited) cells for the player's floor only, and RynthAi's snapshot
painted them by x/y, so the cells walked on other floors showed up too. RynthAi now paints the floor you
stand on, separately for each floor (RynthSuite 2026-11-11-dungeon-trail-stays-on-its-floor). The map draws
each shown floor's own walked cells: the player's floor in full, the other shown floors dimmed as their walls
are. Floors that aren't shown draw none.
