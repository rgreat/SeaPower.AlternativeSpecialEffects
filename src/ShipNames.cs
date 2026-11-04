using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// The game's ships by their names in the game's language, for the settings window: UnitMetadataProvider.UnitData
    /// ((category, ini name) -> (id, SimpleObjectParameters)), surface ships and submarines; the name is the class name
    /// (SimpleObjectParameters.ClassName - native or NATO, as the game shows it), else the default unit name, else the ini name.
    /// Equal names get the unit name / ini name added. Read once, when first needed (the game has its unit data by then).
    /// </summary>
    static class ShipNames
    {
        static Dictionary<string, string> _byIni;        // ini -> shown name
        static Dictionary<string, string> _byName;       // shown name -> ini
        static List<string> _sorted;
        static List<string> _nations;                    // the ships' nations (Nation= of their ini files), sorted

        static void Load()
        {
            if (_byIni != null && _byIni.Count > 0) return;
            _byIni = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sorted = new List<string>();
            _nations = new List<string>();
            try
            {
                Type tProv = null, tSingleton = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name != "Seapower-Scripts") continue;
                    foreach (var t in a.GetTypes())
                    {
                        if (t.Name == "UnitMetadataProvider") tProv = t;
                        else if (t.Name == "Singleton`1" && t.Namespace == "SeaPower") tSingleton = t;
                    }
                }
                if (tProv == null || tSingleton == null) return;
                var gen = tSingleton.MakeGenericType(tProv);
                var exists = gen.GetMethod("InstanceExists", BindingFlags.Static | BindingFlags.Public);
                if (exists != null && !(bool)exists.Invoke(null, new object[] { false })) return;   // not made yet: no new one
                var inst = gen.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                var data = inst == null ? null : tProv.GetProperty("UnitData").GetValue(inst, null) as IEnumerable;
                if (data == null) return;
                var raw = new List<KeyValuePair<string, string[]>>();   // ini -> class, default
                foreach (var kv in data)
                {
                    var kt = kv.GetType();
                    var key = kt.GetProperty("Key").GetValue(kv, null); var value = kt.GetProperty("Value").GetValue(kv, null);
                    string cat = key.GetType().GetField("Item1").GetValue(key) as string, ini = key.GetType().GetField("Item2").GetValue(key) as string;
                    var sop = value.GetType().GetField("Item2").GetValue(value);
                    if (sop == null || string.IsNullOrEmpty(ini)) continue;
                    // only the ships' own ini files (vessels/<id>.ini): not their extra loadouts ("<id>@<loadout>")
                    if (!string.Equals(cat, "vessels", StringComparison.OrdinalIgnoreCase) || ini.IndexOf('@') >= 0) continue;
                    string tn = sop.GetType().Name;
                    if (tn.IndexOf("Vessel", StringComparison.Ordinal) < 0 && tn.IndexOf("Submarine", StringComparison.Ordinal) < 0) continue;
                    try { var fn = sop.GetType().GetField("_defaultNation"); var nat = fn == null ? null : fn.GetValue(sop) as string; if (!string.IsNullOrEmpty(nat) && !_nations.Contains(nat)) _nations.Add(nat); } catch { }
                    string cls = Prop(sop, "ClassName"), def = Prop(sop, "DefaultName");
                    raw.Add(new KeyValuePair<string, string[]>(ini, new[] { cls, def }));
                }
                // names: the class; the same class twice -> with the unit's name; still the same -> with the ini name
                var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in raw) { string n = First(r.Value[0], r.Value[1], r.Key); int c; count.TryGetValue(n, out c); count[n] = c + 1; }
                foreach (var r in raw)
                {
                    string n = First(r.Value[0], r.Value[1], r.Key);
                    if (count[n] > 1 && !string.IsNullOrEmpty(r.Value[1]) && !string.Equals(r.Value[1], n, StringComparison.OrdinalIgnoreCase)) n = n + " (" + r.Value[1] + ")";
                    if (_byName.ContainsKey(n)) n = n + " [" + r.Key + "]";
                    _byIni[r.Key] = n; _byName[n] = r.Key;
                }
                _sorted = new List<string>(_byName.Keys);
                _sorted.Sort(StringComparer.CurrentCultureIgnoreCase);
                _nations.Sort(StringComparer.CurrentCultureIgnoreCase);
            }
            catch (Exception e) { SettingsWindow.Trace("ShipNames: " + e.Message); }
        }

        static string Prop(object o, string name)
        {
            try { var p = o.GetType().GetProperty(name); return p == null ? null : p.GetValue(o, null) as string; } catch { return null; }
        }

        static string First(params string[] s) { foreach (var x in s) if (!string.IsNullOrEmpty(x)) return x.Trim(); return ""; }

        static MethodInfo _getText; static object _langInst;

        /// <summary>The name of a nation in the game's language: [Nations] of its nations.ini (US = USA, Soviet = USSR ...); the key with spaces when there is none.</summary>
        public static string NationDisplay(string key)
        {
            key = key ?? "";
            try
            {
                if (_getText == null || _langInst == null)
                {
                    var asm = AsePlugin.GameAssembly();
                    var th = asm == null ? null : asm.GetType("SeaPower.LanguageResourceHandler"); var sg = asm == null ? null : asm.GetType("SeaPower.Singleton`1");
                    if (th != null && sg != null)
                    {
                        _langInst = sg.MakeGenericType(th).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                        _getText = th.GetMethod("getText", new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) });
                    }
                }
                if (_getText != null && _langInst != null)
                {
                    var t = _getText.Invoke(_langInst, new object[] { "Nations", key, false, false }) as string;
                    if (!string.IsNullOrEmpty(t) && !string.Equals(t, key, StringComparison.Ordinal)) return t;
                }
            }
            catch { }
            return key.Replace('_', ' ');
        }

        /// <summary>A shown nation (or a key typed in) -> the key.</summary>
        public static string NationToKey(string text)
        {
            Load();
            text = (text ?? "").Trim();
            foreach (var n in _nations) if (string.Equals(NationDisplay(n), text, StringComparison.OrdinalIgnoreCase) || string.Equals(n, text, StringComparison.OrdinalIgnoreCase)) return n;
            return text.Replace(' ', '_');
        }

        /// <summary>Nations containing the text (in the shown name or in the key), shown names, sorted; at most 'max'.</summary>
        public static List<string> NationFilter(string text, int max)
        {
            Load();
            var l = new List<string>(); text = (text ?? "").Trim();
            foreach (var n in _nations)
            {
                string d = NationDisplay(n);
                if (text.Length > 0 && d.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                l.Add(d);
            }
            l.Sort(StringComparer.CurrentCultureIgnoreCase);
            if (l.Count > max) l.RemoveRange(max, l.Count - max);
            return l;
        }

        /// <summary>The name shown for an ini name (the ini name itself when the game does not know it).</summary>
        public static string Display(string ini)
        {
            Load();
            string n;
            return ini != null && _byIni.TryGetValue(ini, out n) ? n : ini;
        }

        /// <summary>A shown name (or an ini name typed in) -> the ini name.</summary>
        public static string ToIni(string text)
        {
            Load();
            if (string.IsNullOrEmpty(text)) return "";
            string ini;
            return _byName.TryGetValue(text, out ini) ? ini : text;
        }

        /// <summary>Names containing the text (in the name or in the ini name), sorted; at most 'max'.</summary>
        public static List<string> Filter(string text, int max)
        {
            Load();
            var list = new List<string>();
            text = (text ?? "").Trim();
            foreach (var n in _sorted)
            {
                if (text.Length > 0 && n.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 && _byName[n].IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(n);
                if (list.Count >= max) break;
            }
            return list;
        }
    }
}

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// The per-ship funnel smoke multipliers: [FunnelSmoke] Ships = "ini=particles,duration,lightness; ini=...", multipliers of the group's values.
    /// </summary>
    static class ShipFunnel
    {
        public sealed class Item { public string Ini; public float Particles = 1f, Duration = 1f, Lightness = 1f, Thickness = 1f; }

        public static List<Item> Parse(string text)
        {
            var l = new List<Item>();
            foreach (var part in (text ?? "").Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                string ini = (eq < 0 ? part : part.Substring(0, eq)).Trim();
                if (ini.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) ini = ini.Substring(0, ini.Length - 4);
                if (ini.Length == 0 || l.Exists(x => string.Equals(x.Ini, ini, StringComparison.OrdinalIgnoreCase))) continue;
                var it = new Item { Ini = ini };
                if (eq >= 0)
                {
                    var v = part.Substring(eq + 1).Split(',');
                    float f;
                    if (v.Length > 0 && Num(v[0], out f)) it.Particles = Math.Max(0f, Math.Min(10f, f));
                    if (v.Length > 1 && Num(v[1], out f)) it.Duration = Math.Max(0.01f, Math.Min(10f, f));
                    if (v.Length > 2 && Num(v[2], out f)) it.Lightness = Math.Max(0f, Math.Min(10f, f));
                    if (v.Length > 3 && Num(v[3], out f)) it.Thickness = Math.Max(0f, Math.Min(10f, f));
                }
                l.Add(it);
            }
            return l;
        }

        static bool Num(string s, out float v)
        {
            return float.TryParse((s ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);
        }

        public static string Write(List<Item> l)
        {
            var parts = new List<string>();
            foreach (var it in l)
                parts.Add(it.Ini + "=" + F(it.Particles) + "," + F(it.Duration) + "," + F(it.Lightness) + "," + F(it.Thickness));
            return string.Join("; ", parts.ToArray());
        }

        static string F(float v) { return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
    }
}
