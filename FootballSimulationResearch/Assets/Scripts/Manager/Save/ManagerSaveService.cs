using System.IO;
using UnityEngine;

namespace Manager.Save
{
    // Pure file I/O for ManagerSaveData - JsonUtility + Application.persistentDataPath,
    // the standard Unity pattern (distinct from Data/EvidenceExporter's unrelated CSV
    // research exports). Single fixed save slot, matching the single "LOAD CAREER"
    // button on the title screen (no slot picker exists to choose between multiple).
    public static class ManagerSaveService
    {
        private const string SaveFileName = "career_save.json";

        private static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public static bool HasSaveFile()
        {
            return File.Exists(SavePath);
        }

        public static void Save(ManagerSaveData data)
        {
            string json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(SavePath, json);
        }

        // Returns null (not an exception) if no save exists or the file is corrupt -
        // callers (the title screen's LOAD CAREER button) should treat both the same
        // way: there's nothing to load.
        public static ManagerSaveData Load()
        {
            if (!File.Exists(SavePath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(SavePath);
                return JsonUtility.FromJson<ManagerSaveData>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ManagerSaveService: failed to load save file - {e.Message}");
                return null;
            }
        }
    }
}
