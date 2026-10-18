# Graphics: low-memory safeguard (engine 2026.10.18.11)

## What happened

A tester's client (test build 2026.10.18.4) died on every launch since 16:56:
- **The fill-up:** about 13 s after reaching the world (near Hebian-to), Asheron's Call's own memory climbed from 450 MB to 3.1 GB. The .NET heap stayed at 49 MB.
- **The end:** free address space fell to 66 MB with no free block left. The 32-bit client has 4 GB. RynthNav then failed with "Insufficient memory", the main thread froze, and the client closed.
- **The trigger:** that launch was the first to write the account's Adaptive degrade, scenery draw distance and Sync to display refresh into AC's settings.

With Adaptive degrade off, AC loads every object in range at full detail. With a far draw distance in a busy town, that is more than a 32-bit client can hold.

## The safeguard

- **Settings > Graphics warns** when Adaptive degrade is off with a scenery or landscape draw distance of High or more. It uses this account's value, else AC's own.
- **The engine falls back on its own:** within 10 minutes of the client starting, when free address space drops under 200 MB or the largest free block under 128 MB, it checks Adaptive degrade. If AC runs with it off, the engine turns it on in this account's profile. The save is written at once, because the client may be about to die.
- **The next launch applies it:** the launcher writes the profile into UserPreferences.ini. The engine log and the Graphics panel say what was done. It happens at most once a session, and only when degrade is off, so a slow leak later in a session changes nothing.

## Tests

- `tools/AcGraphicsPrefsTests`: the risk rule (degrade off or on, near or far distances, AC's defaults). 62 checks pass.
