using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Locackthon.Core;
using Locackthon.Data;

namespace Locackthon.UI
{
    /// Lives in LanternPrototype. Ensures a CollectionDatabase exists, and wires the main-screen
    /// 도감 button (Btn_OpenCollection) to LOAD the dedicated CollectionScene (via GameStateManager).
    /// The Collection Book UI no longer lives here — it's its own scene (see CollectionSceneController).
    public class CollectionBookBootstrap : MonoBehaviour
    {
        [Tooltip("Master spirit list — used to create/seed the persistent CollectionDatabase.")]
        [SerializeField] private SpiritRegistry registry;

        [Tooltip("The 도감 button on the main screen (Btn_OpenCollection). Clicking it loads CollectionScene.")]
        [SerializeField] private Button externalOpenButton;

        private void Awake()
        {
            EnsureDatabase();

            if (externalOpenButton != null)
            {
                externalOpenButton.onClick.RemoveListener(OpenCollectionScene);
                externalOpenButton.onClick.AddListener(OpenCollectionScene);
            }
            else
            {
                Debug.LogWarning("[CollectionBookBootstrap] externalOpenButton (Btn_OpenCollection) not assigned.");
            }
        }

        private void OpenCollectionScene()
        {
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.EnterCollection();
            }
            else
            {
                Debug.LogWarning("[CollectionBookBootstrap] No GameStateManager — direct SceneManager load of CollectionScene.");
                SceneManager.LoadScene("CollectionScene");
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
