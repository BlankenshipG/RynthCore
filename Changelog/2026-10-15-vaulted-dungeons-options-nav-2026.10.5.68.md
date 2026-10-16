# Options > Navigation > Vaulted Dungeons (2026.10.5.68)

Date: 2026-10-15
Previous release: 2026.10.5.67

## What

Adds an Options > Navigation checkbox **Vaulted Dungeons** (under Infinite Leaftide). When on,
RynthAi shows RynthILT's Mini Remote "Vaulted Dungeons" tab on ILT / ACECustom worlds.

Also: ChatData.ChannelForType maps chat type 0x1C (28) to Channels so ILT Trade/General lines
match RynthChat's classifier.

## Files

- UI/Data/SettingsData.cs — EnableVaultedDungeons + Navigation schema row
- UI/Data/ChatData.cs — type 0x1C → Channels
