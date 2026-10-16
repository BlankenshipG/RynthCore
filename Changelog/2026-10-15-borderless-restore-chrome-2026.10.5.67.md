# 2026.10.5.67 - Borderless Windowed: restore title bar when turning off

Previous release: 2026.10.5.66.

## Bug

Turning **Borderless Windowed** off did not bring back the Asheron's Call title bar
and borders — especially when the client was maximized (or otherwise filled the
monitor). `IsLikelyExclusiveFullscreen` treated that maximized borderless frame as
exclusive fullscreen and skipped the restore path, so chrome stayed gone. Turning
the setting back on then did nothing useful.

Seen on Silentkelpie (pid 34764 / earlier 55676): log showed `borderless windowed on`
and never `window borders restored`.

## Fix

- Do not block chrome restore for maximized borderless windows we stripped.
- Only skip applying borderless when the client already looks like true exclusive
  fullscreen and we have not already taken chrome off.
- If the saved style was lost (hot reload) or already lacked a caption, rebuild
  chrome with the full border mask so Off still heals a stuck window.
- Settings toggle uses the same HWND resolver as Tick.

## Test plan

- [ ] Windowed AC: Borderless on → chrome gone; off → title bar and borders return.
- [ ] Maximized windowed: same on/off cycle restores chrome.
- [ ] Exclusive fullscreen: Borderless toggle does not fight the client.
