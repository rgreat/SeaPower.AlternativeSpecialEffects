using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using Noesis;
using Rgreat.Kit;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// The mod's settings window, opened by the button in the main menu and in the pause menu; built like Alternative Damage
    /// Model's (same look: title bar to drag, X, groups of rows, a help pane with the setting under the mouse, Cancel / Apply / OK),
    /// without profiles.
    ///  - Works on a copy: nothing changes until Apply or OK; then the .cfg is saved.
    ///  - A yellow dot marks a value changed and not applied yet (as in ADM: differs from the saved one); the reset arrow puts the default in;
    ///    Cancel drops what was not applied (the saved values come back).
    /// Noesis elements are built in code; buttons, text and check boxes get the game's own styles.
    /// While a text box has the keyboard, the game's hot keys are off (InputHandler.typingActive, as the game's own text boxes do).
    /// </summary>
    static class SettingsWindow
    {
        public static Action<string> Trace = s => { };
        public static Action<string> Warn = s => { };

        enum Kind { Number, Bool, Lang, Ships, Choice, Action }

        sealed class Field
        {
            public string Tab, Group, Key, Label, Unit, Game;
            public Kind Kind;
            public float Step, Min, Max;
            public Action Act;      // Kind.Action: what the button does
        }

        static readonly List<Field> Fields = new List<Field>();
        static readonly List<string> Tabs = new List<string>();
        static string _curTab = "tabs.lights";
        // groups shown one at a time as sub-tabs inside their tab (the engine groups of the funnel smoke)
        static readonly List<string> SubGroups = new List<string>();
        static string _curSub;
        static StackPanel _subBar, _pageBar;
        // the pages of the Funnel Smoke tab (a strip above its groups): group -> page
        static readonly List<string> Pages = new List<string> { "page.general", "page.engines", "page.ships", "page.nations" };
        static string _curPage = "page.general";
        const string FunnelTab = "tabs.funnel";

        static string PageOf(string group)
        {
            if (group == "grp.funnel") return "page.general";
            if (group == "grp.ships") return "page.ships";
            if (group == "grp.nations") return "page.nations";
            return SubGroups.Contains(group) ? "page.engines" : null;
        }

        /// <summary>A button in the list of options (no value, nothing to save).</summary>
        static void AddAction(string tab, string group, string key, string label, Action act)
        {
            if (!Tabs.Contains(tab)) Tabs.Add(tab);
            Fields.Add(new Field { Tab = tab, Group = group, Key = key, Label = label, Unit = "", Kind = Kind.Action, Act = act });
        }

        static void Add(string tab, string group, string key, string label, string unit, Kind kind, float step, float min, float max, string game = null)
        {
            if (!Tabs.Contains(tab)) Tabs.Add(tab);
            Fields.Add(new Field { Tab = tab, Group = group, Key = key, Label = label, Unit = unit, Kind = kind, Step = step, Min = min, Max = max, Game = game });
        }

        static SettingsWindow()
        {
            const string T1 = "tabs.lights", T2 = "tabs.fires", T3 = "tabs.world", T4 = "tabs.guns", T6 = "tabs.funnel", T5 = "tabs.debug";
            Add(T1, "grp.flashes", "Effects/MuzzleFlashLight", "Light flash when guns fire", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T1, "grp.flashes", "Effects/HitFlashLight", "Light flash when shells hit", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T1, "grp.flashes", "Effects/MissileLaunchFlashLight", "Light of missile boosters at launch", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T1, "grp.flashes", "Effects/FlashPower", "Flash brightness", "x", Kind.Number, 0.1f, 0.1f, 5f);
            Add(T1, "grp.flashes", "Effects/FlashLimit", "Limit flash glare", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T1, "grp.flashes", "Effects/DayBrightness", "Flash and fire brightness by day", "x", Kind.Number, 0.05f, 0.1f, 1f);
            Add(T1, "grp.fireLight", "Effects/FireFlicker", "Fire light flicker", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T1, "grp.fireLight", "Effects/FireLightPower", "Fire light brightness", "x", Kind.Number, 0.1f, 0.1f, 10f);
            Add(T1, "grp.rafts", "Effects/LifeRaftLights", "Life raft lights", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T2, "grp.smoke", "Effects/ExtraSmokeEmitters", "Fire and smoke points by ship length", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T2, "grp.smoke", "Effects/SmokeBySeverity", "Fire look by its strength", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T2, "grp.smoke", "Effects/SmokeStartOpacity", "Small fire smoke at the start", "% of the game's", Kind.Number, 5f, 0f, 100f);
            Add(T2, "grp.smoke", "Effects/SmokeEndOpacity", "Small fire smoke at the moderate stage", "% of the game's", Kind.Number, 5f, 0f, 100f);
            Add(T2, "grp.smoke", "Effects/SmokeModerateFrom", "Moderate fire from strength", "% of maximum", Kind.Number, 5f, 1f, 100f);
            Add(T2, "grp.smoke", "Effects/SmokeSevereFrom", "Severe fire from strength", "% of maximum", Kind.Number, 5f, 1f, 100f);
            Add(T2, "grp.smoke", "Effects/SmokeAfterClouds", "Smoke and clouds in the right order", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T2, "grp.smoke", "Effects/SmokeKeepOnStop", "Keep live smoke when the fire stage changes", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T2, "grp.smoke", "Effects/SmokeSortFix", "Smoke columns overlap in the right order", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T2, "grp.smoke", "Effects/SmokeDensityFactor", "Fire and smoke density", "times", Kind.Number, 0.1f, 0f, 100f);
            Add(T2, "grp.smoke", "Effects/SmokeLifetimeFactor", "Smoke lasts longer", "times", Kind.Number, 0.5f, 1f, 100f);
            Add(T2, "grp.smoke", "Effects/SmokeFireShare", "Animation part with flames", "% of its length", Kind.Number, 5f, 5f, 95f);
            Add(T2, "grp.smoke", "Effects/SmokeSpreadGrowth", "Late smoke sprites grow", "% · 0 = game", Kind.Number, 10f, 0f, 200f);
            Add(T2, "grp.smoke", "Effects/SmokeSpreadFade", "Growing sprites fade", "% · 0 = game", Kind.Number, 10f, 0f, 300f);
            Add(T3, "grp.sinking", "Effects/BubblesStopMinutes", "Sinking bubbles stop after", "min · 0 = never", Kind.Number, 1f, 0f, 600f);
            Add(T3, "grp.view", "UI/HideUnobservedTargets3D", "Hide targets that are not seen in 3D", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T3, "grp.view", "UI/HideUnobservedSubmarines3D", "Hide submarines too", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T3, "grp.view", "Effects/ColourGradingNoDepthMask", "Colour grading without the depth mask", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T4, "grp.hits", "Effects/GunHitEffects", "Spectacular gun hit effects", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T4, "grp.tracers", "Guns/MachineGunBurstFix", "Machine gun tracers in the whole burst", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T4, "grp.tracers", "Guns/MachineGunSoundLoopFix", "Seamless machine gun sound", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T4, "grp.tracers", "Guns/HitSoundFix", "Always play gun hit sound", "", Kind.Bool, 0, 0, 0, "game: off");
            for (int c = 1; c <= HitEffects.Tiers; c++)
            {
                string g = "grp.hitC" + c;
                SubGroups.Add(g);
                if (c < HitEffects.Tiers) Add(T4, g, "GunHits/Class" + c + "UpToKg", "Shells up to mass", "kg", Kind.Number, 0.1f, 0.05f, 5000f);
                Add(T4, g, "GunHits/Class" + c + "ShellModel", "Shell shape", string.Join(",", HitEffects.Models), Kind.Choice, 0, 0, 0);
                Add(T4, g, "GunHits/Class" + c + "ShellSize", "Shell size", "x", Kind.Number, 0.1f, 0.1f, 10f);
                Add(T4, g, "GunHits/Class" + c + "ShellColor", "Shell colour", string.Join(",", HitEffects.Colors), Kind.Choice, 0, 0, 0);
                foreach (var sl in HitEffects.Slots) Add(T4, g, "GunHits/Class" + c + sl, "Size", "x", Kind.Number, 0.1f, 0.05f, 10f);
            }
            Add(T6, "grp.funnel", "FunnelSmoke/Enabled", "Funnel smoke by engine type", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T6, "grp.funnel", "FunnelSmoke/PowerExponent", "Smoke by engine power", "", Kind.Number, 0.1f, 0f, 2f);
            Add(T6, "grp.funnel", "FunnelSmoke/SizeByPower", "Particle size by power", "", Kind.Number, 0.1f, 0f, 2f);
            Add(T6, "grp.funnel", "FunnelSmoke/PowerReference", "Reference power", "hp", Kind.Number, 5000f, 1000f, 500000f);
            Add(T6, "grp.funnel", "FunnelSmoke/GlobalThickness", "Funnel smoke thickness (all groups)", "x", Kind.Number, 0.5f, 0f, 50f);
            Add(T6, "grp.funnel", "FunnelSmoke/GlobalDensity", "Funnel smoke particles (all groups)", "x", Kind.Number, 0.1f, 0f, 20f);
            Add(T6, "grp.funnelDiesel", "FunnelSmoke/DieselDensityIdle", "DensityIdle", "x", Kind.Number, 0.1f, 0f, 20f);
            Add(T6, "grp.funnelDiesel", "FunnelSmoke/DieselDensityFull", "DensityFull", "x", Kind.Number, 0.1f, 0f, 20f);
            Add(T6, "grp.funnelDiesel", "FunnelSmoke/DieselLightIdle", "LightIdle", "% · 0 black, 100 white", Kind.Number, 5f, 0f, 100f);
            Add(T6, "grp.funnelDiesel", "FunnelSmoke/DieselLightFull", "LightFull", "% · 0 black, 100 white", Kind.Number, 5f, 0f, 100f);
            Add(T6, "grp.funnelDiesel", "FunnelSmoke/DieselThicknessIdle", "ThicknessIdle", "x", Kind.Number, 0.05f, 0f, 5f);
            Add(T6, "grp.funnelDiesel", "FunnelSmoke/DieselThicknessFull", "ThicknessFull", "x", Kind.Number, 0.05f, 0f, 5f);
            Add(T6, "grp.funnelDiesel", "FunnelSmoke/DieselLifeIdle", "LifeIdle", "x", Kind.Number, 0.05f, 0.01f, 1f);
            Add(T6, "grp.funnelDiesel", "FunnelSmoke/DieselLifeFull", "LifeFull", "x", Kind.Number, 0.05f, 0.01f, 1f);
            Add(T6, "grp.funnelSteam", "FunnelSmoke/SteamDensityIdle", "DensityIdle", "x", Kind.Number, 0.1f, 0f, 20f);
            Add(T6, "grp.funnelSteam", "FunnelSmoke/SteamDensityFull", "DensityFull", "x", Kind.Number, 0.1f, 0f, 20f);
            Add(T6, "grp.funnelSteam", "FunnelSmoke/SteamLightIdle", "LightIdle", "% · 0 black, 100 white", Kind.Number, 5f, 0f, 100f);
            Add(T6, "grp.funnelSteam", "FunnelSmoke/SteamLightFull", "LightFull", "% · 0 black, 100 white", Kind.Number, 5f, 0f, 100f);
            Add(T6, "grp.funnelSteam", "FunnelSmoke/SteamThicknessIdle", "ThicknessIdle", "x", Kind.Number, 0.05f, 0f, 5f);
            Add(T6, "grp.funnelSteam", "FunnelSmoke/SteamThicknessFull", "ThicknessFull", "x", Kind.Number, 0.05f, 0f, 5f);
            Add(T6, "grp.funnelSteam", "FunnelSmoke/SteamLifeIdle", "LifeIdle", "x", Kind.Number, 0.05f, 0.01f, 1f);
            Add(T6, "grp.funnelSteam", "FunnelSmoke/SteamLifeFull", "LifeFull", "x", Kind.Number, 0.05f, 0.01f, 1f);
            Add(T6, "grp.funnelGasTurbine", "FunnelSmoke/GasTurbineDensityIdle", "DensityIdle", "x", Kind.Number, 0.1f, 0f, 20f);
            Add(T6, "grp.funnelGasTurbine", "FunnelSmoke/GasTurbineDensityFull", "DensityFull", "x", Kind.Number, 0.1f, 0f, 20f);
            Add(T6, "grp.funnelGasTurbine", "FunnelSmoke/GasTurbineLightIdle", "LightIdle", "% · 0 black, 100 white", Kind.Number, 5f, 0f, 100f);
            Add(T6, "grp.funnelGasTurbine", "FunnelSmoke/GasTurbineLightFull", "LightFull", "% · 0 black, 100 white", Kind.Number, 5f, 0f, 100f);
            Add(T6, "grp.funnelGasTurbine", "FunnelSmoke/GasTurbineThicknessIdle", "ThicknessIdle", "x", Kind.Number, 0.05f, 0f, 5f);
            Add(T6, "grp.funnelGasTurbine", "FunnelSmoke/GasTurbineThicknessFull", "ThicknessFull", "x", Kind.Number, 0.05f, 0f, 5f);
            Add(T6, "grp.funnelGasTurbine", "FunnelSmoke/GasTurbineLifeIdle", "LifeIdle", "x", Kind.Number, 0.05f, 0.01f, 1f);
            Add(T6, "grp.funnelGasTurbine", "FunnelSmoke/GasTurbineLifeFull", "LifeFull", "x", Kind.Number, 0.05f, 0.01f, 1f);
            Add(T6, "grp.funnelNuclear", "FunnelSmoke/NuclearDensityIdle", "DensityIdle", "x", Kind.Number, 0.1f, 0f, 20f);
            Add(T6, "grp.funnelNuclear", "FunnelSmoke/NuclearDensityFull", "DensityFull", "x", Kind.Number, 0.1f, 0f, 20f);
            Add(T6, "grp.funnelNuclear", "FunnelSmoke/NuclearLightIdle", "LightIdle", "% · 0 black, 100 white", Kind.Number, 5f, 0f, 100f);
            Add(T6, "grp.funnelNuclear", "FunnelSmoke/NuclearLightFull", "LightFull", "% · 0 black, 100 white", Kind.Number, 5f, 0f, 100f);
            Add(T6, "grp.funnelNuclear", "FunnelSmoke/NuclearThicknessIdle", "ThicknessIdle", "x", Kind.Number, 0.05f, 0f, 5f);
            Add(T6, "grp.funnelNuclear", "FunnelSmoke/NuclearThicknessFull", "ThicknessFull", "x", Kind.Number, 0.05f, 0f, 5f);
            Add(T6, "grp.funnelNuclear", "FunnelSmoke/NuclearLifeIdle", "LifeIdle", "x", Kind.Number, 0.05f, 0.01f, 1f);
            Add(T6, "grp.funnelNuclear", "FunnelSmoke/NuclearLifeFull", "LifeFull", "x", Kind.Number, 0.05f, 0.01f, 1f);
            Add(T6, "grp.funnelCombined", "FunnelSmoke/CombinedDensityIdle", "DensityIdle", "x", Kind.Number, 0.1f, 0f, 20f);
            Add(T6, "grp.funnelCombined", "FunnelSmoke/CombinedDensityFull", "DensityFull", "x", Kind.Number, 0.1f, 0f, 20f);
            Add(T6, "grp.funnelCombined", "FunnelSmoke/CombinedLightIdle", "LightIdle", "% · 0 black, 100 white", Kind.Number, 5f, 0f, 100f);
            Add(T6, "grp.funnelCombined", "FunnelSmoke/CombinedLightFull", "LightFull", "% · 0 black, 100 white", Kind.Number, 5f, 0f, 100f);
            Add(T6, "grp.funnelCombined", "FunnelSmoke/CombinedThicknessIdle", "ThicknessIdle", "x", Kind.Number, 0.05f, 0f, 5f);
            Add(T6, "grp.funnelCombined", "FunnelSmoke/CombinedThicknessFull", "ThicknessFull", "x", Kind.Number, 0.05f, 0f, 5f);
            Add(T6, "grp.funnelCombined", "FunnelSmoke/CombinedLifeIdle", "LifeIdle", "x", Kind.Number, 0.05f, 0.01f, 1f);
            Add(T6, "grp.funnelCombined", "FunnelSmoke/CombinedLifeFull", "LifeFull", "x", Kind.Number, 0.05f, 0.01f, 1f);
            foreach (var g in new[] { "Diesel", "Steam", "GasTurbine", "Nuclear", "Combined" }) SubGroups.Add("grp.funnel" + g);
            Add(T6, "grp.ships", "FunnelSmoke/Ships", "Ships", "", Kind.Ships, 0, 0, 0);
            Add(T6, "grp.nations", "FunnelSmoke/Nations", "Nations", "", Kind.Ships, 0, 0, 0);
            Add(T5, "grp.diagnostics", "General/Debug", "Debug trace", "", Kind.Bool, 0, 0, 0);
            AddAction(T5, "grp.diagnostics", "Debug/ScreenshotTabs", "Screenshot all tabs now", StartShots);
            Add(T5, "grp.diagnostics", "Debug/CamOffColor", "Off: Amplify Color (colour grading)", "", Kind.Bool, 0, 0, 0);
            Add(T5, "grp.diagnostics", "Debug/CamOffUnderwater", "Off: Ceto water post effect", "", Kind.Bool, 0, 0, 0);
            Add(T5, "grp.diagnostics", "Debug/CamOffOcclusion", "Off: Amplify Occlusion (shading)", "", Kind.Bool, 0, 0, 0);
            Add(T5, "grp.diagnostics", "Debug/CamOffBloom", "Off: Amplify Bloom (glow)", "", Kind.Bool, 0, 0, 0);
        }

        // ------------------------------------------------------------ colours (the game's dark panels)
        static readonly Color CLine = WinStyle.Line, CText = WinStyle.Text, CDim = WinStyle.Dim,
                              CWarn = WinStyle.Warn, CTitle = WinStyle.Title, CField = WinStyle.Field, CHl = WinStyle.Hl;

        static SolidColorBrush B(Color c) { return WinStyle.B(c); }

        // ------------------------------------------------------------ state
        // the window itself (Rgreat.Kit): where it is, its size, dragging, resizing, the game's keyboard
        static readonly WinFrame _f = MakeFrame();

        static WinFrame MakeFrame()
        {
            var f = new WinFrame(820f, 855f);   // screen pixels: converted to UI units at the first opening
            f.Id = SelfId;
            f.Trace = s => Trace(s);
            f.ResizeTip = () => Loc.T("set.resizeTip");
            f.LanguageLabel = () => Loc.T("lang.caption");
            f.Languages = () => { var l = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", Loc.T("lang.default")) }; l.AddRange(Loc.GameLanguages()); return l; };
            f.LanguageChanged = () => { Loc.Refresh(); _redraw = 2; };   // the window is rebuilt a couple of frames later (Tick)
            return f;
        }
        static TextBlock _help, _status;
        static StackPanel _body, _tabBar;
        static TabScroll _tabScroll;   // the one-line strip of tabs (Rgreat.Kit)
        static readonly Dictionary<string, string> _work = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static AsePlugin P { get { return AsePlugin.Instance; } }

        static ConfigEntryBase Entry(string key)
        {
            if (P == null) return null;
            foreach (var e in P.Entries) if (string.Equals(e.Definition.Section + "/" + e.Definition.Key, key, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        public static bool IsOpen { get { return _f.Attached; } }

        // ------------------------------------------------------------ the switch between the mods' windows (Rgreat.Hub.SettingsHub)
        const string SelfId = "alternative-special-effects";
        static bool _kept;   // taken off the screen by the switch to another mod's window, the edits kept (Open puts them back)
        public static bool IsDirty() { return (_kept || IsOpen) && Dirty(); }
        public static bool Kept() { return _kept; }
        public static void Hide() { Close(); _kept = true; }
        public static void CommitKept() { Commit(true); _kept = false; }
        public static void DiscardKept() { Load(); Close(); _kept = false; }
        /// <summary>Cancel / ✕: the edits are dropped, here and in the windows of the other mods that are kept.</summary>
        static void Cancel() { Rgreat.Hub.SettingsHub.DiscardOthers(SelfId); Load(); Close(); }

        // ------------------------------------------------------------ open / close

        /// <summary>Opens the window on the screen of 'anchor' (the gear clicked); 'other' when the gear's own screen has no panel for it.</summary>
        public static void Open(UIElement anchor, UIElement other)
        {
            if (anchor == null) { anchor = other; other = null; }
            if (P == null || anchor == null) { Warn("Settings window: not ready."); return; }
            try
            {
                if (IsOpen)
                {
                    bool shown = false;
                    try { shown = _f.Root != null && _f.Root.IsVisible; } catch { }
                    if (shown) return;
                }
                Close();
                var panel = _f.Locate(anchor, other);
                if (_kept) _kept = false; else Load();   // put back after a switch to another mod's window: the edits are kept
                BuildWindow();
                _f.Mount(panel);
                Show();
            }
            catch (Exception e) { Warn("Settings window could not be opened: " + e.Message); Trace("SettingsWindow.Open: " + e); }
        }


        static void Close()
        {
            Typing(false);
            _f.Unmount();
        }

        static void Load()
        {
            _work.Clear();
            foreach (var e in P.Entries) _work[e.Definition.Section + "/" + e.Definition.Key] = e.GetSerializedValue();
        }

        static string Saved(Field f) { var e = Entry(f.Key); return e == null ? "" : e.GetSerializedValue(); }
        static string Default(Field f) { var e = Entry(f.Key); return e == null ? "" : TomlTypeConverter.ConvertToString(e.DefaultValue, e.SettingType); }
        static string Get(Field f) { string v; return _work.TryGetValue(f.Key, out v) ? v : ""; }

        static bool Same(string a, string b)
        {
            bool p, q;
            if (bool.TryParse((a ?? "").Trim(), out p) && bool.TryParse((b ?? "").Trim(), out q)) return p == q;
            float x, y;
            if (TryNum(a, out x) && TryNum(b, out y)) return Math.Abs(x - y) < 1e-5f;
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        static bool Dirty() { foreach (var f in Fields) if (!Same(Get(f), Saved(f))) return true; return false; }


        // ------------------------------------------------------------ building

        static TextBlock Text(string s, Color c, float size = 13f) { return _f.Text(s, c, size); }
        static Button Btn(string s, Action click, float minW = 0f) { return _f.Btn(s, click, minW); }
        static TextBox Edit(string text, float width) { return _f.Edit(text, width); }
        static void Typing(bool on) { _f.Typing(on); }
        static Path Cross(float size) { return WinStyle.Cross(size); }
        static bool TryNum(string s, out float v) { return WinStyle.TryNum(s, out v); }
        static float Num(string s) { return WinStyle.Num(s); }
        static TextBlock GroupTitle(string s) { return WinStyle.GroupTitle(s); }
        static DependencyObject SafeParent(DependencyObject e) { return WinStyle.SafeParent(e); }



        static void BuildWindow(bool keepRoot = false)
        {
            if (keepRoot && _f.Root != null) _f.Root.Children.Clear();
            else _f.NewRoot();
            var win = _f.Window();
            var parts = new List<UIElement>();
            parts.Add(_f.TitleBar(Loc.T("set.title"), Cancel));
            var modRow = _f.ModRow();   // the installed mods: a click switches to that mod's window
            if (modRow != null) parts.Add(modRow);

            _tabBar = new StackPanel { Orientation = Orientation.Horizontal };
            _tabScroll = new TabScroll(_tabBar, B(CTitle), B(CLine), B(CText));   // one line, scrolled by the arrows at its ends / the wheel; the chosen tab is kept in view
            parts.Add(_f.ToolRow(_tabScroll.Root, Loc.T("set.search"), () => Show(), () => TabChanged(_curTab), RevertTab, Loc.T("set.revertTab")));   // the strip of tabs, the search, the revert of the tab

            _body = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0f, 6f, 0f, 6f) };
            var scroll = _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var area = _f.Card(scroll);
            parts.Add(area);

            parts.Add(_f.HelpPane(Loc.T("set.hint")));
            _help = _f.Help;

            parts.Add(_f.Footer(null, out _status, Loc.T("set.cancel"), Cancel,   // the saved values back: edits not applied are dropped (here and in the other mods' windows)
                                Loc.T("set.apply"), () => Commit(false), Loc.T("set.ok"), () => Commit(true)));
            _f.Assemble(win, parts, area);
        }

        // ------------------------------------------------------------ rows

        static void Show()
        {
            if (_body == null) return;
            _body.Children.Clear();
            if (_tabBar != null)
            {
                _tabBar.Children.Clear();
                Border chosen = null;
                bool searching = !string.IsNullOrEmpty(_f.Query);
                if (searching && !TabHasHits(_curTab)) foreach (var t0 in Tabs) if (TabHasHits(t0)) { _curTab = t0; break; }   // the search: a tab with nothing found is left
                foreach (var t in Tabs)
                {
                    if (searching && !TabHasHits(t)) continue;   // the tabs with nothing found are hidden
                    string tab = t;
                    bool sel = tab == _curTab;
                    var b = _f.TabButton(Loc.T(tab), sel, TabChanged(tab), () => { _curTab = tab; Show(); });
                    _tabBar.Children.Add(b);
                    if (sel) chosen = b;
                }
                if (_tabScroll != null && chosen != null) _tabScroll.Reveal(chosen);
            }
            string group = null; bool subBar = false; _subBar = null; _pageBar = null;
            if (!string.IsNullOrEmpty(_f.Query)) { ShowFound(); UpdateStatus(); return; }
            if (_curTab == FunnelTab) _body.Children.Add(PageTabs());
            if (_curSub == null || !SubGroups.Contains(_curSub) || !HasFields(_curTab, _curSub))
            {
                _curSub = null;
                foreach (var g in SubGroups) if (HasFields(_curTab, g)) { _curSub = g; break; }
            }
            foreach (var f in Fields)
            {
                if (f.Tab != _curTab) continue;
                if (_curTab == FunnelTab && PageOf(f.Group) != _curPage) continue;
                if (SubGroups.Contains(f.Group))
                {
                    if (!subBar)
                    {
                        subBar = true;
                        if (f.Group.StartsWith("grp.hitC")) _body.Children.Add(GroupTitle(Loc.T("hdr.hitClasses")));
                        _body.Children.Add(SubTabs(_curTab));
                    }
                    if (f.Group != _curSub) continue;
                }
                if (f.Group != group)
                {
                    group = f.Group;
                    if (group.StartsWith("grp.hitC"))
                    {
                        int hc = int.Parse(group.Substring(8)); var d = Text(HitCalText(hc), CDim, 12f);
                        _hitDesc = () => d.Text = HitCalText(hc);
                        d.TextWrapping = TextWrapping.Wrap; d.Margin = new Thickness(12f, 4f, 12f, 4f);
                        _body.Children.Add(d);
                    }
                    else { string gr = group, tabNow = _curTab; _body.Children.Add(_f.GroupHeader(Loc.T(gr), () => GroupChanged(tabNow, gr), () => RevertWhere(x => x.Tab == tabNow && x.Group == gr), Loc.T("set.revertGroup"))); }
                }
                if (f.Kind == Kind.Ships) ShipRows(f); else _body.Children.Add(Row(f));
            }
            UpdateStatus();
        }

        static bool FieldHit(Field f)
        {
            return f.Kind != Kind.Ships && _f.Match(Loc.T("lbl." + f.Key));
        }

        static bool TabHasHits(string tab) { foreach (var f in Fields) if (f.Tab == tab && FieldHit(f)) return true; return false; }

        /// <summary>The search: the options of the tab that fit, all its pages one under the other.</summary>
        static void ShowFound()
        {
            string group = null; int n = 0;
            foreach (var f in Fields)
            {
                if (f.Tab != _curTab || !FieldHit(f)) continue;
                FrameworkElement row;
                try { row = Row(f); } catch (Exception e) { Trace("settings row '" + f.Key + "' failed: " + e); continue; }   // the title of a group goes in only with a row under it
                if (f.Group != group) { group = f.Group; _body.Children.Add(GroupTitle(Loc.T(group))); }
                _body.Children.Add(row); n++;
            }
            if (n == 0) { var t = Text(Loc.T("set.nothing"), CDim, 13f); t.Margin = new Thickness(12f, 14f, 12f, 0f); _body.Children.Add(t); }
        }

        static bool GroupChanged(string tab, string group)
        {
            foreach (var f in Fields) if (f.Tab == tab && f.Group == group && f.Kind != Kind.Action && !Same(Get(f), Saved(f))) return true;
            return false;
        }

        /// <summary>The saved values back into the options picked (edits not applied dropped; nothing closes, the place in the list stays).</summary>
        static void RevertWhere(Func<Field, bool> pick)
        {
            foreach (var f in Fields) if (f.Kind != Kind.Action && pick(f)) _work[f.Key] = Saved(f);
            _f.KeepScroll(_scroll, Show);
        }

        static void RevertTab() { string t = _curTab; RevertWhere(x => x.Tab == t); }

        /// <summary>The strip of the pages of the Funnel Smoke tab.</summary>
        static FrameworkElement PageTabs()
        {
            _pageBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12f, 10f, 12f, 2f) };
            foreach (var pg in Pages)
            {
                string page = pg; bool sel = page == _curPage;
                var b = _f.PageButton(Loc.T(page), sel, PageChanged(page), () => { _curPage = page; Show(); });
                b.Tag = page;
                _pageBar.Children.Add(b);
            }
            return _pageBar;
        }

        static bool PageChanged(string page)
        {
            foreach (var f in Fields) if (f.Tab == FunnelTab && PageOf(f.Group) == page && !Same(Get(f), Saved(f))) return true;
            return false;
        }

        /// <summary>The strip of sub-tabs (one per group in SubGroups that has fields on this tab), drawn above the group that is selected.</summary>
        static FrameworkElement SubTabs(string tab)
        {
            _subBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12f, 10f, 12f, 4f) };
            foreach (var g in SubGroups)
            {
                bool has = false; foreach (var f in Fields) if (f.Tab == tab && f.Group == g) { has = true; break; }
                if (!has) continue;
                string grp = g; bool sel = grp == _curSub;
                var b = _f.PageButton(Loc.T(grp), sel, GroupChanged(grp), () => { _curSub = grp; Show(); });
                b.Tag = grp;
                _subBar.Children.Add(b);
            }
            return _subBar;
        }

        static bool HasFields(string tab, string group)
        {
            foreach (var f in Fields) if (f.Tab == tab && f.Group == group) return true;
            return false;
        }

        static bool GroupChanged(string group)
        {
            foreach (var f in Fields) if (f.Group == group && !Same(Get(f), Saved(f))) return true;
            return false;
        }

        static void UpdateTabDots()
        {
            if (_pageBar != null)
                foreach (var c in _pageBar.Children)
                {
                    var b = c as Border; var sp = b == null ? null : b.Child as StackPanel; var g = b == null ? null : b.Tag as string;
                    if (sp == null || g == null) continue;
                    bool has = sp.Children.Count > 1, want = PageChanged(g);
                    if (has == want) continue;
                    if (want) sp.Children.Insert(0, new Ellipse { Width = 6f, Height = 6f, Fill = B(CWarn), Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center });
                    else sp.Children.RemoveAt(0);
                }
            if (_subBar != null)
                foreach (var c in _subBar.Children)
                {
                    var b = c as Border; var sp = b == null ? null : b.Child as StackPanel; var g = b == null ? null : b.Tag as string;
                    if (sp == null || g == null) continue;
                    bool has = sp.Children.Count > 1, want = GroupChanged(g);
                    if (has == want) continue;
                    if (want) sp.Children.Insert(0, new Ellipse { Width = 6f, Height = 6f, Fill = B(CWarn), Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center });
                    else sp.Children.RemoveAt(0);
                }
            if (_tabBar == null) return;
            int i = 0;
            foreach (var t in Tabs)
            {
                if (!string.IsNullOrEmpty(_f.Query) && !TabHasHits(t)) continue;   // the tabs with nothing found are not in the bar (search)
                if (i >= _tabBar.Children.Count) break;
                var b = _tabBar.Children[i++] as Border; var sp = b == null ? null : b.Child as StackPanel;
                if (sp == null) continue;
                bool has = sp.Children.Count > 1, want = TabChanged(t);
                if (has == want) continue;
                if (want) sp.Children.Insert(0, new Ellipse { Width = 6f, Height = 6f, Fill = B(CWarn), Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center });
                else sp.Children.RemoveAt(0);
            }
        }

        static bool TabChanged(string tab)
        {
            foreach (var f in Fields) if (f.Tab == tab && !Same(Get(f), Saved(f))) return true;
            return false;
        }

        static Action _hitDesc;

        /// <summary>The calibre of a hit-effect class from the mass limits set now: a shell weighs about 48 kg at 152 mm and the mass goes with the cube of the calibre.</summary>
        static string HitCalText(int c)
        {
            Func<int, double> lim = n =>
            {
                string v; double d;
                return n >= 1 && n <= HitEffects.Tiers - 1 && _work.TryGetValue("GunHits/Class" + n + "UpToKg", out v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : 0.0;
            };
            Func<double, string> cal = kg =>
            {
                double d = 152.0 * Math.Pow(Math.Max(kg, 0.001) / 48.0, 1.0 / 3.0);
                return (d < 60.0 ? Math.Round(d) : Math.Round(d / 5.0) * 5.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            };
            if (c <= 1) return string.Format(Loc.T("desc.hitCalUp"), c, cal(lim(1)));
            if (c >= HitEffects.Tiers) return string.Format(Loc.T("desc.hitCalFrom"), c, cal(lim(c - 1)));
            return string.Format(Loc.T("desc.hitCalRange"), c, cal(lim(c - 1)) + "-" + cal(lim(c)));
        }

        static void UpdateStatus()
        {
            _f.UpdateModDots();
            if (_hitDesc != null) _hitDesc();
            if (_status == null) return;
            bool any = false;
            foreach (var f in Fields) if (!Same(Get(f), Saved(f))) { any = true; break; }
            _status.Text = Loc.T(any ? "set.changed" : "set.clean");
            _f.UpdateTools();
        }


        static FrameworkElement Row(Field f)
        {
            if (f.Kind == Kind.Action) return ActionRow(f);
            var g = new Grid { Background = B(Color.FromArgb(0, 0, 0, 0)) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star), MinWidth = 330f });   // the names take what the window gains
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star), MinWidth = 200f });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110f) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30f) });
            var outer = new Border { Child = g, Padding = new Thickness(12f, 4f, 6f, 4f), Background = B(Color.FromArgb(0, 0, 0, 0)) };

            var lab = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dot = new Ellipse { Width = 6f, Height = 6f, Fill = B(CWarn), Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center };
            lab.Children.Add(dot);
            lab.Children.Add(Text(Loc.T("lbl." + f.Key), CText));
            g.Children.Add(lab);

            Action mark = () => { dot.Visibility = Same(Get(f), Saved(f)) ? Visibility.Hidden : Visibility.Visible; UpdateStatus(); UpdateTabDots(); };
            Action refresh;
            var editor = f.Kind == Kind.Bool ? BoolEditor(f, mark, out refresh) : f.Kind == Kind.Lang ? LangEditor(f, mark, out refresh) : f.Kind == Kind.Choice ? ChoiceEditor(f, mark, out refresh) : NumberEditor(f, mark, out refresh);
            Grid.SetColumn(editor, 1);
            g.Children.Add(editor);

            string d = Default(f).Replace("True", Loc.T("set.on")).Replace("False", Loc.T("set.off")).Replace("true", Loc.T("set.on")).Replace("false", Loc.T("set.off"));
            if (f.Kind == Kind.Lang) d = Loc.T("set.gameLang");
            if (f.Kind == Kind.Choice) d = Loc.T("opt." + Default(f));
            var game = new TextBlock { Text = Loc.F("set.default", d) + (f.Game != null ? "\n" + Loc.T("set.gameOff") : ""), Foreground = B(CDim), FontSize = 11f, TextAlignment = TextAlignment.Right,
                                       VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(game, 2);
            g.Children.Add(game);

            var rst = _f.ResetButton(Loc.T("set.resetTip"), () => { _work[f.Key] = Default(f); refresh(); mark(); });
            Grid.SetColumn(rst, 3);
            g.Children.Add(rst);

            mark();
            _f.Hover(outer, () => Loc.T("lbl." + f.Key), () => { var en = Entry(f.Key); return Loc.Or("help." + f.Key, en != null && en.Description != null ? en.Description.Description : ""); });
            return outer;
        }

        /// <summary>A row with a button (Kind.Action): the label, the button, the help on hover.</summary>
        static FrameworkElement ActionRow(Field f)
        {
            var g = new Grid { Background = B(Color.FromArgb(0, 0, 0, 0)) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star), MinWidth = 330f });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star), MinWidth = 200f });
            var outer = new Border { Child = g, Padding = new Thickness(12f, 4f, 6f, 4f), Background = B(Color.FromArgb(0, 0, 0, 0)) };
            var lab = Text(Loc.T("lbl." + f.Key), CText); lab.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(lab);
            var btn = Btn(Loc.T("btn.run"), f.Act, 70f); btn.HorizontalAlignment = HorizontalAlignment.Left; btn.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(btn, 1); g.Children.Add(btn);
            outer.MouseEnter += (o, e) =>
            {
                outer.Background = B(CHl);
                _f.ShowHelp(Loc.T("lbl." + f.Key), Loc.Or("help." + f.Key, ""));
            };
            outer.MouseLeave += (o, e) => outer.Background = B(Color.FromArgb(0, 0, 0, 0));
            return outer;
        }

        // ---- screenshots of every tab (the settings kit, SettingsShotKit.cs): the window fitted to each tab, a PNG in BepInEx/AlternativeSpecialEffects_screens
        /// <summary>The screenshots of the tabs of the window on the screen (the Game Interface's Debug runs it for every mod).</summary>
        internal static System.Collections.IEnumerator ShotsRun() { return SettingsShots.Run(new ShotAdapter(), System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "AlternativeSpecialEffects_screens"), s => Trace(s), s => Warn(s)); }

        static void StartShots()
        {
            if (P == null || SettingsShots.Busy) return;
            P.StartCoroutine(ShotsRun());
        }

        sealed class ShotAdapter : IShotWindow
        {
            string _tab0, _page0;
            public bool Begin() { if (_f.Root == null || _body == null || !_f.Root.IsVisible) return false; _tab0 = _curTab; _page0 = _curPage; return true; }
            public int TabCount { get { return Tabs.Count; } }
            public bool TabUsable(int tab) { return true; }
            public int Pages(int tab) { return Tabs[tab] == FunnelTab ? SettingsWindow.Pages.Count : 1; }
            public void Show(int tab, int page) { _curTab = Tabs[tab]; if (_curTab == FunnelTab) _curPage = SettingsWindow.Pages[page]; SettingsWindow.Show(); }
            public string Name(int tab, int page) { return Loc.T(Tabs[tab]) + (Tabs[tab] == FunnelTab ? "_p" + (page + 1) : ""); }
            public bool Rect(out float x, out float y, out float w, out float h) { return ShotFit.Rect(_f.Root, out x, out y, out w, out h); }
            public void FitMax() { _f.ClearHelp(); ShotFit.Max(_f.Root, _f.Move); }
            public void FitWidth() { ShotFit.Width(_f.Root, _body, _scroll, _f.Move, _f.ShotMinW); }
            public void FitHeight() { ShotFit.Height(_f.Root, _scroll, _f.Move, 420f); }
            public void End() { _curTab = _tab0; _curPage = _page0; ShotFit.Restore(_f.Root, _f.Move, _f.W, _f.H); SettingsWindow.Show(); }
        }




        static FrameworkElement NumberEditor(Field f, Action mark, out Action refresh)
        {
            return _f.Number(() => Get(f), null, v => _work[f.Key] = v.ToString("0.##", CultureInfo.InvariantCulture), f.Step, f.Min, f.Max,
                             string.IsNullOrEmpty(f.Unit) ? null : Loc.Or("unit." + f.Key, f.Unit), mark, out refresh);
        }

        static FrameworkElement BoolEditor(Field f, Action mark, out Action refresh)
        {
            return _f.Check(() => Get(f).Trim().Equals("true", StringComparison.OrdinalIgnoreCase), on => { _work[f.Key] = on ? "true" : "false"; mark(); }, out refresh);
        }

        static FrameworkElement ChoiceEditor(Field f, Action mark, out Action refresh)
        {
            var codes = new List<string>(f.Unit.Split(','));
            var labels = new List<string>();
            foreach (var c in codes) labels.Add(Loc.T("opt." + c));
            return CodeCombo(f, codes, labels, mark, out refresh);
        }

        /// <summary>A drop-down over a list of values (codes) with their texts.</summary>
        static FrameworkElement CodeCombo(Field f, List<string> codes, List<string> labels, Action mark, out Action refresh)
        {
            return _f.Combo(labels, () => { int i = codes.IndexOf((Get(f) ?? "").Trim()); return i < 0 ? 0 : i; },
                            i => { if (i >= codes.Count || codes[i] == (Get(f) ?? "").Trim()) return; _work[f.Key] = codes[i]; mark(); }, 200f, 24f, out refresh);
        }

        static FrameworkElement LangEditor(Field f, Action mark, out Action refresh)
        {
            var codes = new List<string> { "" };
            foreach (var l in Loc.GameLanguages()) if (!codes.Contains(l.Key)) codes.Add(l.Key);
            var names = new Dictionary<string, string>();
            foreach (var l in Loc.GameLanguages()) names[l.Key] = l.Value;
            var labels = new List<string>();
            foreach (var c in codes) labels.Add(string.IsNullOrEmpty(c) ? Loc.T("set.gameLang") : (names.ContainsKey(c) ? names[c] + " (" + c + ")" : c));
            return CodeCombo(f, codes, labels, mark, out refresh);
        }

        // ------------------------------------------------------------ single ships: multipliers of the funnel smoke ([FunnelSmoke] Ships)

        static Grid ShipGrid()
        {
            var g = new Grid { Background = B(Color.FromArgb(0, 0, 0, 0)) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star), MinWidth = 210f });
            for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122f) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30f) });
            return g;
        }

        static void ShipHelp(Border outer, string title, string key)
        {
            outer.MouseEnter += (o, e) =>
            {
                outer.Background = B(CHl);
                _f.ShowHelp(title, Loc.Or(key, ""));
            };
            outer.MouseLeave += (o, e) => outer.Background = B(Color.FromArgb(0, 0, 0, 0));
        }

        static void ShipRows(Field f)
        {
            bool nat = f.Key.EndsWith("Nations", StringComparison.OrdinalIgnoreCase);
            Func<string, string> show = nat ? (Func<string, string>)ShipNames.NationDisplay : ShipNames.Display;
            Func<string, string> toKey = nat ? (Func<string, string>)ShipNames.NationToKey : ShipNames.ToIni;
            Func<string, int, List<string>> filter = nat ? (Func<string, int, List<string>>)ShipNames.NationFilter : ShipNames.Filter;
            string helpKey = "help." + f.Key;
            var list = ShipFunnel.Parse(Get(f));
            Action save = () => { _work[f.Key] = ShipFunnel.Write(list); UpdateStatus(); UpdateTabDots(); };
            string[] heads = { Loc.T("ships.particles"), Loc.T("ships.duration"), Loc.T("ships.lightness"), Loc.T("ships.thickness") };
            if (list.Count > 0)
            {
                var hg = ShipGrid();
                for (int c = 0; c < 4; c++) { var h = Text(heads[c], CDim, 11f); h.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(h, c + 1); hg.Children.Add(h); }
                _body.Children.Add(new Border { Child = hg, Padding = new Thickness(12f, 2f, 6f, 0f) });
            }
            foreach (var it0 in list)
            {
                var it = it0;
                var g = ShipGrid();
                var outer = new Border { Child = g, Padding = new Thickness(12f, 4f, 6f, 4f), Background = B(Color.FromArgb(0, 0, 0, 0)) };
                var nm = Text(show(it.Ini), CText);
                try { ToolTipService.SetToolTip(nm, it.Ini); } catch { }
                g.Children.Add(nm);
                Func<float, float, float, float, Func<float>, Action<float>, FrameworkElement> num = (step, min, max, cur, get, set) =>
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    var box = Edit(cur.ToString("0.##", CultureInfo.InvariantCulture), 46f);
                    Action<float> put = v =>
                    {
                        v = Math.Max(min, Math.Min(max, v));
                        v = (float)Math.Round(v * 100f) / 100f;
                        box.Text = v.ToString("0.##", CultureInfo.InvariantCulture);
                        set(v); save();
                    };
                    var minus = new Button { Content = "−", Width = 24f, Height = 24f, Padding = new Thickness(0f) };
                    var plus = new Button { Content = "+", Width = 24f, Height = 24f, Padding = new Thickness(0f) };
                    minus.Click += (o, e) => put((float)Math.Round((Num(box.Text) - step) / step) * step);
                    plus.Click += (o, e) => put((float)Math.Round((Num(box.Text) + step) / step) * step);
                    Action typed = () => { float v; if (TryNum(box.Text, out v)) put(v); else box.Text = get().ToString("0.##", CultureInfo.InvariantCulture); };
                    box.LostFocus += (o, e) => typed();
                    box.KeyDown += (o, e) => { if (e.Key == Key.Return) { typed(); e.Handled = true; } };
                    sp.Children.Add(minus); sp.Children.Add(box); sp.Children.Add(plus);
                    return sp;
                };
                var e1 = num(0.1f, 0f, 10f, it.Particles, () => it.Particles, v => it.Particles = v);
                var e2 = num(0.05f, 0.01f, 10f, it.Duration, () => it.Duration, v => it.Duration = v);
                var e3 = num(0.05f, 0f, 10f, it.Lightness, () => it.Lightness, v => it.Lightness = v);
                var e4 = num(0.05f, 0f, 10f, it.Thickness, () => it.Thickness, v => it.Thickness = v);
                Grid.SetColumn(e1, 1); Grid.SetColumn(e2, 2); Grid.SetColumn(e3, 3); Grid.SetColumn(e4, 4);
                g.Children.Add(e1); g.Children.Add(e2); g.Children.Add(e3); g.Children.Add(e4);
                var rm = new Button { Content = Cross(8f), Width = 24f, Height = 22f, Padding = new Thickness(0f), HorizontalAlignment = HorizontalAlignment.Right,
                                      HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
                try { ToolTipService.SetToolTip(rm, Loc.T(nat ? "ships.removeNation" : "ships.remove")); } catch { }
                rm.Click += (o, e) => { list.Remove(it); save(); Show(); };
                Grid.SetColumn(rm, 5); g.Children.Add(rm);
                ShipHelp(outer, show(it.Ini), helpKey);
                _body.Children.Add(outer);
            }
            // add a ship: a drop-down of the game's ships by their names; typing filters it, any ini name can be typed too
            var ag = new Grid();
            ag.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ag.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ag.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            var aouter = new Border { Child = ag, Padding = new Thickness(12f, 8f, 6f, 8f) };
            var nameBox = new ComboBox { IsEditable = true, Width = 230f, MinHeight = 24f, HorizontalAlignment = HorizontalAlignment.Left, StaysOpenOnEdit = true };
            Action<string> fill = t0 => { nameBox.Items.Clear(); foreach (var n in filter(t0, 300)) nameBox.Items.Add(n); };
            fill("");
            string lastFilter = "";
            nameBox.KeyUp += (o, e) =>
            {
                if (e.Key == Key.Return || e.Key == Key.Up || e.Key == Key.Down) return;
                string t = nameBox.Text ?? "";
                if (t == lastFilter) return;
                lastFilter = t;
                fill(t);
                nameBox.Text = t;
                try { var tb = nameBox.Template.FindName("PART_EditableTextBox", nameBox) as TextBox; if (tb != null) tb.CaretIndex = t.Length; } catch { }
                nameBox.IsDropDownOpen = nameBox.Items.Count > 0 && t.Length > 0;
            };
            nameBox.GotFocus += (o, e) => Typing(true);
            nameBox.LostFocus += (o, e) => Typing(false);
            ag.Children.Add(nameBox);
            var err = Text("", CWarn, 11f); err.Margin = new Thickness(8f, 0f, 0f, 0f);
            var addB = new Button { Content = Loc.T(nat ? "ships.addNation" : "ships.add"), Padding = new Thickness(10f, 2f, 10f, 2f), Margin = new Thickness(8f, 0f, 0f, 0f), MinHeight = 24f };
            addB.Click += (o, e) =>
            {
                string n = toKey((nameBox.SelectedItem as string ?? nameBox.Text ?? "").Trim());
                if (!nat && n.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
                if (n.Length == 0 || n.IndexOfAny(new[] { ';', '=', ',', '\n' }) >= 0) { err.Text = Loc.T(nat ? "ships.pickNation" : "ships.pick"); return; }
                if (!list.Exists(x => string.Equals(x.Ini, n, StringComparison.OrdinalIgnoreCase))) list.Add(new ShipFunnel.Item { Ini = n });
                save(); Show();
            };
            Grid.SetColumn(addB, 1); ag.Children.Add(addB);
            Grid.SetColumn(err, 2); ag.Children.Add(err);
            ShipHelp(aouter, Loc.T(nat ? "ships.addNation" : "ships.add"), helpKey);
            _body.Children.Add(aouter);
        }

        static ScrollViewer _scroll;
        static float _keepScroll; static int _scrollBack;

        static void Commit(bool close)
        {
            Typing(false);
            Rgreat.Hub.SettingsHub.CommitOthers(SelfId);   // the edits kept in the other mods' windows are applied too
            if (!close && _scroll != null) { _keepScroll = (float)_scroll.VerticalOffset; _scrollBack = 4; }   // Apply keeps the place in the list
            foreach (var f in Fields)
            {
                var e = Entry(f.Key);
                if (e == null || Same(Get(f), e.GetSerializedValue())) continue;
                try { e.SetSerializedValue(Get(f)); } catch (Exception ex) { Trace(f.Key + ": " + ex.Message); }
            }
            try { P.Cfg.Save(); } catch (Exception ex) { Warn("Saving the settings failed: " + ex.Message); }
            if (close) { Close(); return; }
            Load();
            Show();
            _redraw = 2;   // the whole window is rebuilt a couple of frames later: not inside the click of a button that is being replaced
        }

        static int _redraw;

        /// <summary>Called every frame: the delayed rebuild of the window after Apply (the texts follow the new language).</summary>
        public static void Tick()
        {
            if (_scrollBack > 0 && _redraw <= 0)
            {
                // the new window is laid out a frame or two after it is built: the offset is put back until it holds
                if (_scroll != null && IsOpen) { try { _scroll.ScrollToVerticalOffset(_keepScroll); } catch { } }
                _scrollBack--;
            }
            if (_redraw <= 0) return;
            if (--_redraw > 0) return;
            if (_f.Root == null || !IsOpen) return;
            try { BuildWindow(true); Show(); } catch (Exception ex) { Trace("rebuild: " + ex); }
        }
    }
}
