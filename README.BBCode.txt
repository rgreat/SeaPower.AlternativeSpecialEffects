Version 1.6.0.

Special effects of the 3D picture in one mod with its own settings window: lights, fires, smoke and guns. Takes over the effects that were spread over Alternative Weapons Control, Alternative Damage Model and the rgreat Cumulative Pack.

[h1]Mod require [b]Anchor Chain[/b] or [b]RATS Keel[/b][/h1]

[h1]Features[/h1]
[h3]Lights[/h3]
[olist]
[*][b]Gunfire, hits and missile launches[/b] light up their surroundings in 3D: muzzle flash of guns, CIWS and machine guns, a flash where a shell hits, a light that follows a missile booster after launch. The bigger the weapon, the brighter and longer the flash.
[*][b]Fires on ships flicker[/b], brighter and stronger with the strength of the fire.
[*][b]Day dimming:[/b] by day the lights are dimmed (option, default 0.5) and the flashes alive at once share a total brightness limit, so the picture does not blow out into white and pink. Every effect can be switched off, and brightness is adjustable.
[/olist]

[h3]Fires and smoke[/h3]
[olist]
[*][b]Fire and smoke points by ship length[/b] (1 / 2 / 3 / 5 instead of the game's three), each showing the fire of its own section.
[*][b]The look of a fire by its strength:[/b] thresholds of small / moderate / severe fire and the smoke opacity are settings.
[*][b]Funnel smoke by engine type:[/b] groups Diesel, Steam, GasTurbine, Nuclear and Combined (nuclear + oil-fired boost, smokes only above the full-ahead speed), each with its own density, lightness (black to white), thickness and duration from idle to full power, all editable; a change of the engine mode affects only the new particles. Any single ship or country can get its own multipliers (tabs Ships and Countries); [b]funnel_groups.txt[/b] forces a ship into another group.
[*][b]Smoke order:[/b] columns of smoke of one ship overlap in the right order; smoke and clouds overlap in the right order; the smoke does not change its colour sharply at the horizon.
[*][b]Fire and smoke density:[/b] a multiplier of the number of emitted particles (1 = as in the game).
[*][b]Smoke lasts longer:[/b] the smoke part of the fire animation runs slower, so the smoke thins out later (5x by default, adjustable; the flames keep the game's speed; late sprites grow and fade).
[*][b]Wreck effect timer:[/b] the effect of a sinking wreck stops after N minutes of game time, also after a saved game is loaded.
[/olist]

[h3]Guns[/h3]
[olist]
[*][b]More visible gun hit effects[/b] by shell size: 10 classes, each with its own effects and sizes. Applied and removed at once, no restart. Shells in flight get a shape, colour and size by the same classes.
[*][b]Machine guns:[/b] tracers and the muzzle burst sound last for the whole burst. Gun hit sound fix.
[/olist]

[h3]View[/h3]
[olist]
[*][b]Blinking red lights on life rafts[/b] (own switch, works at once).
[*][b]Unobserved targets:[/b] targets of other sides that are not seen visually are not drawn in 3D, with their wakes (fading out by themselves) and sonar cables. The camera does not centre on or follow them and returns to the selected one when it becomes visible (off by default; submarines are included only with a separate option, also off).
[/olist]

[h3]Settings and languages[/h3]
[olist]
[*]Settings window in the game with tabs: Lights, Fires and Smoke, World, Guns, Funnel Smoke (pages General, Engine types, Ships, Countries), Debug; search by option names. Opens from the main menu (under Mod Manager) and from the pause menu (Esc, under Options). With several of the author's mods installed there is one [b]Alternative Mods[/b] button; it opens the window of the mod used last, and the row of the mods at the top of the window switches between them. The language is one drop-down for all of them.
[*]On the first start the values of the same options are taken from the old Alternative Weapons Control and Alternative Damage Model settings.
[*]Localized into 9 languages (en, ru, cn, de, es, fr, ko, ja, vn). The [b]lang/<code>.ini[/b] files can be edited to translate or change texts.
[/olist]

[h1]Compatibility[/h1]
[list]
[*]Supports Sea Power 0.9.2 (0.9.0 and later).
[*]Loaders: Anchor Chain or RATS Keel (temporarily unavailable).
[*]Can be switched on/off in the game's Mods menu.
[*]If you use Alternative Weapons Control, Alternative Damage Model or the rgreat Cumulative Pack, use their latest versions: the moved options are removed from them.
[/list]

[h1]Install[/h1]
[olist]
[*]Subscribe to [url=https://steamcommunity.com/workshop/filedetails/?id=3380210757]Anchor Chain[/url] or [url=https://steamcommunity.com/workshop/filedetails/?id=3835527459]RATS Keel[/url] (temporarily unavailable) and follow instructions on mods page.
[*]Subscribe to this mod.
[*]Start [b]Sea Power[/b].
[*]Enable the mod in the game's [b]Mod Manager[/b] and press the [b]Apply[/b] button.
[/olist]

[h1]Usage[/h1]
[list]
[*][b]Settings in the game:[/b] the Special Effects (or Alternative Mods) button in the main menu and in the pause menu.
[*]The settings file is [b]user/AlternativeSpecialEffects/AlternativeSpecialEffects.cfg[/b].
[/list]
