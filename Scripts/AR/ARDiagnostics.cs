using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace Locackthon.AR
{
    // Runtime XR diagnostics. On every load of ARCaptureScene, dumps to logcat:
    //   - active XRLoader name (so we can confirm ARCore is loaded on Android, not Simulation)
    //   - XR Manager init state
    //   - ARSession.state and notSupportedReason
    //   - AR camera clearFlags + bg color (rules out "yellow comes from clear color")
    //   - whether ARCameraBackground is enabled and finds its material
    //   - URP renderer hint
    //
    // Self-bootstraps via [RuntimeInitializeOnLoadMethod] — no scene wiring.
    public static class ARDiagnostics
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
            var go = new GameObject("ARDiagnostics");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        private class Runner : MonoBehaviour
        {
            private IEnumerator Start()
            {
                Debug.Log($"[ARDiag] platform={Application.platform} unityVersion={Application.unityVersion} isEditor={Application.isEditor}");

                var settings = XRGeneralSettings.Instance;
                var manager = settings != null ? settings.Manager : null;
                if (manager == null)
                {
                    Debug.LogError("[ARDiag] XRGeneralSettings.Instance.Manager is NULL. XR Plug-in Management is not configured for this build target.");
                }
                else
                {
                    Debug.Log($"[ARDiag] XRManager isInitializationComplete={manager.isInitializationComplete}");

                    // If init never ran (or failed), try once more so we can see the error.
                    if (!manager.isInitializationComplete)
                    {
                        Debug.Log("[ARDiag] Manager not initialized — calling InitializeLoaderSync()…");
                        manager.InitializeLoaderSync();
                        Debug.Log($"[ARDiag] After InitializeLoaderSync: isInitializationComplete={manager.isInitializationComplete}");
                    }

                    if (manager.activeLoader == null)
                    {
                        Debug.LogError("[ARDiag] manager.activeLoader is NULL. ARCore failed to load on this device. Check: ARCore Services installed, device supports ARCore, manifest entries.");
                    }
                    else
                    {
                        string loaderName = manager.activeLoader.GetType().FullName;
                        Debug.Log($"[ARDiag] activeLoader = {loaderName}");
                        if (loaderName.Contains("Simulation") && Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.OSXEditor)
                        {
                            Debug.LogError($"[ARDiag] !!! XR Simulation is active on a NON-EDITOR platform ({Application.platform}). This is the bug. The Android tab in XR Plug-in Management has Simulation enabled — disable it.");
                        }
                    }
                }

                // Wait two frames so AR Foundation's components have run their first Update.
                yield return null;
                yield return null;

                var session = FindFirstObjectByType<ARSession>();
                if (session == null) Debug.LogError("[ARDiag] No ARSession in scene.");
                else Debug.Log($"[ARDiag] ARSession.state={ARSession.state} notTrackingReason={ARSession.notTrackingReason}");

                var cam = Camera.main;
                if (cam == null) Debug.LogWarning("[ARDiag] Camera.main is null.");
                else
                {
                    Debug.Log($"[ARDiag] AR Camera '{cam.name}' clearFlags={cam.clearFlags} bgColor=(r={cam.backgroundColor.r:F2},g={cam.backgroundColor.g:F2},b={cam.backgroundColor.b:F2})");
                    var bg = cam.GetComponent<ARCameraBackground>();
                    if (bg == null) Debug.LogError("[ARDiag] AR Camera has no ARCameraBackground component — camera feed will never draw.");
                    else
                    {
                        var mat = bg.material;
                        Debug.Log($"[ARDiag] ARCameraBackground enabled={bg.enabled} useCustomMaterial={bg.useCustomMaterial} material={(mat == null ? "<NULL — URP renderer feature is missing>" : mat.shader.name)}");
                        if (mat == null)
                        {
                            Debug.LogError("[ARDiag] ARCameraBackground.material is NULL. Add the AR Background Renderer Feature to the active URP Renderer (Mobile_Renderer.asset). Use menu: Locackthon → Setup → Ensure AR Background Renderer Feature.");
                        }
                    }
                }

                var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                Debug.Log($"[ARDiag] currentRenderPipeline={(rp == null ? "<built-in>" : rp.GetType().Name)}");

                Destroy(gameObject);
            }
        }
    }
}
