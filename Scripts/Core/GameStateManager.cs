using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Locackthon.Data;

namespace Locackthon.Core
{
    // The brain of the game — DontDestroyOnLoad singleton that survives scene loads and
    // owns three pieces of cross-scene state:
    //
    //   ActiveTargetSpirit  — the spirit the AR scene should currently render. Cleared by
    //                         ReturnToLantern after capture.
    //   LastSelectedSpirit  — the most recent DEV pick or proximity target. NEVER cleared
    //                         after being set, so the Lantern flame and watched-spirit gate
    //                         stay consistent across the AR round-trip.
    //   captured            — runtime set of captured spirits this session; mirrored into
    //                         CollectionDatabase for persistence.
    //
    // Public methods are the ONLY way other scripts should change scenes:
    //   EnterARCapture(spirit)  → loads ARCaptureScene with that spirit as the target.
    //   ReturnToLantern(spirit) → records capture, loads LanternPrototype.
    //   SetActiveTargetSpirit() → DEV "select without entering" — pre-sets state but stays put.
    public class GameStateManager : MonoBehaviour
    {
        private static GameStateManager _instance;

        /// Lazy, never-null singleton accessor. Tries (1) the cached instance, then (2) any
        /// existing instance already in a loaded scene, then (3) auto-creates a fallback
        /// GameObject. The fallback's Awake will mark it DontDestroyOnLoad and seed _instance.
        /// Callers that read .Instance then dereference fields cannot crash on null anymore.
        public static GameStateManager Instance
        {
            get
            {
                if (_instance != null) return _instance;

                var found = FindFirstObjectByType<GameStateManager>(FindObjectsInactive.Include);
                if (found != null)
                {
                    _instance = found;
                    Debug.LogWarning("[GameStateManager][trace] Instance getter recovered an unwired scene instance — cache was null but a GameStateManager exists in a loaded scene. Likely the previous Instance died mid-flow.");
                    return _instance;
                }

                Debug.LogError("[GameStateManager][trace] Instance getter found NOTHING in any loaded scene — auto-creating a fallback GameObject. Inspector overrides won't apply on this instance (it uses C# defaults including rewardAfterSingleCapture=true).");
                var go = new GameObject("GameStateManager (auto)");
                go.AddComponent<GameStateManager>(); // Awake handles _instance + DontDestroyOnLoad
                return _instance;
            }
            private set { _instance = value; }
        }

        [Header("Scenes")]
        [SerializeField] private string titleSceneName = "TitleScene";
        [SerializeField] private string lanternSelectSceneName = "LanternSelectScene";
        [SerializeField] private string lanternSceneName = "LanternPrototype";
        [SerializeField] private string arSceneName = "ARCaptureScene";
        [SerializeField] private string rewardSceneName = "RewardScene";
        [SerializeField] private string endingSceneName = "EndingScene";
        [SerializeField] private string collectionSceneName = "CollectionScene";

        [Header("Reward Gate")]
        [Tooltip("DEMO MODE: when true, ReturnToLantern routes to RewardScene after ANY successful capture. When false, only after CollectionDatabase.IsComplete (the full 4-spirit gate). Set false for the real game flow. NOTE: this C# default is authoritative for the APK flow — the first scene to touch GameStateManager.Instance is TitleScene (which has no scene GSM), so an auto-created fallback using THIS default becomes the surviving singleton.")]
        [SerializeField] private bool rewardAfterSingleCapture = false;

        public Spirit ActiveTargetSpirit { get; private set; }
        // Survives ReturnToLantern, scene reloads, and the AR round-trip. DEV mode writes here
        // so the Lantern flame tint and watched-spirit gate keep matching the user's last pick
        // even after they capture and bounce back to the Lantern scene.
        public Spirit LastSelectedSpirit { get; private set; }
        public IReadOnlyCollection<Spirit> Captured => captured;

        // Which lantern the player picked in LanternSelectScene (0..2). Set by the
        // carousel's 선택 confirm button via SetSelectedLanternIndex. Survives scene loads
        // like the other cross-scene state above. Defaults to 0 (first lantern).
        public int SelectedLanternIndex { get; private set; }

        private readonly HashSet<Spirit> captured = new HashSet<Spirit>();

        private void Awake()
        {
            // Read the backing field directly here — DO NOT route through the property getter,
            // because the getter would auto-create a fallback while the real Awake is still
            // initializing this scene-authored instance, ending up with two GSMs in the same
            // frame.
            if (_instance != null && _instance != this)
            {
                Debug.Log($"[GameStateManager][trace] Awake: duplicate instance (scene='{gameObject.scene.name}') — destroying self. Existing _instance.captured.Count={_instance.captured.Count}.");
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log($"[GameStateManager][trace] Awake: I am the Instance (scene='{gameObject.scene.name}'). DontDestroyOnLoad applied. rewardAfterSingleCapture={rewardAfterSingleCapture}  captured.Count={captured.Count}  ActiveTargetSpirit={(ActiveTargetSpirit == null ? "<null>" : ActiveTargetSpirit.name)}.");
        }

        private void OnDestroy()
        {
            // If the live singleton is being destroyed, drop the cache so the next .Instance
            // access auto-recovers (or auto-creates). Without this, callers would dereference
            // a destroyed-but-not-null UnityEngine.Object reference — the classic
            // MissingReferenceException source.
            if (_instance == this)
            {
                Debug.LogWarning("[GameStateManager][trace] OnDestroy: the LIVE Instance is being destroyed (scene unload, manual Destroy, or app quit). Clearing cache; next Instance access will auto-recover.");
                _instance = null;
            }
        }

        [ContextMenu("Dev: Clear runtime captured HashSet")]
        private void DevClearCaptured()
        {
            captured.Clear();
            Debug.Log("[GameStateManager] Runtime captured HashSet cleared.");
        }

        public void EnterLanternSelect() => SceneManager.LoadScene(lanternSelectSceneName);

        public void EnterLanternMain() => SceneManager.LoadScene(lanternSceneName);

        public void EnterTitle() => SceneManager.LoadScene(titleSceneName);

        public void EnterReward() => SceneManager.LoadScene(rewardSceneName);

        public void EnterEnding() => SceneManager.LoadScene(endingSceneName);

        // 도감: load the dedicated Collection scene. CollectionDatabase is DontDestroyOnLoad,
        // so captured state carries across this load.
        public void EnterCollection() => SceneManager.LoadScene(collectionSceneName);

        // Back from the Collection scene (or any sub-scene) to the main Lantern screen.
        public void ReturnToLanternMain() => SceneManager.LoadScene(lanternSceneName);

        public void EnterARCapture(Spirit target)
        {
            if (target == null)
            {
                Debug.LogWarning("[GameStateManager][trace] EnterARCapture called with NULL target — bailing, NOT loading AR scene.");
                return;
            }
            Debug.Log($"[GameStateManager][trace] EnterARCapture({target.name}). Setting ActiveTargetSpirit and LastSelectedSpirit, then loading {arSceneName}.");
            ActiveTargetSpirit = target;
            LastSelectedSpirit = target;
            SceneManager.LoadScene(arSceneName);
        }

        // Records the lantern the player confirmed in the LanternSelect carousel. Clamped to
        // the valid 0..2 range so a bad caller can never poison the cross-scene state.
        public void SetSelectedLanternIndex(int index)
        {
            int clamped = Mathf.Clamp(index, 0, 2);
            Debug.Log($"[GameStateManager][trace] SetSelectedLanternIndex({index}) → stored {clamped}.");
            SelectedLanternIndex = clamped;
        }

        // DEV mode "select-without-entering": preset the active spirit so the AR scene tints to
        // the right accent colour when the user finally taps Open Camera, without forcing the
        // scene transition here.
        public void SetActiveTargetSpirit(Spirit target)
        {
            Debug.Log($"[GameStateManager][trace] SetActiveTargetSpirit({(target == null ? "<null>" : target.name)}).");
            ActiveTargetSpirit = target;
            LastSelectedSpirit = target;
        }

        public void ReturnToLantern(Spirit capturedSpirit = null)
        {
            string spiritName = capturedSpirit != null ? capturedSpirit.name : "<null>";
            int countBefore = captured.Count;
            bool wasAlreadyInSet = capturedSpirit != null && captured.Contains(capturedSpirit);

            Debug.Log($"[GameStateManager][trace] ReturnToLantern ENTRY  capturedSpirit={spiritName}  alreadyInCapturedSet={wasAlreadyInSet}  captured.CountBefore={countBefore}  rewardAfterSingleCapture={rewardAfterSingleCapture}");

            bool didCaptureHappen = capturedSpirit != null;
            bool newlyCaptured = false;
            if (didCaptureHappen)
            {
                newlyCaptured = captured.Add(capturedSpirit);
                if (CollectionDatabase.Instance != null)
                {
                    CollectionDatabase.Instance.AddSpirit(capturedSpirit);
                }
                else
                {
                    Debug.LogWarning("[GameStateManager] No CollectionDatabase.Instance — capture will not be persisted to disk.");
                }
            }
            ActiveTargetSpirit = null;

            // Reward routing.
            //
            //   rewardAfterSingleCapture == true  (DEMO): any non-null capturedSpirit → RewardScene.
            //     IMPORTANT: NOT gated on newlyCaptured. The captured HashSet may already contain
            //     this spirit (prior run, disabled Domain Reload, etc.); demo mode still rewards.
            //   rewardAfterSingleCapture == false (REAL): only the capture that completes the set
            //     → RewardScene. Gated on newlyCaptured && IsComplete to avoid double-firing.
            //
            // The back button (capturedSpirit == null) never rewards on either path.
            bool collectionComplete = CollectionDatabase.Instance != null && CollectionDatabase.Instance.IsComplete;
            bool demoReward = rewardAfterSingleCapture && didCaptureHappen;
            bool realReward = !rewardAfterSingleCapture && newlyCaptured && collectionComplete;
            bool shouldReward = demoReward || realReward;

            string targetScene = shouldReward ? rewardSceneName : lanternSceneName;
            string reason = demoReward ? "demoReward (rewardAfterSingleCapture && capturedSpirit!=null)"
                          : realReward ? "realReward (newlyCaptured && IsComplete)"
                          : "no-reward (gate failed)";
            Debug.Log($"[GameStateManager][trace] ReturnToLantern DECISION  capturedSpirit={spiritName}  newlyCaptured={newlyCaptured}  captured.CountAfter={captured.Count}  rewardAfterSingleCapture={rewardAfterSingleCapture}  collectionComplete={collectionComplete}  demoReward={demoReward}  realReward={realReward}  →  {targetScene}  ({reason})");

            if (shouldReward)
            {
                SceneManager.LoadScene(rewardSceneName);
                return;
            }
            SceneManager.LoadScene(lanternSceneName);
        }

        public bool IsCaptured(Spirit spirit) => spirit != null && captured.Contains(spirit);

        [ContextMenu("Dev: Force AR with first watched Spirit")]
        private void DevForceAR()
        {
            var watcher = FindFirstObjectByType<Lantern.ProximityWatcher>();
            if (watcher == null)
            {
                Debug.LogWarning("[GameStateManager] No ProximityWatcher in scene to pull a target spirit from.");
                return;
            }
            var target = watcher.AnyWatchedSpirit;
            if (target == null)
            {
                Debug.LogWarning("[GameStateManager] ProximityWatcher has no spirits in its watched list.");
                return;
            }
            EnterARCapture(target);
        }
    }
}
