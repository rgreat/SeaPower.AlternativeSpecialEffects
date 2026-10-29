# Alternative Special Effects - notes for continuing the work (for Claude)

Repo: https://github.com/rgreat/SeaPower.AlternativeSpecialEffects. Same pattern as the other rgreat mods (BepInEx 5 + Harmony, started by Anchor Chain,
no compile-time dependency on the game's assemblies except Noesis/UnityEngine; game types are looked up by name, a missing name = warning + feature off).

## Build in the sandbox
```
dotnet build -c Release -p:ManagedDir=/home/claude/seapower.gamefiles/Managed -p:BepInExCore=/home/claude/game/bepinex/BepInEx/core -p:AnchorChainDll=/home/claude/game/ac/AnchorChain.dll
```
`nuget.config` has an empty source list (no network). Decompiled game code: `/home/claude/game/decomp`. Check the decompilation before guessing a member.

## User preferences
Replies in Russian, short; no routine boilerplate, no "not tested" disclaimers; no commit/push/release without an explicit command; project source files change only
on the user's command; mod docs in English; one .cfg switch per feature; events/hooks instead of polling; warn before anything that breaks 0.8.6/0.8.7;
sizes in screen pixels; labels must fit their columns; the mod zip keeps the structure (Release/, src/, csproj, docs at the root).

## Origin of the options (names and sections are the same as in the old mods - the migration matches by [Section] Key)
| Option | From |
|---|---|
| Effects: MuzzleFlashLight, HitFlashLight, MissileLaunchFlashLight, FlashPower, FlashLimit, DayBrightness, FireFlicker, FireLightPower | Alternative Weapons Control (AWC) Flash.cs |
| Optics: BinocularsNormalView, NormalViewDayOnly | AWC Optics.cs |
| Guns: MachineGunBurstFix, MachineGunSoundLoopFix, HitSoundFix | AWC GunFixes.cs (only these; AimBeyondMaxRange, MaxAccuracyMeters, fire control stay in AWC) |
| Effects: ExtraSmokeEmitters, SmokeBySeverity, SmokeStartOpacity, SmokeEndOpacity, SmokeModerateFrom, SmokeSevereFrom | Alternative Damage Model (ADM) FireSmoke.cs |
| Effects: BubblesStopMinutes | ADM BubblesOff (rewritten) |
| UI: HideUnobservedTargets3D | ADM |
| Data: ammunition `[Particles]` of 47 calibres; life raft lights | RgreatCumulativePack |

Not moved (user decision): HidePipScene, ShipModelView, ShowHullCollider, ShowDeckCorners, FireShowsZones, WreckDragFactor, KeepCentreOfMass.
The old mods still contain these options until the user says to remove them (publish this mod first). Both mods active = the same effect twice (flashes,
smoke points): remove from the old ones when this mod is released.

## Source map
- `AsePlugin.cs` plugin: cfg binds, `Entries` (what the window loads/saves), Mods-menu tick (`ModState`), cfg file watcher, `WireModules`/`ApplySettings`, `TryPatch`.
- `AnchorChainEntry.cs` entry point + `ModState` (reads the tick from usersettings.ini [LoadOrder]).
- `Menus.cs` buttons: `MainMenuView` ctor postfix inserts a `MenuButton` under Mod Manager (index n-4 of the StackPanel in `scrollViewer`); `PauseMenuViewModel.Initialise(list)` prefix adds a
  `PauseMenuButton` after ("PauseMenu","Options") - made with that existing key (no missing-text error) and the mod's text set through the protected setter; the mod's `LanguageChanged`
  handler is re-subscribed after each button so it runs after the game's own and keeps the text. The window anchor is the static `Instance` of `MainMenuView` / `PauseMenuView`.
- `SettingsWindow.cs` table-driven window (copy of AWC's; `Add(tab, group, key, label, unit, kind, step, min, max, gameDefault)`), `Loc.cs` texts (keys `set.* tabs.* grp.* lbl.* unit.* help.*`, `menu.button`), `Release/lang/*.ini` (AWC translations reused by key; the new keys are English except ru labels).
- `Migrate.cs` first start only (no cfg file yet): reads `user/AlternativeWeaponsControl/AlternativeWeaponsControl.cfg` and `user/AdvancedDamageControl/AlternativeDamageModel.cfg` and sets the same [Section]/Key values.
- `Flash.cs` flashes, fire light, fire flicker; `Optics.cs`; `EffectsGuns.cs` gun effect hooks; `FireSmoke.cs`; `HideUnseen.cs`; `BubblesOff.cs`; `R.cs` reflection helpers.

## Save / load rules (checked against the decompiled code)
- No timer outside objects of the scene; game-time deadlines (`GameTime.missionElapsedTime`, not `Time.time`: time compression caps `Time.timeScale`).
- Bubbles: the save keeps only `IsSinking`, not `_sinkTime`, and the load does not call `Sink()` or respawn the bubbles. So `BubblesOff` patches `Compartments.Sink` (postfix)
  and adds a `BubblesTimer` component to the ship's transform where the bubbles are children; it stops them after `BubblesStopMinutes` of game time and destroys itself. A ship loaded as sinking has no bubbles
  from the game, nothing to stop. The timer dies with the scene.
- No cached scene objects in statics: the sky singleton is looked up per call (guarded by `InstanceExists(false)`); the donor AudioSources and clip loudness table are bound to the `EffectsManager` instance and collected again when it is gone.
- Smoke: `FireSmoke.Tick` runs once a second per ship from the game's own `Compartments.OnFixedUpdate` prefix; per-ship state is in a `ConditionalWeakTable`.

## Data layer (`Release/ammunition.fix`, `vessels.fix`, `vessels`)
`#!extend` files (IniHandler): every extend file of a target is read before the target file; for a key the FIRST value read wins (`if (!currentSection.ContainsKey(key)) Add`),
an empty `Key=` is a value and wins too. Several mods may extend one target: their keys merge; a key set by two of them goes to the one earlier in the directory list.
The 47 `ase_<calibre>.fix.ini` files hold only `[Particles]` (taken from the pack's `ammunition.fix`, whose other sections - Physics, WarheadData - stay in the pack). Life raft: three `ase_all_life_raft*.patch.ini`,
the `MeshLights` materials and `red.png`. File names carry the `ase_` prefix so they never share a path with the pack's files.

## Open items
- Remove the moved options from AWC, ADM and the pack (only on the user's command, after this mod is published): the `[Particles]` sections from the pack's 47 files, its life raft files.
- Check in the game: both menu buttons, window over the main menu and over the pause menu, language switch with the pause menu open, the save/load check from PUBLISHING.md.
- thumbnail.jpg is AWC's, replace it. Help texts of the ADM-origin options are English in all languages (ADM keeps its texts by hash).
