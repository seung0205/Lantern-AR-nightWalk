using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Locackthon.Data;

namespace Locackthon.Core
{
    /// Persistent record of which Spirit ScriptableObjects the player has captured.
    /// Singleton, survives scene loads, saves to a JSON file in Application.persistentDataPath
    /// so the collection survives app restart.
    public class CollectionDatabase : MonoBehaviour
    {
        public static CollectionDatabase Instance { get; private set; }

        [Header("Registry")]
        [Tooltip("Master list of every spirit in the game. Captured spirits are matched against this registry on load so we can resolve them by name.")]
        [SerializeField] private SpiritRegistry registry;

        [Header("Persistence")]
        [Tooltip("File name (under Application.persistentDataPath) used to persist the captured set as JSON.")]
        [SerializeField] private string saveFileName = "collection.json";

        [Tooltip("DEMO BUILD: when true, the persisted collection (collection.json) is wiped once per app launch so every play session starts with 0 caught — judges always get launch → 0 → 증발 → … → 4 catches → ending. Set false to keep a persistent collection across launches.")]
        [SerializeField] private bool resetOnLaunch = true;

        // Guards the resetOnLaunch wipe to at most once per PROCESS (the first time the database
        // initializes this launch), not on every scene reload that might re-run Load(). C# statics
        // reset on app restart and on editor domain reload, which is exactly the "per launch"
        // granularity we want — a fresh launch (or a fresh Play session) wipes; a scene transition
        // within the same session does not.
        private static bool _didResetThisLaunch;

        public SpiritRegistry Registry => registry;
        public IReadOnlyCollection<Spirit> GetCapturedSpirits() => captured;
        public int CapturedCount => captured.Count;

        /// Most recently captured spirit this session (set by AddSpirit). In-memory only — it is
        /// NOT persisted to JSON, so after an app restart it is null until the next capture.
        /// Used by the Collection Book's featured-spirit display.
        public Spirit LastCaptured { get; private set; }

        /// True when every spirit in the registry has been captured. Returns false if the
        /// registry isn't assigned or is empty, so a missing registry never spuriously
        /// reports "complete".
        public bool IsComplete => registry != null && registry.Count > 0 && captured.Count >= registry.Count;

        private readonly HashSet<Spirit> captured = new HashSet<Spirit>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Defer Load() if the registry was not assigned in the inspector — the bootstrap will
            // call SetRegistry shortly after, which loads. This avoids two redundant Load() calls.
            if (registry != null) Load();
        }

        /// Assign the master Spirit registry at runtime (used by CollectionBookBootstrap when this
        /// component is created from code). Triggers a Load() so any persisted captures are restored.
        public void SetRegistry(SpiritRegistry registry)
        {
            this.registry = registry;
            Load();
        }

        public void AddSpirit(Spirit spirit)
        {
            if (spirit == null)
            {
                Debug.LogWarning("[Collection] AddSpirit called with null.");
                return;
            }

            LastCaptured = spirit; // most-recently-captured, even on a re-capture of the same spirit

            if (!captured.Add(spirit))
            {
                Debug.Log($"[Collection] Already captured: {spirit.displayName} ({spirit.name}). Total: {captured.Count}");
                return;
            }

            Debug.Log($"[Collection] Added: {spirit.displayName} ({spirit.name}). Total: {captured.Count}");
            Save();
        }

        public bool HasCaptured(Spirit spirit) => spirit != null && captured.Contains(spirit);

        public void Clear()
        {
            captured.Clear();
            Debug.Log("[Collection] Cleared all captured spirits.");
            Save();
        }

        // ----- persistence -----

        [System.Serializable]
        private class SaveData
        {
            public List<string> capturedNames = new List<string>();
        }

        private string SavePath => Path.Combine(Application.persistentDataPath, saveFileName);

        private void Save()
        {
            var data = new SaveData();
            foreach (var s in captured)
            {
                if (s != null) data.capturedNames.Add(s.name);
            }

            try
            {
                string json = JsonUtility.ToJson(data);
                File.WriteAllText(SavePath, json);
                Debug.Log($"[Collection] Saved {data.capturedNames.Count} spirit(s) → {SavePath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Collection] Save failed: {e.Message}");
            }
        }

        private void Load()
        {
            captured.Clear();

            // DEMO reset: on the first DB initialization this launch, wipe any persisted collection
            // so the session starts at 0 caught. Runs at most once per process (see guard); skips
            // loading the old saved captures entirely.
            if (resetOnLaunch && !_didResetThisLaunch)
            {
                _didResetThisLaunch = true;
                string resetPath = SavePath;
                try
                {
                    if (File.Exists(resetPath)) File.Delete(resetPath);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Collection] resetOnLaunch: could not delete '{resetPath}': {e.Message}");
                }
                Debug.Log("[Collection] resetOnLaunch=true → cleared persisted collection; starting this launch with 0 captured.");
                return; // do not load old saved captures
            }

            string path = SavePath;
            if (!File.Exists(path))
            {
                Debug.Log($"[Collection] No save file at {path} — starting fresh.");
                return;
            }
            if (registry == null)
            {
                Debug.LogWarning("[Collection] Registry is not assigned; cannot resolve saved spirit names.");
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data == null || data.capturedNames == null) return;

                int resolved = 0;
                foreach (var name in data.capturedNames)
                {
                    var s = registry.FindByName(name);
                    if (s != null)
                    {
                        captured.Add(s);
                        resolved++;
                    }
                    else
                    {
                        Debug.LogWarning($"[Collection] Saved spirit '{name}' is not in the registry — skipping.");
                    }
                }
                Debug.Log($"[Collection] Loaded {resolved}/{data.capturedNames.Count} spirit(s) from {path}.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Collection] Load failed: {e.Message}");
            }
        }

        [ContextMenu("Dev: Clear collection")]
        private void DevClear() => Clear();

        [ContextMenu("Dev: Print save path")]
        private void DevPrintSavePath() => Debug.Log($"[Collection] Save path: {SavePath}");
    }
}
