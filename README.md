# Alternative Special Effects

A Sea Power mod (BepInEx + Anchor Chain) that collects the special effects of the 3D picture in one place and makes them tunable.

## Features

**Lights**
- Muzzle flash of guns, CIWS and machine guns; flash where a shell hits; light that follows a missile booster after launch.
- Fire light on ships: brighter and flickering by the strength of the fire. Flashes and fire light are dimmed by day.

**Fires and sinking**
- Fire and smoke points by ship length (1 / 2 / 3 / 5 instead of the game's three), each showing its own section's fire.
- The look of a fire by its strength (small / moderate / severe thresholds and smoke opacity are settings).
- The sinking-bubbles effect of a wreck stops after N minutes of game time (also after a saved game is loaded).

**Guns and view**
- Machine gun burst and sound-loop fixes, gun hit sound fix.
- Binoculars at maximum zoom start with the normal view.
- Targets of other sides that are not seen visually are not drawn in 3D (off by default).
- More visible gun hit effects for calibres from 37 mm up, and red lights on life rafts (data files).

## Settings

A **Special Effects** button in the main menu (under Mod Manager) and in the pause menu (Esc, under Options) opens the settings window.
The file is `Sea Power_Data/StreamingAssets/user/AlternativeSpecialEffects/AlternativeSpecialEffects.cfg`; changes made to it
while the game runs are picked up. On the first start the values of the same options are taken from the old
Alternative Weapons Control and Alternative Damage Model settings.

## Install

Subscribe in the Steam Workshop (needs Anchor Chain) or copy the contents of `Release` to
`Sea Power_Data/StreamingAssets/AlternativeSpecialEffects/` and tick the mod in the Mods menu.
Compatible with Sea Power 0.8.6 / 0.8.7.

Source: https://github.com/rgreat/SeaPower.AlternativeSpecialEffects
