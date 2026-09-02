using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Locackthon.Core;
using Locackthon.Data;
using Locackthon.Gameplay;
using Locackthon.Gps;
using Locackthon.Lantern;

namespace Locackthon.UI
{
    // Dev Mode UI overlay for the Lantern scene.
    //
    // What it does:
    //   - "DEV" gear button at top-left expands a panel of "Test <Spirit>" buttons.
    //   - Tapping a spirit teleports simulated GPS to that spirit's lat/lon, swaps the
    //     ProximityWatcher's watched list, tints the SpiritBinder, and saves the choice on
    //     GameStateManager. The user stays on the Lantern screen — they then tap "Open
    //     Camera" themselves to enter AR.
    //   - The selection persists across the AR round-trip via GameStateManager.LastSelectedSpirit,
    //     so the same flame colour shows up after capture-and-return.
    //
    // Self-bootstraps via [RuntimeInitializeOnLoadMethod] when LanternPrototype loads — no
    // scene wiring is required. Add or remove spirits from the panel by editing
    // DefaultSpiritAssetNames below.
    public class DevModeSpiritSelector : MonoBehaviour
    {
        // Spirit assets to expose, in display order. Matched against SpiritRegistry by asset name.
        private static readonly string[] DefaultSpiritAssetNames =
        {
            "Spirit_Evaporate",
            "Spirit_Heated",
            "Spirit_Melting",
            "Spirit_Fallen",
        };

        private const string LanternSceneName = "LanternPrototype";

        // Colour palette aligned with CollectionBookBootstrap.
        private static readonly Color PanelBg  = new Color(0.10f, 0.08f, 0.14f, 0.95f);
        private static readonly Color GearBg   = new Color(0.10f, 0.08f, 0.14f, 0.85f);
        private static readonly Color WarmText = new Color(1f, 0.95f, 0.85f, 1f);
        private static readonly Color ToastBg  = new Color(0.05f, 0.04f, 0.08f, 0.92f);

        private GpsService gps;
        private SpiritRegistry registry;
        private RectTransform panelRect;
        private GameObject toastGo;
        private TMP_Text toastText;
        private Coroutine toastRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != LanternSceneName) return;
            if (FindFirstObjectByType<DevModeSpiritSelector>() != null) return;
            var go = new GameObject("DevModeSpiritSelector");
            go.AddComponent<DevModeSpiritSelector>();
        }

        private IEnumerator Start()
        {
            // CollectionBookBootstrap.Awake creates CollectionDatabase; wait briefly for it.
            float waited = 0f;
            while ((gps == null || registry == null) && waited < 2f)
            {
                if (gps == null) gps = FindFirstObjectByType<GpsService>();
                if (registry == null && CollectionDatabase.Instance != null)
                    registry = CollectionDatabase.Instance.Registry;
                if (gps != null && registry != null) break;
                yield return null;
                waited += Time.unscaledDeltaTime;
            }

            if (gps == null)
            {
                Debug.LogWarning("[DevModeSpiritSelector] No GpsService found in Lantern scene — Dev Mode UI will do nothing.");
                yield break;
            }

            // REAL-GPS GATE. This whole component — the DEV teleport panel AND the demo auto-advance
            // teleport — is dev/indoor-testing only. When GpsService is in real device-GPS mode
            // (useDevMode = false), it must do NOTHING: no panel, no SetSimulatedCoordinate, no
            // SetWatched override, no flame retint. Proximity is then driven purely by the player's
            // physical location, so they must actually walk to each spirit's 행궁동 coordinate.
            if (!gps.IsDevMode)
            {
                Debug.Log("[DevModeSpiritSelector] useDevMode=false (real device GPS) — DevMode UI and auto-advance teleport DISABLED. Proximity is driven by physical location only.");
                yield break;
            }

            if (registry == null)
            {
                Debug.LogWarning("[DevModeSpiritSelector] No SpiritRegistry available (CollectionDatabase has none) — Dev Mode UI cannot list spirits.");
                yield break;
            }

            BuildUI();

            // DEMO auto-advance. On every Lantern (re)load — launch and each return-from-catch —
            // teleport the simulated dev location onto the NEXT UNCAUGHT spirit, in the fixed order
            // Evaporate → Heated → Melting → Fallen (DefaultSpiritAssetNames). ReapplySelection
            // moves the sim GPS onto that spirit (distance ≈ 0), swaps the watched-spirit gate, and
            // tints the flame, so the proximity alert fires automatically without the DEV panel.
            //
            // "Uncaught" is read from CollectionDatabase — the same authoritative set that drives
            // GameStateManager's all-4 reward gate (IsComplete) — so auto-advance and the ending
            // stay consistent: when the last uncaught spirit is caught, ReturnToLantern routes
            // straight to RewardScene and this Lantern reload never happens. If we somehow reach
            // the Lantern with all 4 caught, PickNextUncaughtSpirit returns null and we don't
            // teleport (leaving the scene's authored sim coordinate untouched).
            var next = PickNextUncaughtSpirit();
            if (next != null)
            {
                Debug.Log($"[DevModeSpiritSelector] Auto-advance → next uncaught '{next.name}' ({next.displayName}). Teleporting sim location to ({next.latitude:F6},{next.longitude:F6}) so its proximity alert fires.");
                ReapplySelection(next);
            }
            else
            {
                Debug.Log("[DevModeSpiritSelector] Auto-advance: all spirits captured — not teleporting (reward/ending should already have triggered).");
            }
        }

        // Returns the first spirit in DefaultSpiritAssetNames order that is NOT yet in the
        // CollectionDatabase, or null when every one of them is captured. This is the demo's
        // "where does the player go next" decision.
        private Spirit PickNextUncaughtSpirit()
        {
            var db = CollectionDatabase.Instance;
            foreach (var assetName in DefaultSpiritAssetNames)
            {
                var spirit = registry.FindByName(assetName);
                if (spirit == null) continue;
                bool caught = db != null && db.HasCaptured(spirit);
                if (!caught) return spirit;
            }
            return null; // all captured
        }

        // Shared application path used by both initial DEV pick and post-scene-reload restoration.
        private void ReapplySelection(Spirit spirit)
        {
            if (spirit == null) return;

            // Write GameStateManager.LastSelectedSpirit FIRST so any code that reads it during
            // the next steps (notably SpiritBinder.Apply, which prefers LastSelectedSpirit over
            // its serialized field to avoid the scene-reload flicker) sees the new value, not
            // the previous DEV pick. Wrong order made the Lantern flame show the *previous*
            // spirit's accent on first tap, and only catch up on a second tap of the same one.
            if (GameStateManager.Instance != null) GameStateManager.Instance.SetActiveTargetSpirit(spirit);

            if (gps != null) gps.SetSimulatedCoordinate(spirit.latitude, spirit.longitude);
            var watcher = FindFirstObjectByType<ProximityWatcher>();
            if (watcher != null) watcher.SetWatched(spirit);
            var binder = FindFirstObjectByType<SpiritBinder>();
            if (binder != null) binder.SetSpirit(spirit);
        }

        private void BuildUI()
        {
            var canvas = EnsureCanvas();
            EnsureEventSystem();

            var panel = BuildGearButton(canvas);
            panelRect = panel;
            BuildPanelContents(panel);
            BuildToast(canvas);

            panel.gameObject.SetActive(false);
        }

        private Canvas EnsureCanvas()
        {
            var existing = FindFirstObjectByType<Canvas>();
            if (existing != null) return existing.rootCanvas;

            var go = new GameObject("DevMode_Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            var t = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (t != null) go.AddComponent(t);
            else go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        private RectTransform BuildGearButton(Canvas canvas)
        {
            // Gear button — top-left.
            var btnGo = new GameObject("DevGearButton",
                typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(canvas.transform, false);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(40f, -40f);
            rt.sizeDelta = new Vector2(120f, 120f);

            btnGo.GetComponent<Image>().color = GearBg;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(btnGo.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "DEV";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 36f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = WarmText;
            tmp.raycastTarget = false;

            // Panel — anchored just below gear, top-left aligned.
            var panelGo = new GameObject("DevGearPanel",
                typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelGo.transform.SetParent(canvas.transform, false);
            var prt = panelGo.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 1f);
            prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot     = new Vector2(0f, 1f);
            prt.anchoredPosition = new Vector2(40f, -180f);
            prt.sizeDelta = new Vector2(540f, 0f);
            panelGo.GetComponent<Image>().color = PanelBg;

            var vlg = panelGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 20, 20);
            vlg.spacing = 12f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = panelGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RectTransform panel = prt;

            btnGo.GetComponent<Button>().onClick.AddListener(() =>
            {
                bool willOpen = !panel.gameObject.activeSelf;
                panel.gameObject.SetActive(willOpen);
                Debug.Log($"[DevModeSpiritSelector] Panel {(willOpen ? "OPENED" : "CLOSED")}.");
            });

            return panel;
        }

        private void BuildPanelContents(RectTransform panel)
        {
            // Header line: shows current sim coord.
            var header = BuildLabel(panel, "Dev: choose a spirit to teleport to", 30f, FontStyles.Bold);
            header.color = new Color(WarmText.r, WarmText.g, WarmText.b, 0.85f);

            int built = 0;
            foreach (var assetName in DefaultSpiritAssetNames)
            {
                var spirit = registry.FindByName(assetName);
                if (spirit == null)
                {
                    Debug.LogWarning($"[DevModeSpiritSelector] Spirit asset '{assetName}' is not in the SpiritRegistry — skipping.");
                    continue;
                }
                BuildSpiritButton(panel, spirit);
                built++;
            }

            if (built == 0)
            {
                BuildLabel(panel, "(No matching spirits in registry.)", 28f, FontStyles.Italic);
            }
        }

        private void BuildSpiritButton(RectTransform parent, Spirit spirit)
        {
            var go = new GameObject($"Btn_{spirit.name}",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 96f;
            le.preferredHeight = 96f;

            var img = go.GetComponent<Image>();
            img.color = spirit.accentColor;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16f, 0f);
            lrt.offsetMax = new Vector2(-16f, 0f);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            string display = string.IsNullOrEmpty(spirit.displayName) ? spirit.name : spirit.displayName;
            tmp.text = $"Test {display}";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 38f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(0.05f, 0.04f, 0.08f, 1f);
            tmp.raycastTarget = false;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            Spirit captured = spirit;
            btn.onClick.AddListener(() => OnTestSpiritClicked(captured));
        }

        private TMP_Text BuildLabel(RectTransform parent, string text, float fontSize, FontStyles style)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = fontSize + 12f;
            le.preferredHeight = fontSize + 12f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = WarmText;
            tmp.raycastTarget = false;
            return tmp;
        }

        private void BuildToast(Canvas canvas)
        {
            toastGo = new GameObject("DevModeToast",
                typeof(RectTransform), typeof(Image));
            toastGo.transform.SetParent(canvas.transform, false);
            var rt = toastGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 240f);
            rt.sizeDelta = new Vector2(820f, 110f);
            toastGo.GetComponent<Image>().color = ToastBg;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(toastGo.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(20f, 0f);
            lrt.offsetMax = new Vector2(-20f, 0f);
            toastText = labelGo.AddComponent<TextMeshProUGUI>();
            toastText.alignment = TextAlignmentOptions.Center;
            toastText.fontSize = 34f;
            toastText.fontStyle = FontStyles.Bold;
            toastText.color = WarmText;
            toastText.raycastTarget = false;

            toastGo.SetActive(false);
        }

        private void OnTestSpiritClicked(Spirit spirit)
        {
            if (spirit == null) return;
            Debug.Log($"[DevModeSpiritSelector][trace] Test button pressed: asset={spirit.name}  displayName={spirit.displayName}  lat={spirit.latitude}  lon={spirit.longitude}  accentColor=#{ColorUtility.ToHtmlStringRGB(spirit.accentColor)}.");
            ReapplySelection(spirit);
            ShowToast($"Selected: {spirit.displayName} — tap Open Camera to enter AR");
            if (panelRect != null) panelRect.gameObject.SetActive(false);
        }

        private void ShowToast(string msg)
        {
            if (toastGo == null || toastText == null) return;
            toastText.text = msg;
            toastGo.SetActive(true);
            if (toastRoutine != null) StopCoroutine(toastRoutine);
            toastRoutine = StartCoroutine(HideToastAfter(2.5f));
        }

        private IEnumerator HideToastAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (toastGo != null) toastGo.SetActive(false);
            toastRoutine = null;
        }
    }
}
