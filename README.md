# AutoLootMod

# Currently down please do not use at this time


**AutoLootMod** is a quality-of-life mod for Embers Adrift that automatically loots all items and currency from loot windows as soon as they appear. No more clicking "Take All"—just open any loot window and watch your loot instantly transfer to your inventory!

## Features

- Instantly loots all items and currency from loot containers as soon as they appear.
- Works for any loot window (mob drops, chests, etc).
- Only triggers when items are actually present—never misses loot due to timing.
- Ensures loot is only taken once per container, even if items arrive in batches.
- **Loot Roll Aware:** If any item in the loot container is under a pending need/greed (loot roll) state, auto-looting is skipped for that container to prevent interfering with loot distribution.

## How It Works

AutoLootMod hooks into the game’s container system and listens for new loot items being added to loot containers. When a loot window is opened and items arrive, the mod automatically calls the same logic as pressing "Take All", transferring all loot to your inventory immediately—**unless any item is currently pending a loot roll** (Need/Greed), in which case the mod will skip auto-looting for that container.

## Compatibility

- Game: **Embers Adrift**

## Credits

Developed by **MrJambix**.

## Disclaimer

This mod is distributed for educational and accessibility purposes only. Use at your own risk and always respect the game’s Terms of Service and community guidelines.
