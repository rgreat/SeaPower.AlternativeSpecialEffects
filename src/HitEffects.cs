using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// [Effects] GunHitEffects: the hit effects of gun shells by their size. Every projectile ammunition gets one of 10 classes (tiers) by its
    /// mass (the ini key [General] Mass, kg; the upper mass of the classes 1..9 is a setting). A tier sets, for the five hit
    /// effects of the ammunition (hit on a ship / in the air / on the water / on the ground and the default one), a collection of the effects and a scale:
    ///  - ship: the mod's own collections ASE_Ship_1..9 (variants of the game's prefabs, one is picked at random at each hit; registered in the
    ///    EffectsManager after each EffectsLoader.load, because the manager is built again for each mission);
    ///  - air / water / ground: the game's own classes of effects.ini;
    ///  - scale: Effect._scale (the game puts it into localScale of the spawned effect); settings in the window, per tier and per effect.
    /// The values go into the Effect objects of AmmunitionParameters in place (postfix of the constructor); the game's own values are kept and come back when the switch goes off.
    /// Not touched: ammunition that is not a projectile, without mass, star shells (the kept effects are star shell ones).
    /// </summary>
    static class HitEffects
    {
        public static Func<bool> Want = () => false;
        public static Action<string> Trace = s => { };
        /// <summary>Upper mass (kg) of the tier 0..8 (the last tier has none).</summary>
        public static Func<int, float> UpToKg = DefaultUp;
        /// <summary>Scale of the tier (0..9) for the effect (0 ship, 1 air, 2 water, 3 ground).</summary>
        public static Func<int, int, float> Scale = DefaultScale;

        public const int Tiers = 10;
        public static readonly string[] Slots = { "Ship", "Air", "Water", "Ground" };

        // ------------------------------------------------------------ the tiers
        static readonly float[] UpKg = { 0.4f, 5, 8, 16, 28, 42, 110, 400, 1200 };
        static readonly float[][] Scales =
        {
            //          ship air  water ground
            new[] { 1f, 0.7f, 1f, 1f },
            new[] { 1f, 1f, 0.8f, 0.8f },
            new[] { 1f, 1.3f, 1f, 0.8f },
            new[] { 1f, 0.8f, 1f, 1f },
            new[] { 1f, 1f, 1.2f, 1.3f },
            new[] { 1f, 1.3f, 1.5f, 1f },
            new[] { 1.3f, 1.6f, 0.6f, 1.3f },
            new[] { 0.9f, 2f, 0.8f, 1f },
            new[] { 1.2f, 2.5f, 1f, 1f },
            new[] { 1.5f, 2.5f, 1.2f, 1f },
        };
        static readonly string[] Air = { "SmallFlakExplosions", "SmallFlakExplosions", "SmallFlakExplosions", "MediumFlakExplosions", "MediumFlakExplosions", "MediumFlakExplosions", "MediumFlakExplosions", "MediumFlakExplosions", "MediumFlakExplosions", "MediumFlakExplosions" };
        static readonly string[] Water = { "TinyWaterSplashes", "SmallWaterSplashes", "SmallWaterSplashes", "SmallWaterSplashes", "SmallWaterSplashes", "SmallWaterSplashes", "LargeWaterSplashes", "LargeWaterSplashes", "LargeWaterSplashes", "LargeWaterSplashes" };
        // null = the game's own value stays (no effect for the smallest shells, as in the game)
        static readonly string[] Ground = { null, "SmallFlakExplosions", "MediumFlakExplosions", "SmallGroundHitExplosions", "SmallGroundHitExplosions", "MediumGroundHitExplosions", "MediumGroundHitExplosions", "LargeGroundHitExplosions", "LargeGroundHitExplosions", "VeryLargeGroundHitExplosions" };

        const string P = "effects/Particle Emitters/", X = "effects/explosions/emitters/";
        /// <summary>The collections ASE_Ship_1..9: the variants (prefab paths) of the hit on a ship.</summary>
        static readonly string[][] ShipVariants =
        {
            new[] { P + "impact_small_shell_multi" },
            new[] { P + "impact_small_shell" },
            new[] { P + "impact_medium_shell" },
            new[] { P + "impact_large_shell", X + "missile_hit_ship_small" },
            new[] { X + "missile_hit_ship_small", X + "missile_hit_ship_small_2", X + "missile_hit_ship_small_3", P + "impact_large_shell" },
            new[] { X + "missile_hit_ship_small_3", X + "missile_hit_ship_medium", X + "missile_hit_ship_medium_2" },
            new[] { X + "missile_hit_ship_medium", X + "missile_hit_ship_medium_2", X + "missile_hit_ship_3" },
            new[] { X + "missile_hit_ship_medium_2", X + "missile_hit_ship_3", X + "missile_hit_ship_large" },
            new[] { X + "missile_hit_ship_large", X + "missile_hit_ship_large_2", X + "missile_hit_ship_large_3", X + "missile_hit_ship_large_4" },
        };
        static string ShipClass(int tier) { return "ASE_Ship_" + (Math.Min(tier, 8) + 1); }

        public static float DefaultUp(int tier) { return UpKg[tier]; }
        public static float DefaultScale(int tier, int slot) { return Scales[tier][slot]; }

        static readonly string[] Names = { "HitDefaultExplosion", "HitAirExplosion", "HitShipExplosion", "HitGroundExplosion", "HitWaterSplash" };
        static readonly string[] Fields = { "_hitDefaultExplosionEffect", "_hitAirExplosionEffect", "_hitShipExplosionEffect", "_hitGroundExplosionEffect", "_hitWaterSplashEffect" };
        // the effect slot -> the slot of the tier settings (0 ship, 1 air, 2 water, 3 ground)
        static readonly int[] SlotOf = { 3, 1, 0, 3, 2 };

        sealed class Kept { public object Effect; public string Class; public object Single; public bool Available, Ignored; public Vector3 Scale; }
        sealed class State { public Kept[] Kept = new Kept[Names.Length]; public bool Applied, Skip; public float Kg; }

        static readonly ConditionalWeakTable<object, State> States = new ConditionalWeakTable<object, State>();
        static readonly List<WeakReference> Live = new List<WeakReference>();
        static FieldInfo _fName, _fType, _fMass; static FieldInfo[] _fEffects = new FieldInfo[Names.Length];
        static FieldInfo _eClass, _eSingle, _eAvail, _eIgnored, _eScale;
        static MethodInfo _loadPrefab, _addEffect, _addClass; static PropertyInfo _loaderInstance, _managerInstance; static Type _tLoader;
        static bool _ok;

        public static bool Init(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var tAp = asm.GetType("SeaPower.AmmunitionParameters");
                var tEf = asm.GetType("SeaPower.Effect");
                var tMgr = asm.GetType("SeaPower.EffectsManager");
                var tLoad = asm.GetType("SeaPower.EffectsLoader");
                _tLoader = asm.GetType("SeaPower.ResourceLoader");
                if (tAp == null || tEf == null || _tLoader == null || tMgr == null || tLoad == null) { Trace("HitEffects: types not found"); return false; }
                _fName = tAp.GetField("_ammunitionFileName", all); _fType = tAp.GetField("_type", all); _fMass = tAp.GetField("_massInKg", all);
                for (int i = 0; i < Fields.Length; i++) _fEffects[i] = tAp.GetField(Fields[i], all);
                _eClass = tEf.GetField("_effectClassName", all); _eSingle = tEf.GetField("_singleEffect", all);
                _eAvail = tEf.GetField("_isAvailable", all); _eIgnored = tEf.GetField("_isIgnored", all); _eScale = tEf.GetField("_scale", all);
                _loadPrefab = _tLoader.GetMethod("getGameObjectResource", all, null, new[] { typeof(string), typeof(bool) }, null);
                _addEffect = tMgr.GetMethod("addEffect", all); _addClass = tMgr.GetMethod("addEffectClass", all);
                var singleton = asm.GetType("SeaPower.Singleton`1");
                const BindingFlags inst = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
                _loaderInstance = singleton == null ? null : singleton.MakeGenericType(_tLoader).GetProperty("Instance", inst);
                _managerInstance = singleton == null ? null : singleton.MakeGenericType(tMgr).GetProperty("Instance", inst);
                var ctor = tAp.GetConstructor(all, null, new[] { typeof(string), typeof(int), asm.GetType("SeaPower.WeaponSystem") }, null);
                var load = tLoad.GetMethod("load", all);
                _ok = _fName != null && _fType != null && _fMass != null && _eClass != null && _eSingle != null && _eAvail != null && _eIgnored != null && _eScale != null
                      && _loadPrefab != null && _addEffect != null && _addClass != null && _loaderInstance != null && _managerInstance != null
                      && ctor != null && load != null && Array.TrueForAll(_fEffects, f => f != null);
                if (!_ok) { Trace("HitEffects: game members not found"); return false; }
                harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(HitEffects).GetMethod(nameof(CtorPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                harmony.Patch(load, postfix: new HarmonyMethod(typeof(HitEffects).GetMethod(nameof(LoadPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                RegisterClasses();   // the effects of a mission that is already loaded
                return true;
            }
            catch (Exception e) { Trace("HitEffects.Init: " + e.Message); return false; }
        }

        // ------------------------------------------------------------ own collections

        static void LoadPostfix() { RegisterClasses(); }

        /// <summary>Puts ASE_Ship_n into the EffectsManager of the mission (it is built again for each mission; a name that is there stays as it is).</summary>
        static void RegisterClasses()
        {
            if (!_ok) return;
            try
            {
                var mgr = _managerInstance.GetValue(null, null);
                var loader = _loaderInstance.GetValue(null, null);
                if (mgr == null || loader == null) return;
                for (int t = 0; t < ShipVariants.Length; t++)
                {
                    var names = new List<string>();
                    foreach (var path in ShipVariants[t])
                    {
                        var go = _loadPrefab.Invoke(loader, new object[] { path, true }) as UnityEngine.Object;
                        if (go == null) { Trace("HitEffects: prefab '" + path + "' not found"); continue; }
                        _addEffect.Invoke(mgr, new object[] { path, go });
                        names.Add(path);
                    }
                    if (names.Count > 0) _addClass.Invoke(mgr, new object[] { "ASE_Ship_" + (t + 1), names });
                }
            }
            catch (Exception e) { Trace("HitEffects.RegisterClasses: " + e.Message); }
        }

        // ------------------------------------------------------------ the ammunition

        static void CtorPostfix(object __instance)
        {
            if (!_ok || __instance == null) return;
            try
            {
                string name = _fName.GetValue(__instance) as string;
                if (name == null || _fType.GetValue(__instance).ToString() != "Projectile") return;
                float kg = Convert.ToSingle(_fMass.GetValue(__instance), CultureInfo.InvariantCulture);
                if (kg <= 0f) return;
                var st = new State { Kg = kg };
                for (int i = 0; i < Names.Length; i++)
                {
                    var e = _fEffects[i].GetValue(__instance);
                    if (e == null) continue;
                    var k = new Kept { Effect = e, Class = _eClass.GetValue(e) as string, Single = _eSingle.GetValue(e), Available = (bool)_eAvail.GetValue(e), Ignored = (bool)_eIgnored.GetValue(e), Scale = (Vector3)_eScale.GetValue(e) };
                    st.Kept[i] = k;
                    var go = k.Single as UnityEngine.Object;
                    if ((k.Class ?? "").IndexOf("starshell", StringComparison.OrdinalIgnoreCase) >= 0 || (go != null && go.name.IndexOf("starshell", StringComparison.OrdinalIgnoreCase) >= 0)) st.Skip = true;
                }
                if (st.Skip) return;
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

        /// <summary>The tier by the mass: the first one whose upper mass is not below it.</summary>
        static int TierOf(State st)
        {
            int n = UpKg.Length;
            for (int t = 0; t < n; t++) if (st.Kg <= UpToKg(t)) return t;
            return n;
        }

        /// <summary>The switch or a setting changed (the window, the .cfg, the Mods menu tick): the values go into every noted object again, or the game's own ones come back.</summary>
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
                        if (o != null && States.TryGetValue(o, out st) && (want || st.Applied)) Apply(st, want);
                    }
            }
            catch (Exception e) { Trace("HitEffects.Sync: " + e.Message); }
        }

        static void Apply(State st, bool on)
        {
            int tier = TierOf(st);
            for (int i = 0; i < Names.Length; i++)
            {
                var k = st.Kept[i]; if (k == null) continue;
                if (!on)
                {
                    _eClass.SetValue(k.Effect, k.Class); _eSingle.SetValue(k.Effect, k.Single); _eScale.SetValue(k.Effect, k.Scale);
                    _eAvail.SetValue(k.Effect, k.Available); _eIgnored.SetValue(k.Effect, k.Ignored);
                    continue;
                }
                int slot = SlotOf[i];
                string cls = slot == 0 ? ShipClass(tier) : slot == 1 ? Air[tier] : slot == 2 ? Water[tier] : Ground[tier];
                if (cls == null) continue;   // no effect of this size: the game's own value
                float s = Math.Max(0.05f, Scale(tier, slot));
                _eIgnored.SetValue(k.Effect, false); _eSingle.SetValue(k.Effect, null); _eClass.SetValue(k.Effect, cls);
                _eScale.SetValue(k.Effect, new Vector3(s, s, s)); _eAvail.SetValue(k.Effect, true);
            }
            st.Applied = on;
        }
    }
}
