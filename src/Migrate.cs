using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// The first start (no settings file yet): the values of the options this mod took over from Alternative Weapons Control
    /// and Alternative Damage Model are read from their .cfg files, so a player keeps his tuning. The old files are only read.
    /// The option names are the same as in the old mods, so the match is by [Section] Key.
    /// </summary>
    static class Migrate
    {
        static readonly string[] Old =
        {
            Path.Combine("AlternativeWeaponsControl", "AlternativeWeaponsControl.cfg"),
            Path.Combine("AdvancedDamageControl", "AlternativeDamageModel.cfg"),
        };

        public static void FromOldMods(AsePlugin p)
        {
            string user = Path.Combine(UnityEngine.Application.streamingAssetsPath, "user");
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in Old)
            {
                string path = Path.Combine(user, rel);
                if (!File.Exists(path)) continue;
                Read(path, found);
            }
            if (found.Count == 0) return;
            int n = 0;
            foreach (var e in p.Entries)
            {
                string key = e.Definition.Section + "/" + e.Definition.Key, v;
                // the language and the debug switch are the mod's own
                if (key == "General/Debug" || key == "General/Language") continue;
                if (!found.TryGetValue(key, out v)) continue;
                try { e.SetSerializedValue(v); n++; } catch { }
            }
            if (n > 0)
            {
                p.Cfg.Save();
                AsePlugin.Say("Settings taken over from Alternative Weapons Control / Alternative Damage Model: " + n + ".");
            }
        }

        /// <summary>A minimal reader of the BepInEx .cfg: [Section] and Key = value lines.</summary>
        static void Read(string path, Dictionary<string, string> into)
        {
            string section = "";
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']') { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                into[section + "/" + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }
    }
}
