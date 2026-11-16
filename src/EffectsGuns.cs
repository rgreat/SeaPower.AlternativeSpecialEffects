using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// The gun, missile and explosion hooks of the effects:
    ///  - the light flashes (Flash.cs): muzzle (GunBarrel.LaunchProjectile, CIWS.update), missile launch (Missile.ScheduleFlightEffects),
    ///    torpedo / bomb / mortar detonation (WeaponBase.CreateDesctructionEffects) and every explosion the game spawns (EffectsManager.spawnEffect);
    ///  - MachineGunBurstFix: machine guns (Type=CIWS) show their tracers through the whole volley. Their tracers are particle
    ///    systems with a fixed duration (1.1 s, not looping): after it they stop emitting although the volley goes on.
    ///    Looping is switched on for every system of the effect that plays at the start of a volley.
    ///  - HitSoundFix: gun hits on ships always sound. Some hit effects (missile_hit_ship_small, _4) have no AudioSource at all:
    ///    they get one with an explosion / flak clip taken from another effect; sounds are audible from at least 50 units.
    ///  - MachineGunSoundLoopFix: the looped fire sound skips the quiet tail of its clip (a gap in the middle of a long burst).
    /// Unity's particle and audio modules are not referenced: read by reflection.
    /// The donor sounds and the clip loudness table belong to the EffectsManager of the scene: when it changes (a save is loaded, another
    /// mission starts) they are collected again, so nothing here keeps an object of the scene that is gone.
    /// </summary>
    public partial class AsePlugin
    {
        [ThreadStatic] static object _hitProjectile;   // Projectile in ProcessCollision
        static Type _tPs, _tAudio;
        const float HitSoundMinRange = 50f;
        static readonly List<object> _donors = new List<object>();   // AudioSources of effect prefabs with an explosion / flak clip
        static bool _donorsDone;
        static UnityEngine.Object _donorsOwner;                      // the EffectsManager the donors came from

        void HookEffectsGuns(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _tPs = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule");
            _tAudio = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule");
            var ci = asm.GetType("SeaPower.CIWS");
            var sf = ci == null ? null : ci.GetMethod("startFire", inst);
            if (sf != null) harmony.Patch(sf, postfix: Hm("CiwsStartFire"));
            var cu = ci == null ? null : ci.GetMethod("update", inst);
            if (cu != null) harmony.Patch(cu, postfix: Hm("CiwsUpdate"));
            var pr = asm.GetType("SeaPower.Projectile");
            var pc = pr == null ? null : pr.GetMethod("ProcessCollision", inst);
            if (pc != null) harmony.Patch(pc, prefix: Hm("HitEnter"), finalizer: Hm("HitLeave"));
            var em = asm.GetType("SeaPower.EffectsManager");
            var se = em == null ? null : em.GetMethod("spawnEffect", inst);
            if (se != null) harmony.Patch(se, postfix: Hm("HitEffect"));
            PlayWorld = em == null ? null : em.GetMethod("PlayWorldSound", inst);
            var wbT = asm.GetType("SeaPower.WeaponBase");
            var cde = wbT == null ? null : wbT.GetMethod("CreateDesctructionEffects", inst | BindingFlags.DeclaredOnly);
            if (cde != null) harmony.Patch(cde, prefix: Hm("DetonationEnter"), postfix: Hm("DetonationFlash"), finalizer: Hm("DetonationLeave"));
            else Trace("WeaponBase.CreateDesctructionEffects not found: no torpedo / bomb / mortar flash");
            var mi = asm.GetType("SeaPower.Missile");
            var sfm = mi == null ? null : mi.GetMethod("ScheduleFlightEffects", inst | BindingFlags.DeclaredOnly);
            if (sfm != null) harmony.Patch(sfm, prefix: Hm("MissileLaunchPrefix"), postfix: Hm("MissileLaunchFlash")); else Trace("Missile.ScheduleFlightEffects not found: no missile launch flash");
            var gb = asm.GetType("SeaPower.GunBarrel");
            var lpm = gb == null ? null : gb.GetMethod("LaunchProjectile", inst | BindingFlags.DeclaredOnly);
            if (lpm != null) harmony.Patch(lpm, postfix: Hm("BarrelFlash")); else Trace("GunBarrel.LaunchProjectile not found: no muzzle flash");
            if (sf == null || pc == null || se == null) Trace("effects hooks: not all found (CIWS.startFire " + (sf != null) + ", ProcessCollision " + (pc != null) + ", spawnEffect " + (se != null) + ")");
        }

        static bool FixOn(bool burst)
        {
            var a = Active; if (a == null || !Enabled) return false;
            var e = burst ? a._mgBurstFix : a._hitSoundFix;
            return e != null && e.Value;
        }

        static void CiwsStartFire(object __instance)
        {
            try
            {
                if (!FixOn(true) || _tPs == null) return;
                var root = R.F(__instance, "_tracers") as GameObject;
                if (root == null) return;
                foreach (var p in root.GetComponentsInChildren(_tPs, true))
                {
                    var m = R.P(p, "main");
                    if (R.B(R.P(p, "isPlaying")) && !R.B(R.P(m, "loop"))) R.SetP(m, "loop", true);
                }
            }
            catch (Exception e) { Trace("CIWS start: " + e.Message); }
        }

        // The fire sound is a clip (tripleA, 2.16 s) looped by the game while a burst goes on. Its tail (or head) is quiet: on every
        // wrap there is a gap in the middle of a long burst. The loudness of the clip is measured (50 ms windows) and playback
        // jumps from the end of the loud part back to its start.
        static readonly Dictionary<int, float[]> _loopRegion = new Dictionary<int, float[]>();   // clip id -> { start, end }; null = nothing to skip

        static float[] LoopRegion(object clip)
        {
            int id = ((UnityEngine.Object)clip).GetInstanceID();
            float[] r;
            if (_loopRegion.TryGetValue(id, out r)) return r;
            _loopRegion[id] = null;
            try
            {
                int n = R.I(R.P(clip, "samples")), ch = R.I(R.P(clip, "channels")), fr = R.I(R.P(clip, "frequency"));
                var buf = new float[n * ch];
                var gd = clip.GetType().GetMethod("GetData", new[] { typeof(float[]), typeof(int) });
                if (gd == null || !(bool)gd.Invoke(clip, new object[] { buf, 0 })) { Trace("sound loop: cannot read clip " + ((UnityEngine.Object)clip).name); return null; }
                int w = Math.Max(1, fr / 20) * ch, cnt = buf.Length / w;
                var rms = new float[cnt]; float max = 0f;
                for (int i = 0; i < cnt; i++)
                {
                    double sum = 0; for (int j = 0; j < w; j++) { float v = buf[i * w + j]; sum += v * v; }
                    rms[i] = (float)Math.Sqrt(sum / w); if (rms[i] > max) max = rms[i];
                }
                if (max <= 0f) return null;
                int a = 0, b = cnt - 1; float thr = 0.6f * max;   // the loud part: the fade-out before the silence is cut off too
                while (a < cnt && rms[a] < thr) a++;
                while (b > a && rms[b] < thr) b--;
                float len = (float)n / fr, start = a * 0.05f, end = Math.Min(len, (b + 1) * 0.05f);
                if (start > 0.1f || len - end > 0.1f) r = new[] { start, end };
                _loopRegion[id] = r;
            }
            catch (Exception e) { Trace("sound loop: " + e.Message); }
            return r;
        }

        static void CiwsSoundLoop(object ciws)
        {
            var a = Active;
            if (a == null || !Enabled || a._mgSoundLoop == null || !a._mgSoundLoop.Value) return;
            var ds = R.F(ciws, "_distanceSound");   // the burst is played by an emitter of the distance-sound system
            var snd = ds == null ? null : R.F(ds, "Near");
            if (snd == null || !R.B(R.P(snd, "isPlaying"))) return;
            var clip = R.P(snd, "clip");
            if (clip == null) return;
            var rg = LoopRegion(clip);
            if (rg == null) return;
            float t = R.Fl(R.P(snd, "time"));
            if (t >= rg[1]) R.SetP(snd, "time", rg[0] + (t - rg[1]));
            else if (t < rg[0]) R.SetP(snd, "time", rg[0]);
        }

        static Type _tCiwsF; static FieldInfo _fCiwsFiring;

        /// <summary>The CIWS / machine gun is firing (the game's _isFiring): read through a cached field, so an idle mount costs one field read a frame.</summary>
        static bool CiwsFiring(object c)
        {
            var t = c.GetType();
            if (!ReferenceEquals(t, _tCiwsF))
            {
                _tCiwsF = t;
                _fCiwsFiring = t.GetField("_isFiring", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            return _fCiwsFiring == null || (bool)_fCiwsFiring.GetValue(c);   // field not found: as before, everything runs
        }

        static void CiwsUpdate(object __instance)
        {
            if (!CiwsFiring(__instance)) return;
            try { CiwsSoundLoop(__instance); } catch (Exception e) { Trace("sound loop: " + e.Message); }
            CiwsFlash(__instance);
        }

        static void HitEnter(object __instance) { if (FixOn(false)) _hitProjectile = __instance; }
        static Exception HitLeave(Exception __exception) { _hitProjectile = null; return __exception; }

        static void HitEffect(GameObject __result, object ep)
        {
            if (__result == null) return;
            ExplosionFlash(__result, ep);
            if (_hitProjectile == null || _tAudio == null || !FixOn(false)) return;
            try
            {
                var srcs = __result.GetComponentsInChildren(_tAudio, true);
                if (srcs.Length == 0)
                {
                    if (!_donorsDone || (_donorsOwner == null && _donorsDone)) FindDonors();
                    if (_donors.Count == 0) return;
                    var d = _donors[UnityEngine.Random.Range(0, _donors.Count)];
                    var n = __result.AddComponent(_tAudio);
                    R.SetP(n, "clip", R.P(d, "clip"));
                    R.SetP(n, "playOnAwake", false);
                    R.SetP(n, "loop", false);
                    R.SetP(n, "volume", R.P(d, "volume"));
                    R.SetP(n, "spatialBlend", R.P(d, "spatialBlend"));
                    R.SetP(n, "rolloffMode", R.P(d, "rolloffMode"));
                    R.SetP(n, "minDistance", R.P(d, "minDistance"));
                    R.SetP(n, "outputAudioMixerGroup", R.P(d, "outputAudioMixerGroup"));
                    _added.Add(n);
                    srcs = new Component[] { n };
                }
                var em = PlayWorld == null ? null : WorldSoundOwner();
                if (em == null) return;
                foreach (var a in srcs)
                {
                    // the game plays its own sources itself (delay by distance, far clip); only ours go through PlayWorldSound
                    if (R.P(a, "clip") == null || !_added.Contains(a) || !R.B(R.P(a, "enabled"))) continue;
                    R.SetP(a, "maxDistance", Math.Max(HitSoundMinRange, R.Fl(R.P(a, "maxDistance"))));
                    PlayWorld.Invoke(em, new object[] { a });
                }
            }
            catch (Exception e) { Trace("hit sound: " + e.Message); }
        }

        static readonly HashSet<object> _added = new HashSet<object>();   // AudioSources this mod put on hit effects (pooled effects keep them)
        static System.Reflection.MethodInfo PlayWorld;                    // EffectsManager.PlayWorldSound(AudioSource)
        static UnityEngine.Object WorldSoundOwner()
        {
            if (_donorsOwner == null) _donorsOwner = UnityEngine.Object.FindObjectOfType(PlayWorld.DeclaringType);
            return _donorsOwner;
        }

        static void FindDonors()
        {
            _donorsDone = true;
            _donors.Clear(); _loopRegion.Clear(); _donorsOwner = null;
            _added.RemoveWhere(o => (o as UnityEngine.Object) == null);   // sources of effects the game has destroyed (a mission was left)
            var em = UnityEngine.Object.FindObjectOfType(GameAssembly().GetType("SeaPower.EffectsManager"));
            _donorsOwner = em;
            var dict = em == null ? null : R.F(em, "_effectsDictionary") as System.Collections.IDictionary;
            if (dict == null) return;
            foreach (System.Collections.DictionaryEntry de in dict)
            {
                var go = de.Value as GameObject;
                if (go == null) continue;
                foreach (var a in go.GetComponentsInChildren(_tAudio, true))
                {
                    var cl = R.P(a, "clip") as UnityEngine.Object;
                    if (cl != null && (cl.name.StartsWith("explosion") || cl.name.StartsWith("flak"))) { _donors.Add(a); break; }
                }
            }
        }
    }
}
