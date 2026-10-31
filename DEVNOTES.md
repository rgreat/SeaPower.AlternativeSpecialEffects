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
| Gun hit effects (47 ammunition files), life raft lights | RgreatCumulativePack |

Not moved (user decision): HidePipScene, ShipModelView, ShowHullCollider, ShowDeckCorners, FireShowsZones, WreckDragFactor, KeepCentreOfMass.
The old mods still contain these options until the user says to remove them (publish this mod first). Both mods active = the same effect twice (flashes,
smoke points): remove from the old ones when this mod is released.

## Source map
- `AsePlugin.cs` plugin: cfg binds, `Entries` (what the window loads/saves), Mods-menu tick (`ModState`), cfg file watcher, `WireModules`/`ApplySettings`, `TryPatch`.
- `AnchorChainEntry.cs` entry point + `ModState` (reads the tick from usersettings.ini [LoadOrder]).
- `SettingsHub.cs` the rgreat settings hub (protocol v1), THE SAME FILE in every rgreat mod; canonical copy here. One menu entry (main menu under Mod Manager, pause menu under Options) for the settings of all installed and ticked rgreat mods.
  The mods register in a registry in `AppDomain` data (BCL types only, shared between dlls): `Register(id, title, version, order, open, active)`; `Install(harmony, gameAssembly, trace, language)` hooks `MainMenuView` ctor and `PauseMenuViewModel.Initialise(list)`.
  The first mod's hook puts the button in (main menu: a Button named `rgreatSettings`; pause: the list noted in AppDomain data `rgreat.settings.v1.pause`), the others find it. One active mod: its own title, opens its window. Several: "Alternative Mods" opens a list (Popup).
  Order: 10 Game Interface, 20 Weapons Control, 30 Damage Model, 40 Special Effects. Do not change the protocol or the logic in one copy only: edit here, copy to the others.
- `SettingsWindow.cs` table-driven window (copy of AWC's; `Add(tab, group, key, label, unit, kind, step, min, max, gameDefault)`), `Loc.cs` texts (keys `set.* tabs.* grp.* lbl.* unit.* help.*`, `menu.button`), `Release/lang/*.ini` (AWC translations reused by key; the new keys are English except ru labels).
- `Migrate.cs` first start only (no cfg file yet): reads `user/AlternativeWeaponsControl/AlternativeWeaponsControl.cfg` and `user/AdvancedDamageControl/AlternativeDamageModel.cfg` (old folder name) / `user/AlternativeDamageControl/AlternativeDamageModel.cfg` (new name, wins) and sets the same [Section]/Key values.
- `Flash.cs` flashes, fire light, fire flicker; `Optics.cs`; `EffectsGuns.cs` gun effect hooks; `FireSmoke.cs`; `HideUnseen.cs`; `BubblesOff.cs`; `R.cs` reflection helpers.
- `SettingsHub.Install` does not patch the menu classes at plugin start (their static constructors register Noesis properties too early: native crash of the Release build); it waits for the end of the load screen (`LoadScreen.ExecuteLoadScreen` iterator finished) and then patches, adding the button to an already built main menu.
- Smoke colour over the sky (brown turns grey): tried switching `_AdditivePass` of `fx_firesmoke_*` (shader `Particles/Particle_Multi_Pass`, props `_ColorLit/_ColorUnlit/_minBright/_FogFadeFactor`) - no effect, removed. OPEN: smoke and cloud rendering also changes a lot with the camera distance to the ship and sometimes with the camera tilt (user screenshots); to be investigated (shader lighting / fog by distance?). The debug dump still lists each smoke material's properties.
- `FireSmoke.Scatter` (postfix of `CreateFireEffect`, only when the ship already has other fire effects): `randomSeed` + `useAutoRandomSeed`, the random start moment (`Simulate(random 0..start lifetime)` + `Play()`) is done at the next `Tick`, after `Density` has set the smoke's opacity by the fire's strength, so the pre-run particles follow the strength too. The settings window keeps the scroll offset across Apply (`_keepScroll`, put back for a few frames while the rebuilt window lays out).
- `FireSmoke.Sort` (once a second from `Tick`, `SmokeSortFix`): Unity sorts particle systems by the distance to the renderer bounds' centre; a tall plume's centre is high up. `sortingFudge` (a distance bias, lower = in front) = the game's own + (distance camera-fire point - distance camera-bounds centre). Per-particle sorting across systems is impossible (separate renderers, two materials per effect). Lag of up to a second after camera moves.
- `SmokeDensityFactor`: done in `Prolong` (state per effect from the game's base values): `emission.rateOverTimeMultiplier` x factor on every system of the effect (flames included; bursts are not touched), `maxParticles` raised to fit; applies to burning ships at once.
- `FireSmoke.Prolong` (postfix of `CreateFireEffect`, once per effect instance, new effects only): the fire system's sprites carry fire and smoke in one animation (fire gone after `SmokeFireShare`, default 50% of it). Its first half keeps the game's speed, the second runs `SmokeLifetimeFactor` times slower: `startLifetimeMultiplier` and `maxParticles` x (0.5 + 0.5k), and only the texture sheet animation's frame curve is re-timed with a piecewise-linear warp (movement, size, colour are NOT touched: re-timing velocity looked slower in game) (curves resampled, gradient keys moved + a key at the split). Smoke-only systems (material name has "smoke") are stretched whole. Debug trace lists what was re-timed. `Prolong` also runs once a second from `Tick` for every fire effect of the ship (state per effect, computed from the game's base values), so a changed setting reaches burning ships at once. The split is a setting to tune in game.
- `HideUnseen.cs`: the source of "seen" is the game's visual sensors: postfix of `SensorSystemVisual.runVisualScan` (own side only) stores the sensor's `_detectedObjects` (rebuilt in one go per scan, kept until the next); an object is seen when any list holds it. `Vehicle.IsObservedByVisual`/`DetectingSensors` are NOT used (reset at the start of every `UpdateFromECS`, Visual bit absent for a moment once per game second - blinked). Contacts become known via postfix of `Vehicle.UpdateFromECS` (record with `ReportingSide` = player). The camera is not sent to a hidden contact (prefix of `RenderPosition.switchToObject`) and lets go of one that becomes hidden (`deselectObjectAndDetachCamera`); the blocked/released target is remembered (`_pending`, cleared by any other switch) and the camera is sent back with `switchToObject` as soon as it is shown again. Wake effects are not cut off but stopped at the source so what exists fades by itself (all parented under the unit's "Particles" child by the game loaders): particle systems (bow wave, prop wash, cavitation, funnel smoke) emission off; `Ceto.AddFoamTrail.duration = 0` (no new foam, old foam ages out); `Ceto.AddWaveOverlay` (Kelvin patch, single overlay) disabled. Restored on show. The towed/dipping sonar cable (a line renderer of its own object, not part of the unit model) and the towed body are taken from `ObjectBase._obp._towedSystems` and hidden with it. Hides with `Renderer.forceRenderingOff`; no timers. Life raft lights blink: `_Interval=2` in `vessels/all_life_rafts/red_light*.ini` (value is a guess, tune in-game).

## Save / load rules (checked against the decompiled code)
- No timer outside objects of the scene; game-time deadlines (`GameTime.missionElapsedTime`, not `Time.time`: time compression caps `Time.timeScale`).
- Bubbles: the save keeps only `IsSinking`, not `_sinkTime`, and the load does not call `Sink()` or respawn the bubbles. So `BubblesOff` patches `Compartments.Sink` (postfix)
  and adds a `BubblesTimer` component to the ship's transform where the bubbles are children; it stops them after `BubblesStopMinutes` of game time and destroys itself. A ship loaded as sinking has no bubbles
  from the game, nothing to stop. The timer dies with the scene.
- No cached scene objects in statics: the sky singleton is looked up per call (guarded by `InstanceExists(false)`); the donor AudioSources and clip loudness table are bound to the `EffectsManager` instance and collected again when it is gone.
- Smoke: `FireSmoke.Tick` runs once a second per ship from the game's own `Compartments.OnFixedUpdate` prefix; per-ship state is in a `ConditionalWeakTable`.

## Data and switches
Gun hit effects (`HitEffects.cs`, `Release/hit_effects.txt`, [Effects] GunHitEffects): the `[Particles]` values of 47 ammunition files (from the pack) are NOT game data: the file is `.txt`, read by the mod.
A postfix on the `AmmunitionParameters(string, int, WeaponSystem)` constructor notes each object (weak reference, state in a `ConditionalWeakTable`), keeps in memory the game's own values of its five hit `Effect` objects
(HitDefault/Air/Ship/Ground Explosion, HitWaterSplash; taken after the game loaded the ini, so other mods' layers are in them) and, with the switch on, writes the file's values into those Effect objects in place
(`_singleEffect` prefab via `ResourceLoader.getGameObjectResource`, `_effectClassName`, `_isAvailable`, `_isIgnored`; same meaning as the ini keys). A change of the switch / the Mods tick (`ApplySettings` -> `HitEffects.Sync`)
applies or reverts every noted object at once. A loaded game builds new AmmunitionParameters: the constructor hook handles them.
Life raft lights (`RaftLights.cs`, [Effects] LifeRaftLights): the three `ase_all_life_raft*.patch.ini` in `vessels.fix` + `vessels/all_life_rafts/` are an ordinary `#!extend` layer of the mod (always loaded while the mod is ticked).
The switch shows / hides the parts drawn with the shader `Nils/Effects/MeshLights` on the rafts (`ObjectsManager._listOfAllRafts`, and a postfix of `AddObjectToItsTaskforce` for rafts that appear, also after a load).
The extend rules of the game, for reference (IniHandler): extend files of a target are read before the target, the FIRST value of a key wins, an empty `Key=` is a value.

## Open items
- Remove the moved options from AWC, ADM and the pack (only on the user's command, after this mod is published): the `[Particles]` sections from the pack's 47 files, its life raft files.
- Check in the game: both menu buttons, window over the main menu and over the pause menu, language switch with the pause menu open, the save/load check from PUBLISHING.md.
- Help texts of the ADM-origin options are English in all languages (ADM keeps its texts by hash).

## Planned
- Fire and smoke emitted by one particle system along the whole ship (positions along the hull, by section strength) instead of from the fixed points: per-particle depth sorting then works for the whole plume. `SmokeSortFix` is the stopgap until then.
