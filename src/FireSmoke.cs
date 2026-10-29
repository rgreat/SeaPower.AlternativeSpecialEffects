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

        // ------------------------------------------------------------ diagnostics: what the fire effects are made of (trace)

        static readonly HashSet<string> Dumped = new HashSet<string>();
        static FieldInfo _fePs;

        /// <summary>Postfix of Compartments.CreateFireEffect: the effect's particle systems, once per effect (for smooth smoke).
        /// Reflection only: the particle and audio modules are not referenced.</summary>
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
                }
                foreach (var l in root.GetComponentsInChildren<Light>(true)) sb.Append("\n  Light '").Append(l.name).Append("' ").Append(l.intensity.ToString("0.##"));
                if (tAudio != null) foreach (var a in root.GetComponentsInChildren(tAudio, true)) sb.Append("\n  Audio '").Append(((Component)a).name).Append("' ").Append(P(a, "volume"));
                Trace(sb.ToString());
            }
            catch (Exception e) { Trace("FireSmoke.DumpEffect: " + e.Message); }
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
