using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// Alternative Special Effects - Sea Power mod: everything that changes how the 3D picture looks and sounds.
    ///  - Lights: muzzle / missile booster / shell hit flashes, brighter and flickering fire light, day dimming.
    ///  - Fires: smoke and fire points by ship length, the look of a fire by its strength.
    ///  - Sinking: the bubbles of a wreck stop after a time (game time, also after a saved game is loaded).
    ///  - Guns: machine gun bursts and sound loop, the hit sound.
    ///  - Binoculars: normal view at the maximum zoom. 3D view: targets that are not seen are not drawn.
    /// Settings: a button in the main menu and in the pause menu (Esc in a mission) opens the settings window.
    /// Follows its tick in the game's Mods menu (Anchor Chain starts every mod folder): while it is off nothing is changed.
    /// No compile-time dependency on the game's assemblies: types and members are looked up by name.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public partial class AsePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.rgreat.seapower.alternativespecialeffects";
        public const string PluginName = "Alternative Special Effects";
        public const string PluginVersion = "1.1.0";

        public static readonly string BuildId = ReadBuildId();

        static AsePlugin _inst;
        public static AsePlugin Instance { get { return _inst; } }
        static volatile bool Enabled;                   // ticked in the game's Mods menu (always true for a BepInEx/plugins copy)
        internal static AsePlugin Active { get { return Enabled ? _inst : null; } }
        static bool _debug;
        static string _traceFile;
        bool _patchTried;
        static Type _tObjectBase;                       // SeaPower.ObjectBase: its assembly is the game's

        // ------------------------------------------------------------ settings
        public ConfigFile Cfg;
        internal ConfigEntry<string> _language;
        internal ConfigEntry<bool> _debugEntry, _flashLimit, _flashMissile, _fireFlicker, _optNormal, _optDayOnly, _flashMuzzle, _flashHit,
                                   _mgBurstFix, _mgSoundLoop, _hitSoundFix, _smoke, _density, _smokeSort, _hide3d, _hideSubs, _gunHits, _raftLights;
        internal ConfigEntry<float> _flashPower, _firePower, _dayBright, _densityMin, _densityEnd, _smokeMod, _smokeSev, _smokeLife, _smokeShare, _smokeDens, _bubbles;
        public static string UserDir { get; private set; }
        internal bool CfgIsNew;                         // the settings file did not exist: the old mods' values are taken over

        /// <summary>The settings the window shows (in this order).</summary>
        internal readonly List<ConfigEntryBase> Entries = new List<ConfigEntryBase>();

        static string ReadBuildId()
        {
            try
            {
                foreach (AssemblyMetadataAttribute a in typeof(AsePlugin).Assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
                    if (a.Key == "Build") return a.Value;
            }
            catch { }
            return "unknown";
        }

        static void Trace(string msg)
        {
            if (!_debug) return;
            try { File.AppendAllText(_traceFile, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine); }
            catch { }
        }

        internal static void Say(string msg) { if (_inst != null) _inst.Logger.LogInfo(msg); }
        static void Warn(string msg) { if (_inst != null) _inst.Logger.LogWarning(msg); Trace("WARN " + msg); }

        static HarmonyMethod Hm(string name)
        {
            return new HarmonyMethod(typeof(AsePlugin).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));
        }

        internal static Assembly GameAssembly()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "Seapower-Scripts") return a;
            return null;
        }

        /// <summary>The settings file: Sea Power_Data/StreamingAssets/user/AlternativeSpecialEffects/AlternativeSpecialEffects.cfg (BepInEx/config when that folder cannot be used).</summary>
        void OpenSettingsFile()
        {
            try
            {
                UserDir = Path.Combine(Path.Combine(UnityEngine.Application.streamingAssetsPath, "user"), "AlternativeSpecialEffects");
                Directory.CreateDirectory(UserDir);
                string path = Path.Combine(UserDir, "AlternativeSpecialEffects.cfg");
                CfgIsNew = !File.Exists(path);
                Cfg = new ConfigFile(path, true);
            }
            catch (Exception e)
            {
                Logger.LogWarning("Settings in StreamingAssets/user not available (" + e.Message + "); BepInEx/config is used.");
                CfgIsNew = !File.Exists(Config.ConfigFilePath);
                Cfg = Config;
                UserDir = Paths.ConfigPath;
            }
        }

        void Awake()
        {
            _inst = this;
            OpenSettingsFile();

            _fireFlicker = Cfg.Bind("Effects", "FireFlicker", true, "The light of a fire on a ship flickers, deeper and faster the stronger the fire (small / moderate / severe).");
            _firePower = Cfg.Bind("Effects", "FireLightPower", 3f, "Brightness of the fire light on ships (the game's own fire light multiplied; its range grows too). 1 = the game's.");
            _flashMuzzle = Cfg.Bind("Effects", "MuzzleFlashLight", true, "A short light flash at the muzzle when guns, CIWS and machine guns fire (visible at night).");
            _flashMissile = Cfg.Bind("Effects", "MissileLaunchFlashLight", true, "A light follows a missile for as long as its booster burns after launch (the booster burn time of the missile).");
            _flashHit = Cfg.Bind("Effects", "HitFlashLight", true, "A short light flash where a gun shell hits (not in the water).");
            _dayBright = Cfg.Bind("Effects", "DayBrightness", 0.5f, "Brightness of the flashes and of the fire light by day, 1 = no dimming. In full sun the lights are multiplied by this (at night they are not changed): the sun and a bright light add up and blow out into white and pink.");
            _flashPower = Cfg.Bind("Effects", "FlashPower", 1f, "Brightness and size of the flashes, 1 = default.");
            _flashLimit = Cfg.Bind("Effects", "FlashLimit", true, "All the flashes alive at once share a total brightness limit, so many hits together do not over-expose the picture.");
            _smoke = Cfg.Bind("Effects", "ExtraSmokeEmitters", true,
                "Number of fire and smoke points by ship length instead of the game's three: under 25 m 1, under 50 m 2, under 75 m 3, from 75 m 5 " +
                "(one per section, each showing its own section's fire). Ships spawned from now on.");
            _density = Cfg.Bind("Effects", "SmokeBySeverity", true,
                "Fire looks by the fire's strength (share of its maximum) instead of the game's stages: small, moderate from " +
                "SmokeModerateFrom, severe from SmokeSevereFrom (the game: 25% / 50%); the small fire's smoke grows from " +
                "SmokeStartOpacity to SmokeEndOpacity. Only the look - damage and spreading stay the game's. Off = as in the game.");
            _densityMin = Cfg.Bind("Effects", "SmokeStartOpacity", 10f, "Small fire: smoke opacity of new particles at 0% of the fire's maximum, % of the game's.");
            _densityEnd = Cfg.Bind("Effects", "SmokeEndOpacity", 100f, "Small fire: smoke opacity of new particles at SmokeModerateFrom, % of the game's; between - evenly by the fire's strength.");
            _smokeMod = Cfg.Bind("Effects", "SmokeModerateFrom", 50f, "The moderate fire's look from this strength, % of the fire's maximum (the game: 25%).");
            _smokeLife = Cfg.Bind("Effects", "SmokeLifetimeFactor", 10f, "Ship fire smoke lasts this many times longer before it thins out (1 = as in the game).");
            _smokeShare = Cfg.Bind("Effects", "SmokeFireShare", 50f, "Ship fire sprite animation: the first part, % of its length, has the flames in it and keeps the game's speed; only the rest (smoke) is slowed by SmokeLifetimeFactor.");
            _smokeSort = Cfg.Bind("Effects", "SmokeSortFix", true, "Columns of smoke of one ship overlap in the right order: each smoke system is sorted by the distance to its fire point, not to its bounds' centre (a tall plume's centre is high up). Off = as in the game.");
            _smokeDens = Cfg.Bind("Effects", "SmokeDensityFactor", 2f, "Density of the ship fire and smoke: the number of particles they emit is multiplied by this (1 = as in the game; default 2). Nothing else changes.");
            _smokeSev = Cfg.Bind("Effects", "SmokeSevereFrom", 90f, "The severe fire's look from this strength, % of the fire's maximum (the game: 50%).");
            _gunHits = Cfg.Bind("Effects", "GunHitEffects", true,
                "More visible and spectacular hits of gun shells (calibres from 37 mm up): the hit effects of 47 gun ammunition files are replaced (hit_effects.txt of the mod, from the rgreat cumulative pack). " +
                "Off = the game's own hit effects. Takes effect at once; a shell already in the air keeps the effect it had.");
            _raftLights = Cfg.Bind("Effects", "LifeRaftLights", true,
                "A blinking red light on life rafts (visible at night). Off = rafts without lights. Takes effect at once.");
            _bubbles = Cfg.Bind("Effects", "BubblesStopMinutes", 10f,
                "Minutes (game time, so time compression counts) after a ship starts to sink, when its sinking-bubbles effect stops emitting " +
                "(the game never stops it while the wreck exists). Saves resources in long missions. 0 = never, as in the game.");
            _hide3d = Cfg.Bind("UI", "HideUnobservedTargets3D", false,
                "Do not draw in the 3D view the targets of other sides that are not observed visually (known by radar, sonar or data only). Your own units are always drawn.");
            _hideSubs = Cfg.Bind("UI", "HideUnobservedSubmarines3D", false,
                "With HideUnobservedTargets3D on, submarines are hidden too. Off: submarines are always drawn (they are almost never seen visually). Only counts when HideUnobservedTargets3D is on.");
            _optNormal = Cfg.Bind("Optics", "BinocularsNormalView", true, "Binoculars at the maximum magnification start with the normal view (the game starts them with its night / thermal filter on). The game's filter key switches the filter on.");
            _optDayOnly = Cfg.Bind("Optics", "NormalViewDayOnly", true, "The normal view is the default only by day; at night the game's own default filter stays.");
            _mgBurstFix = Cfg.Bind("Guns", "MachineGunBurstFix", true, "Machine guns show their muzzle burst on every shot: a pooled burst effect that is still playing is restarted.");
            _mgSoundLoop = Cfg.Bind("Guns", "MachineGunSoundLoopFix", true, "The looped fire sound of machine guns skips the silent part of its clip (a gap in the middle of a long burst).");
            _hitSoundFix = Cfg.Bind("Guns", "HitSoundFix", true, "Gun hits always play their explosion sound: an effect whose sound did not start (pooled instance) is made to play it.");
            _debugEntry = Cfg.Bind("General", "Debug", false, "Verbose logging and BepInEx/AlternativeSpecialEffects.trace.txt.");
            _language = Cfg.Bind("General", "Language", "",
                "Language of the mod's texts: a code of a game language (en, ru ...); empty = the game's own language. The texts are in the files lang/<code>.ini in the mod folder - edit them to translate.");

            // the order of the window's fields is the table in SettingsWindow; this list is what it loads and saves
            foreach (var en in new ConfigEntryBase[] { _flashMuzzle, _flashHit, _flashMissile, _flashPower, _flashLimit, _dayBright, _fireFlicker, _firePower,
                                                        _gunHits, _raftLights, _smoke, _density, _smokeSort, _smokeDens, _densityMin, _densityEnd, _smokeMod, _smokeSev, _smokeLife, _smokeShare, _bubbles, _hide3d, _hideSubs, _optNormal, _optDayOnly,
                                                        _mgBurstFix, _mgSoundLoop, _hitSoundFix, _debugEntry, _language })
                Entries.Add(en);

            Loc.Trace = Trace; Loc.Forced = () => _language == null ? "" : _language.Value;
            foreach (var en in Entries) Loc.Register("help." + en.Definition.Section + "/" + en.Definition.Key, en.Description == null ? "" : en.Description.Description);

            if (CfgIsNew) { try { Migrate.FromOldMods(this); } catch (Exception e) { Logger.LogWarning("Taking over the old mods' settings failed: " + e.Message); } }

            _debug = _debugEntry.Value;
            _traceFile = Path.Combine(Paths.BepInExRootPath, "AlternativeSpecialEffects.trace.txt");
            if (_debug) { try { File.WriteAllText(_traceFile, ""); } catch { } }
            Trace(PluginName + " " + PluginVersion + " build " + BuildId);
            SettingsWindow.Trace = Trace;
            SettingsWindow.Warn = Warn;
            Cfg.SettingChanged += (s, e) => { _debug = _debugEntry.Value; ApplySettings(); };
            WireModules();
            ApplySettings();

            RefreshEnabled();
            TryPatch();
            Logger.LogInfo(PluginName + " " + PluginVersion + " (build " + BuildId + ") loaded" +
                           (ModState.Controlled ? "; follows its tick in the game's Mods menu." : "."));
        }

        void Update()
        {
            if (!_patchTried) TryPatch();
            try { SettingsWindow.Tick(); } catch (Exception e) { Trace("settings tick: " + e.Message); }
        }

        // ------------------------------------------------------------ modules and their settings

        static float Pct(float v, float lo, float hi) { return Math.Max(lo, Math.Min(hi, v)) / 100f; }

        void WireModules()
        {
            HitEffects.Trace = Trace; HitEffects.Want = () => Enabled && _gunHits.Value;
            RaftLights.Trace = Trace; RaftLights.Want = () => Enabled && _raftLights.Value;
            FireSmoke.Trace = Trace; FireSmoke.Verbose = () => _debug;
            FireSmoke.IsEnabled = () => Enabled && _smoke.Value;
            FireSmoke.SortFix = () => Enabled && _smokeSort.Value;
            FireSmoke.DensityFactor = () => Enabled ? Math.Max(0f, Math.Min(100f, _smokeDens.Value)) : 1f;
            FireSmoke.LifeFactor = () => Enabled ? Math.Max(1f, Math.Min(100f, _smokeLife.Value)) : 1f;
            FireSmoke.DensityOn = () => Enabled && _density.Value;
            HideUnseen.Trace = Trace; HideUnseen.IsEnabled = () => Enabled && _hide3d.Value; HideUnseen.HideSubs = () => _hideSubs.Value;
            BubblesOff.Trace = Trace; BubblesOff.IsEnabled = () => Enabled && _bubbles.Value > 0f;
            BubblesOff.Minutes = () => Math.Max(0f, _bubbles.Value);
        }

        /// <summary>Settings (or the Mods tick) changed: the values the modules read as plain numbers, and the effects that must be undone.</summary>
        void ApplySettings()
        {
            HitEffects.Sync(); RaftLights.Sync();
            FireSmoke.StartOpacity = Pct(_densityMin.Value, 0f, 100f);
            FireSmoke.EndOpacity = Pct(_densityEnd.Value, 0f, 100f);
            FireSmoke.ModerateFrom = Pct(_smokeMod.Value, 1f, 100f);
            FireSmoke.FireShare = Pct(_smokeShare.Value, 5f, 95f);
            FireSmoke.SevereFrom = Math.Max(FireSmoke.ModerateFrom, Pct(_smokeSev.Value, 1f, 100f));
            try { if (!(Enabled && _hide3d.Value)) HideUnseen.RestoreAll(); else if (!_hideSubs.Value) HideUnseen.RestoreSubmarines(); } catch (Exception e) { Trace("RestoreAll: " + e.Message); }
        }

        // ------------------------------------------------------------ the Mods menu tick

        static void RefreshEnabled()
        {
            bool? state = ModState.Read();
            if (state == null && ModState.Controlled) Warn("Could not read the game's mod list; " + PluginName + " stays on.");
            bool now = state != false;
            if (now == Enabled) return;
            Enabled = now;
            if (ModState.Controlled)
                Say(now ? "Enabled in the game's Mods menu (" + ModState.ModId + ")." : "Disabled in the game's Mods menu (" + ModState.ModId + "); idle.");
            if (_inst != null) _inst.ApplySettings();
        }

        static WeakReference _loadIter = new WeakReference(null);

        static void LoadScreenPrefix(object __instance)
        {
            if (__instance == null || ReferenceEquals(_loadIter.Target, __instance)) return;
            _loadIter = new WeakReference(__instance);
            try { RefreshEnabled(); } catch (Exception e) { Trace("LoadScreenPrefix: " + e.Message); }
        }

        static void ModsSavedPostfix()
        {
            try { RefreshEnabled(); } catch (Exception e) { Trace("ModsSavedPostfix: " + e.Message); }
        }

        // ------------------------------------------------------------ the game's fire code: smoke points and looks

        static void SmokeInitPostfix(object __instance) { try { FireSmoke.AfterInit(__instance); } catch (Exception e) { Trace("SmokeInit: " + e.Message); } }
        static void SmokeFireStatePostfix(object __instance) { try { FireSmoke.AfterUpdateFireState(__instance); } catch (Exception e) { Trace("SmokeFireState: " + e.Message); } }
        static void SmokeCreatePostfix(object __instance, object fireEffect)
        {
            try { FireSmoke.Prolong(fireEffect); FireSmoke.Scatter(__instance, fireEffect); } catch (Exception e) { Trace("SmokeScatter: " + e.Message); }
            if (_debug) { try { FireSmoke.DumpEffect(fireEffect); } catch { } }
        }
        static IEnumerable<CodeInstruction> SmokeCreateTranspiler(IEnumerable<CodeInstruction> ins) { return FireSmoke.CreateFireEffectTranspiler(ins); }

        /// <summary>Once a second per ship (the game's own fixed update of its compartments): the look of the fire by its strength.</summary>
        static void SmokeTickPrefix(object __instance)
        {
            if (!Enabled) return;
            try { FireSmoke.Tick(__instance); } catch (Exception e) { Trace("SmokeTick: " + e.Message); }
        }

        // ------------------------------------------------------------ patching

        void TryPatch()
        {
            var asm = GameAssembly();
            if (asm == null) return;   // not loaded yet: next frame
            _patchTried = true;
            _tObjectBase = asm.GetType("SeaPower.ObjectBase");
            var harmony = new Harmony(PluginGuid);
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try { HookFireFlicker(harmony, asm); } catch (Exception e) { Warn("Fire flicker failed: " + e.Message); }
            try { HookOptics(harmony, asm); } catch (Exception e) { Warn("Binocular view failed: " + e.Message); }
            try { if (!HitEffects.Init(harmony, asm)) Warn("Gun hit effects: game members not found; the game's own hit effects stay."); } catch (Exception e) { Warn("Gun hit effects failed: " + e.Message); }
            try { if (!RaftLights.Init(harmony, asm)) Warn("Life raft lights: game members not found; the lights cannot be switched."); } catch (Exception e) { Warn("Life raft lights failed: " + e.Message); }
            try { HookEffectsGuns(harmony, asm); } catch (Exception e) { Warn("Gun effects failed: " + e.Message); }
            try { if (!BubblesOff.Init(harmony, asm)) Warn("Sinking bubbles: game members not found; they are not stopped."); } catch (Exception e) { Warn("Sinking bubbles failed: " + e.Message); }
            try { if (!HideUnseen.Init(harmony, asm)) Warn("Hide unobserved targets: game members not found."); } catch (Exception e) { Warn("Hide unobserved targets failed: " + e.Message); }
            try
            {
                var tComp = asm.GetType("SeaPower.Compartments");
                if (tComp != null && FireSmoke.Init(asm))
                {
                    harmony.Patch(tComp.GetMethod("Init", all), postfix: Hm("SmokeInitPostfix"));
                    harmony.Patch(tComp.GetMethod("UpdateFireState", all), postfix: Hm("SmokeFireStatePostfix"));
                    harmony.Patch(tComp.GetMethod("CreateFireEffect", all), postfix: Hm("SmokeCreatePostfix"), transpiler: Hm("SmokeCreateTranspiler"));
                    var fixedUpdate = tComp.GetMethod("OnFixedUpdate", all);
                    if (fixedUpdate != null) harmony.Patch(fixedUpdate, prefix: Hm("SmokeTickPrefix"));
                    else Warn("Compartments.OnFixedUpdate not found: the fire look by strength is not updated.");
                }
                else Warn("Fire smoke: game members not found; the game's three points and stages are used.");
            }
            catch (Exception e) { Warn("Fire smoke hooks failed: " + e.Message); Trace(e.ToString()); }
            try
            {
                Rgreat.Hub.SettingsHub.Register("alternative-special-effects", () => Loc.T("menu.button"), PluginVersion, 40,
                    anchor => SettingsWindow.Open(anchor as Noesis.UIElement, null), () => Enabled);
                Rgreat.Hub.SettingsHub.Install(harmony, asm, Trace, () => Loc.Language);
            }
            catch (Exception e) { Warn("Settings menu entry failed: " + e.Message); Trace(e.ToString()); }
            try
            {
                var fm = asm.GetType("SeaPower.FileManager");
                var save = fm == null ? null : fm.GetMethod("SaveDirectories", all);
                if (save != null) harmony.Patch(save, postfix: Hm("ModsSavedPostfix"));
                var ls = asm.GetType("SeaPower.LoadScreen");
                if (ls != null)
                    foreach (var nt in ls.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                        if (nt.Name.StartsWith("<ExecuteLoadScreen>", StringComparison.Ordinal))
                        {
                            var mn = nt.GetMethod("MoveNext", all);
                            if (mn != null) harmony.Patch(mn, prefix: Hm("LoadScreenPrefix"));
                            break;
                        }
            }
            catch (Exception e) { Warn("Mods menu hooks failed: " + e.Message); }
        }
    }
}
