using System;
using System.Reflection;
using HarmonyLib;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// The game's colour grading (AmplifyColorEffect on the main camera) uses a depth mask: the grading depends on the depth in the depth buffer
    /// (CameraManager.setAmplicyColorValues, every frame from DynamicSky: UseDepthMask = not overcast). Transparent smoke writes no depth, so the
    /// grading of a smoke pixel follows what is behind it: the sea (near) or the sky (the far plane) - the smoke changed its colour sharply at the
    /// horizon. With this option the mask is off (the same grading everywhere): a postfix of setAmplicyColorValues sets UseDepthMask = false.
    /// Off: the game's value comes back with the next frame.
    /// </summary>
    static class ColourGrading
    {
        public static Func<bool> NoDepthMask = () => false;   // [Effects] ColourGradingNoDepthMask
        static FieldInfo _effect;
        static MemberInfo _use;

        public static bool Init(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var t = asm.GetType("SeaPower.CameraManager");
            var m = t == null ? null : t.GetMethod("setAmplicyColorValues", all);
            _effect = t == null ? null : t.GetField("_amplifyColorEffect", all);
            if (m == null || _effect == null) return false;
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(ColourGrading).GetMethod("Post", BindingFlags.Static | BindingFlags.NonPublic)));
            return true;
        }

        static void Post(object __instance)
        {
            try
            {
                if (!NoDepthMask()) return;
                var e = _effect.GetValue(__instance);
                if (e == null) return;
                if (_use == null)
                {
                    const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    _use = (MemberInfo)e.GetType().GetField("UseDepthMask", inst) ?? e.GetType().GetProperty("UseDepthMask", inst);
                    if (_use == null) return;
                }
                var f = _use as FieldInfo;
                if (f != null) f.SetValue(e, false); else ((PropertyInfo)_use).SetValue(e, false, null);
            }
            catch { }
        }
    }
}
