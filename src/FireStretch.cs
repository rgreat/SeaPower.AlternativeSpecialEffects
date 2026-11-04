using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.ParticleSystemJobs;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// Two stages of a fire particle's life. The fire system stays exactly as the game made it; when a particle has lived SmokeFireShare of its life
    /// (the fire part of its sprite animation is over) its remaining life is made SmokeLifetimeFactor times longer: only the inverse start lifetime is
    /// changed, the share of the life already lived stays, so every over-lifetime curve (picture, size, colour, velocity) goes on from the value it had,
    /// k times slower. Done by a particle job the engine schedules itself after its own update of the system (OnParticleUpdateJobScheduled): no polling.
    /// A particle is stretched once: its inverse lifetime is then below Cut, which lies between the game's range of 1/lifetime and the stretched one.
    /// (A custom data mark does not work: the custom data module writes its curve into the stream every update, the mark was lost and the particles
    /// were stretched again every frame - they froze and never died.)
    /// </summary>
    public sealed class AseFireStretch : MonoBehaviour
    {
        float _thr = 30f, _invK = 1f, _cut = float.MaxValue, _kill = 101f;
        ParticleSystem _ps;
        // the game's size and colour over the lifetime (kept once), and the values the slowed stage was last built for
        ParticleSystem.MinMaxCurve _size0; ParticleSystem.MinMaxGradient _col0; bool _sizeOk, _colOk;
        float _bShare = -1f, _bK = -1f, _bGrow = -1f, _bFade = -1f;

        /// <summary>Puts the stretch on a fire system (once) and sets its split and factor.</summary>
        public static bool Set(ParticleSystem ps, float share, float k, float grow, float fade, Action<string> trace)
        {
            if (ps == null) return false;
            var st = ps.GetComponent<AseFireStretch>();
            bool first = st == null;
            if (first)
            {
                st = ps.gameObject.AddComponent<AseFireStretch>(); st._ps = ps;
                var sol = ps.sizeOverLifetime;
                st._size0 = sol.size;
                st._sizeOk = sol.enabled && !sol.separateAxes && (st._size0.mode == ParticleSystemCurveMode.Curve || st._size0.mode == ParticleSystemCurveMode.TwoCurves);
                if (st._sizeOk) st._size0 = Copy(st._size0);
                var col = ps.colorOverLifetime;
                st._col0 = col.color;
                st._colOk = col.enabled && (st._col0.mode == ParticleSystemGradientMode.Gradient || st._col0.mode == ParticleSystemGradientMode.TwoGradients);
                if (st._colOk) st._col0 = Copy(st._col0);
            }
            k = Math.Max(1f, k);
            float lo, hi; LifeRange(ps.main.startLifetime, out lo, out hi);
            st._thr = Mathf.Clamp(share, 0f, 1f) * 100f;
            st._invK = 1f / k;
            // the game's particles: 1/hi .. 1/lo; stretched ones: 1/(hi k) .. 1/(lo k). With a gap the cut is in its middle; without one (k <= hi/lo)
            // a short-lived particle can be stretched again until it is below the cut (a few times at most, never endlessly).
            float gameMin = 1f / hi, stretchedMax = 1f / (lo * k);
            st._cut = stretchedMax < gameMin ? Mathf.Sqrt(gameMin * stretchedMax) : gameMin * 0.999f;
            st.Spread(share, k, grow, fade);
            if (first) trace("fire stretch: '" + ps.name + "' life " + lo + ".." + hi + " x" + k + ", 1/life cut " + st._cut + ", size curve " + st._sizeOk + ", colour gradient " + st._colOk + ", removed at " + st._kill.ToString("0") + "% of the life");
            return true;
        }

        // ------------------------------------------------------------ the slowed stage: bigger, more transparent sprites
        // A slowed particle flies k times longer, so the sprites of the column end up far apart. Over the slowed stage (u = share..1 of the life, only
        // slowed particles are there) the size curve is multiplied by M and the alpha by M^-fade; M = 1 + grow (r - 1), r = how much farther apart the
        // sprites are than in the game at the same point of their life: (share + (1 - share) x k) / (share + (1 - share) x), x = 0..1 through the stage.
        // The first stage (u < share) keeps the game's curves exactly.

        static float Mul(float u, float share, float k, float grow)
        {
            if (u <= share) return 1f;
            float x = (u - share) / (1f - share), r = (share + (1f - share) * x * k) / (share + (1f - share) * x);
            return 1f + grow * (r - 1f);
        }

        void Spread(float share, float k, float grow, float fade)
        {
            if (Math.Abs(share - _bShare) < 1e-4f && Math.Abs(k - _bK) < 1e-4f && Math.Abs(grow - _bGrow) < 1e-4f && Math.Abs(fade - _bFade) < 1e-4f) return;
            _bShare = share; _bK = k; _bGrow = grow; _bFade = fade;
            if (_sizeOk)
            {
                var sol = _ps.sizeOverLifetime;
                sol.size = _size0.mode == ParticleSystemCurveMode.Curve
                    ? new ParticleSystem.MinMaxCurve(_size0.curveMultiplier, SizeCurve(_size0.curve, share, k, grow))
                    : new ParticleSystem.MinMaxCurve(_size0.curveMultiplier, SizeCurve(_size0.curveMin, share, k, grow), SizeCurve(_size0.curveMax, share, k, grow));
            }
            if (_colOk)
            {
                var col = _ps.colorOverLifetime;
                col.color = _col0.mode == ParticleSystemGradientMode.Gradient
                    ? new ParticleSystem.MinMaxGradient(Faded(_col0.gradient, share, k, grow, fade))
                    : new ParticleSystem.MinMaxGradient(Faded(_col0.gradientMin, share, k, grow, fade), Faded(_col0.gradientMax, share, k, grow, fade));
            }
            _kill = KillAt(share, k, grow, fade);
        }

        /// <summary>
        /// Frame time: the late, grown sprites are big but nearly transparent, and every one of them is still drawn (two passes of the fire shader).
        /// A slowed particle is removed once its opacity (the game's alpha x M^-fade) stays below 2% to the end of its life: % of the life, 101 = never.
        /// </summary>
        float KillAt(float share, float k, float grow, float fade)
        {
            if (!_colOk) return 101f;
            const float Eps = 0.02f; const int N = 200;
            float last = -1f;
            for (int i = 0; i <= N; i++)
            {
                float u = share + (1f - share) * i / N;
                float a = _col0.mode == ParticleSystemGradientMode.Gradient ? _col0.gradient.Evaluate(u).a : Math.Max(_col0.gradientMin.Evaluate(u).a, _col0.gradientMax.Evaluate(u).a);
                if (a * Mathf.Pow(Mul(u, share, k, grow), -fade) >= Eps) last = u;
            }
            if (last < 0f) return share * 100f + 1f;
            return last >= 1f ? 101f : (last + (1f - share) / N) * 100f;
        }

        /// <summary>The game's keys up to the split as they are, then the game's value x M sampled over the slowed stage.</summary>
        static AnimationCurve SizeCurve(AnimationCurve c, float share, float k, float grow)
        {
            var nc = new AnimationCurve { preWrapMode = c.preWrapMode, postWrapMode = c.postWrapMode };
            foreach (var key in c.keys) if (key.time < share - 1e-4f) nc.AddKey(key);
            int first = nc.length;
            const int N = 24;
            for (int i = 0; i <= N; i++) { float u = share + (1f - share) * i / N; nc.AddKey(new Keyframe(u, c.Evaluate(u) * Mul(u, share, k, grow))); }
            for (int i = first; i < nc.length; i++) nc.SmoothTangents(i, 0f);
            return nc;
        }

        /// <summary>The colours as they are; the alpha: the game's keys up to the split, then the game's alpha x M^-fade over the slowed stage (8 keys at most).</summary>
        static Gradient Faded(Gradient g, float share, float k, float grow, float fade)
        {
            var a = new System.Collections.Generic.List<GradientAlphaKey>();
            foreach (var key in g.alphaKeys) if (key.time < share - 1e-4f && a.Count < 5) a.Add(key);
            int n = 8 - a.Count;
            for (int i = 0; i < n; i++)
            {
                float u = share + (1f - share) * i / (n - 1);
                a.Add(new GradientAlphaKey(g.Evaluate(u).a * Mathf.Pow(Mul(u, share, k, grow), -fade), u));
            }
            var r = new Gradient { mode = g.mode };
            r.SetKeys(g.colorKeys, a.ToArray());
            return r;
        }

        static ParticleSystem.MinMaxCurve Copy(ParticleSystem.MinMaxCurve c)
        {
            Func<AnimationCurve, AnimationCurve> cp = x => x == null ? null : new AnimationCurve(x.keys) { preWrapMode = x.preWrapMode, postWrapMode = x.postWrapMode };
            return c.mode == ParticleSystemCurveMode.Curve ? new ParticleSystem.MinMaxCurve(c.curveMultiplier, cp(c.curve)) : new ParticleSystem.MinMaxCurve(c.curveMultiplier, cp(c.curveMin), cp(c.curveMax));
        }

        static ParticleSystem.MinMaxGradient Copy(ParticleSystem.MinMaxGradient c)
        {
            Func<Gradient, Gradient> cp = x => { if (x == null) return null; var r = new Gradient { mode = x.mode }; r.SetKeys(x.colorKeys, x.alphaKeys); return r; };
            return c.mode == ParticleSystemGradientMode.Gradient ? new ParticleSystem.MinMaxGradient(cp(c.gradient)) : new ParticleSystem.MinMaxGradient(cp(c.gradientMin), cp(c.gradientMax));
        }

        static void LifeRange(ParticleSystem.MinMaxCurve c, out float lo, out float hi)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant: lo = hi = c.constant; break;
                case ParticleSystemCurveMode.TwoConstants: lo = c.constantMin; hi = c.constantMax; break;
                default:
                    lo = float.MaxValue; hi = 0f;
                    foreach (var cv in new[] { c.curve, c.curveMin, c.curveMax })
                        if (cv != null) foreach (var key in cv.keys) { lo = Math.Min(lo, key.value * c.curveMultiplier); hi = Math.Max(hi, key.value * c.curveMultiplier); }
                    break;
            }
            hi = Math.Max(hi, 0.01f); lo = Mathf.Clamp(lo, 0.01f, hi);
        }

        void OnParticleUpdateJobScheduled()
        {
            if (_invK >= 0.999f) return;
            if (_ps == null) _ps = GetComponent<ParticleSystem>();
            new StretchJob { Thr = _thr, InvK = _invK, Cut = _cut, Kill = _kill }.Schedule(_ps);
        }

        struct StretchJob : IJobParticleSystem
        {
            public float Thr, InvK, Cut, Kill;

            public void Execute(ParticleSystemJobData d)
            {
                NativeArray<float> alive = d.aliveTimePercent, inv = d.inverseStartLifetimes;
                int n = d.count;
                for (int i = 0; i < n; i++)
                {
                    float a = alive[i];
                    if (a >= Kill) alive[i] = 100f;   // invisible from here on: the engine removes it
                    else if (a >= Thr && inv[i] >= Cut) inv[i] = inv[i] * InvK;
                }
            }
        }
    }
}
