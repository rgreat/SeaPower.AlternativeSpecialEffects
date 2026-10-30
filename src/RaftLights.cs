using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// [Effects] LifeRaftLights: the lights of the life rafts are submodels of the rafts' ini files (Release/vessels.fix, "#!extend" patches,
    /// always part of the game's data while the mod is ticked) drawn with the shader Nils/Effects/MeshLights. The switch shows or hides those
    /// parts on the raft objects, so it works at once:
    ///  - a change of the switch (window, .cfg, Mods menu tick): every raft of ObjectsManager._listOfAllRafts;
    ///  - a raft that appears (a postfix of ObjectsManager.AddObjectToItsTaskforce, which also catches the rafts of a loaded game): that raft.
    /// The light is told by its material's shader, not by a name: the game names a part by its submodel or by its mesh.
    /// Nothing is kept: the state is on the raft objects themselves, which the game destroys with the scene.
    /// </summary>
    static class RaftLights
    {
        public static Func<bool> Want = () => true;
        public static Action<string> Trace = s => { };

        static Type _tManager, _tBase; static FieldInfo _fRafts, _fObp; static FieldInfo _fIni; static PropertyInfo _pInstance; static MethodInfo _mExists;
        static bool _ok;

        public static bool Init(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                _tManager = asm.GetType("SeaPower.ObjectsManager"); _tBase = asm.GetType("SeaPower.ObjectBase");
                var add = _tManager == null || _tBase == null ? null : _tManager.GetMethod("AddObjectToItsTaskforce", all, null, new[] { _tBase }, null);
                _fRafts = _tManager == null ? null : _tManager.GetField("_listOfAllRafts", all);
                _fObp = _tBase == null ? null : _tBase.GetField("_obp", all);
                _fIni = _fObp == null ? null : _fObp.FieldType.GetField("_objectIniName", all);
                var singleton = asm.GetType("SeaPower.Singleton`1");
                var gen = singleton == null || _tManager == null ? null : singleton.MakeGenericType(_tManager);
                _pInstance = gen == null ? null : gen.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                _mExists = gen == null ? null : gen.GetMethod("InstanceExists", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                _ok = add != null && _fRafts != null && _fObp != null && _fIni != null && _pInstance != null && _mExists != null;
                if (!_ok) { Trace("RaftLights: game members not found"); return false; }
                harmony.Patch(add, postfix: new HarmonyMethod(typeof(RaftLights).GetMethod(nameof(AddPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                return true;
            }
            catch (Exception e) { Trace("RaftLights.Init: " + e.Message); return false; }
        }

        static void AddPostfix(object obj)
        {
            if (!_ok || obj == null) return;
            try { if (!Want() && IsRaft(obj)) Set(obj, false); } catch (Exception e) { Trace("RaftLights add: " + e.Message); }
        }

        static bool IsRaft(object obj)
        {
            var obp = _fObp.GetValue(obj);
            var name = obp == null ? null : _fIni.GetValue(obp) as string;
            return name != null && name.StartsWith("all_life_raft", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The switch changed: every raft that exists now.</summary>
        public static void Sync()
        {
            if (!_ok) return;
            try
            {
                if (!(bool)_mExists.Invoke(null, new object[] { false })) return;
                var mgr = _pInstance.GetValue(null, null);
                var list = mgr == null ? null : _fRafts.GetValue(mgr) as IList;
                if (list == null) return;
                bool want = Want();
                foreach (var raft in list) if (raft != null && IsRaft(raft)) Set(raft, want);
            }
            catch (Exception e) { Trace("RaftLights.Sync: " + e.Message); }
        }

        static void Set(object raft, bool on)
        {
            var comp = raft as Component;
            if (comp == null) return;
            foreach (var r in comp.GetComponentsInChildren<Renderer>(true))
            {
                var mat = r == null ? null : r.sharedMaterial;
                if (mat != null && mat.shader != null && mat.shader.name.IndexOf("MeshLights", StringComparison.OrdinalIgnoreCase) >= 0)
                    r.gameObject.SetActive(on);
            }
        }
    }
}
