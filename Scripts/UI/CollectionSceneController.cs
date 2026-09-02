using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Locackthon.Core;
using Locackthon.Data;

namespace Locackthon.UI
{
    /// Drives the dedicated CollectionScene. On entry it ensures a CollectionDatabase exists,
    /// initializes the scene-authored CollectionBookUI (registry + devUnlockAll), shows the grid,
    /// and wires the back button to return to LanternPrototype via GameStateManager.
    /// The Collection Book visuals are real serialized objects in this scene — edit them freely.
    public class CollectionSceneController : MonoBehaviour
    {
        [Tooltip("Master spirit list — the book's data source, and used to seed CollectionDatabase if entered directly.")]
        [SerializeField] private SpiritRegistry registry;
        [Tooltip("The scene-authored Collection Book view. Auto-found if left empty.")]
        [SerializeField] private CollectionBookUI bookUI;
        [Tooltip("Back button → returns to LanternPrototype.")]
        [SerializeField] private Button backButton;
        [Tooltip("DEV ONLY: display all spirits as caught for demos. Display-only — never writes JSON.")]
        [SerializeField] private bool devUnlockAll = false;

        private void Awake()
        {
            EnsureDatabase();

            if (bookUI == null) bookUI = FindFirstObjectByType<CollectionBookUI>(FindObjectsInactive.Include);

            if (backButton != null)
            {
                backButton.onClick.RemoveListener(ReturnToMain);
                backButton.onClick.AddListener(ReturnToMain);
            }
            else
            {
                Debug.LogWarning("[CollectionSceneController] backButton not assigned.");
            }
        }

        private void Start()
        {
            if (bookUI != null)
            {
                bookUI.Initialize(registry, devUnlockAll);
                bookUI.Show();
            }
            else
            {
                Debug.LogWarning("[CollectionSceneController] No CollectionBookUI found in scene.");
            }
        }

        private void ReturnToMain()
        {
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.ReturnToLanternMain();
            }
            else
            {
                Debug.LogWarning("[CollectionSceneController] No GameStateManager — direct SceneManager load of LanternPrototype.");
                SceneManager.LoadScene("LanternPrototype");
            }
        }

        private void EnsureDatabase()
        {
            if (CollectionDatabase.Instance != null)
            {
                if (CollectionDatabase.Instance.Registry == null && registry != null)
                {
                    CollectionDatabase.Instance.SetRegistry(registry);
                }
                return;
            }

            var go = new GameObject("CollectionDatabase");
            var db = go.AddComponent<CollectionDatabase>();
            db.SetRegistry(registry);
        }
    }
}
