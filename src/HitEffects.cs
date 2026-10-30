using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// [Effects] GunHitEffects: the hit effects of gun ammunition (Release/hit_effects.txt, from the rgreat cumulative pack) are put into the
    /// ammunition parameters at run time, so the switch works at once, in a mission too.
    ///  - Postfix of the AmmunitionParameters constructor (it has read the ammunition ini by then): the object is noted; with the switch on
    ///    the file's values go into its Effect objects (HitShipExplosion, HitAirExplosion, HitWaterSplash, HitGroundExplosion, HitDefaultExplosion),
    ///    the game's own values of those Effects are kept in memory (taken after the game loaded the ini, so any other mod's data layer is in them).
    ///  - A change of the switch (the window, the .cfg, the Mods menu tick) puts the file's values in, or the kept ones back, in every noted object.
    /// The Effect objects are changed in place: the game shares some of them (the water entry splash is the hit splash). The values mean what
    /// the same keys mean in the ini: Name = a prefab (empty = none, "none" = ignored), NameClass = an effect class of effects.ini.
    /// Save / load: the state is in the objects of the scene (weak references): a loaded game builds new AmmunitionParameters, the constructor hook handles them.
    /// </summary>
    static class HitEffects
    {
        public static Func<bool> Want = () => false;
        public static Action<string> Trace = s => { };

        static readonly string[] Names = { "HitDefaultExplosion", "HitAirExplosion", "HitShipExplosion", "HitGroundExplosion", "HitWaterSplash" };
        static readonly string[] Fields = { "_hitDefaultExplosionEffect", "_hitAirExplosionEffect", "_hitShipExplosionEffect", "_hitGroundExplosionEffect", "_hitWaterSplashEffect" };

        sealed class Kept { public object Effect; public string Class; public object Single; public bool Available, Ignored; }
        sealed class State { public Kept[] Kept = new Kept[Names.Length]; public bool Applied; public Dictionary<string, string> Data; }

        static Dictionary<string, Dictionary<string, string>> _data;
        static readonly ConditionalWeakTable<object, State> States = new ConditionalWeakTable<object, State>();
        static readonly List<WeakReference> Live = new List<WeakReference>();
        static FieldInfo _fName; static FieldInfo[] _fEffects = new FieldInfo[Names.Length];
        static FieldInfo _eClass, _eSingle, _eAvail, _eIgnored;
        static MethodInfo _loadPrefab; static PropertyInfo _loaderInstance; static Type _tLoader;
        static bool _ok;

        public static bool Init(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var tAp = asm.GetType("SeaPower.AmmunitionParameters");
                var tEf = asm.GetType("SeaPower.Effect");
                _tLoader = asm.GetType("SeaPower.ResourceLoader");
                if (tAp == null || tEf == null || _tLoader == null) { Trace("HitEffects: types not found"); return false; }
                _fName = tAp.GetField("_ammunitionFileName", all);
                for (int i = 0; i < Fields.Length; i++) _fEffects[i] = tAp.GetField(Fields[i], all);
                _eClass = tEf.GetField("_effectClassName", all); _eSingle = tEf.GetField("_singleEffect", all);
                _eAvail = tEf.GetField("_isAvailable", all); _eIgnored = tEf.GetField("_isIgnored", all);
                _loadPrefab = _tLoader.GetMethod("getGameObjectResource", all, null, new[] { typeof(string), typeof(bool) }, null);
                var singleton = asm.GetType("SeaPower.Singleton`1");
                _loaderInstance = singleton == null ? null : singleton.MakeGenericType(_tLoader).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                var ctor = tAp.GetConstructor(all, null, new[] { typeof(string), typeof(int), asm.GetType("SeaPower.WeaponSystem") }, null);
                _ok = _fName != null && _eClass != null && _eSingle != null && _eAvail != null && _eIgnored != null && _loadPrefab != null && _loaderInstance != null && ctor != null
                      && Array.TrueForAll(_fEffects, f => f != null);
                if (!_ok) { Trace("HitEffects: game members not found"); return false; }
                harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(HitEffects).GetMethod(nameof(CtorPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                return true;
            }
            catch (Exception e) { Trace("HitEffects.Init: " + e.Message); return false; }
        }

        static void CtorPostfix(object __instance)
        {
            if (!_ok || __instance == null) return;
            try
            {
                var data = Data();
                string name = _fName.GetValue(__instance) as string;
                Dictionary<string, string> mine;
                if (name == null || !data.TryGetValue(name, out mine)) return;
                var st = new State { Data = mine };
                for (int i = 0; i < Names.Length; i++)
                {
                    var e = _fEffects[i].GetValue(__instance);
                    if (e == null) continue;
                    st.Kept[i] = new Kept { Effect = e, Class = _eClass.GetValue(e) as string, Single = _eSingle.GetValue(e), Available = (bool)_eAvail.GetValue(e), Ignored = (bool)_eIgnored.GetValue(e) };
                }
                lock (Live)
                {
                    States.Remove(__instance); States.Add(__instance, st);
                    Live.RemoveAll(w => !w.IsAlive);
                    Live.Add(new WeakReference(__instance));
                }
                if (Want()) Apply(st, true);
            }
            catch (Exception e) { Trace("HitEffects ctor: " + e.Message); }
        }

        /// <summary>The switch changed (or the mod got ticked / unticked): the file's values or the game's own ones in every noted object.</summary>
        public static void Sync()
        {
            if (!_ok) return;
            bool want = Want();
            try
            {
                lock (Live)
                    foreach (var w in Live)
                    {
                        var o = w.Target; State st;
                        if (o != null && States.TryGetValue(o, out st) && st.Applied != want) Apply(st, want);
                    }
            }
            catch (Exception e) { Trace("HitEffects.Sync: " + e.Message); }
        }

        static void Apply(State st, bool on)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                var k = st.Kept[i]; if (k == null) continue;
                string single, cls;
                bool hasSingle = st.Data.TryGetValue(Names[i], out single), hasClass = st.Data.TryGetValue(Names[i] + "Class", out cls);
                if (!hasSingle && !hasClass) continue;
                if (!on)
                {
                    _eClass.SetValue(k.Effect, k.Class); _eSingle.SetValue(k.Effect, k.Single);
                    _eAvail.SetValue(k.Effect, k.Available); _eIgnored.SetValue(k.Effect, k.Ignored);
                    continue;
                }
                string text = hasSingle ? single : null;
                bool ground = Names[i] == "HitGroundExplosion";
                if (text != null && (text.ToLowerInvariant() == "none" || text.ToLowerInvariant() == "false"))
                {
                    _eIgnored.SetValue(k.Effect, true); _eSingle.SetValue(k.Effect, null); _eClass.SetValue(k.Effect, null); _eAvail.SetValue(k.Effect, false);
                    continue;
                }
                object prefab = k.Single;
                if (hasSingle) prefab = string.IsNullOrEmpty(text) ? null : Prefab(text);
                else if (ground) prefab = null;   // a new HitGroundExplosion in the ini: its own text (none here), not the default explosion's
                string klass = hasClass ? cls : (k.Class ?? "");
                _eIgnored.SetValue(k.Effect, false); _eSingle.SetValue(k.Effect, prefab); _eClass.SetValue(k.Effect, klass);
                _eAvail.SetValue(k.Effect, prefab != null || !string.IsNullOrEmpty(klass));
            }
            st.Applied = on;
        }

        static object Prefab(string path)
        {
            try
            {
                var loader = _loaderInstance.GetValue(null, null);
                return loader == null ? null : _loadPrefab.Invoke(loader, new object[] { path, true });
            }
            catch (Exception e) { Trace("HitEffects prefab '" + path + "': " + e.Message); return null; }
        }

        // ------------------------------------------------------------ the data file

        static Dictionary<string, Dictionary<string, string>> Data()
        {
            if (_data != null) return _data;
            var d = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string dir = Path.GetDirectoryName(typeof(HitEffects).Assembly.Location) ?? "";
                string path = Path.Combine(dir, "hit_effects.txt");
                if (File.Exists(path))
                {
                    Dictionary<string, string> cur = null;
                    foreach (var raw in File.ReadAllLines(path))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                        if (line[0] == '[' && line[line.Length - 1] == ']') { cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); d[line.Substring(1, line.Length - 2).Trim()] = cur; continue; }
                        int eq = line.IndexOf('=');
                        if (cur == null || eq <= 0) continue;
                        cur[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
                else Trace("HitEffects: " + path + " not found");
            }
            catch (Exception e) { Trace("HitEffects data: " + e.Message); }
            _data = d;
            return d;
        }
    }
}
