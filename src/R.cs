using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace AlternativeSpecialEffects
{
    /// <summary>Reflection helpers for game objects (fields and properties of any visibility, methods by name; cached).</summary>
    static class R
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        // type -> name -> member: no key string is built per call (these run every frame)
        static readonly Dictionary<Type, Dictionary<string, FieldInfo>> Fields = new Dictionary<Type, Dictionary<string, FieldInfo>>();
        static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> Props = new Dictionary<Type, Dictionary<string, PropertyInfo>>();
        static readonly Dictionary<Type, Dictionary<string, MethodInfo[]>> Methods = new Dictionary<Type, Dictionary<string, MethodInfo[]>>();
        static Dictionary<string, T> Of<T>(Dictionary<Type, Dictionary<string, T>> d, Type t)
        {
            Dictionary<string, T> m;
            if (!d.TryGetValue(t, out m)) d[t] = m = new Dictionary<string, T>();
            return m;
        }

        static FieldInfo Fi(Type t, string n)
        {
            var m = Of(Fields, t);
            FieldInfo f;
            if (m.TryGetValue(n, out f)) return f;
            for (var ty = t; ty != null && f == null; ty = ty.BaseType) f = ty.GetField(n, All);
            m[n] = f;
            return f;
        }

        static PropertyInfo Pi(Type t, string n)
        {
            var m = Of(Props, t);
            PropertyInfo p;
            if (m.TryGetValue(n, out p)) return p;
            for (var ty = t; ty != null && p == null; ty = ty.BaseType) p = ty.GetProperty(n, All);
            m[n] = p;
            return p;
        }

        public static object F(object o, string n)
        {
            if (o == null) return null;
            try { var f = Fi(o.GetType(), n); return f == null ? null : f.GetValue(o); } catch { return null; }
        }
        public static object P(object o, string n)
        {
            if (o == null) return null;
            try { var p = Pi(o.GetType(), n); return p == null ? null : p.GetValue(o, null); } catch { return null; }
        }

        public static void SetP(object o, string n, object v)
        {
            if (o == null) return;
            try { var p = Pi(o.GetType(), n); if (p != null) p.SetValue(o, v, null); } catch { }
        }

        /// <summary>Calls a method by name: the overload whose parameters take the arguments; missing trailing ones get their defaults.</summary>
        public static object Call(object o, string n, params object[] args)
        {
            if (o == null) return null;
            var t = o.GetType();
            var mm = Of(Methods, t);
            MethodInfo[] ms;
            if (!mm.TryGetValue(n, out ms))
            {
                var l = new List<MethodInfo>();
                for (var ty = t; ty != null; ty = ty.BaseType)
                    foreach (var m in ty.GetMethods(All)) if (m.Name == n && !m.ContainsGenericParameters) l.Add(m);
                ms = l.ToArray();
                mm[n] = ms;
            }
            foreach (var m in ms)
            {
                var ps = m.GetParameters();
                if (ps.Length < args.Length) continue;
                bool ok = true;
                for (int i = 0; i < ps.Length && ok; i++)
                {
                    if (i < args.Length)
                    {
                        var a = args[i];
                        if (a == null) ok = !ps[i].ParameterType.IsValueType;
                        else ok = ps[i].ParameterType.IsInstanceOfType(a);
                    }
                    else ok = ps[i].IsOptional;
                }
                if (!ok) continue;
                var full = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++) full[i] = i < args.Length ? args[i] : ps[i].DefaultValue;
                return m.Invoke(o, full);
            }
            throw new MissingMethodException(t.Name, n);
        }

        public static bool B(object v) { return v is bool && (bool)v; }
        public static int I(object v) { try { return v == null ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return 0; } }
        public static float Fl(object v) { try { return v == null ? 0f : Convert.ToSingle(v, CultureInfo.InvariantCulture); } catch { return 0f; } }
        public static string S(object v) { return v == null ? null : v as string ?? v.ToString(); }
    }
}
