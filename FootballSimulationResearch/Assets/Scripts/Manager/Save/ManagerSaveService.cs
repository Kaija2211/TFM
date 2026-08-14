using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Manager.Save
{
    // Pure file I/O for ManagerSaveData - JsonUtility + Application.persistentDataPath,
    // the standard Unity pattern (distinct from Data/EvidenceExporter's unrelated CSV
    // research exports), still exactly build-safe as before.
    //
    // Multi-save support (session 15, Thomas: "I think we should do multiple saves and
    // you can choose which one to load") - was a single fixed "career_save.json" slot;
    // now one file per career, named by a stable GUID (ManagerSaveData.SaveId) rather
    // than the player-facing save name, so renaming a save (if that's ever added) or a
    // name with filesystem-invalid characters can never break the file link. Every save
    // in the folder is small enough that ListAllSaves just loads all of them fully for
    // the browser - no separate lightweight-metadata format needed at this scale.
    public static class ManagerSaveService
    {
        private const string SaveFilePrefix = "career_";
        private const string SaveFileExtension = ".json";

        private static string SaveDirectory => Application.persistentDataPath;

        private static string PathFor(string saveId) => Path.Combine(SaveDirectory, $"{SaveFilePrefix}{saveId}{SaveFileExtension}");

        public static bool HasAnySaves()
        {
            if (!Directory.Exists(SaveDirectory))
            {
                return false;
            }

            return Directory.GetFiles(SaveDirectory, $"{SaveFilePrefix}*{SaveFileExtension}").Length > 0;
        }

        // Skips (with a warning, not an exception) any file that fails to parse -
        // one corrupt save shouldn't take the whole browser down with it.
        public static List<ManagerSaveData> ListAllSaves()
        {
            List<ManagerSaveData> result = new List<ManagerSaveData>();

            if (!Directory.Exists(SaveDirectory))
            {
                return result;
            }

            foreach (string path in Directory.GetFiles(SaveDirectory, $"{SaveFilePrefix}*{SaveFileExtension}"))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    ManagerSaveData data = JsonUtility.FromJson<ManagerSaveData>(json);
                    if (data != null) result.Add(data);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"ManagerSaveService: skipped unreadable save file '{path}' - {e.Message}");
                }
            }

            return result;
        }

        // Backs the title screen's CONTINUE button - the most recently *saved* career,
        // not the most recently *created* one (LastSavedUtc, not any season/fixture
        // count), so exiting an older career keeps it as Continue's target for exactly
        // as long as it's genuinely the last one you played.
        public static ManagerSaveData GetMostRecentSave()
        {
            ManagerSaveData mostRecent = null;

            foreach (ManagerSaveData data in ListAllSaves())
            {
                if (mostRecent == null || string.CompareOrdinal(data.LastSavedUtc, mostRecent.LastSavedUtc) > 0)
                {
                    mostRecent = data;
                }
            }

            return mostRecent;
        }

        // Assigns SaveId on first save (a brand new career won't have one yet) so every
        // save afterward - this session and any future one - keeps landing on the same
        // file rather than minting a new one each time.
        public static void Save(ManagerSaveData data)
        {
            if (string.IsNullOrEmpty(data.SaveId))
            {
                data.SaveId = Guid.NewGuid().ToString("N");
            }

            data.LastSavedUtc = DateTime.UtcNow.ToString("o");

            string json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(PathFor(data.SaveId), json);
        }

        // Returns null (not an exception) if no save exists or the file is corrupt -
        // callers should treat both the same way: there's nothing to load.
        public static ManagerSaveData Load(string saveId)
        {
            string path = PathFor(saveId);

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<ManagerSaveData>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"ManagerSaveService: failed to load save '{saveId}' - {e.Message}");
                return null;
            }
        }

        public static void Delete(string saveId)
        {
            string path = PathFor(saveId);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
