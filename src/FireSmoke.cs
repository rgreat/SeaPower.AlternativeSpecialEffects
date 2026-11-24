using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// [Effects] ExtraSmokeEmitters: the number of fire / smoke points by ship length instead of the game's three:
    /// under 25 m 1 (the whole ship), under 50 m 2 (sections 0-2 and 3-4), under 75 m the game's three, from 75 m five (below).
    /// The game (Compartments): _firesCount = 3 effect points from the ship's ini, [Compartments] Compartment1..3FirePosition -
    /// the middle of the ship and the points between the two end sections at each end; UpdateFireState gives each point the worst
    /// fire of its sections (0+1, 2, 3+4), so one end point smokes for two sections at once. Every effect array, the fire decals
    /// and updateFireEffect / UpdateFireDecals go by _firesCount and _obp._compartmentEffectPositions.
    /// Here, for five points: after Compartments.Init the points become five - one per section - and
    /// _firesCount 5 (the arrays made again, empty); after UpdateFireState each point gets its own section's fire.
    /// The five points: the middle point is section 2; the end points lie 1.5 sections from it, so sections 1 and 3 are 2/3 of
    /// the way to them and sections 0 and 4 4/3 of the way (along the ship; height and side as the end point), never past 45% of
    /// the ship's length from the middle. New ships only (the points are made when a ship is spawned).
    /// </summary>
    static class FireSmoke
    {
        public static Func<bool> IsEnabled = () => false;
        public static Func<bool> AfterClouds = () => false;   // [Effects] SmokeAfterClouds
        public static Func<bool> SortFix = () => false;       // [Effects] SmokeSortFix
        public static Func<float> DensityFactor = () => 1f;   // [Effects] SmokeDensityFactor
        public static Func<float> LifeFactor = () => 1f;     // [Effects] SmokeLifetimeFactor
        public static Func<bool> DensityOn = () => false;   // [Effects] SmokeBySeverity
        public static float StartOpacity = 0.1f;              // [Effects] SmokeStartOpacity: the small fire's smoke as the fire starts
        public static float EndOpacity = 1f;                // [Effects] SmokeEndOpacity: the small fire's smoke at ModerateFrom
        public static float ModerateFrom = 0.50f;           // [Effects] SmokeModerateFrom: the moderate look from this share of the maximum
        public static float SevereFrom = 0.90f;             // [Effects] SmokeSevereFrom: the severe look from this share
        public static Action<string> Trace = s => { };
        public static Func<bool> Verbose = () => false;

        static bool _ok;
        static FieldInfo _firesCount, _compCount, _currentFires, _fireDecal, _small, _moderate, _severe, _obp, _positions, _sys, _length;
        static PropertyInfo _fireState;
        static MethodInfo _updateState, _updateEffect;

        sealed class Mark { }
        static readonly ConditionalWeakTable<object, Mark> Five = new ConditionalWeakTable<object, Mark>();   // ships with five points

        public static bool Init(Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                HookSort();
                var t = asm.GetType("SeaPower.Compartments");
                var tObp = asm.GetType("SeaPower.ObjectBaseParameters");
                var tSc = asm.GetType("SeaPower.SystemCompartment");
                _firesCount = t.GetField("_firesCount", all); _compCount = t.GetField("_compartmentsCount", all);
                _currentFires = t.GetField("_currentFires", all); _fireDecal = t.GetField("_fireDecal", all);
                _small = t.GetField("_smallFireEffects", all); _moderate = t.GetField("_moderateFireEffects", all); _severe = t.GetField("_severeFireEffects", all);
                _obp = t.GetField("_obp", all); _sys = t.GetField("_systemCompartments", all);
                _positions = tObp == null ? null : tObp.GetField("_compartmentEffectPositions", all);
                _length = tObp == null ? null : tObp.GetField("_length", all);
                _fireState = tSc == null ? null : tSc.GetProperty("FireState", all);
                _updateState = t.GetMethod("UpdateFireState", all, null, Type.EmptyTypes, null);
                _updateEffect = t.GetMethod("updateFireEffect", all, null, Type.EmptyTypes, null);
                _ok = _firesCount != null && _compCount != null && _currentFires != null && _fireDecal != null && _small != null && _moderate != null &&
                      _severe != null && _obp != null && _positions != null && _sys != null && _fireState != null;
            }
            catch (Exception e) { Trace("FireSmoke.Init: " + e.Message); _ok = false; }
            return _ok;
        }

        static float Length(object obp)
        {
            try
            {
                if (_length != null) return Convert.ToSingle(_length.GetValue(obp));
                var p = obp.GetType().GetProperty("_length", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return p == null ? 0f : Convert.ToSingle(p.GetValue(obp, null));
            }
            catch { return 0f; }
        }

        /// <summary>Fire / smoke points by ship length: under 25 m 1, under 50 m 2, under 75 m 3 (the game's own), from 75 m 5.</summary>
        public static int CountFor(float length) { return length < 25f ? 1 : length < 50f ? 2 : length < 75f ? 3 : 5; }

        /// <summary>Postfix of Compartments.Init.</summary>
        public static void AfterInit(object comp)
        {
            if (!_ok || comp == null || !IsEnabled()) return;
            try
            {
                if ((int)_firesCount.GetValue(comp) != 3 || (int)_compCount.GetValue(comp) != 5) return;
                var obp = _obp.GetValue(comp);
                if (obp == null) return;
                float len = Length(obp);
                int target = CountFor(len);
                if (target == 3) return;   // the game's own three
                var pos = _positions.GetValue(obp) as Vector3[];
                if (pos == null) return;
                if (pos.Length == 3)
                {
                    Vector3 fore = pos[0], mid = pos[1], aft = pos[2];
                    if (fore == Vector3.zero && mid == Vector3.zero && aft == Vector3.zero) return;   // no points in the ini
                    float lim = 0.45f * len;
                    Func<Vector3, float, Vector3> along = (end, f) =>
                    {
                        float z = mid.z + (end.z - mid.z) * f;
                        z = Mathf.Clamp(z, mid.z - lim, mid.z + lim);
                        // height and side: between the middle and the end point for 2/3, as the end point beyond it
                        float k = Math.Min(1f, f);
                        return new Vector3(mid.x + (end.x - mid.x) * k, mid.y + (end.y - mid.y) * k, z);
                    };
                    if (target == 5) pos = new[] { along(fore, 4f / 3f), along(fore, 2f / 3f), mid, along(aft, 2f / 3f), along(aft, 4f / 3f) };
                    else if (target == 2) pos = new[] { fore, aft };
                    else pos = new[] { mid };
                    _positions.SetValue(obp, pos);
                }
                if (pos.Length != target) return;
                // the effect arrays for the new number of points (none made yet: they are made when a fire first shows);
                // two points: three slots - the game's UpdateFireState writes a third one for a heavier ship
                int size = Math.Max(target, 3);
                if (target == 1) size = 1;
                foreach (var f in new[] { _currentFires, _fireDecal, _small, _moderate, _severe })
                {
                    var old = f.GetValue(comp) as Array;
                    var et = old != null ? old.GetType().GetElementType() : f.FieldType.GetElementType();
                    var arr = Array.CreateInstance(et, size);
                    if (old != null) Array.Copy(old, arr, Math.Min(old.Length, size));
                    f.SetValue(comp, arr);
                }
                _firesCount.SetValue(comp, target);
                Five.Remove(comp); Five.Add(comp, new Mark());
                Trace("fire smoke: " + target + " points (" + len.ToString("0") + " m)");
            }
            catch (Exception e) { Trace("FireSmoke.AfterInit: " + e.Message); }
        }

        // ------------------------------------------------------------ smoke at once (the game: only every FireCheckTime, 10 s)

        sealed class Watch { public float Next; public int[] States = { -1, -1, -1, -1, -1 }; }
        static readonly ConditionalWeakTable<object, Watch> Watches = new ConditionalWeakTable<object, Watch>();

        /// <summary>
        /// Prefix tick of Compartments.OnFixedUpdate, once a second (game time): when the fire stage of any section has changed, the
        /// fire / smoke points are updated at once (UpdateFireState + updateFireEffect), not at the next fire check up to 10 s later
        /// (the game updates the effects only there; after a hit it updates them from the old states).
        /// </summary>
        public static void Tick(object comp)
        {
            if (!_ok || comp == null || _updateState == null || _updateEffect == null) return;
            float now = Time.time;
            var w = Watches.GetOrCreateValue(comp);
            if (now < w.Next) return;
            w.Next = now + 1f;
            try
            {
                var sys = _sys.GetValue(comp) as Array;
                if (sys == null) return;
                bool changed = false;
                for (int i = 0; i < sys.Length && i < 5; i++)
                {
                    var sc = sys.GetValue(i);
                    int st = sc == null ? 0 : Convert.ToInt32(_fireState.GetValue(sc, null));
                    if (st != w.States[i]) { changed |= w.States[i] >= 0; w.States[i] = st; }
                }
                ProlongAll(comp);
                Funnel(comp, sys);
                if (Verbose()) DumpFunnel(comp);
                bool density = DensityOn();
                if (density)
                {
                    // the looks follow the fire's strength (not only the game's stages): every second, effects only on a change
                    var fires = _currentFires.GetValue(comp) as Array;
                    int[] before = Snapshot(fires);
                    _updateState.Invoke(comp, null);
                    if (changed || !Same(before, _currentFires.GetValue(comp) as Array))
                    {
                        _updateEffect.Invoke(comp, null);
                        if (Verbose()) Trace("smoke: effects switched (" + (changed ? "game stage" : "look") + " changed)");
                    }
                    Density(comp, sys);
                }
                else
                {
                    if (changed)
                    {
                        _updateState.Invoke(comp, null);
                        _updateEffect.Invoke(comp, null);
                    }
                    if (_dimmed) ResetLooks();
                }
                if (Verbose()) Probe(comp, sys);
            }
            catch (Exception e) { Trace("FireSmoke.Tick: " + e.Message); }
        }

        /// <summary>Every new fire effect gets its own random seed, so neighbouring fires do not animate identically (no pre-run: the start is as in the game).</summary>
        public static void RandomSeed(object fireEffect)
        {
            if (fireEffect == null) return;
            if (_fePs2 == null) _fePs2 = fireEffect.GetType().GetField("_fireEffectPS", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var root = _fePs2 == null ? null : _fePs2.GetValue(fireEffect) as Component;
            if (root == null) return;
            var tPs = root.GetType();
            var auto = tPs.GetProperty("useAutoRandomSeed"); var seed = tPs.GetProperty("randomSeed");
            if (seed != null) seed.SetValue(root, (uint)UnityEngine.Random.Range(1, int.MaxValue), null);
            if (auto != null) auto.SetValue(root, true, null);
        }

        /// <summary>Every fire effect of a ship, burning or not: the lifetime settings are applied to it (new settings reach burning ships at once).</summary>
        static readonly float[][] _mine = new float[2][];
        static FieldInfo[] _fireSets;

        static void ProlongAll(object comp)
        {
            var cam = Camera.main; bool sort = (SortFix() || AfterClouds()) && cam != null;
            if (_fireSets == null) _fireSets = new[] { _small, _moderate, _severe };
            foreach (var f in _fireSets)
            {
                var arr = f.GetValue(comp) as Array;
                if (arr != null) foreach (var fe in arr) if (fe != null) { Prolong(fe); Sort(fe, cam, sort); }
            }
        }

        // ------------------------------------------------------------ smoke and clouds
        // Clouds (TurboClouds.buildMesh) are one renderer per sky cell, kilometres wide, with many billboard quads; Unity sorts a whole cell against a smoke
        // column by the distance to its centre, so a cell whose centre is nearer covered the smoke even with its clouds behind the ship. Drawing all smoke after
        // the clouds (the first fix) was wrong the other way: from above the clouds or at a far ship the smoke showed through clouds in front of it.
        // Now every column is put, before each frame, in front of or behind the clouds (sortingOrder group, see PreCull) by where the cloud layer
        // (CloudsManager.getCloudsMinHeight .. getCloudsMaxHeight) is on the line from the camera to the column: more of the layer before it than behind it
        // (over the same length) - behind the clouds, else in front. Whole columns against whole cells: not per pixel.
        static Type _tClouds; static MethodInfo _mMinH, _mMaxH; static UnityEngine.Object _clouds; static float _cloudTry = -100f;

        /// <summary>The heights of the cloud layer; false when there are no clouds (or they are not loaded). The manager is looked up lazily, at most every 10 s.</summary>
        static bool CloudLayer(out float h0, out float h1)
        {
            h0 = h1 = 0f;
            try
            {
                if (_clouds == null)
                {
                    if (Time.unscaledTime - _cloudTry < 10f) return false;
                    _cloudTry = Time.unscaledTime;
                    if (_tClouds == null)
                    {
                        foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { _tClouds = a.GetType("SeaPower.CloudsManager"); if (_tClouds != null) break; }
                        if (_tClouds != null) { _mMinH = _tClouds.GetMethod("getCloudsMinHeight", Type.EmptyTypes); _mMaxH = _tClouds.GetMethod("getCloudsMaxHeight", Type.EmptyTypes); }
                    }
                    if (_tClouds == null || _mMinH == null || _mMaxH == null) return false;
                    _clouds = UnityEngine.Object.FindObjectOfType(_tClouds);
                    if (_clouds == null) return false;
                    Trace("smoke and clouds: cloud layer " + _mMinH.Invoke(_clouds, null) + " .. " + _mMaxH.Invoke(_clouds, null));
                }
                h0 = Convert.ToSingle(_mMinH.Invoke(_clouds, null)); h1 = Convert.ToSingle(_mMaxH.Invoke(_clouds, null));
                return h0 >= 0f && h1 >= h0;   // -1: not loaded
            }
            catch { return false; }
        }

        /// <summary>+1: the smoke at p is in front of the clouds, -1: behind them. The part of the ray inside the cloud layer before p is compared with the part after it
        /// (up to the same distance again).</summary>
        static int CloudSide(Vector3 cam, Vector3 p, float h0, float h1)
        {
            Vector3 v = p - cam; float d = v.magnitude;
            if (d < 1e-4f) return 1;
            float dy = v.y / d, ta, tb;
            if (Math.Abs(dy) < 1e-6f) { if (cam.y < h0 || cam.y > h1) return 1; ta = 0f; tb = float.MaxValue; }
            else { ta = (h0 - cam.y) / dy; tb = (h1 - cam.y) / dy; if (ta > tb) { float x = ta; ta = tb; tb = x; } }
            ta = Math.Max(ta, 0f);
            if (tb <= ta) return 1;
            float front = Math.Max(0f, Math.Min(tb, d) - ta), back = Math.Max(0f, Math.Min(tb, 2f * d) - Math.Max(ta, d));
            return front > back ? -1 : 1;
        }

        sealed class SortSys { public Renderer R; public float Base, Now; public int BaseOrd, NowOrd; }
        static readonly ConditionalWeakTable<object, List<SortSys>> Sorts = new ConditionalWeakTable<object, List<SortSys>>();
        static PropertyInfo _sFudge;

        /// <summary>
        /// Unity sorts particle systems by the distance to their bounds' centre, and a tall plume's centre is high up and noisy: two columns overlap in the wrong order.
        /// So the columns are ranked by the distance to their fire points and the rank is written into sortingOrder (see Apply). The distances change with the camera, so it is
        /// done before the camera culls (Camera.onPreCull / the scriptable pipeline's beginCameraRendering): a turn of the view is right at once.
        /// The once-a-second fire update only makes the list of a new effect and puts the game's own values back when the option is off.
        /// </summary>
        static void Sort(object fe, Camera cam, bool on)
        {
            Track(fe, on, () =>
            {
                var rs = new List<Renderer>();
                if (_fePs2 == null) _fePs2 = fe.GetType().GetField("_fireEffectPS", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var root = _fePs2 == null ? null : _fePs2.GetValue(fe) as Component;
                if (root != null)
                    foreach (var c in root.gameObject.GetComponentsInChildren(root.GetType(), true))
                    {
                        string name = ((Component)c).name ?? "";
                        if (!ReferenceEquals(c, root) && !name.StartsWith("shipfire", StringComparison.OrdinalIgnoreCase)) continue;   // flames: as they are
                        var r = ((Component)c).GetComponent<Renderer>(); if (r != null) rs.Add(r);
                    }
                return rs;
            });
        }

        /// <summary>A smoke column (a fire effect or a funnel's smoke): its renderers are listed once and then ordered before every frame; off - the game's values back.</summary>
        static void Track(object key, bool on, Func<List<Renderer>> make)
        {
            List<SortSys> l;
            if (!Sorts.TryGetValue(key, out l))
            {
                if (!on) return;
                l = new List<SortSys>(); Sorts.Add(key, l); Active.Add(l); Seq[l] = _seq++;
                foreach (var r in make())
                {
                    if (_sFudge == null) _sFudge = r.GetType().GetProperty("sortingFudge");
                    float b = _sFudge == null ? 0f : Convert.ToSingle(_sFudge.GetValue(r, null));
                    l.Add(new SortSys { R = r, Base = b, Now = b, BaseOrd = r.sortingOrder, NowOrd = r.sortingOrder });
                }
            }
            else if (on && !Seq.ContainsKey(l)) { Active.Add(l); Seq[l] = _seq++; }   // dropped when sorting was switched off
            if (!on) Apply(l, false, 0, 0);
        }

        static readonly List<List<SortSys>> Active = new List<List<SortSys>>();
        static bool _hooked;

        /// <summary>on: the column's sorting order = the game's + 1000 x side (in front of / behind the clouds; 0 = as the game) - the rank by distance counted from
        /// the middle column (nearer ones more); off: the game's values.</summary>
        static void Apply(List<SortSys> l, bool on, int rank, int side)
        {
            foreach (var s in l)
            {
                if (s.R == null) continue;
                int ord = on ? s.BaseOrd + 1000 * side - rank : s.BaseOrd;
                if (ord != s.NowOrd) { s.NowOrd = ord; s.R.sortingOrder = ord; }
                if (s.Now != s.Base && _sFudge != null) { s.Now = s.Base; _sFudge.SetValue(s.R, s.Base, null); }
            }
        }

        // Seen from the side, two columns stand at almost the same distance. The order is made of the fire points only (never the particle bounds, which are noisy):
        // sorted by the exact distance (game units, no rounding), exactly equal ones keep their creation order; the rank goes into the sortingOrder, which Unity compares before any distance.
        static readonly Dictionary<List<SortSys>, int> Seq = new Dictionary<List<SortSys>, int>();
        static readonly List<KeyValuePair<float, List<SortSys>>> Ordered = new List<KeyValuePair<float, List<SortSys>>>();
        static int _seq;

        static void HookSort()
        {
            if (_hooked) return;
            _hooked = true;
            Camera.onPreCull += PreCull;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += (ctx, cam) => PreCull(cam);
        }

        public static Func<List<string>> CamOff = () => new List<string>();   // [Debug] CamOff*: the game's camera effects switched off for a test
        public static int CamOffVersion;
        static int _camOffDone = -1, _camOffCam;
        static readonly Dictionary<string, bool> CamWas = new Dictionary<string, bool>();

        /// <summary>Debug: the camera effects named in the [Debug] options are switched off (their own state is put back when the option goes off).</summary>
        static void CamEffects(Camera cam)
        {
            if (_camOffDone == CamOffVersion && _camOffCam == cam.GetInstanceID()) return;
            if (_camOffCam != cam.GetInstanceID()) CamWas.Clear();
            _camOffDone = CamOffVersion; _camOffCam = cam.GetInstanceID();
            var off = CamOff();
            foreach (var b in cam.GetComponents<Behaviour>())
            {
                string n = b.GetType().Name;
                if (n != "AmplifyColorEffect" && n != "UnderWaterPostEffect" && n != "AmplifyOcclusionEffect" && n != "AmplifyBloomEffect") continue;
                bool was;
                if (off.Contains(n)) { if (!CamWas.ContainsKey(n)) CamWas[n] = b.enabled; b.enabled = false; Trace("camera effect " + n + " off (test)"); }
                else if (CamWas.TryGetValue(n, out was)) { b.enabled = was; CamWas.Remove(n); Trace("camera effect " + n + " back to " + was); }
            }
        }

        static bool _skyDumped;

        /// <summary>Debug, once: what is drawn around the smoke - the sky meshes the game finds by name and every renderer in a late render queue
        /// (for the colour step of the smoke at the horizon: something drawn after the smoke where the depth is the far plane).</summary>
        static void DumpSky(Camera cam)
        {
            _skyDumped = true;
            try
            {
                var sb = new System.Text.StringBuilder("sky and late renderers (camera '" + cam.name + "' clear " + cam.clearFlags + ", far " + cam.farClipPlane + ", depth mode " + cam.depthTextureMode + ", colour space " + QualitySettings.activeColorSpace + "):");
                foreach (var n in new[] { "Skybox", "High Altitude Clouds", "Sun Flare", "Moon Flare" })
                {
                    var go = GameObject.Find(n);
                    var r = go == null ? null : go.GetComponent<Renderer>();
                    sb.Append("\n  '").Append(n).Append("' ");
                    if (r == null) { sb.Append("not found"); continue; }
                    sb.Append("active ").Append(go.activeInHierarchy).Append(" layer ").Append(go.layer).Append(" order ").Append(r.sortingOrder).Append(" bounds ").Append(r.bounds.size).Append(" at ").Append(r.bounds.center);
                    foreach (var m in r.sharedMaterials) if (m != null) sb.Append("\n    material '").Append(m.name).Append("' shader '").Append(m.shader == null ? "?" : m.shader.name).Append("' queue ").Append(m.renderQueue);
                }
                int k = 0;
                foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                {
                    var m = r.sharedMaterial;
                    if (m == null || m.renderQueue < 2500 || r is ParticleSystemRenderer) continue;
                    if (++k > 40) break;
                    sb.Append("\n  late: '").Append(r.name).Append("' ").Append(r.GetType().Name).Append(" layer ").Append(r.gameObject.layer).Append(" material '").Append(m.name).Append("' shader '").Append(m.shader == null ? "?" : m.shader.name).Append("' queue ").Append(m.renderQueue).Append(" order ").Append(r.sortingOrder).Append(" bounds ").Append(r.bounds.size);
                }
                foreach (var b in cam.GetComponents<Behaviour>()) sb.Append("\n  camera component ").Append(b.GetType().FullName).Append(" enabled ").Append(b.enabled);
                Trace(sb.ToString());
            }
            catch (Exception e) { Trace("DumpSky: " + e.Message); }
        }

        static void PreCull(Camera cam)
        {
            if (!_skyDumped && Verbose() && cam != null && cam == Camera.main && Time.timeSinceLevelLoad > 5f) DumpSky(cam);
            if (cam != null && cam == Camera.main) CamEffects(cam);
            bool sort = SortFix(), clouds = AfterClouds();
            if (!(sort || clouds) && Active.Count > 0 && cam != null && cam == Camera.main)
            {   // sorting was switched off: the game's values back, the lists emptied (Track puts a column back when it is switched on again)
                foreach (var l in Active) Apply(l, false, 0, 0);
                Active.Clear(); Seq.Clear();
            }
            if (cam == null || cam != Camera.main || !(sort || clouds)) return;
            float h0 = 0f, h1 = 0f;
            bool layer = clouds && CloudLayer(out h0, out h1);
            Vector3 cp = cam.transform.position;
            Ordered.Clear();
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var l = Active[i]; Transform t = null;
                foreach (var s in l) if (s.R != null) { t = s.R.transform; break; }
                if (t == null) { Active.RemoveAt(i); Seq.Remove(l); continue; }
                Ordered.Add(new KeyValuePair<float, List<SortSys>>((t.position - cp).magnitude, l));
            }
            if (sort) Ordered.Sort((x, y) => { int c = x.Key.CompareTo(y.Key); return c != 0 ? c : Seq[x.Value].CompareTo(Seq[y.Value]); });   // exact distance; only equal ones go by creation order
            int mid = Ordered.Count / 2;   // the middle column keeps the game's order, the nearer ones get more, the farther ones less
            for (int i = 0; i < Ordered.Count; i++)
            {
                var l = Ordered[i].Value;
                int side = 0;
                if (layer) foreach (var s in l) if (s.R != null) { side = CloudSide(cp, s.R.bounds.center, h0, h1); break; }
                Apply(l, true, sort ? i - mid : 0, side);
            }
        }

        // ------------------------------------------------------------ smoke that lasts longer

        static PropertyInfo _lMain, _lLife, _lMax, _lEm, _lRate;
        public static float SpreadGrow = 0.5f, SpreadFade = 1f;   // [Effects] SmokeSpreadGrowth / SmokeSpreadFade: sprites of the slowed stage grow and fade
        public static float FireShare = 0.3f;   // [Effects] SmokeFireShare: the first part of the sprite animation has the fire in it, the rest only smoke

        sealed class Sys { public object Ps; public bool Smoke, SmokeOnly; public float BaseRate; public float BaseLife; public int BaseMax; }
        sealed class LifeState { public float K = -1f, Share = -1f, Dens = -1f, Grow = -1f, Fade = -1f; public List<Sys> S = new List<Sys>(); }
        static readonly ConditionalWeakTable<object, LifeState> Lives = new ConditionalWeakTable<object, LifeState>();

        /// <summary>
        /// A fire effect (new or already burning, once a second from Tick). The sprites of the fire system carry the fire and the smoke in one animation
        /// (the fire is gone after SmokeFireShare of it). The system stays as the game made it (life, every over-lifetime curve): each particle lives its
        /// first SmokeFireShare exactly as in the game, then AseFireStretch (a particle job) makes the rest of its life SmokeLifetimeFactor times longer,
        /// so every curve (picture, size, colour, velocity) goes on from where it was, k times slower: no jump, the particle does not brake.
        /// A smoke-only system (its material is a smoke one) gets a longer start lifetime. The rate is the game's x SmokeDensityFactor.
        /// Everything is computed from the values as the game made them, so a changed setting reaches burning ships at once (new crossings follow it).
        /// </summary>
        public static void Prolong(object fireEffect)
        {
            if (fireEffect == null) return;
            float k = Math.Max(1f, LifeFactor()), dens = Math.Max(0f, DensityFactor());
            LifeState st;
            if (!Lives.TryGetValue(fireEffect, out st))
            {
                if (k <= 1.001f && Math.Abs(dens - 1f) < 1e-3f) return;
                st = new LifeState(); Lives.Add(fireEffect, st);
                if (_fePs2 == null) _fePs2 = fireEffect.GetType().GetField("_fireEffectPS", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var root = _fePs2 == null ? null : _fePs2.GetValue(fireEffect) as Component;
                if (root == null) return;
                var tPs = root.GetType();
                if (_lMain == null)
                {
                    _lMain = tPs.GetProperty("main");
                    var tMain = _lMain == null ? null : _lMain.PropertyType;
                    _lLife = tMain == null ? null : tMain.GetProperty("startLifetimeMultiplier");
                    _lMax = tMain == null ? null : tMain.GetProperty("maxParticles");
                    _lEm = tPs.GetProperty("emission");
                    _lRate = _lEm == null ? null : _lEm.PropertyType.GetProperty("rateOverTimeMultiplier");
                }
                if (_lLife == null || _lMax == null || _lRate == null) { Trace("FireSmoke: smoke lifetime members not found"); return; }
                foreach (var c in root.gameObject.GetComponentsInChildren(tPs, true))
                {
                    string name = ((Component)c).name ?? "";
                    bool smoke = ReferenceEquals(c, root) || name.StartsWith("shipfire", StringComparison.OrdinalIgnoreCase);   // the others are flames: their life is the game's
                    var rend = ((Component)c).GetComponent<Renderer>();
                    var mat = rend == null ? null : rend.sharedMaterial;
                    var main = _lMain.GetValue(c, null);
                    st.S.Add(new Sys { Ps = c, Smoke = smoke, BaseRate = Convert.ToSingle(_lRate.GetValue(_lEm.GetValue(c, null), null)), SmokeOnly = mat != null && mat.name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0,
                                       BaseLife = Convert.ToSingle(_lLife.GetValue(main, null)), BaseMax = Convert.ToInt32(_lMax.GetValue(main, null)) });
                }
            }
            float share = FireShare;
            if (Math.Abs(st.K - k) < 1e-4f && Math.Abs(st.Share - share) < 1e-4f && Math.Abs(st.Dens - dens) < 1e-4f && Math.Abs(st.Grow - SpreadGrow) < 1e-4f && Math.Abs(st.Fade - SpreadFade) < 1e-4f) return;
            st.K = k; st.Share = share; st.Dens = dens; st.Grow = SpreadGrow; st.Fade = SpreadFade;
            var log = new System.Text.StringBuilder();
            foreach (var sy in st.S)
            {
                var ps = (Component)sy.Ps;
                if (ps == null) continue;
                float whole = !sy.Smoke ? 1f : sy.SmokeOnly ? k : share + (1f - share) * k;   // the new lifetime / the game's
                var main = _lMain.GetValue(ps, null);
                if (sy.SmokeOnly) _lLife.SetValue(main, sy.BaseLife * whole, null);
                _lMax.SetValue(main, (int)Math.Min(100000f, sy.BaseMax * whole * Math.Max(1f, dens) + 1f), null);   // room for the particles, nothing else changes
                _lRate.SetValue(_lEm.GetValue(ps, null), sy.BaseRate * dens, null);
                string how = !sy.Smoke ? "as made" : sy.SmokeOnly ? "whole x" + whole.ToString("0.##", CultureInfo.InvariantCulture)
                           : AseFireStretch.Set(ps as ParticleSystem, share, k, SpreadGrow, SpreadFade, Trace) ? "after " + (share * 100f).ToString("0") + "% x" + k.ToString("0.##", CultureInfo.InvariantCulture) : "not stretched";
                log.Append("\n  '").Append(ps.name).Append("' ").Append(how);
            }
            if (Verbose()) Trace("smoke lifetime x" + k.ToString("0.##", CultureInfo.InvariantCulture) + " from " + (share * 100f).ToString("0") + "%" + log);
        }

        // ------------------------------------------------------------ smoke opacity by the fire's strength within its stage

        static FieldInfo _maxSev, _disp, _fePs2;
        static PropertyInfo _severity;
        static Type _tPs;
        static PropertyInfo _psMain, _mainColor, _gradMode, _gradColor, _gradMin, _gradMax, _gradG, _gradGMin, _gradGMax;

        /// <summary>The smoke systems of an effect and their colours as made (the game's three effects: a smoke system with fire
        /// drawn in its sprites, "shipfire_*", and smoke puffs "shipfire_m_part_2"; the flames "fireemitter" / "Fire_Flares" stay).</summary>
        sealed class Look { public List<object> Ps = new List<object>(); public List<object> Base = new List<object>(); public float K = -1f; }
        static readonly ConditionalWeakTable<object, Look> Looks = new ConditionalWeakTable<object, Look>();
        static readonly HashSet<string> DumpedLook = new HashSet<string>();

        static bool DensityInit(object comp, object sc)
        {
            if (_tPs != null) return true;
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _maxSev = sc.GetType().GetField("_maxFireSeverity", all);
            _severity = sc.GetType().GetProperty("FireSeverity", all);
            _disp = comp.GetType().GetField("_displacement", all);
            _tPs = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule");
            if (_tPs == null) return false;
            _psMain = _tPs.GetProperty("main");
            _mainColor = _psMain == null ? null : _psMain.PropertyType.GetProperty("startColor");
            var tGrad = _mainColor == null ? null : _mainColor.PropertyType;
            if (tGrad != null) { _gradMode = tGrad.GetProperty("mode"); _gradColor = tGrad.GetProperty("color"); _gradMin = tGrad.GetProperty("colorMin"); _gradMax = tGrad.GetProperty("colorMax"); _gradG = tGrad.GetProperty("gradient"); _gradGMin = tGrad.GetProperty("gradientMin"); _gradGMax = tGrad.GetProperty("gradientMax"); }
            return _maxSev != null && _severity != null && _mainColor != null && _gradMode != null && _gradColor != null;
        }

        static Look LookOf(object fireEffect)
        {
            Look l;
            if (Looks.TryGetValue(fireEffect, out l)) return l;
            l = new Look();
            if (_fePs2 == null) _fePs2 = fireEffect.GetType().GetField("_fireEffectPS", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var root = _fePs2 == null ? null : _fePs2.GetValue(fireEffect) as Component;
            if (root != null)
                foreach (var c in root.gameObject.GetComponentsInChildren(_tPs, true))
                {
                    string n = ((Component)c).name ?? "";
                    if (!ReferenceEquals(c, root) && !n.StartsWith("shipfire", StringComparison.OrdinalIgnoreCase)) continue;   // flames: as they are
                    l.Ps.Add(c);
                    l.Base.Add(Detached(_mainColor.GetValue(_psMain.GetValue(c, null), null)));   // start colour as made, own copy
                }
            Looks.Add(fireEffect, l);
            if (root != null && DumpedLook.Add(root.gameObject.name))
            {
                var modes = new List<string>();
                foreach (var g in l.Base) modes.Add(Convert.ToString(_gradMode.GetValue(g, null)) + " a=" + Alpha(g).ToString("0.##", CultureInfo.InvariantCulture));
                Trace("smoke opacity: '" + root.gameObject.name + "' " + l.Ps.Count + " smoke systems, colour modes " + string.Join(",", modes.ToArray()));
            }
            return l;
        }

        /// <summary>Opacity k (0..1) of the smoke, new particles only: the start colour's alpha × k (Color / TwoColors modes).</summary>
        static void Apply(Look l, float k)
        {
            if (Math.Abs(k - l.K) < 0.01f) return;
            if (l.K < 0f) Dimmed.Add(new WeakReference(l));
            if (k < 0.999f) _dimmed = true;
            l.K = k;
            for (int i = 0; i < l.Ps.Count; i++)
            {
                var main = _psMain.GetValue(l.Ps[i], null);
                var grad = l.Base[i];
                int mode = Convert.ToInt32(_gradMode.GetValue(grad, null));
                object g2 = CopyStruct(grad);
                if (mode == 0) { var c = (Color)_gradColor.GetValue(grad, null); c.a *= k; _gradColor.SetValue(g2, c, null); }
                else if (mode == 2 && _gradMin != null && _gradMax != null)
                {
                    var a = (Color)_gradMin.GetValue(grad, null); a.a *= k; _gradMin.SetValue(g2, a, null);
                    var b = (Color)_gradMax.GetValue(grad, null); b.a *= k; _gradMax.SetValue(g2, b, null);
                }
                else if (mode == 1 && _gradG != null) _gradG.SetValue(g2, Faded(_gradG.GetValue(grad, null) as Gradient, k), null);
                else if (mode == 3 && _gradGMin != null && _gradGMax != null)
                {
                    _gradGMin.SetValue(g2, Faded(_gradGMin.GetValue(grad, null) as Gradient, k), null);
                    _gradGMax.SetValue(g2, Faded(_gradGMax.GetValue(grad, null) as Gradient, k), null);
                }
                else continue;   // random colour: left as it is
                _mainColor.SetValue(main, g2, null);   // MainModule is a handle: the setter writes to the particle system
            }
        }

        /// <summary>
        /// The start colour as made, detached from the particle system: a gradient read from it stays tied to the system's own
        /// (seen in game: each new opacity was applied on top of the last one - 0.56, 0.32, 0.19 ... down to 0), so the gradients
        /// are copied key by key once and only these copies are used as the base.
        /// </summary>
        static object Detached(object grad)
        {
            int mode = Convert.ToInt32(_gradMode.GetValue(grad, null));
            if (mode != 1 && mode != 3) return grad;   // plain colours are values
            object g2 = CopyStruct(grad);
            if (mode == 1 && _gradG != null) _gradG.SetValue(g2, Faded(_gradG.GetValue(grad, null) as Gradient, 1f), null);
            if (mode == 3 && _gradGMin != null && _gradGMax != null)
            {
                _gradGMin.SetValue(g2, Faded(_gradGMin.GetValue(grad, null) as Gradient, 1f), null);
                _gradGMax.SetValue(g2, Faded(_gradGMax.GetValue(grad, null) as Gradient, 1f), null);
            }
            return g2;
        }

        /// <summary>A copy of a gradient with every alpha key × k (colours as they are). The game's gradient is not changed:
        /// Gradient is a class, the start colour as made keeps the original.</summary>
        static Gradient Faded(Gradient g, float k)
        {
            if (g == null) return null;
            var a = g.alphaKeys;
            for (int i = 0; i < a.Length; i++) a[i].alpha *= k;
            var r = new Gradient { mode = g.mode };
            r.SetKeys(g.colorKeys, a);
            return r;
        }

        /// <summary>The alpha of a start colour: Color - its alpha; TwoColors - the max's; gradients - the highest alpha key;
        /// random colour - -1.</summary>
        static float Alpha(object grad)
        {
            int mode = Convert.ToInt32(_gradMode.GetValue(grad, null));
            if (mode == 0) return ((Color)_gradColor.GetValue(grad, null)).a;
            if (mode == 2 && _gradMax != null) return ((Color)_gradMax.GetValue(grad, null)).a;
            if (mode == 1 && _gradG != null) return MaxAlpha(_gradG.GetValue(grad, null) as Gradient);
            if (mode == 3 && _gradGMax != null) return Math.Max(MaxAlpha(_gradGMin.GetValue(grad, null) as Gradient), MaxAlpha(_gradGMax.GetValue(grad, null) as Gradient));
            return -1f;
        }

        static float MaxAlpha(Gradient g)
        {
            if (g == null) return -1f;
            float m = 0f;
            foreach (var key in g.alphaKeys) m = Math.Max(m, key.alpha);
            return m;
        }

        static object CopyStruct(object boxed)
        {
            var m = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            return m.Invoke(boxed, null);
        }

        static int[] Snapshot(Array a)
        {
            if (a == null) return null;
            var r = new int[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = Convert.ToInt32(a.GetValue(i));
            return r;
        }

        static bool Same(int[] a, Array b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != Convert.ToInt32(b.GetValue(i))) return false;
            return true;
        }

        /// <summary>The sections a fire point shows: five points - its own; the game's points - as its UpdateFireState groups them.</summary>
        static void Sections(int count, float disp, int p, int n, out int from, out int to)
        {
            if (count == 5) { from = p; to = p; }
            else if (count == 1) { from = 0; to = n - 1; }
            else if (count == 2) { if (p == 0) { from = 0; to = 2; } else { from = 3; to = 4; } }
            else if (disp < 159f) { from = 0; to = n - 1; }
            else if (disp < 900f) { if (p == 0) { from = 0; to = 2; } else { from = 3; to = 4; } }
            else if (p == 0) { from = 0; to = 1; } else if (p == 1) { from = 2; to = 2; } else { from = 3; to = 4; }
        }

        /// <summary>The strongest fire among a point's sections, as a share of the maximum (0..1).</summary>
        static float Strength(Array sys, int from, int to)
        {
            float f = 0f;
            for (int i = from; i <= to && i < sys.Length; i++)
            {
                var sc = sys.GetValue(i);
                if (sc == null) continue;
                float max = Convert.ToSingle(_maxSev.GetValue(sc));
                if (max > 0f) f = Math.Max(f, Convert.ToSingle(_severity.GetValue(sc, null)) / max);
            }
            return f;
        }

        /// <summary>
        /// The look of each fire point by the fire's strength (share of the maximum): small below SmokeModerateFrom, moderate below
        /// SmokeSevereFrom, severe above (the game: 25% / 50%). Only the look: damage, spreading and the game's own stages stay.
        /// A point with no fire (the game's stage No) stays without.
        /// </summary>
        static void Restage(object comp, Array sys, Array fires)
        {
            if (sys.Length < 1 || !DensityInit(comp, sys.GetValue(0))) return;
            int count = (int)_firesCount.GetValue(comp);
            float disp = _disp == null ? 1000f : Convert.ToSingle(_disp.GetValue(comp));
            var tFire = fires.GetType().GetElementType();
            for (int p = 0; p < count && p < fires.Length; p++)
            {
                if (Convert.ToInt32(fires.GetValue(p)) == 0) continue;
                int from, to; Sections(count, disp, p, sys.Length, out from, out to);
                float f = Strength(sys, from, to);
                int stage = f >= SevereFrom ? 3 : f >= ModerateFrom ? 2 : 1;
                fires.SetValue(Enum.ToObject(tFire, stage), p);
            }
        }

        /// <summary>
        /// The small fire's smoke: the opacity of its new particles goes evenly from SmokeStartOpacity at 0% of the fire's maximum
        /// to SmokeEndOpacity at SmokeModerateFrom. The particle rate stays the game's.
        /// The moderate and severe looks stay as the game makes them (their own sprites show the fire's strength).
        /// </summary>
        static void Density(object comp, Array sys)
        {
            if (sys.Length < 1 || !DensityInit(comp, sys.GetValue(0))) return;
            int count = (int)_firesCount.GetValue(comp);
            var fires = _currentFires.GetValue(comp) as Array;
            var small = _small.GetValue(comp) as Array;
            if (fires == null || small == null) return;
            float disp = _disp == null ? 1000f : Convert.ToSingle(_disp.GetValue(comp));
            for (int p = 0; p < count && p < fires.Length && p < small.Length; p++)
            {
                if (Convert.ToInt32(fires.GetValue(p)) != 1) continue;
                var fe = small.GetValue(p);
                if (fe == null) continue;
                int from, to; Sections(count, disp, p, sys.Length, out from, out to);
                float t = ModerateFrom > 0f ? Mathf.Clamp01(Strength(sys, from, to) / ModerateFrom) : 1f;
                Apply(LookOf(fe), Mathf.Lerp(Mathf.Clamp01(StartOpacity), Mathf.Clamp01(EndOpacity), t));
            }
        }

        static PropertyInfo _psCount, _psEmitting, _psPlaying;

        /// <summary>Debug: each burning point once a second - strength, game stage, look, k, rates read back, live particles.</summary>
        static void Probe(object comp, Array sys)
        {
            try
            {
                if (sys.Length < 1 || !DensityInit(comp, sys.GetValue(0))) return;
                int count = (int)_firesCount.GetValue(comp);
                float disp = _disp == null ? 1000f : Convert.ToSingle(_disp.GetValue(comp));
                var fires = _currentFires.GetValue(comp) as Array;
                if (fires == null) return;
                if (_psCount == null) { _psCount = _tPs.GetProperty("particleCount"); _psEmitting = _tPs.GetProperty("isEmitting"); _psPlaying = _tPs.GetProperty("isPlaying"); }
                var sets = new[] { _small.GetValue(comp) as Array, _moderate.GetValue(comp) as Array, _severe.GetValue(comp) as Array };
                for (int p = 0; p < count && p < fires.Length; p++)
                {
                    int look = Convert.ToInt32(fires.GetValue(p));
                    if (look == 0) continue;
                    int from, to; Sections(count, disp, p, sys.Length, out from, out to);
                    var sb = new System.Text.StringBuilder();
                    sb.Append("smoke probe p").Append(p).Append(" f=").Append((Strength(sys, from, to) * 100f).ToString("0.0", CultureInfo.InvariantCulture)).Append('%');
                    sb.Append(" game=");
                    for (int i = from; i <= to && i < sys.Length; i++) { var sc = sys.GetValue(i); sb.Append(sc == null ? 0 : Convert.ToInt32(_fireState.GetValue(sc, null))); }
                    sb.Append(" look=").Append(look);
                    for (int s = 0; s < 3; s++)
                    {
                        var fe = sets[s] == null || p >= sets[s].Length ? null : sets[s].GetValue(p);
                        if (fe == null) continue;
                        var l = LookOf(fe);
                        sb.Append(s == 0 ? " | S" : s == 1 ? " | M" : " | L");
                        if (s == 0) sb.Append(" k=").Append(l.K.ToString("0.00", CultureInfo.InvariantCulture));
                        for (int i = 0; i < l.Ps.Count; i++)
                        {
                            object ps = l.Ps[i];
                            float a = Alpha(_mainColor.GetValue(_psMain.GetValue(ps, null), null));
                            sb.Append(' ').Append(((Component)ps).name.Replace("(Clone)", "")).Append(" a ").Append(a.ToString("0.##", CultureInfo.InvariantCulture))
                              .Append(" n ").Append(_psCount.GetValue(ps, null))
                              .Append((bool)_psPlaying.GetValue(ps, null) ? " play" : " stop")
                              .Append((bool)_psEmitting.GetValue(ps, null) ? "+emit" : "");
                        }
                    }
                    Trace(sb.ToString());
                }
            }
            catch (Exception e) { Trace("smoke probe: " + e.Message); }
        }

        static bool _dimmed;
        static readonly List<WeakReference> Dimmed = new List<WeakReference>();

        /// <summary>The option switched off: every smoke dimmed by it back to the game's opacity.</summary>
        static void ResetLooks()
        {
            foreach (var w in Dimmed) { var l = w.Target as Look; if (l != null) try { Apply(l, 1f); } catch { } }
            Dimmed.Clear();
            _dimmed = false;
        }

        // ------------------------------------------------------------ the game's CreateFireEffect: "pointlight" may be missing

        /// <summary>transform.Find(name)?.gameObject - the game takes .gameObject of Find's result unchecked.</summary>
        public static GameObject SafeChild(Transform t, string name)
        {
            if (t == null) return null;
            var c = t.Find(name);
            return c == null ? null : c.gameObject;
        }

        /// <summary>
        /// Compartments.CreateFireEffect: "Transform.Find("pointlight").gameObject" throws when the effect has no such light
        /// (the next line's null check never runs): the effect gets no sound and the other fire points of the ship are not updated
        /// that time. Find + get_gameObject -> SafeChild.
        /// </summary>
        public static IEnumerable<CodeInstruction> CreateFireEffectTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var safe = typeof(FireSmoke).GetMethod("SafeChild", BindingFlags.Static | BindingFlags.Public);
            int n = 0;
            for (int i = 0; i < list.Count - 1; i++)
            {
                var a = list[i].operand as MethodInfo; var b = list[i + 1].operand as MethodInfo;
                if (a == null || b == null || a.Name != "Find" || a.DeclaringType != typeof(Transform) || b.Name != "get_gameObject") continue;
                list[i] = new CodeInstruction(OpCodes.Call, safe).MoveLabelsFrom(list[i]);
                list.RemoveAt(i + 1);
                n++;
            }
            Trace("CreateFireEffect: " + n + " light lookups made safe");
            return list;
        }

        public static Func<bool> KeepOnStop = () => false;

        /// <summary>Replaces the game's ParticleSystem.Stop() in FireEffect.Deactivate: only emission stops, live particles stay.</summary>
        public static void StopFire(ParticleSystem ps)
        {
            if (ps == null) return;
            if (KeepOnStop()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            else ps.Stop();
        }

        public static IEnumerable<CodeInstruction> DeactivateTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var repl = typeof(FireSmoke).GetMethod("StopFire", BindingFlags.Static | BindingFlags.Public);
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i].operand as MethodInfo;
                if (m == null || m.Name != "Stop" || m.DeclaringType != typeof(ParticleSystem)) continue;
                if (m.GetParameters().Length == 0) { list[i] = new CodeInstruction(OpCodes.Call, repl).MoveLabelsFrom(list[i]); n++; }
            }
            Trace("FireEffect.Deactivate: " + n + " Stop() calls replaced");
            return list;
        }

        // ------------------------------------------------------------ diagnostics: what the fire effects are made of (trace)

        static readonly HashSet<string> Dumped = new HashSet<string>();
        static FieldInfo _fePs;

        /// <summary>Postfix of Compartments.CreateFireEffect: the effect's particle systems, once per effect (for smooth smoke).
        /// Reflection only: the particle and audio modules are not referenced.</summary>
        /// <summary>Debug: a MinMaxCurve as text - its mode and constants or curve keys (time:value).</summary>
        static string Mmc(object v)
        {
            try
            {
                if (v == null || v is string) return "?";
                var t = v.GetType(); int mode = Convert.ToInt32(t.GetProperty("mode").GetValue(v, null));
                Func<string, string> key = n =>
                {
                    var c = t.GetProperty(n).GetValue(v, null) as AnimationCurve;
                    if (c == null) return "-";
                    var ks = new System.Text.StringBuilder();
                    foreach (var k in c.keys) ks.Append(k.time.ToString("0.##", CultureInfo.InvariantCulture)).Append(':').Append(k.value.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ');
                    return "[" + ks.ToString().TrimEnd() + "]";
                };
                string mul = " x" + Convert.ToSingle(t.GetProperty("curveMultiplier").GetValue(v, null)).ToString("0.###", CultureInfo.InvariantCulture);
                switch (mode)
                {
                    case 0: return "const " + t.GetProperty("constant").GetValue(v, null);
                    case 1: return "curve " + key("curve") + mul;
                    case 2: return "twoCurves " + key("curveMin") + " .. " + key("curveMax") + mul;
                    default: return "twoConst " + t.GetProperty("constantMin").GetValue(v, null) + " .. " + t.GetProperty("constantMax").GetValue(v, null);
                }
            }
            catch { return "?"; }
        }

        public static void DumpEffect(object fireEffect)
        {
            try
            {
                if (fireEffect == null) return;
                if (_fePs == null) _fePs = fireEffect.GetType().GetField("_fireEffectPS", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var ps = _fePs == null ? null : _fePs.GetValue(fireEffect) as Component;
                if (ps == null) return;
                var root = ps.gameObject;
                if (!Dumped.Add(root.name)) return;
                var tPs = ps.GetType();
                var tAudio = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule");
                var sb = new System.Text.StringBuilder("fire effect '" + root.name + "':");
                foreach (var c in root.GetComponentsInChildren(tPs, true))
                {
                    object main = P(c, "main"), em = P(c, "emission");
                    sb.Append("\n  PS '").Append(((Component)c).name).Append("' rate ").Append(P(em, "rateOverTimeMultiplier"))
                      .Append(" dist ").Append(P(em, "rateOverDistanceMultiplier")).Append(" bursts ").Append(P(em, "burstCount"))
                      .Append(" size ").Append(P(main, "startSizeMultiplier")).Append(" life ").Append(P(main, "startLifetimeMultiplier"))
                      .Append(" max ").Append(P(main, "maxParticles")).Append(" loop ").Append(P(main, "loop"));
                    sb.Append("\n    main speed ").Append(P(main, "simulationSpeed")).Append(" delay ").Append(Mmc(P(main, "startDelay"))).Append(" prewarm ").Append(P(main, "prewarm")).Append(" duration ").Append(P(main, "duration")).Append(" scaling ").Append(P(main, "scalingMode")).Append(" space ").Append(P(main, "simulationSpace")).Append(" gravity ").Append(Mmc(P(main, "gravityModifier"))).Append(" lossyScale ").Append(((Component)c).transform.lossyScale.ToString("0.###"));
                    object shp = P(c, "shape");
                    sb.Append("\n    shape on=").Append(P(shp, "enabled")).Append(" type ").Append(P(shp, "shapeType")).Append(" radius ").Append(P(shp, "radius")).Append(" scale ").Append(P(shp, "scale"));
                    sb.Append("\n    modules on:");
                    foreach (var mn in new[] { "velocityOverLifetime", "limitVelocityOverLifetime", "inheritVelocity", "forceOverLifetime", "colorOverLifetime", "colorBySpeed", "sizeBySpeed", "rotationOverLifetime", "rotationBySpeed", "noise", "externalForces", "subEmitters", "trails", "lights" })
                    { object md = P(c, mn); if (md != null && !(md is string) && Convert.ToString(P(md, "enabled")) == "True") sb.Append(' ').Append(mn); }
                    object sub = P(c, "subEmitters");
                    if (sub != null && !(sub is string) && Convert.ToString(P(sub, "enabled")) == "True")
                        try
                        {
                            int sn = Convert.ToInt32(P(sub, "subEmittersCount")); sb.Append("\n    subEmitters:");
                            var gs = sub.GetType().GetMethod("GetSubEmitterSystem"); var gt = sub.GetType().GetMethod("GetSubEmitterType");
                            for (int si = 0; si < sn; si++) sb.Append(" '").Append(((Component)gs.Invoke(sub, new object[] { si })).name).Append("' ").Append(gt.Invoke(sub, new object[] { si }));
                        }
                        catch { }
                    sb.Append("\n    startSize ").Append(Mmc(P(main, "startSize"))).Append(" startLifetime ").Append(Mmc(P(main, "startLifetime"))).Append(" speed ").Append(Mmc(P(main, "startSpeed")));
                    object sol = P(c, "sizeOverLifetime"), tsa = P(c, "textureSheetAnimation");
                    sb.Append("\n    sizeOverLifetime on=").Append(P(sol, "enabled")).Append(" sep=").Append(P(sol, "separateAxes")).Append(" size ").Append(Mmc(P(sol, "size")));
                    sb.Append("\n    sheet on=").Append(P(tsa, "enabled")).Append(" tiles ").Append(P(tsa, "numTilesX")).Append("x").Append(P(tsa, "numTilesY")).Append(" frame ").Append(Mmc(P(tsa, "frameOverTime"))).Append(" start ").Append(Mmc(P(tsa, "startFrame")));
                    DumpMaterial(sb, ((Component)c).GetComponent<Renderer>());
                }
                foreach (var l in root.GetComponentsInChildren<Light>(true)) sb.Append("\n  Light '").Append(l.name).Append("' ").Append(l.intensity.ToString("0.##"));
                if (tAudio != null) foreach (var a in root.GetComponentsInChildren(tAudio, true)) sb.Append("\n  Audio '").Append(((Component)a).name).Append("' ").Append(P(a, "volume"));
                Trace(sb.ToString());
            }
            catch (Exception e) { Trace("FireSmoke.DumpEffect: " + e.Message); }
        }

        /// <summary>Debug: the material of a particle renderer - shader, queue and its colour / number properties (for the smoke colour question).</summary>
        static void DumpMaterial(System.Text.StringBuilder sb, Renderer r)
        {
            try
            {
                var m = r == null ? null : r.sharedMaterial;
                if (m == null || m.shader == null) return;
                var sh = m.shader;
                sb.Append("\n    renderer");
                foreach (var pn0 in new[] { "renderMode", "sortMode", "sortingFudge", "sortingOrder", "sortingLayerID", "maxParticleSize", "alignment" })
                { var pi = r.GetType().GetProperty(pn0); if (pi != null) { try { sb.Append(' ').Append(pn0).Append('=').Append(pi.GetValue(r, null)); } catch { } } }
                sb.Append("\n    material '").Append(m.name).Append("' shader '").Append(sh.name).Append("' queue ").Append(m.renderQueue);
                for (int i = 0; i < sh.GetPropertyCount(); i++)
                {
                    string pn = sh.GetPropertyName(i); var t = sh.GetPropertyType(i);
                    if (t == UnityEngine.Rendering.ShaderPropertyType.Color) sb.Append(' ').Append(pn).Append('=').Append(m.GetColor(pn));
                    else if (t == UnityEngine.Rendering.ShaderPropertyType.Float || t == UnityEngine.Rendering.ShaderPropertyType.Range) sb.Append(' ').Append(pn).Append('=').Append(m.GetFloat(pn).ToString("0.###", CultureInfo.InvariantCulture));
                }
            }
            catch (Exception e) { sb.Append(" (material: ").Append(e.Message).Append(')'); }
        }

        // ------------------------------------------------------------ funnel smoke by the engine type and load

        public static Func<float> FunnelGlobal = () => 1f;                  // [FunnelSmoke] GlobalDensity
        public static Func<float> FunnelGlobalThick = () => 1f;             // [FunnelSmoke] GlobalThickness
        public static Func<string, float[]> FunnelNation = n => null;      // [FunnelSmoke] Nations: nation -> the same multipliers
        public static Func<string, float[]> FunnelShip = n => null;        // [FunnelSmoke] Ships: ini name -> particles, duration, lightness, thickness multipliers
        public static Func<bool> FunnelOn = () => false;                    // [FunnelSmoke] Enabled
        public static Func<float> FunnelPowerExp = () => 0f, FunnelPowerRef = () => 50000f, FunnelSizeExp = () => 0f;
        public static Func<string, float[]> FunnelGroup = g => null;        // group -> density idle, full, lightness idle, full (0..1), thickness idle, full, lifetime idle, full
        sealed class FSys { public object Ps; public float Rate; public int Max; public object Col, LifeCol; public Renderer Rend; public Color Tint, Work; public bool HasTint; public float Size = 1f, Life = 1f, SzMult = 1f; public Keyframe[] SzKeys; public bool SzOn, SzOk; }
        sealed class FState { public List<FSys> S = new List<FSys>(); public float D = -1f, B = -1f, O = -1f, Z = -1f, Om = -1f, K = -1f; }
        static readonly ConditionalWeakTable<object, FState> Funnels = new ConditionalWeakTable<object, FState>();
        static FieldInfo _cVessel, _vInUse, _vVel; static PropertyInfo _vMaxSpeed, _psType; static PropertyInfo _lEm2, _lRate2;
        static Dictionary<string, string> _forced;
        static PropertyInfo _maxP;
        static PropertyInfo _lMaxP() { return _maxP ?? (_maxP = _psMain.PropertyType.GetProperty("maxParticles")); }

        /// <summary>funnel_groups.txt (next to the dll): "ini_name = Group" forces a ship into a group; Diesel, Steam, GasTurbine, Nuclear, Combined.</summary>
        static Dictionary<string, string> Forced()
        {
            if (_forced != null) return _forced;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(FireSmoke).Assembly.Location) ?? "", "funnel_groups.txt");
                if (System.IO.File.Exists(path))
                    foreach (var raw in System.IO.File.ReadAllLines(path))
                    {
                        string line = raw.Trim(); int eq = line.IndexOf('=');
                        if (line.Length == 0 || line[0] == ';' || line[0] == '#' || eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim(); if (k.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) k = k.Substring(0, k.Length - 4);
                        d[k] = line.Substring(eq + 1).Trim();
                    }
            }
            catch (Exception e) { Trace("funnel_groups.txt: " + e.Message); }
            return _forced = d;
        }

        /// <summary>
        /// The funnel smoke of a ship (the game only plays / stops it): the emission rate and the start colour follow the engine group (the type of
        /// the power system in use, or the group forced in funnel_groups.txt) and the load (speed / maximum speed): density x (idle -> full power),
        /// brightness x of the colour, opacity %. Computed from the prefab's own values; once a second with the fire's update.
        /// </summary>
        static void Funnel(object comp, Array sys)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (sys.Length < 1 || !DensityInit(comp, sys.GetValue(0))) return;
            if (_cVp == null) _cVp = comp.GetType().GetField("_vp", all);
            var vp = _cVp == null ? null : _cVp.GetValue(comp);
            if (vp == null) return;
            if (_vpFunnel == null) { _vpFunnel = vp.GetType().GetField("_funnelSmoke", all); _vpName = vp.GetType().GetField("_objectIniName", all); _vpHp = vp.GetType().GetField("_horsePower", all); }
            var root = _vpFunnel == null ? null : _vpFunnel.GetValue(vp) as Component;
            if (root == null) return;
            if (_lEm2 == null) { _lEm2 = _tPs.GetProperty("emission"); _lRate2 = _lEm2 == null ? null : _lEm2.PropertyType.GetProperty("rateOverTimeMultiplier"); if (_lRate2 == null) return; }
            FState st;
            if (!Funnels.TryGetValue(root, out st))
            {
                st = new FState(); Funnels.Add(root, st);
                foreach (var c in root.gameObject.GetComponentsInChildren(_tPs, true))
                    st.S.Add(new FSys { Ps = c, LifeCol = null, Rend = ((Component)c).GetComponent<Renderer>(), Max = Convert.ToInt32(_lMaxP().GetValue(_psMain.GetValue(c, null), null)), Rate = Convert.ToSingle(_lRate2.GetValue(_lEm2.GetValue(c, null), null)), Col = Detached(_mainColor.GetValue(_psMain.GetValue(c, null), null)) }); SizeInit(st.S[st.S.Count - 1]);
            }
            float d = 1f, b = -1f, o = 1f, om = 1f, k = 1f; string group = "-";
            if (FunnelOn())
            {
                string name = _vpName == null ? "" : Convert.ToString(_vpName.GetValue(vp));
                if (!Forced().TryGetValue(name, out group)) group = GroupOf(comp);
                var p = FunnelGroup(group);
                if (p != null)
                {
                    float load = group == "Combined" ? BoostLoad(comp) : Load(comp);
                    d = Math.Max(0f, FunnelGlobal()) * Mathf.Lerp(p[0], p[1], load) * Ratio(vp, FunnelPowerExp(), 1f, 2f); b = Mathf.Clamp01(Mathf.Lerp(p[2], p[3], load)); o = Mathf.Clamp(Math.Max(0f, FunnelGlobalThick()) * Mathf.Lerp(p[4], p[5], load), 0f, 100f); om = Mathf.Clamp(Math.Max(0f, FunnelGlobalThick()) * Math.Max(p[4], p[5]), 1e-4f, 100f); k = Mathf.Clamp(Mathf.Lerp(p[6], p[7], load), 0.01f, 1f);
                    float tm = 1f;
                    _mine[0] = FunnelNation(NationOf(vp)); _mine[1] = FunnelShip(name);
                    foreach (var mine in _mine)
                        if (mine != null) { d *= mine[0]; k = Mathf.Clamp(k * mine[1], 0.01f, 10f); if (b >= 0f) b = Mathf.Clamp01(b * mine[2]); tm *= mine[3]; }
                    o = Mathf.Clamp(o * tm, 0f, 100f); om = Mathf.Clamp(om * tm, 1e-4f, 100f);
                }
            }
            Track(st, SortFix() || AfterClouds(), () => { var rs = new List<Renderer>(); foreach (var s in st.S) if (s.Rend != null) rs.Add(s.Rend); return rs; });
            float z = FunnelOn() ? Ratio(vp, FunnelSizeExp()) : 1f;
            if (Math.Abs(d - st.D) < 0.02f && Math.Abs(b - st.B) < 0.02f && Math.Abs(o - st.O) < 0.005f && Math.Abs(om - st.Om) < 0.005f && Math.Abs(k - st.K) < 0.005f && Math.Abs(z - st.Z) < 0.01f) return;
            st.D = d; st.B = b; st.O = o; st.Om = om; st.K = k; st.Z = z;
            foreach (var s in st.S)
            {
                _lRate2.SetValue(_lEm2.GetValue(s.Ps, null), s.Rate * d, null);
                var main = _psMain.GetValue(s.Ps, null);
                _lMaxP().SetValue(main, (int)Math.Min(100000f, s.Max * Math.Max(1f, d) * Math.Max(1f, k) + 1f), null);   // room for the particles
                ApplyTint(s, b, o, om);
                if (_mLifeMul != null) _mLifeMul.SetValue(main, s.Life * k, null);
                ApplySize(s, z, k);
            }
            if (Verbose()) Trace("funnel smoke: '" + root.gameObject.name + "' group " + group + " density x" + d.ToString("0.00", CultureInfo.InvariantCulture) + " lightness " + (b < 0f ? "game" : (b * 100f).ToString("0") + "%") + " opacity " + (o * 100f).ToString("0") + "% duration x" + k.ToString("0.00", CultureInfo.InvariantCulture));
        }

        static FieldInfo _vpNation;

        /// <summary>The nation of the ship (its variant's Nation= in the ini; the nation override of the mission counts).</summary>
        static string NationOf(object vp)
        {
            try
            {
                if (_vpNation == null) _vpNation = FindField(vp.GetType(), "_nation");
                return _vpNation == null ? "" : Convert.ToString(_vpNation.GetValue(vp)) ?? "";
            }
            catch { return ""; }
        }

        /// <summary>(horsepower / reference) ^ exponent, limited to 0.1..4: 1 when the exponent is 0 or the power is unknown.</summary>
        static float Ratio(object vp, float e, float lo = 0.1f, float hi = 4f)
        {
            float r = Math.Max(1f, FunnelPowerRef());
            if (e <= 0f || _vpHp == null) return 1f;
            float hp; try { hp = Convert.ToSingle(_vpHp.GetValue(vp)); } catch { return 1f; }
            if (hp <= 0f) return 1f;
            return Mathf.Clamp(Mathf.Pow(hp / r, e), lo, hi);
        }

        static PropertyInfo _mLifeMul, _mSizeMul, _tSizeLife, _slEnabled, _slSize, _cvMode, _cvCurve, _cvMul; static ConstructorInfo _cvCtor;

        /// <summary>The prefab's size: the start size and the size-over-lifetime curve (null when that module is off).</summary>
        static void SizeInit(FSys s)
        {
            try
            {
                var main = _psMain.GetValue(s.Ps, null);
                if (_mSizeMul == null) _mSizeMul = _psMain.PropertyType.GetProperty("startSizeMultiplier");
                if (_tSizeLife == null)
                {
                    _tSizeLife = _tPs.GetProperty("sizeOverLifetime");
                    _slEnabled = _tSizeLife.PropertyType.GetProperty("enabled"); _slSize = _tSizeLife.PropertyType.GetProperty("size");
                    _cvMode = _slSize.PropertyType.GetProperty("mode"); _cvCurve = _slSize.PropertyType.GetProperty("curve"); _cvMul = _slSize.PropertyType.GetProperty("curveMultiplier");
                    _cvCtor = _slSize.PropertyType.GetConstructor(new[] { typeof(float), typeof(AnimationCurve) });
                }
                if (_mSizeMul == null || _cvCtor == null) return;
                s.Size = Convert.ToSingle(_mSizeMul.GetValue(main, null));
                if (_mLifeMul == null) _mLifeMul = _psMain.PropertyType.GetProperty("startLifetimeMultiplier");
                if (_mLifeMul != null) s.Life = Convert.ToSingle(_mLifeMul.GetValue(main, null));
                var sl = _tSizeLife.GetValue(s.Ps, null);
                if (!(bool)_slEnabled.GetValue(sl, null)) { s.SzKeys = new[] { new Keyframe(0f, 1f), new Keyframe(1f, 1f) }; s.SzOn = false; s.SzOk = true; return; }
                var cv = _slSize.GetValue(sl, null);
                if (Convert.ToInt32(_cvMode.GetValue(cv, null)) != 1) { s.SzOk = false; return; }   // only a single curve is re-scaled
                s.SzKeys = ((AnimationCurve)_cvCurve.GetValue(cv, null)).keys; s.SzMult = Convert.ToSingle(_cvMul.GetValue(cv, null)); s.SzOn = true; s.SzOk = true;
            }
            catch { s.SzOk = false; }
        }

        /// <summary>Size by the engines' power: a large ship (z > 1) starts with bigger particles and ends at the game's size, a small one (z < 1) starts at the game's size and ends smaller.</summary>
        static void ApplySize(FSys s, float z, float k)
        {
            try
            {
                var main = _psMain.GetValue(s.Ps, null);
                if (_mSizeMul != null) _mSizeMul.SetValue(main, s.Size * Math.Max(1f, z), null);
                if (!s.SzOk || s.SzKeys == null) return;
                float e = z > 1f ? 1f / z : z, kk = Mathf.Clamp(k, 0.01f, 1f);
                var sl = _tSizeLife.GetValue(s.Ps, null);
                if (!s.SzOn && e >= 0.999f) { _slEnabled.SetValue(sl, false, null); return; }
                // A shorter life must not make the sprite grow faster: the particle lives kk of the game's time, so it follows only the first kk of the game's
                // size curve, stretched over its (shorter) life - the same size at the same age in seconds, the end of the curve is simply never reached.
                var src = new AnimationCurve(s.SzKeys);
                var list = new List<Keyframe>();
                if (kk >= 0.999f) list.AddRange(s.SzKeys);
                else
                {
                    foreach (var key in s.SzKeys) if (key.time < kk - 1e-4f) { var c = key; c.time = key.time / kk; c.inTangent *= kk; c.outTangent *= kk; list.Add(c); }
                    float tan = (src.Evaluate(Math.Min(1f, kk + 0.001f)) - src.Evaluate(Math.Max(0f, kk - 0.001f))) / 0.002f * kk;
                    list.Add(new Keyframe(1f, src.Evaluate(kk), tan, tan));
                }
                var arr = list.ToArray();
                for (int i = 0; i < arr.Length; i++) { float f = Mathf.Lerp(1f, e, Mathf.Clamp01(arr[i].time)); arr[i].value *= f; arr[i].inTangent *= f; arr[i].outTangent *= f; }
                _slSize.SetValue(sl, _cvCtor.Invoke(new object[] { s.SzMult, new AnimationCurve(arr) }), null);
                _slEnabled.SetValue(sl, true, null);
            }
            catch { }
        }

        /// <summary>
        /// Lightness and thickness. The funnel smoke material is Alpha Blended: colour = 2 x _TintColor x particle colour x texture, _TintColor alpha is only 0.02 and the
        /// particle's own alpha is already 1. A plain multiplier of _TintColor keeps the hue of the texture and of the particle colours (brownish): more of it
        /// only went from black to orange, never white. So _TintColor is set per channel: the average colour of the smoke (the texture's, weighted by its alpha,
        /// read once through a small render texture, x the start colour x the colour over the lifetime) becomes a neutral grey of the wanted lightness
        /// (0 black .. 1 white), in the project's colour space. Thickness multiplies the alpha. Per renderer through a property block (the material is shared).
        /// The tint is fixed (white at the group's highest thickness); the load changes only the particles' start colour (a fraction of it), so smoke already in the air keeps its look.
        /// l &lt; 0: the game's colour.
        /// </summary>
        static void ApplyTint(FSys s, float l, float o, float om)
        {
            try
            {
                if (s.Rend == null) return;
                bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
                if (!s.HasTint)
                {
                    var m = s.Rend.sharedMaterial;
                    if (m == null || !m.HasProperty("_TintColor")) { s.HasTint = true; s.Rend = null; return; }
                    s.Tint = m.GetColor("_TintColor"); s.HasTint = true;
                    s.Work = SmokeColour(s, m);
                }
                // The tint (per renderer, it changes every particle of the system) is fixed: the brightest and most opaque smoke the group can make.
                // What changes with the engine's load is only the start colour of the particles (a fraction of that), so smoke already in the air keeps its look.
                var tl = new float[3]; var wk = new[] { s.Work.r, s.Work.g, s.Work.b };
                for (int i = 0; i < 3; i++) tl[i] = l < 0f ? 1f : Math.Min(16f, 1f / (2f * Math.Max(wk[i], 1e-3f)));      // linear tint that makes white
                var pb = new MaterialPropertyBlock();
                s.Rend.GetPropertyBlock(pb);
                Color c;
                if (l < 0f) c = s.Tint;
                else
                {
                    Func<float, float> g = v => lin ? Mathf.LinearToGammaSpace(v) : v;
                    c = new Color(g(tl[0]), g(tl[1]), g(tl[2]), 0f);
                }
                c.a = s.Tint.a * om;
                pb.SetColor("_TintColor", c);
                s.Rend.SetPropertyBlock(pb);
                // the particles' start colour: lightness as a fraction of white (per channel, what the tint can reach), opacity as a fraction of the tint's
                float[] f = { 1f, 1f, 1f };
                if (l >= 0f)
                {
                    float want = lin ? Mathf.GammaToLinearSpace(l) : l;
                    for (int i = 0; i < 3; i++)
                    {
                        float top = Math.Min(1f, 2f * tl[i] * Math.Max(wk[i], 1e-3f)), fl = Mathf.Clamp01(want / Math.Max(top, 1e-3f));
                        f[i] = lin ? Mathf.LinearToGammaSpace(fl) : fl;
                    }
                }
                float fa = Mathf.Clamp01(o / om);
                Func<Color, Color> sc = k => new Color(k.r * f[0], k.g * f[1], k.b * f[2], k.a * fa);
                var gr = (ParticleSystem.MinMaxGradient)s.Col;
                ParticleSystem.MinMaxGradient ng;
                if (gr.mode == ParticleSystemGradientMode.Color) ng = new ParticleSystem.MinMaxGradient(sc(gr.color));
                else if (gr.mode == ParticleSystemGradientMode.TwoColors) ng = new ParticleSystem.MinMaxGradient(sc(gr.colorMin), sc(gr.colorMax));
                else return;
                _mainColor.SetValue(_psMain.GetValue(s.Ps, null), ng, null);
            }
            catch { }
        }

        static readonly Dictionary<int, Color> TexAvg = new Dictionary<int, Color>();

        /// <summary>The smoke's average colour without the tint, in the working colour space: texture (alpha weighted) x start colour x colour over the lifetime (alpha weighted).</summary>
        static Color SmokeColour(FSys s, Material m)
        {
            bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Func<Color, Color> w = c => lin ? c.linear : c;
            Color tex = Color.white;
            var t = m.mainTexture;
            if (t != null && !TexAvg.TryGetValue(t.GetInstanceID(), out tex))
            {
                tex = Color.white;
                RenderTexture rt = null; Texture2D rd = null; var prev = RenderTexture.active;
                try
                {
                    rt = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    Graphics.Blit(t, rt);
                    RenderTexture.active = rt;
                    rd = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
                    rd.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); rd.Apply(false);
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    foreach (var px in rd.GetPixels()) { r += px.r * px.a; g += px.g * px.a; b += px.b * px.a; a += px.a; }
                    if (a > 1e-3f) tex = new Color(r / a, g / a, b / a, 1f);
                }
                catch (Exception e) { Trace("funnel smoke: texture colour not read: " + e.Message); }
                finally { RenderTexture.active = prev; if (rt != null) RenderTexture.ReleaseTemporary(rt); if (rd != null) UnityEngine.Object.Destroy(rd); }
                TexAvg[t.GetInstanceID()] = tex;
                Trace("funnel smoke: texture '" + t.name + "' average colour " + tex);
            }
            Color v = Color.white;
            try
            {
                var sc = (ParticleSystem.MinMaxGradient)s.Col;
                if (sc.mode == ParticleSystemGradientMode.Color) v = w(sc.color);
                else if (sc.mode == ParticleSystemGradientMode.TwoColors) v = (w(sc.colorMin) + w(sc.colorMax)) * 0.5f;
                var ps = s.Ps as ParticleSystem;
                var col = ps == null ? default(ParticleSystem.ColorOverLifetimeModule) : ps.colorOverLifetime;
                if (ps != null && col.enabled && col.color.mode == ParticleSystemGradientMode.Gradient && col.color.gradient != null)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int i = 0; i <= 32; i++) { var e = col.color.gradient.Evaluate(i / 32f); var we = w(e); r += we.r * e.a; g += we.g * e.a; b += we.b * e.a; a += e.a; }
                    if (a > 1e-3f) v = new Color(v.r * r / a, v.g * g / a, v.b * b / a, 1f);
                }
            }
            catch { }
            var res = new Color(v.r * tex.r, v.g * tex.g, v.b * tex.b, 1f);
            var game = new Color(2f * w(s.Tint).r * res.r, 2f * w(s.Tint).g * res.g, 2f * w(s.Tint).b * res.b);
            Trace("funnel smoke: '" + s.Rend.name + "' smoke colour " + res + ", the game's result " + (lin ? game.gamma : game));
            return res;
        }

        static PropertyInfo _colLife, _colLifeOn, _colLifeCol;

        /// <summary>The colour-over-lifetime of a system as the prefab made it (null when it is off or not a gradient).</summary>
        static object BaseLifeColour(object ps)
        {
            try
            {
                if (_colLife == null) { _colLife = _tPs.GetProperty("colorOverLifetime"); _colLifeOn = _colLife.PropertyType.GetProperty("enabled"); _colLifeCol = _colLife.PropertyType.GetProperty("color"); }
                var m = _colLife.GetValue(ps, null);
                if (!(bool)_colLifeOn.GetValue(m, null)) return null;
                var c = _colLifeCol.GetValue(m, null);
                int mode = Convert.ToInt32(_gradMode.GetValue(c, null));
                return mode == 1 || mode == 3 ? Detached(c) : null;
            }
            catch { return null; }
        }

        static string LifeAlpha(object ps)
        {
            try { var b = BaseLifeColour(ps); return b == null ? "-" : Alpha(b).ToString("0.##", CultureInfo.InvariantCulture); } catch { return "?"; }
        }
        /// <summary>The group of a ship: the type of the power system in use (a combined plant switches), Electric / AIP / None count as no smoke (Nuclear).</summary>
        static string GroupOf(object comp)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                if (_cVessel == null) _cVessel = comp.GetType().GetField("_vessel", all);
                var v = _cVessel == null ? null : _cVessel.GetValue(comp);
                if (v == null) return "Steam";
                if (_vInUse == null) _vInUse = FindField(v.GetType(), "_powerSystemInUse");
                var ps = _vInUse == null ? null : _vInUse.GetValue(v);
                if (ps == null) return "Steam";
                if (_psType == null) _psType = ps.GetType().GetProperty("Type", all);
                string t = Convert.ToString(_psType.GetValue(ps, null));
                return t == "Diesel" || t == "Steam" || t == "GasTurbine" || t == "Nuclear" ? t : "Nuclear";
            }
            catch { return "Steam"; }
        }

        static FieldInfo FindField(Type t, string name)
        {
            for (; t != null; t = t.BaseType) { var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); if (f != null) return f; }
            return null;
        }

        static PropertyInfo _vCmd, _cmdValue; static MethodInfo _cmdDemanded;

        /// <summary>The speed the ship is ordered to make, knots (-1 when it cannot be read).</summary>
        static float Demanded(object v)
        {
            try
            {
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                if (_vCmd == null) _vCmd = v.GetType().GetProperty("SpeedCommand", all);
                var cmd = _vCmd == null ? null : _vCmd.GetValue(v, null);
                if (cmd == null) return -1f;
                if (_cmdValue == null) _cmdValue = cmd.GetType().GetProperty("Value", all);
                var val = _cmdValue == null ? null : _cmdValue.GetValue(cmd, null);
                if (val == null) return -1f;
                if (_cmdDemanded == null) _cmdDemanded = val.GetType().GetMethod("GetDemandedSpeedInKnots", all);
                return _cmdDemanded == null ? -1f : Convert.ToSingle(_cmdDemanded.Invoke(val, new object[] { v }));
            }
            catch { return -1f; }
        }

        /// <summary>The load of the plant, 0..1: the speed over the maximum speed; a ship that is speeding up (ordered speed above the real one) counts as at full power.</summary>
        static float Load(object comp)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var v = _cVessel == null ? null : _cVessel.GetValue(comp);
                if (v == null) return 0f;
                if (_vVel == null) { _vVel = FindField(v.GetType(), "_velocityInKnots"); _vMaxSpeed = v.GetType().GetProperty("MaxForwardSpeedInKnots", all); }
                if (_vVel == null || _vMaxSpeed == null) return 0f;
                float max = Convert.ToSingle(_vMaxSpeed.GetValue(v, null));
                float speed = Math.Abs(Convert.ToSingle(_vVel.GetValue(v)));
                if (max <= 0.1f) return 0f;
                float want = Demanded(v);
                if (want > speed + Math.Max(0.5f, 0.03f * max)) return 1f;   // speeding up: full power
                return Mathf.Clamp01(speed / max);
            }
            catch { return 0f; }
        }

        /// <summary>
        /// Nuclear + oil-fired boost: the nuclear plant alone drives the ship up to the speed of the full-ahead telegraph (4/5 of the maximum), the oil-fired
        /// boilers are lit only above it. 0 up to that speed, 1 at the maximum; a flank speed ordered while the ship is still speeding up counts as 1.
        /// </summary>
        static float BoostLoad(object comp)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var v = _cVessel == null ? null : _cVessel.GetValue(comp);
                if (v == null) return 0f;
                if (_vVel == null) { _vVel = FindField(v.GetType(), "_velocityInKnots"); _vMaxSpeed = v.GetType().GetProperty("MaxForwardSpeedInKnots", all); }
                if (_vVel == null || _vMaxSpeed == null) return 0f;
                float max = Convert.ToSingle(_vMaxSpeed.GetValue(v, null)), speed = Math.Abs(Convert.ToSingle(_vVel.GetValue(v)));
                if (max <= 0.1f) return 0f;
                float full = 0.8f * max;                                   // the full-ahead telegraph (4 of 5)
                float want = Demanded(v);
                if (want > full + 0.03f * max && want > speed + Math.Max(0.5f, 0.03f * max)) return 1f;   // flank ordered, speeding up
                return Mathf.Clamp01((speed - full) / Math.Max(0.01f, max - full));
            }
            catch { return 0f; }
        }

        static Color Tint(Color c, float rgb, float a) { return new Color(Mathf.Clamp01(c.r * rgb), Mathf.Clamp01(c.g * rgb), Mathf.Clamp01(c.b * rgb), Mathf.Clamp01(c.a * a)); }

        static Gradient Tint(Gradient g, float rgb, float a)
        {
            if (g == null) return null;
            var ck = g.colorKeys; var ak = g.alphaKeys;
            for (int i = 0; i < ck.Length; i++) ck[i].color = Tint(ck[i].color, rgb, 1f);
            for (int i = 0; i < ak.Length; i++) ak[i].alpha = Mathf.Clamp01(ak[i].alpha * a);
            var r = new Gradient { mode = g.mode };
            r.SetKeys(ck, ak);
            return r;
        }

        static readonly HashSet<string> DumpedFunnel = new HashSet<string>();
        static FieldInfo _cVp, _vpFunnel, _vpName, _vpHp;

        /// <summary>Debug: the funnel smoke of a ship (once per ship class) - how its prefab emits and what colour it starts with (to tune it by engine type and load).</summary>
        static void DumpFunnel(object comp)
        {
            try
            {
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                if (_cVp == null) _cVp = comp.GetType().GetField("_vp", all);
                var vp = _cVp == null ? null : _cVp.GetValue(comp);
                if (vp == null) return;
                if (_vpFunnel == null) { _vpFunnel = vp.GetType().GetField("_funnelSmoke", all); _vpName = vp.GetType().GetField("_objectIniName", all); _vpHp = vp.GetType().GetField("_horsePower", all); }
                var ps = _vpFunnel == null ? null : _vpFunnel.GetValue(vp) as Component;
                string name = _vpName == null ? "?" : Convert.ToString(_vpName.GetValue(vp));
                if (ps == null || !DumpedFunnel.Add(name)) return;
                var sb = new System.Text.StringBuilder("funnel smoke of '" + name + "' (hp " + (_vpHp == null ? "?" : Convert.ToString(_vpHp.GetValue(vp))) + "), object '" + ps.gameObject.name + "':");
                foreach (var c in ps.gameObject.GetComponentsInChildren(ps.GetType(), true))
                {
                    object main = P(c, "main"), em = P(c, "emission"), col = P(main, "startColor");
                    sb.Append("\n  PS '").Append(((Component)c).name).Append("' rate ").Append(P(em, "rateOverTimeMultiplier")).Append(" dist ").Append(P(em, "rateOverDistanceMultiplier"))
                      .Append(" bursts ").Append(P(em, "burstCount")).Append(" size ").Append(P(main, "startSizeMultiplier")).Append(" life ").Append(P(main, "startLifetimeMultiplier"))
                      .Append(" speed ").Append(P(main, "startSpeedMultiplier")).Append(" max ").Append(P(main, "maxParticles")).Append(" space ").Append(P(main, "simulationSpace"))
                      .Append(" colour mode ").Append(P(col, "mode")).Append(" ").Append(P(col, "color")).Append(" ").Append(P(col, "colorMin")).Append(" ").Append(P(col, "colorMax"))
                      .Append(" colorOverLifetime ").Append(P(P(c, "colorOverLifetime"), "enabled")).Append(" (max alpha ").Append(LifeAlpha(c)).Append(")");
                    DumpMaterial(sb, ((Component)c).GetComponent<Renderer>());
                }
                Trace(sb.ToString());
            }
            catch (Exception e) { Trace("FireSmoke.DumpFunnel: " + e.Message); }
        }

        static object P(object o, string name)
        {
            try { var p = o == null ? null : o.GetType().GetProperty(name); return p == null ? "?" : p.GetValue(o, null); } catch { return "?"; }
        }

        /// <summary>Postfix of Compartments.UpdateFireState: each of the five points shows its own section's fire.</summary>
        public static void AfterUpdateFireState(object comp)
        {
            if (!_ok || comp == null) return;
            try
            {
                var fires = _currentFires.GetValue(comp) as Array;
                var sys = _sys.GetValue(comp) as Array;
                if (fires == null || sys == null) return;
                Mark m;
                if (Five.TryGetValue(comp, out m))
                {
                    int count = (int)_firesCount.GetValue(comp);
                    if (count == 5)
                        for (int i = 0; i < 5 && i < fires.Length && i < sys.Length; i++)
                        {
                            var sc = sys.GetValue(i);
                            if (sc != null) fires.SetValue(_fireState.GetValue(sc, null), i);
                        }
                    else if (count == 1 || count == 2)
                        for (int p = 0; p < count && p < fires.Length; p++)
                        {
                            int from, to; Sections(count, 1000f, p, sys.Length, out from, out to);
                            int worst = 0;
                            for (int i = from; i <= to && i < sys.Length; i++)
                            {
                                var sc = sys.GetValue(i);
                                if (sc != null) worst = Math.Max(worst, Convert.ToInt32(_fireState.GetValue(sc, null)));
                            }
                            fires.SetValue(Enum.ToObject(fires.GetType().GetElementType(), worst), p);
                        }
                }
                if (DensityOn()) Restage(comp, sys, fires);
            }
            catch (Exception e) { Trace("FireSmoke.AfterUpdateFireState: " + e.Message); }
        }
    }
}
