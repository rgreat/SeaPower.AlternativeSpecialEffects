# Alternative Special Effects

A Sea Power mod (BepInEx + Anchor Chain or R.A.T.S. Keel) that collects the special effects of the 3D picture in one place and makes them tunable.

## Features

**Lights**
- Muzzle flash of guns, CIWS and machine guns; flash where a shell hits; light that follows a missile booster after launch.
- Fire light on ships: brighter and flickering by the strength of the fire. Flashes and fire light are dimmed by day.

**Fires and sinking**
- Fire and smoke points by ship length (1 / 2 / 3 / 5 instead of the game's three), each showing its own section's fire.
- The look of a fire by its strength (small / moderate / severe thresholds and smoke opacity are settings).
- Funnel smoke of ships by engine type and load (Diesel, Steam, GasTurbine, Nuclear, Combined groups; each has its own editable density, lightness (black to white), thickness and duration from idle to full power; any single ship (Ships tab) or country (Countries tab) can get its own multipliers; `funnel_groups.txt` forces a ship into another group). A change of the engine mode affects only the new particles.
- Columns of smoke of one ship overlap in the right order, and smoke and clouds overlap in the right order too; the smoke does not change its colour sharply at the horizon.
- Neighbouring fires of one ship animate differently (each has its own random seed).
- Density of fire and smoke: a multiplier of the emitted particles.
- Ship fire smoke lasts longer before it thins out (the smoke part of the animation is 5x slower by default, a setting; the flames keep the game's speed; late sprites grow and fade).
- The sinking-bubbles effect of a wreck stops after N minutes of game time (also after a saved game is loaded).

**Guns and view**
- Machine gun burst and sound-loop fixes, gun hit sound fix.
- Targets of other sides that are not seen visually are not drawn in 3D, with their wakes (fading out by themselves) and sonar cables. The camera does not centre on or follow them and returns to the selected one when it becomes visible (off by default). Submarines are included only with a separate option (also off).
- More visible gun hit effects (10 classes by shell size, with their own effects and sizes, set in the window). Shells in flight get a shape, colour and size by the same classes (so big guns look different from small ones). Blinking red lights on life rafts (each switch works at once).

## Settings

A settings button in the main menu (under Mod Manager) and in the pause menu (Esc, under Options) opens the settings window. With several of the author's mods installed there is one **Alternative Mods** button; it opens the window of the mod used last, and the row of the mods at the top of the window switches between them. The language is one drop-down for all of them.
The file is `Sea Power_Data/StreamingAssets/user/AlternativeSpecialEffects/AlternativeSpecialEffects.cfg`. On the first start the values of the same options are taken from the old
Alternative Weapons Control and Alternative Damage Model settings.

## Compatibility

- Supports Sea Power 0.9.2 (0.9.0 and later).
- Loaders: Anchor Chain or R.A.T.S. Keel (temporarily unavailable).
- Can be switched on/off in the game's Mods menu.

## Install

1. Subscribe to [Anchor Chain](https://steamcommunity.com/workshop/filedetails/?id=3380210757) or [RATS Keel](https://steamcommunity.com/workshop/filedetails/?id=3835527459) (temporarily unavailable) and follow instructions on mods page.
2. Subscribe to this mod (or copy the contents of `Release` to `Sea Power_Data/StreamingAssets/AlternativeSpecialEffects/`).
3. Start **Sea Power**.
4. Enable the mod in the game's **Mod Manager** and press the **Apply** button.

Source: https://github.com/rgreat/SeaPower.AlternativeSpecialEffects
