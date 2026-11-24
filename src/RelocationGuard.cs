using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// EffectsManager.relocate moves every effect of its list by the world shift. One destroyed object in that list throws there, and the relocation stops half-way:
    /// RelocationHandler.relocate never reaches the terrain chunks, so a part of the ground is missing (seen after a mission was started again).
    /// Before the game's loop the dead entries are dropped from the list; the game would have thrown on them anyway.
    /// </summary>
    static class RelocationGuard
    {
        static FieldInfo _list;
        static int _total;

        public static bool Init(Harmony harmony, Assembly asm)
        {
            var tMgr = asm.GetType("SeaPower.EffectsManager");
            if (tMgr == null) return false;
            _list = tMgr.GetField("_activeEffectsForRelocation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var relocate = tMgr.GetMethod("relocate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Vector3) }, null);
            if (_list == null || relocate == null || !typeof(HashSet<GameObject>).IsAssignableFrom(_list.FieldType)) return false;
            harmony.Patch(relocate, prefix: new HarmonyMethod(typeof(RelocationGuard).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
            return true;
        }

        static void Prefix(object __instance)
        {
            try
            {
                var set = _list.GetValue(__instance) as HashSet<GameObject>;
                if (set == null) return;
                int n = set.RemoveWhere(g => g == null);
                if (n > 0)
                {
                    _total += n;
                    AsePlugin.TraceLine("relocation: " + n + " destroyed effect(s) removed from the game's relocation list (" + _total + " in all); the terrain is shifted anyway");
                }
            }
            catch (Exception e) { AsePlugin.TraceLine("RelocationGuard: " + e.Message); }
        }
    }
}
