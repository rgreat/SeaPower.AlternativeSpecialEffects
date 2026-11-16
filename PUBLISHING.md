# Alternative Special Effects: build, check, publish

## Layout
```
AlternativeSpecialEffects.csproj   build; after Build the dll is copied to Release
nuget.config                       empty NuGet source list (offline build)
src/                               plugin sources (see DEVNOTES.md)
Release/                           THE MOD: upload the contents of this folder
  _info.ini  AlternativeSpecialEffects.dll  thumbnail.jpg  lang/*.ini
  vessels.fix/  vessels/                life raft lights (#!extend patches), see DEVNOTES.md
```
In StreamingAssets the mod folder must be named `AlternativeSpecialEffects`: the game keeps the mod's tick in `usersettings.ini` by the folder name and the mod reads it.
Do not put AnchorChain, BepInEx or 0Harmony dlls into the mod folder.

## Build
```
dotnet build -c Release -p:ManagedDir=<Sea Power_Data>/Managed -p:BepInExCore=<BepInEx>/core -p:AnchorChainDll=<AnchorChain.dll>
```
(or `-p:GameDir="D:\Steam\steamapps\common\Sea Power"`, as in the other mods).

## Check in the game
1. Copy `Release` to `StreamingAssets/AlternativeSpecialEffects/`, tick the mod, Apply, restart the game.
2. `BepInEx/LogOutput.log`: `Alternative Special Effects 1.5.0 ... loaded`, `Enabled in the game's Mods menu`, no `not found` warnings.
3. A **Special Effects** button is in the main menu and in the pause menu of a mission; it opens the settings window.
4. Sink a ship, save, load: with BubblesStopMinutes > 0 the bubbles of a ship that sank before the save still stop (new ones are not made by the load).
5. Untick the mod and enter a mission: `Disabled in the game's Mods menu`, nothing changed.
6. Debug in the settings window writes `BepInEx/AlternativeSpecialEffects.trace.txt`.

## Publish
Use the game's Workshop uploader on the contents of `Release`. Short texts (en/ru): `Release/_info.ini`.

## Version
`PluginVersion` in `src/AsePlugin.cs` and `<Version>` in the csproj - change both.
