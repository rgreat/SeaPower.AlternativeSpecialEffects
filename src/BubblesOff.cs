using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// The sinking-bubbles effect (ini [Particles] SinkingBubblesEffect): the game spawns it on the hull once, at the end of Compartments.Sink(), and never ends it
    /// while the wreck exists. Here, N game minutes after the sink started, its particle systems stop emitting (the particles already in the water fade
    /// out), which saves the particle load of long missions. Setting [Effects] BubblesStopMinutes; 0 = never (the game's).
    ///
    /// Event-driven: a postfix of Compartments.Sink() puts a small component (BubblesTimer) on the bubbles' own object. The deadline is a game time
    /// (GameTime.missionElapsedTime), not Unity's scaled seconds: the game caps Time.timeScale, so at high time compression Unity time runs slower than the mission.
    /// The component lives exactly as long as the bubbles do and removes itself when it has stopped them, so a ship that sank long ago costs nothing.
    ///
    /// Save and load: the game saves only IsSinking of a ship (not the moment it sank) and does not call Sink() or spawn the bubbles again when a save is loaded;
    /// the bubbles of a ship that sank before the save do not exist afterwards, so there is nothing to resume. A ship that sinks after the load goes through Sink() as usual.
    /// A timer is never kept outside the bubbles' object, so nothing of the scene that was left can fire later.
    /// </summary>
    static class BubblesOff
    {
        public static Func<bool> IsEnabled = () => false;
        public static Func<float> Minutes = () => 0f;
        public static Action<string> Trace = s => { };

        static FieldInfo _fObp, _fEffect, _fBase, _fPos, _fSingle;
        static MethodInfo _mTransform;
        static PropertyInfo _pTime;
        static Type _tPs, _tStop;
        static MethodInfo _mStop;
        static bool _ok;

        public static bool Init(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var t = asm.GetType("SeaPower.Compartments");
                var sink = t == null ? null : t.GetMethod("Sink", all);
                _fObp = t.GetField("_obp", all); _fBase = t.GetField("_baseObject", all);
                var tO = _fObp == null ? null : _fObp.FieldType;
                _fEffect = tO == null ? null : tO.GetField("_sinkingBubblesEffect", all);
                var tE = _fEffect == null ? null : _fEffect.FieldType;
                _fPos = tE == null ? null : tE.GetField("_position", all);
                _fSingle = tE == null ? null : tE.GetField("_singleEffect", all);
                _mTransform = _fBase == null ? null : _fBase.FieldType.GetMethod("getTransform", all);
                var tGt = asm.GetType("SeaPower.GameTime");
                _pTime = tGt == null ? null : tGt.GetProperty("missionElapsedTime", all);
                _ok = sink != null && _fObp != null && _fBase != null && _fEffect != null && _fPos != null && _mTransform != null && _pTime != null;
                if (!_ok) { Trace("BubblesOff: game members not found; the sinking bubbles are not stopped"); return false; }
                harmony.Patch(sink, postfix: new HarmonyMethod(typeof(BubblesOff).GetMethod(nameof(SinkPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                return true;
            }
            catch (Exception e) { Trace("BubblesOff.Init: " + e.Message); return false; }
        }

        static double Now() { return Convert.ToDouble(_pTime.GetValue(null, null)); }

        /// <summary>Postfix of Compartments.Sink: the bubbles were spawned at its end, as children of the ship's transform.</summary>
        static void SinkPostfix(object __instance)
        {
            if (!_ok) return;
            try
            {
                float minutes = Minutes();
                if (minutes <= 0f || !IsEnabled()) return;
                var obp = _fObp.GetValue(__instance);
                var eff = _fEffect.GetValue(obp);
                var tr = _mTransform.Invoke(_fBase.GetValue(__instance), null) as Transform;
                if (eff == null || tr == null) return;
                var pos = (Vector3)_fPos.GetValue(eff);
                var single = _fSingle == null ? null : _fSingle.GetValue(eff) as GameObject;
                for (int i = 0; i < tr.childCount; i++)
                {
                    var ch = tr.GetChild(i);
                    if ((ch.localPosition - pos).sqrMagnitude > 1e-4f) continue;
                    if (single != null && !ch.name.StartsWith(single.name, StringComparison.Ordinal)) continue;
                    if (ch.GetComponent<BubblesTimer>() != null) continue;
                    var bt = ch.gameObject.AddComponent<BubblesTimer>();
                    bt.Started = Now();   // the setting is read while the bubbles run: it may be changed meanwhile
                }
            }
            catch (Exception e) { Trace("BubblesOff.Sink: " + e.Message); }
        }

        // ParticleSystem lives in a Unity module the build does not reference: looked up in the loaded assemblies.
        internal static int StopAll(Transform t)
        {
            if (_tPs == null)
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _tPs = a.GetType("UnityEngine.ParticleSystem"); _tStop = a.GetType("UnityEngine.ParticleSystemStopBehavior");
                    if (_tPs != null && _tStop != null) break;
                }
                if (_tPs == null || _tStop == null) { Trace("BubblesOff: ParticleSystem type not found"); return 0; }
                _mStop = _tPs.GetMethod("Stop", new[] { typeof(bool), _tStop });
                if (_mStop == null) { Trace("BubblesOff: ParticleSystem.Stop not found"); return 0; }
            }
            if (_mStop == null) return 0;
            int n = 0;
            var stopEmitting = Enum.ToObject(_tStop, 1);     // StopEmitting = 1 (0 would clear the particles at once)
            foreach (var ps in t.GetComponentsInChildren(_tPs)) { _mStop.Invoke(ps, new object[] { false, stopEmitting }); n++; }
            return n;
        }

        internal static bool Ready { get { return _ok; } }
        internal static double GameNow() { return Now(); }
        internal static float MinutesNow() { return IsEnabled() ? Minutes() : 0f; }
        internal static void Log(string s) { Trace(s); }
    }

    /// <summary>On the sinking bubbles' object: stops them when the game time since the ship sank passes [Effects] BubblesStopMinutes, then removes itself.</summary>
    sealed class BubblesTimer : MonoBehaviour
    {
        public double Started;

        void Update()
        {
            // one comparison of two numbers a frame, only while the bubbles run; the game's time is not Unity's (see BubblesOff)
            float minutes = BubblesOff.MinutesNow();
            if (minutes <= 0f) { Destroy(this); return; }   // switched off while it ran: the bubbles go on, as the game makes them
            if (BubblesOff.GameNow() - Started < minutes * 60.0) return;
            int stopped = BubblesOff.StopAll(transform);
            BubblesOff.Log("BubblesOff: bubbles of " + (transform.parent == null ? name : transform.parent.name) + " stopped after " + minutes.ToString("0.#") + " min (" + stopped + " particle systems)");
            Destroy(this);
        }
    }
}
