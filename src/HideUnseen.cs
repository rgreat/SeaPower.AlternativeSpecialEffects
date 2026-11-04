using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// Option [UI] HideUnobservedTargets3D: a contact that is not observed visually (known by radar / sonar / data link only) is not drawn in the 3D view.
    /// Own units are never touched. When the option is turned off, everything hidden is shown again.
    ///
    /// The source of "observed visually" is the game's visual sensors themselves. Each SensorSystemVisual of the player's side rebuilds its list of detected
    /// objects (SensorSystem._detectedObjects) in one go inside runVisualScan and keeps it until its next scan, so after a scan the lists are consistent.
    /// An object is seen when any of those lists holds it. (Vehicle.IsObservedByVisual / DetectingSensors were not used: the game resets them at the start of
    /// every update of the contact record and its track-age bookkeeping leaves the Visual bit off for a moment once per game second - the picture blinked.)
    ///  - a visual sensor of the player's side has finished a scan (postfix of runVisualScan): its list is stored, the objects it gained or lost are re-judged;
    ///  - a contact record of the player's side has been updated (postfix of Vehicle.UpdateFromECS): that is how a contact becomes known to the mod.
    /// The renderers are switched off with Renderer.forceRenderingOff, not Renderer.enabled, which the game itself switches (optical view, rafts, lights).
    /// </summary>
    static class HideUnseen
    {
        public static Func<bool> IsEnabled = () => false;
        public static Func<bool> HideSubs = () => false;      // [UI] HideUnobservedSubmarines3D
        public static Action<string> Trace = s => { };

        static Func<object, object> _vObject, _vSide, _sDetected, _sHost, _oTaskforce, _gPlayerTf;
        static PropertyInfo _oPlayer;
        static PropertyInfo _optInstance, _optHide; static Func<object, object> _gEnemyCtl; static PropertyInfo _rpValue;   // the F10 menu's cheat switches
        static bool _ok;
        static int _scanLogged;
        static Func<object, object> _oType;
        static FieldInfo _trailDuration;
        static Func<object, object> _oObp, _pTowed, _tAttached, _tCable;
        static MethodInfo _cCableObject;
        static MethodInfo _rpGetSelected, _rpDeselect, _rpSwitch; static WeakReference _pending; static PropertyInfo _rpInstance;
        sealed class Hidden
        {
            public Renderer[] R; public bool On = true; public int Flips;
            public List<ParticleSystem> Emitters;                 // particle systems whose emission was switched off
            public List<Behaviour> Overlays;                      // Kelvin wave overlays switched off
            public List<KeyValuePair<Behaviour, float>> Trails;   // foam trails and the durations they had
        }
        sealed class Scan { public WeakReference Sensor; public HashSet<object> Seen = new HashSet<object>(); }
        static readonly ConditionalWeakTable<object, Hidden> Table = new ConditionalWeakTable<object, Hidden>();
        static readonly ConditionalWeakTable<object, Scan> ScanOf = new ConditionalWeakTable<object, Scan>();
        static readonly List<Scan> Scans = new List<Scan>();
        static readonly List<WeakReference> Hid = new List<WeakReference>();

        public static bool Init(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            try
            {
                var tV = asm.GetType("SeaPower.Vehicle");
                _vObject = Acc(tV, "Object");
                _vSide = Acc(tV, "ReportingSide");
                var tObj = asm.GetType("SeaPower.ObjectBase"); var tGlobals = asm.GetType("SeaPower.Globals");
                _oTaskforce = Acc(tObj, "_taskforce");
                _oPlayer = tObj == null ? null : tObj.GetProperty("IsPlayerObject", all);
                _gPlayerTf = Acc(tGlobals, "_playerTaskforce");
                try
                {
                    var tOpt = asm.GetType("SeaPower.OptionsManager"); var tSing = asm.GetType("SeaPower.Singleton`1");
                    _optInstance = tSing.MakeGenericType(tOpt).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                    _optHide = tOpt.GetProperty("HideUnidentifiedIn3D", all);
                    _gEnemyCtl = Acc(tGlobals, "AllowControlOfEnemyUnits");
                }
                catch (Exception e) { Trace("HideUnseen cheat switches: " + e.Message); }
                var tSensor = asm.GetType("SeaPower.SensorSystem"); var tVisual = asm.GetType("SeaPower.SensorSystemVisual");
                _sHost = Acc(tVisual, "Host") ?? Acc(tVisual, "_baseObject");
                _sDetected = Acc(tSensor, "_detectedObjects");
                var scan = tVisual == null ? null : tVisual.GetMethod("runVisualScan", all, null, Type.EmptyTypes, null);
                var update = tV.GetMethod("UpdateFromECS", all, null, Type.EmptyTypes, null);
                if (_vObject == null || _vSide == null || _oTaskforce == null || _gPlayerTf == null || _sHost == null || _sDetected == null || scan == null || update == null)
                {
                    Trace("HideUnseen: members not found:" + (_vObject == null ? " Vehicle.Object" : "") + (_vSide == null ? " Vehicle.ReportingSide" : "") + (_oTaskforce == null ? " ObjectBase._taskforce" : "") +
                          (_gPlayerTf == null ? " Globals._playerTaskforce" : "") + (_sHost == null ? " SensorSystemVisual.Host" : "") + (_sDetected == null ? " SensorSystem._detectedObjects" : "") +
                          (scan == null ? " runVisualScan" : "") + (update == null ? " Vehicle.UpdateFromECS" : ""));
                    return false;
                }
                harmony.Patch(scan, postfix: new HarmonyMethod(typeof(HideUnseen).GetMethod(nameof(ScanPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                harmony.Patch(update, postfix: new HarmonyMethod(typeof(HideUnseen).GetMethod(nameof(UpdatePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                _ok = true;
                try
                {
                    var tObp = asm.GetType("SeaPower.ObjectBaseParameters"); var tTow = asm.GetType("SeaPower.TowedSystem"); var tCab = asm.GetType("SeaPower.Cable");
                    _oType = Acc(tObj, "_type"); _oObp = Acc(tObj, "_obp"); _pTowed = Acc(tObp, "_towedSystems"); _tAttached = Acc(tTow, "_attachedObject"); _tCable = Acc(tTow, "_cable");
                    _cCableObject = tCab == null ? null : tCab.GetMethod("getCableObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (_oObp == null || _pTowed == null || _tAttached == null || _tCable == null || _cCableObject == null) { Trace("HideUnseen: towed system members not found, the sonar cable stays drawn"); _oObp = null; }
                }
                catch (Exception e) { Trace("HideUnseen towed: " + e.Message); _oObp = null; }
                try { foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { var tt = a.GetType("Ceto.AddFoamTrail", false); if (tt != null) { _trailDuration = tt.GetField("duration", BindingFlags.Instance | BindingFlags.Public); break; } } if (_trailDuration == null) Trace("HideUnseen: Ceto.AddFoamTrail.duration not found, foam trails stay"); }
                catch (Exception e) { Trace("HideUnseen foam: " + e.Message); }
                try { InitCamera(harmony, asm); } catch (Exception e) { Trace("HideUnseen camera: " + e.Message); }
                return true;
            }
            catch (Exception e) { Trace("HideUnseen.Init: " + e.Message); return false; }
        }

        /// <summary>A reader of a field or property by name, searching the type and its base classes (the Release and Beta builds of the game differ in what is a field, a property or where it is declared). Static members take null.</summary>
        static Func<object, object> Acc(Type t, string name)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var c = t; c != null; c = c.BaseType)
            {
                var fi = c.GetField(name, f);
                if (fi != null) return o => fi.GetValue(fi.IsStatic ? null : o);
                var pi = c.GetProperty(name, f);
                if (pi != null && pi.GetGetMethod(true) != null) return o => pi.GetValue(pi.GetGetMethod(true).IsStatic ? null : o, null);
            }
            return null;
        }

        static object PlayerSide() { return _gPlayerTf(null); }

        /// <summary>A visual sensor has finished a scan: keep its list, re-judge what it gained or lost.</summary>
        static void ScanPostfix(object __instance)
        {
            if (!_ok || !IsEnabled()) return;
            try
            {
                var owner = _sHost(__instance);
                if (owner == null || !ReferenceEquals(_oTaskforce(owner), PlayerSide())) return;   // the player's side only
                Scan s;
                if (!ScanOf.TryGetValue(__instance, out s)) { s = new Scan { Sensor = new WeakReference(__instance) }; ScanOf.Add(__instance, s); Scans.Add(s); }
                var now = new HashSet<object>();
                var list = _sDetected(__instance) as System.Collections.IEnumerable;
                if (list != null) foreach (var o in list) if (o != null) now.Add(o);
                var before = s.Seen; s.Seen = now;
                if (_scanLogged < 5) { _scanLogged++; Trace("HideUnseen: a visual scan of " + ((owner as UnityEngine.Object) == null ? "?" : ((UnityEngine.Object)owner).name) + ": " + now.Count + " object(s) detected, " + Scans.Count + " sensor(s) of the player's side"); }
                foreach (var o in before) if (!now.Contains(o)) Judge(o);
                foreach (var o in now) if (!before.Contains(o)) Judge(o);
            }
            catch (Exception e) { Trace("HideUnseen scan: " + e.Message); }
        }

        /// <summary>The record the player's side keeps of a contact has been updated: the contact is known to the mod.</summary>
        static void UpdatePostfix(object __instance)
        {
            if (!_ok || !IsEnabled()) return;
            try
            {
                if (!ReferenceEquals(_vSide(__instance), PlayerSide())) return;   // another side's record of the object
                var obj = _vObject(__instance) as Component;
                if (obj == null) return;
                Hidden h;
                if (!Table.TryGetValue(obj, out h))
                {
                    if (Mine(obj)) return;       // own units are always drawn
                    h = new Hidden();
                    Table.Add(obj, h);
                    Trace("HideUnseen: contact " + obj.name + " known, seen " + Seen(obj));
                }
                Apply(obj, h, Visible(obj));
            }
            catch (Exception e) { Trace("HideUnseen: " + e.Message); }
        }

        /// <summary>
        /// The cheat switches of the F10 menu win over this option: "Show Unidentified" (the game's own HideUnidentifiedIn3D is off) or "Control Enemy Units".
        /// Every contact is then drawn; it is judged again at its next update, so nothing is polled.
        /// </summary>
        static bool Cheating()
        {
            try
            {
                var om = _optInstance == null ? null : _optInstance.GetValue(null, null);
                if (om != null && _optHide != null && !(bool)_optHide.GetValue(om, null)) return true;
                var rp = _gEnemyCtl == null ? null : _gEnemyCtl(null);
                if (rp != null)
                {
                    if (_rpValue == null) _rpValue = rp.GetType().GetProperty("Value");
                    if (_rpValue != null && (bool)_rpValue.GetValue(rp, null)) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Drawn: seen by a visual sensor, or a submarine while submarines are not hidden.</summary>
        static bool Visible(Component obj)
        {
            if (Cheating()) return true;
            if (Seen(obj)) return true;
            return !HideSubs() && IsSubmarine(obj);
        }

        static bool IsSubmarine(object obj)
        {
            try { var t = _oType == null ? null : _oType(obj); return t != null && t.ToString() == "Submarine"; }
            catch { return false; }
        }

        static void Judge(object o)
        {
            var obj = o as Component; Hidden h;
            if (obj == null || !Table.TryGetValue(obj, out h)) return;   // not a contact (yet)
            Apply(obj, h, Visible(obj));
        }

        /// <summary>Any live visual sensor of the player's side holds the object in its list.</summary>
        static bool Seen(object obj)
        {
            for (int i = Scans.Count - 1; i >= 0; i--)
            {
                var sensor = Scans[i].Sensor.Target;
                var owner = sensor == null ? null : _sHost(sensor) as UnityEngine.Object;
                if (sensor == null || owner == null) { Scans.RemoveAt(i); continue; }     // the sensor's unit is gone
                if (Scans[i].Seen.Contains(obj)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ the camera: no centring on, no following of a hidden contact

        static void InitCamera(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var tRp = asm.GetType("SeaPower.RenderPosition"); var tObj = asm.GetType("SeaPower.ObjectBase"); var tSingle = asm.GetType("SeaPower.Singleton`1");
            if (tRp == null || tObj == null || tSingle == null) { Trace("HideUnseen: RenderPosition not found, the camera may still centre on a hidden contact"); return; }
            var sw = tRp.GetMethod("switchToObject", all, null, new[] { tObj, typeof(bool), typeof(bool), typeof(bool) }, null);
            _rpGetSelected = tRp.GetMethod("getSelectedObject", all, null, Type.EmptyTypes, null);
            _rpDeselect = tRp.GetMethod("deselectObjectAndDetachCamera", all, null, Type.EmptyTypes, null);
            _rpInstance = tSingle.MakeGenericType(tRp).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            _rpSwitch = sw;
            if (sw == null || _rpGetSelected == null || _rpDeselect == null || _rpInstance == null) { Trace("HideUnseen: camera members not found, the camera may still centre on a hidden contact"); return; }
            harmony.Patch(sw, prefix: new HarmonyMethod(typeof(HideUnseen).GetMethod(nameof(SwitchPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
        }

        /// <summary>The camera is asked to centre on / follow a unit: not a contact that is hidden from the 3D view.</summary>
        static bool SwitchPrefix(object objectToAttach)
        {
            try
            {
                var c = objectToAttach as Component; Hidden h;
                if (_ok && c != null && IsEnabled() && Table.TryGetValue(c, out h) && !h.On) { _pending = new WeakReference(c); Trace("HideUnseen: the camera is not sent to " + c.name + " (hidden)"); return false; }
                _pending = null;                                      // the camera goes elsewhere: nothing to resume
            }
            catch (Exception e) { Trace("HideUnseen switch: " + e.Message); }
            return true;
        }

        /// <summary>A contact that the camera was kept from (selected while hidden, or followed when it got hidden) is visible again: the camera goes to it.</summary>
        static void ResumeCamera(Component obj)
        {
            var p = _pending; if (p == null || !ReferenceEquals(p.Target, obj) || _rpSwitch == null || _rpInstance == null) return;
            _pending = null;
            var rp = _rpInstance.GetValue(null, null); if (rp == null) return;
            _rpSwitch.Invoke(rp, new object[] { obj, false, false, true });
            Trace("HideUnseen: the camera returned to " + obj.name + " (visible)");
        }

        /// <summary>A contact has just been hidden while the camera follows it: the camera is let go.</summary>
        static void ReleaseCamera(Component obj)
        {
            if (_rpInstance == null) return;
            var rp = _rpInstance.GetValue(null, null);
            if (rp == null || !ReferenceEquals(_rpGetSelected.Invoke(rp, null), obj)) return;
            _rpDeselect.Invoke(rp, null);
            _pending = new WeakReference(obj);
            Trace("HideUnseen: the camera stopped following " + obj.name + " (hidden)");
        }

        /// <summary>
        /// The "Particles" child of a unit holds its bow wave, prop wash, cavitation and funnel smoke (particle systems) and, for ships and submarines, the
        /// Kelvin wave overlay and the foam trails (Ceto.AddWaveOverlay / Ceto.AddFoamTrail components that draw into the sea and are not Renderers).
        /// Nothing is switched off as a whole: the generation is stopped and what has been made fades by itself -
        ///  - particle systems: emission off (the game calls Play / Stop, never touches the emission module), the particles alive finish their life;
        ///  - foam trails: duration 0 (AddFoamTrail.AddFoam makes no new foam then; the foam laid already ages and fades by its time line);
        ///  - the Kelvin overlay is a single patch that follows the ship, not a trail: it is switched off (its patch is hidden at once).
        /// </summary>
        static void StopWake(Component obj, Hidden h)
        {
            h.Emitters = new List<ParticleSystem>(); h.Overlays = new List<Behaviour>(); h.Trails = new List<KeyValuePair<Behaviour, float>>();
            foreach (var t in obj.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.name != "Particles") continue;
                foreach (var ps in t.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var em = ps.emission;
                    if (em.enabled) { em.enabled = false; h.Emitters.Add(ps); }
                }
                foreach (var mb in t.GetComponentsInChildren<Behaviour>(true))
                {
                    if (mb == null) continue;
                    var n = mb.GetType().FullName;
                    if (n == "Ceto.AddFoamTrail" && _trailDuration != null) { h.Trails.Add(new KeyValuePair<Behaviour, float>(mb, (float)_trailDuration.GetValue(mb))); _trailDuration.SetValue(mb, 0f); }
                    else if (n == "Ceto.AddWaveOverlay" && mb.enabled) { mb.enabled = false; h.Overlays.Add(mb); }
                }
            }
        }

        static void ResumeWake(Hidden h)
        {
            if (h.Emitters != null) foreach (var ps in h.Emitters) if (ps != null) { var em = ps.emission; em.enabled = true; }
            if (h.Overlays != null) foreach (var mb in h.Overlays) if (mb != null) mb.enabled = true;
            if (h.Trails != null && _trailDuration != null) foreach (var kv in h.Trails) if (kv.Key != null) _trailDuration.SetValue(kv.Key, kv.Value);
            h.Emitters = null; h.Overlays = null; h.Trails = null;
        }

        /// <summary>The renderer belongs to the unit's "Particles" child (its effects fade out by themselves instead of being cut off).</summary>
        static bool InWake(Renderer r, Transform root)
        {
            for (var t = r.transform; t != null && t != root; t = t.parent) if (t.name == "Particles") return true;
            return false;
        }

        /// <summary>
        /// The cable of a towed or dipping sonar (and the towed body) is an object of its own, a sibling of the unit, not part of its model: its renderers
        /// (the cable is a line renderer) come from the unit's TowedSystem list.
        /// </summary>
        static List<Renderer> TowRenderers(Component obj)
        {
            var result = new List<Renderer>();
            if (_oObp == null) return result;
            try
            {
                var obp = _oObp(obj);
                var list = obp == null ? null : _pTowed(obp) as System.Collections.IEnumerable;
                if (list == null) return result;
                foreach (var t in list)
                {
                    if (t == null) continue;
                    var cable = _tCable(t); var cableGo = cable == null ? null : _cCableObject.Invoke(cable, null) as GameObject;
                    var body = _tAttached(t) as GameObject;
                    if (cableGo != null) result.AddRange(cableGo.GetComponentsInChildren<Renderer>(true));
                    if (body != null) result.AddRange(body.GetComponentsInChildren<Renderer>(true));
                }
            }
            catch (Exception e) { Trace("HideUnseen towed: " + e.Message); }
            return result;
        }

        static Renderer[] Merge(Renderer[] a, List<Renderer> b)
        {
            if (b.Count == 0) return a;
            var all = new List<Renderer>(a); all.AddRange(b);
            return all.ToArray();
        }

        static void Apply(Component obj, Hidden h, bool visible)
        {
            if (h.On == visible) return;
            h.On = visible;
            if (!visible)
            {
                var all = new List<Renderer>();                      // a fresh list: parts may have been added since
                foreach (var r in obj.GetComponentsInChildren<Renderer>(true)) if (r != null && !InWake(r, obj.transform)) all.Add(r);
                all.AddRange(TowRenderers(obj));
                h.R = all.ToArray();
                StopWake(obj, h);
                Hid.Add(new WeakReference(obj));
            }
            if (h.R != null) for (int i = 0; i < h.R.Length; i++) if (h.R[i] != null) h.R[i].forceRenderingOff = !visible;
            if (visible) { ResumeWake(h); try { ResumeCamera(obj); } catch (Exception e) { Trace("HideUnseen camera: " + e.Message); } }
            if (!visible) { try { ReleaseCamera(obj); } catch (Exception e) { Trace("HideUnseen camera: " + e.Message); } }
            if (++h.Flips <= 6) Trace("HideUnseen: " + obj.name + (visible ? " shown" : " hidden") + " (" + h.Flips + ")");
        }

        /// <summary>The unit is the player's own or in the player's task force.</summary>
        static bool Mine(object obj)
        {
            try
            {
                if (_oPlayer != null && (bool)_oPlayer.GetValue(obj, null)) return true;
                var tf = _oTaskforce(obj);
                return tf != null && ReferenceEquals(tf, PlayerSide());
            }
            catch { return false; }
        }

        /// <summary>The submarines option was turned off: show the hidden submarines.</summary>
        public static void RestoreSubmarines()
        {
            if (!_ok) return;
            try
            {
                for (int i = 0; i < Hid.Count; i++)
                {
                    var o = Hid[i].Target as Component;
                    Hidden h;
                    if (o == null || !IsSubmarine(o) || !Table.TryGetValue(o, out h) || h.On) continue;
                    Apply(o, h, true);
                }
            }
            catch (Exception e) { Trace("HideUnseen.RestoreSubmarines: " + e.Message); }
        }

        /// <summary>The option was turned off: show everything that was hidden.</summary>
        public static void RestoreAll()
        {
            if (!_ok) return;
            try
            {
                for (int i = 0; i < Hid.Count; i++)
                {
                    var o = Hid[i].Target as Component;
                    Hidden h;
                    if (o == null || !Table.TryGetValue(o, out h) || h.On) continue;
                    h.On = true;
                    for (int k = 0; k < h.R.Length; k++) if (h.R[k] != null) h.R[k].forceRenderingOff = false;
                    ResumeWake(h);
                }
                Hid.Clear();
            }
            catch (Exception e) { Trace("HideUnseen.RestoreAll: " + e.Message); }
        }
    }
}
