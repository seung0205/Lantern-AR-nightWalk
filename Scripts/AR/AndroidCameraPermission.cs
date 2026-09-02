using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Locackthon.AR
{
    // Requests Android CAMERA permission before the AR session starts.
    //
    // Without this, ARCore on Android 6+ silently fails to start the camera and the
    // ARCameraBackground stays empty (showing whatever the Camera's solid clear colour is —
    // which is what the user was misreading as "the yellow XR Simulation environment").
    //
    // Self-bootstraps via [RuntimeInitializeOnLoadMethod] so no scene wiring is needed.
    public static class AndroidCameraPermission
    {
        private const string ARSceneName = "ARCaptureScene";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != ARSceneName) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            var host = new GameObject("AndroidCameraPermissionRunner");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<Runner>();
#else
            Debug.Log("[AndroidCameraPermission] Skipped: not Android player. (XR Simulation in editor uses a fake camera.)");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private class Runner : MonoBehaviour
        {
            private IEnumerator Start()
            {
                if (Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    Debug.Log("[AndroidCameraPermission] CAMERA already granted.");
                    Destroy(gameObject);
                    yield break;
                }

                Debug.Log("[AndroidCameraPermission] Requesting CAMERA permission…");
                Permission.RequestUserPermission(Permission.Camera);

                // Wait up to 15s for the user to dismiss the system dialog.
                float waited = 0f;
                while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && waited < 15f)
                {
                    yield return new WaitForSeconds(0.25f);
                    waited += 0.25f;
                }

                if (Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    Debug.Log("[AndroidCameraPermission] CAMERA granted. AR Foundation will now bring up the live camera background.");
                }
                else
                {
                    Debug.LogWarning("[AndroidCameraPermission] CAMERA was denied. ARCameraBackground will stay blank — user must grant CAMERA in Android app settings.");
                }

                Destroy(gameObject);
            }
        }
#endif
    }
}
