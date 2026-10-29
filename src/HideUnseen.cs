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
    /// Event-driven: the game sets Vehicle.IsObservedByVisual when the observation changes; here the renderers of the contact are switched on that moment.
    /// Own units are never touched. When the option is turned off, everything hidden is shown again.
    /// </summary>
    static class HideUnseen
    {
        public static Func<bool> IsEnabled = () => false;
        public static Action<string> Trace = s => { };

        static FieldInfo _vObject, _oTaskforce, _gPlayerTf;
        static PropertyInfo _oPlayer;
        static bool _ok;
        sealed class Hidden { public Renderer[] R; public bool On = true; }
        static readonly ConditionalWeakTable<object, Hidden> Table = new ConditionalWeakTable<object, Hidden>();
        static readonly List<WeakReference> Hid = new List<WeakReference>();

        public static bool Init(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var tV = asm.GetType("SeaPower.Vehicle");
                _vObject = tV.GetField("Object", all);
                var tObj = asm.GetType("SeaPower.ObjectBase"); var tGlobals = asm.GetType("SeaPower.Globals");
                _oTaskforce = tObj == null ? null : tObj.GetField("_taskforce", all);
                _oPlayer = tObj == null ? null : tObj.GetProperty("IsPlayerObject", all);
                _gPlayerTf = tGlobals == null ? null : tGlobals.GetField("_playerTaskforce", all);
                var set = tV.GetProperty("IsObservedByVisual", all).GetSetMethod(true);
                if (_vObject == null || set == null) { Trace("HideUnseen: members not found"); return false; }
                harmony.Patch(set, postfix: new HarmonyMethod(typeof(HideUnseen).GetMethod(nameof(SetPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                _ok = true;
                return true;
            }
            catch (Exception e) { Trace("HideUnseen.Init: " + e.Message); return false; }
        }

        static void SetPostfix(object __instance, bool value)
        {
            if (!_ok) return;
            try { Show(__instance, value || !IsEnabled()); }
            catch (Exception e) { Trace("HideUnseen: " + e.Message); }
        }

        static void Show(object vehicle, bool visible)
        {
            var obj = _vObject.GetValue(vehicle) as Component;
            if (obj == null) return;
            Hidden h;
            if (!Table.TryGetValue(obj, out h))
            {
                if (visible) return;                       // nothing was hidden yet
                if (Mine(obj)) return;       // own units are always drawn
                h = new Hidden { R = obj.GetComponentsInChildren<Renderer>(true) };
                Table.Add(obj, h);
            }
            if (h.On == visible) return;
            h.On = visible;
            if (!visible) Hid.Add(new WeakReference(obj));
            for (int i = 0; i < h.R.Length; i++) if (h.R[i] != null) h.R[i].enabled = visible;
        }

        /// <summary>The unit is the player's own or in the player's task force.</summary>
        static bool Mine(object obj)
        {
            try
            {
                if (_oPlayer != null && (bool)_oPlayer.GetValue(obj, null)) return true;
                var tf = _oTaskforce == null ? null : _oTaskforce.GetValue(obj);
                var mine = _gPlayerTf == null ? null : _gPlayerTf.GetValue(null);
                return tf != null && ReferenceEquals(tf, mine);
            }
            catch { return false; }
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
                    for (int k = 0; k < h.R.Length; k++) if (h.R[k] != null) h.R[k].enabled = true;
                }
                Hid.Clear();
            }
            catch (Exception e) { Trace("HideUnseen.RestoreAll: " + e.Message); }
        }
    }
}
