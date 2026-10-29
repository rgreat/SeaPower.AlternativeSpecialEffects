using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// Localization. All the mod's texts are in the table below (key, English text); the translations are plain text files
    /// "lang/&lt;code&gt;.ini" in the mod folder, one for each language of the game (the list is the game's own: config.ini [Languages]):
    ///     key = text        (";" starts a comment line; \n in a text is a line break; {0}, {1} are filled in by the mod)
    /// The language is the game's (Options → Language), or [General] Language in the .cfg when it is set. A text missing from the
    /// file is English. The mod creates the files that are missing (the English texts to translate); in an existing file it
    /// appends the keys added later (after "; --- new ---"); it never changes the lines the player has edited.
    /// The settings window's help texts (the .cfg descriptions) are in the files as "help.Section/Key".
    /// </summary>
    static class Loc
    {
        public static Action<string> Trace = s => { };

        static readonly List<string> Keys = new List<string>();
        static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal);
        static Dictionary<string, string> _tr = new Dictionary<string, string>(StringComparer.Ordinal);
        static string _lang = "en", _forced = "";
        static int _checkedAt = -1;
        static bool _filesDone;

        /// <summary>Language from the .cfg ("" = the game's).</summary>
        public static Func<string> Forced = () => "";

        static void D(string key, string en) { Keys.Add(key); En[key] = en; }

        static Loc()
        {
            D("menu.button", "Special Effects");
            // ---- settings window
            D("set.title", "Alternative Special Effects - Settings");
            D("set.hint", "Point at a setting to see what it does.");
            D("set.cancel", "Cancel"); D("set.apply", "Apply"); D("set.ok", "OK");
            D("set.changed", "● changed, not applied yet   ↺ = back to the default");
            D("set.default", "default: {0}"); D("set.on", "on"); D("set.off", "off");
            D("set.gameOff", "game: off");
            D("set.gameLang", "the game's language");
            D("set.resetTip", "Back to the default");
            D("set.resizeTip", "Drag to resize");
            D("tabs.lights", "Lights"); D("tabs.fires", "Fires"); D("tabs.world", "Sinking and view"); D("tabs.guns", "Guns");
            D("tabs.debug", "Debug"); D("tabs.language", "Language");
            D("grp.flashes", "Flashes"); D("grp.fireLight", "Fire light"); D("grp.smoke", "Fire and smoke"); D("grp.sinking", "Sinking");
            D("grp.view", "3D view"); D("grp.binoculars", "Binoculars"); D("grp.tracers", "Tracers and sound");
            D("grp.diagnostics", "Diagnostics"); D("grp.language", "Language");
            D("lbl.Effects/MuzzleFlashLight", "Light flash when guns fire");
            D("lbl.Effects/HitFlashLight", "Light flash when shells hit");
            D("lbl.Effects/MissileLaunchFlashLight", "Light of missile boosters at launch");
            D("lbl.Effects/FlashPower", "Flash brightness");
            D("lbl.Effects/FlashLimit", "Limit flash glare");
            D("lbl.Effects/DayBrightness", "Flash and fire brightness by day");
            D("lbl.Effects/FireFlicker", "Fire light flicker");
            D("lbl.Effects/FireLightPower", "Fire light brightness");
            D("lbl.Optics/BinocularsNormalView", "Normal view in binoculars at max zoom");
            D("lbl.Optics/NormalViewDayOnly", "Normal view only by day");
            D("lbl.Guns/MachineGunBurstFix", "Machine gun tracers in the whole burst");
            D("lbl.Guns/MachineGunSoundLoopFix", "Seamless machine gun sound");
            D("lbl.Guns/HitSoundFix", "Always play gun hit sound");
            D("lbl.General/Debug", "Debug trace");
            D("lbl.General/Language", "Language of the texts (empty = the game's)");
            D("unit.General/Language", "code, e.g. en, ru");
            D("lbl.Effects/ExtraSmokeEmitters", "Fire and smoke points by ship length");
            D("lbl.Effects/SmokeBySeverity", "Fire look by its strength");
            D("lbl.Effects/SmokeStartOpacity", "Small fire smoke at the start"); D("unit.Effects/SmokeStartOpacity", "% of the game's");
            D("lbl.Effects/SmokeEndOpacity", "Small fire smoke at the moderate stage"); D("unit.Effects/SmokeEndOpacity", "% of the game's");
            D("lbl.Effects/SmokeModerateFrom", "Moderate fire from strength"); D("unit.Effects/SmokeModerateFrom", "% of maximum");
            D("lbl.Effects/SmokeSevereFrom", "Severe fire from strength"); D("unit.Effects/SmokeSevereFrom", "% of maximum");
            D("lbl.Effects/BubblesStopMinutes", "Sinking bubbles stop after"); D("unit.Effects/BubblesStopMinutes", "min · 0 = never");
            D("lbl.UI/HideUnobservedTargets3D", "Hide targets that are not seen in 3D");
            D("unit.x", "x");
        }

        // ------------------------------------------------------------ access

        /// <summary>The text of a key in the current language (English when it is not translated).</summary>
        public static string T(string key)
        {
            Check();
            string v;
            if (_tr.TryGetValue(key, out v) && v.Length > 0) return v;
            return En.TryGetValue(key, out v) ? v : key;
        }

        /// <summary>The text with {0}, {1} ... filled in.</summary>
        public static string F(string key, params object[] a)
        {
            var s = T(key);
            try { return string.Format(CultureInfo.InvariantCulture, s, a); }
            catch { try { return string.Format(CultureInfo.InvariantCulture, En.ContainsKey(key) ? En[key] : key, a); } catch { return s; } }
        }

        /// <summary>The translation of a key if there is one, else the given (English) text; for texts that are not in the table (.cfg descriptions).</summary>
        public static string Or(string key, string fallback)
        {
            Check();
            string v;
            return _tr.TryGetValue(key, out v) && v.Length > 0 ? v : fallback;
        }

        /// <summary>Texts known only at run time (the .cfg descriptions): put in the table so that the files get them.</summary>
        public static void Register(string key, string en)
        {
            if (string.IsNullOrEmpty(key) || En.ContainsKey(key)) return;
            Keys.Add(key); En[key] = en ?? "";
            _filesDone = false;
        }

        public static string Language { get { Check(); return _lang; } }

        static bool _checking;

        static void Check()
        {
            if (_checking) return;
            int now = Environment.TickCount;
            if (_checkedAt >= 0 && now - _checkedAt < 1500 && _filesDone) return;
            _checking = true;
            try
            {
                _checkedAt = now;
                string forced = "";
                try { forced = (Forced() ?? "").Trim(); } catch { }
                string lang = forced.Length > 0 ? forced : GameLanguage();
                if (lang != _lang || _tr.Count == 0 && !_filesDone || forced != _forced)
                {
                    _lang = lang; _forced = forced;
                    _tr = Load(lang);
                }
                if (!_filesDone) { _filesDone = true; try { MakeFiles(); } catch (Exception e) { Trace("lang files: " + e.Message); } _tr = Load(_lang); }
            }
            catch (Exception e) { Trace("Loc: " + e.Message); }
            finally { _checking = false; }
        }

        // ------------------------------------------------------------ the game's language

        static Type _tHandler, _tSingleton;

        static string GameLanguage()
        {
            try
            {
                var asm = AsePlugin.GameAssembly();
                if (asm == null) return _lang;
                if (_tHandler == null) { _tHandler = asm.GetType("SeaPower.LanguageResourceHandler"); _tSingleton = asm.GetType("SeaPower.Singleton`1"); }
                var inst = _tSingleton.MakeGenericType(_tHandler).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                if (inst == null) return _lang;
                var l = _tHandler.GetProperty("Language").GetValue(inst, null) as string;
                return string.IsNullOrEmpty(l) ? "en" : l;
            }
            catch { return _lang; }
        }

        /// <summary>The languages of the game: its config.ini [Languages] (code → name); English alone when it cannot be read.</summary>
        public static List<KeyValuePair<string, string>> GameLanguages()
        {
            var res = new List<KeyValuePair<string, string>>();
            try
            {
                var asm = AsePlugin.GameAssembly();
                var utils = asm == null ? null : asm.GetType("SeaPower.Utils");
                MethodInfo open = null;
                if (utils != null)
                    foreach (var m in utils.GetMethods(BindingFlags.Static | BindingFlags.Public))
                    {
                        var p = m.GetParameters();
                        if (m.Name == "openIniFile" && p.Length == 4 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(string)) { open = m; break; }
                    }
                if (open != null)
                {
                    var ini = open.Invoke(null, new object[] { "", "/config.ini", false, false });
                    var d = ini == null ? null : ini.GetType().GetMethod("GetSectionKeyValues", new[] { typeof(string) }).Invoke(ini, new object[] { "Languages" }) as Dictionary<string, string>;
                    if (d != null)
                        foreach (var kv in d)
                            if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value)) res.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
                }
            }
            catch (Exception e) { Trace("game languages: " + e.Message); }
            if (res.Count == 0) res.Add(new KeyValuePair<string, string>("en", "English"));
            return res;
        }

        // ------------------------------------------------------------ files

        static string _dir;

        public static string Dir
        {
            get
            {
                if (_dir == null)
                {
                    string p = "";
                    try { p = Path.GetDirectoryName(typeof(Loc).Assembly.Location) ?? ""; } catch { }
                    _dir = Path.Combine(p, "lang");
                }
                return _dir;
            }
        }

        static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"); }

        static string Unesc(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length) { char n = s[++i]; sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n); }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static Dictionary<string, string> Load(string lang)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(lang) || lang.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return d;
            var path = Path.Combine(Dir, lang + ".ini");
            try
            {
                if (!File.Exists(path)) return d;
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    d[line.Substring(0, eq).Trim()] = Unesc(line.Substring(eq + 1).Trim());
                }
            }
            catch (Exception e) { Trace("lang " + lang + ": " + e.Message); }
            return d;
        }

        static void MakeFiles()
        {
            Directory.CreateDirectory(Dir);
            foreach (var l in GameLanguages()) Sync(l.Key, l.Value);
            Sync("en", "English");
            if (_lang != "en" && !File.Exists(Path.Combine(Dir, _lang + ".ini"))) Sync(_lang, _lang);
        }

        /// <summary>Creates the language file, or appends the keys it lacks.</summary>
        static void Sync(string code, string name)
        {
            if (code.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return;
            var path = Path.Combine(Dir, code + ".ini");
            bool english = code == "en";
            if (!File.Exists(path))
            {
                var sb = new StringBuilder();
                sb.AppendLine("; Alternative Special Effects - texts, " + name + " (" + code + ")");
                sb.AppendLine("; key = text. Edit the text after '='. A line with no translation can stay English or be deleted.");
                sb.AppendLine("; \\n = line break; {0}, {1} ... are filled in by the mod - keep them.");
                sb.AppendLine("; The file is used when the game's language is '" + code + "' (or [General] Language in the .cfg).");
                sb.AppendLine();
                foreach (var k in Keys) sb.AppendLine(k + " = " + Esc(En[k]));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return;
            }
            var have = Load(code);
            var add = new StringBuilder();
            foreach (var k in Keys)
                if (!have.ContainsKey(k) && !HasKeyLine(path, k)) add.AppendLine(k + " = " + Esc(En[k]));
            if (add.Length > 0)
                File.AppendAllText(path, Environment.NewLine + "; --- new ---" + Environment.NewLine + add, new UTF8Encoding(false));
        }

        static HashSet<string> _lineCache; static string _lineCachePath;

        static bool HasKeyLine(string path, string key)
        {
            if (_lineCachePath != path)
            {
                _lineCachePath = path; _lineCache = new HashSet<string>(StringComparer.Ordinal);
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var t = raw.TrimStart(';', '#', ' ');
                    int eq = t.IndexOf('=');
                    if (eq > 0) _lineCache.Add(t.Substring(0, eq).Trim());
                }
            }
            return _lineCache.Contains(key);
        }
    }
}
