using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Locackthon.AR
{
    // Optional keepsake-photo button. Captures the current frame (camera feed + AR overlay)
    // and saves it to the device gallery so the player can find it in Photos / Gallery.
    //
    // Implementation note:
    //   ScreenCapture.CaptureScreenshot writes to Application.persistentDataPath which on
    //   Android is private app storage (/Android/data/<pkg>/files) — the system MediaScanner
    //   never indexes that path, so the photo is invisible to the Gallery app. To make it
    //   actually appear, we have to insert through MediaStore.Images.Media on Android 10+ or
    //   write to public DCIM/Pictures and notify MediaScanner on older versions.
    [RequireComponent(typeof(Button))]
    public class ARPhotoButton : MonoBehaviour
    {
        [SerializeField] private string albumName = "Locackthon";

        private Button button;
        private bool capturing;

        // In-app diagnostic — last save result, displayed via OnGUI so we can see success/failure
        // without USB + logcat. Set on every save attempt.
        private string lastResult = "";
        private float lastResultExpiry;

        private void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(OnClick);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(OnClick);
        }

        private void OnClick()
        {
            if (capturing) { Debug.Log("[ARPhotoButton] Capture already in flight — ignoring tap."); return; }
            ShowResult("Capturing…");
            StartCoroutine(CaptureAndSave());
        }

        private void ShowResult(string msg)
        {
            lastResult = msg;
            lastResultExpiry = Time.unscaledTime + 6f;
            Debug.Log($"[ARPhotoButton] {msg}");
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(lastResult) || Time.unscaledTime > lastResultExpiry) return;
            float h = Mathf.Max(60f, Screen.height / 14f);
            var rect = new Rect(20, Screen.height - 220, Screen.width - 40, h);
            var bg = GUI.skin.box;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(20, Screen.height / 50),
                normal = { textColor = Color.white },
                wordWrap = true,
            };
            GUI.Box(rect, GUIContent.none);
            GUI.Label(rect, "  " + lastResult, style);
        }

        private IEnumerator CaptureAndSave()
        {
            capturing = true;

            // Must wait for end-of-frame so the camera feed + UI overlay are fully composed.
            yield return new WaitForEndOfFrame();

            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            byte[] png = tex.EncodeToPNG();
            Destroy(tex);

            string fileName = $"locackthon_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";

#if UNITY_ANDROID && !UNITY_EDITOR
            SaveToAndroidGallery(png, fileName);
#else
            string path = Path.Combine(Application.persistentDataPath, fileName);
            try { File.WriteAllBytes(path, png); ShowResult($"Saved (non-Android) → {path}"); }
            catch (System.Exception e) { ShowResult($"Save failed: {e.Message}"); }
#endif

            capturing = false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void SaveToAndroidGallery(byte[] png, string fileName)
        {
            // Android 10+ (API 29+) uses MediaStore + RELATIVE_PATH and needs no permission.
            // Android 9 needs WRITE_EXTERNAL_STORAGE — request it (no-op on 10+).
            int sdkInt = 0;
            try
            {
                using var versionClass = new AndroidJavaClass("android.os.Build$VERSION");
                sdkInt = versionClass.GetStatic<int>("SDK_INT");
            }
            catch (System.Exception e) { Debug.LogWarning($"[ARPhotoButton] Could not read Build.VERSION.SDK_INT: {e.Message}"); }
            Debug.Log($"[ARPhotoButton] Android SDK_INT={sdkInt}");

            if (sdkInt < 29 && !Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
            {
                Debug.Log("[ARPhotoButton] Requesting WRITE_EXTERNAL_STORAGE (Android 9 path)…");
                Permission.RequestUserPermission(Permission.ExternalStorageWrite);
                // Don't block on the dialog — the user can tap Photo again after granting.
                if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
                {
                    Debug.LogWarning("[ARPhotoButton] WRITE_EXTERNAL_STORAGE not granted yet. Tap Photo again after granting.");
                    return;
                }
            }

            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");

                using var values = new AndroidJavaObject("android.content.ContentValues");
                values.Call("put", "_display_name", fileName);
                values.Call("put", "mime_type", "image/png");
                values.Call("put", "date_added", (System.DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString());

                if (sdkInt >= 29)
                {
                    // Pictures/Locackthon — visible in Gallery's "Pictures" / app-named album.
                    // NOTE: deliberately NOT setting is_pending — clearing it later requires
                    // ContentResolver.update() with null selection args, which AndroidJavaObject's
                    // overload resolution mishandles. Skipping is_pending is safe for small PNGs
                    // because the file is fully written before close() returns.
                    values.Call("put", "relative_path", $"Pictures/{albumName}");
                }

                using var mediaStoreImagesMedia = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
                using var externalUri = mediaStoreImagesMedia.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");

                using var imageUri = resolver.Call<AndroidJavaObject>("insert", externalUri, values);
                if (imageUri == null)
                {
                    ShowResult("MediaStore.insert returned null Uri.");
                    return;
                }

                using (var outputStream = resolver.Call<AndroidJavaObject>("openOutputStream", imageUri))
                {
                    if (outputStream == null)
                    {
                        ShowResult("openOutputStream returned null.");
                        return;
                    }
                    // 3-arg overload write(byte[], int, int) avoids the AndroidJavaObject.Call
                    // overload-resolution ambiguity between write(int) and write(byte[]).
                    outputStream.Call("write", png, 0, png.Length);
                    outputStream.Call("flush");
                    outputStream.Call("close");
                }

                string uriStr = imageUri.Call<string>("toString");
                ShowResult($"Saved! Pictures/{albumName}/{fileName}\nuri={uriStr}");
            }
            catch (System.Exception e)
            {
                ShowResult($"Save failed: {e.GetType().Name}: {e.Message}");
                Debug.LogError($"[ARPhotoButton] MediaStore save trace:\n{e.StackTrace}");

                // Last-ditch fallback: write the PNG to the app-private path so at least we have
                // a file we can pull via 'adb pull /storage/emulated/0/Android/data/<pkg>/files/'.
                try
                {
                    string privPath = Path.Combine(Application.persistentDataPath, fileName);
                    File.WriteAllBytes(privPath, png);
                    Debug.Log($"[ARPhotoButton] Fallback: wrote private copy to {privPath}");
                }
                catch (System.Exception e2)
                {
                    Debug.LogError($"[ARPhotoButton] Fallback private write also failed: {e2.Message}");
                }
            }
        }
#endif
    }
}
