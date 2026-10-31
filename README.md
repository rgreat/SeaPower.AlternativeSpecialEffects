# Alternative Special Effects

A Sea Power mod (BepInEx + Anchor Chain) that collects the special effects of the 3D picture in one place and makes them tunable.

## Features

**Lights**
- Muzzle flash of guns, CIWS and machine guns; flash where a shell hits; light that follows a missile booster after launch.
- Fire light on ships: brighter and flickering by the strength of the fire. Flashes and fire light are dimmed by day.

**Fires and sinking**
- Fire and smoke points by ship length (1 / 2 / 3 / 5 instead of the game's three), each showing its own section's fire.
- The look of a fire by its strength (small / moderate / severe thresholds and smoke opacity are settings).
- Neighbouring fires of one ship animate differently (random seed and start moment).
- Columns of smoke of one ship overlap in the right order.
- Density of fire and smoke: a multiplier of the emitted particles.
- Ship fire smoke lasts longer before it thins out (the smoke part of the animation is 10x slower by default, a setting; the flames keep the game's speed).
- The sinking-bubbles effect of a wreck stops after N minutes of game time (also after a saved game is loaded).

**Guns and view**
- Machine gun burst and sound-loop fixes, gun hit sound fix.
- Binoculars at maximum zoom start with the normal view.
- Targets of other sides that are not seen visually are not drawn in 3D, with their wakes (fading out by themselves) and sonar cables. The camera does not centre on or follow them and returns to the selected one when it becomes visible (off by default). Submarines are included only with a separate option (also off).
- More visible gun hit effects for calibres from 37 mm up, and blinking red lights on life rafts (each has its own switch and works at once).

## Settings

A settings button in the main menu (under Mod Manager) and in the pause menu (Esc, under Options) opens the settings window. With several of the author's mods installed there is one **Alternative Mods** button that lists them.
The file is `Sea Power_Data/StreamingAssets/user/AlternativeSpecialEffects/AlternativeSpecialEffects.cfg`. On the first start the values of the same options are taken from the old
Alternative Weapons Control and Alternative Damage Model settings.

## Install

Subscribe in the Steam Workshop (needs Anchor Chain) or copy the contents of `Release` to
`Sea Power_Data/StreamingAssets/AlternativeSpecialEffects/` and tick the mod in the Mods menu.
Supports Sea Power version 0.8.7 (Release and Stable Beta).

Source: https://github.com/rgreat/SeaPower.AlternativeSpecialEffects
