using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using Locackthon.Core;
using Locackthon.Data;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Locackthon.AR
{
    // Drives the AR scene's capture flow:
    //   1. Spawns a placeholder spirit in front of the AR camera (or instantiates spiritPrefab).
    //   2. Detects taps via three input paths (EnhancedTouch / Touchscreen / Pointer) plus a
    //      fullscreen UI tap-catcher routed through EventSystem.
    //   3. Hit-tests the tap against the spirit's screen position with a finger-sized forgiveness
    //      radius (works in both bottom-left and top-left coordinate conventions).
    //   4. Plays a short fade animation and a particle burst, then asks GameStateManager to
    //      return to the Lantern scene with the captured spirit.
    //
    // Diagnostic OnGUI HUD shows tap count, hits, last-tap distance, and capture milestone —
    // visible without logcat during on-device testing.
    public class ARCaptureController : MonoBehaviour
    {
        #region Inspector fields

        [Header("AR")]
        [Tooltip("AR camera. If null, Camera.main is used (AR Foundation drives Camera.main on the XR Origin's AR Camera child).")]
        [SerializeField] private Camera arCamera;

        [Header("Spirit Spawn")]
        [Tooltip("Optional 3D prefab to spawn. If null, a primitive sphere is created with the FallbackColor.")]
        [SerializeField] private GameObject spiritPrefab;
        [SerializeField] private Color fallbackColor = new Color(0.10f, 0.95f, 1.00f);

        [Tooltip("Min/max horizontal distance from the player at spawn (meters).")]
        [SerializeField, Min(0.5f)] private float minSpawnDistance = 3f;
        [SerializeField, Min(0.5f)] private float maxSpawnDistance = 5f;

        [Tooltip("Spirit is spawned at the camera's height plus this offset (meters).")]
        [SerializeField] private float spawnHeightOffset = 1.5f;

        [Tooltip("DEMO/DEBUG: spawn the spirit at a FIXED distance directly in FRONT of the camera, at eye level (camera height), instead of a random off-camera bearing. Makes it clearly world-anchored and easy to find — and avoids the 'stuck at the screen edge / on top of the camera' bad-angle spawns. Turn OFF for the 'physically rotate to search' gameplay.")]
        [SerializeField] private bool spawnInFrontFixed = true;
        [Tooltip("Distance (meters) directly ahead of the camera used when spawnInFrontFixed is true. ~1.5–2 m reads as clearly in world space without being on top of the camera.")]
        [SerializeField, Min(0.5f)] private float frontSpawnDistance = 1.8f;

        [Tooltip("Local scale of the placeholder sphere (used only when target.sprite is null and we fall back to the 3D primitive). Bigger = easier to tap.")]
        [SerializeField, Min(0.1f)] private float placeholderScale = 0.5f;

        [Tooltip("World-space size (meters) of the 2D billboard sprite. The sprite's largest bounds dimension is scaled to this length, so a value of 1.0 means \"1 m tall (or wide) at spawn distance.\" Defaults to 1.0 — visible at the 3-5 m spawn range.")]
        [SerializeField, Min(0.05f)] private float billboardWorldSize = 1.0f;

        [Tooltip("Overall base-size multiplier for the spawned spirit billboard. 1.0 = full billboardWorldSize; lower = smaller baseline. This only shrinks the base scale — the natural distance behavior (closer looks bigger, farther looks smaller) is unchanged. Tune here to taste.")]
        [SerializeField, Min(0.05f)] private float spiritScale = 0.7f;

        [Tooltip("Screen-space radius (pixels) within which a tap counts as hitting the spirit.")]
        [SerializeField, Min(0f)] private float tapForgivenessPixels = 120f;

        [Tooltip("Forbidden cone around the camera's forward direction. Ignored when devSpawnInFront is true.")]
        [SerializeField, Range(0f, 90f)] private float forbiddenForwardConeDegrees = 60f;

        [Tooltip("DEV ONLY: true = spawn in front for editor testing. For real phone testing, set false in Inspector.")]
        [SerializeField] private bool devSpawnInFront = true;

        [Tooltip("DEV ONLY: force which spirit spawns, to test all 4 result images without GPS/selection.\n-1 = off (use the real target from GameStateManager).\n0..3 = force compositeSpirits[index] (0=Evaporate, 1=Heated, 2=Melting, 3=Fallen). Set this in the Inspector, enter Play in ARCaptureScene, and the forced spirit spawns + drives the result image.")]
        [SerializeField] private int devSpiritIndex = -1;

        [Tooltip("Show the bottom-screen OnGUI diagnostic strip (tap counts, capture state, screen pos). OFF for normal play.")]
        [SerializeField] private bool showDebugHud = false;

        [Tooltip("Show a small ALWAYS-VISIBLE banner at the top of the AR screen reporting whether the spirit was world-locked to an ARAnchor ('ANCHORED') or fell back to a raw fixed position ('FALLBACK'), plus the live anchor tracking state. Use this to diagnose the 'spirit follows the view' bug on device without logcat. Turn OFF for normal play.")]
        [SerializeField] private bool showAnchorStatusOverlay = true;

#if UNITY_EDITOR
        [Header("Editor Mouse-Look (editor-only)")]
        [Tooltip("Hold right mouse button and drag in the Game view to rotate the AR camera yaw/pitch. Stripped from device builds.")]
        [SerializeField] private bool editorMouseLookEnabled = true;
        [Tooltip("Degrees per pixel of mouse delta.")]
        [SerializeField, Min(0.01f)] private float editorMouseLookSensitivity = 0.2f;
#endif

        [Header("Capture Animation")]
        [Tooltip("Seconds the spirit takes to shrink and fade transparent on capture.")]
        [SerializeField, Min(0.05f)] private float captureDuration = 1.5f;
        [Tooltip("Extra seconds the '잡는중...' toast lingers AFTER the fade finishes, before the result panel appears. Toast turns on at capture start and runs through the fade.")]
        [SerializeField, Min(0f)] private float postCaptureToastSeconds = 0.4f;
        [Tooltip("Optional toast root. Should contain the '잡는중...' text.")]
        [SerializeField] private GameObject captureToast;

        [Tooltip("Number of placeholder burst spheres spawned at the spirit on capture.")]
        [SerializeField, Min(0)] private int burstCount = 8;
        [SerializeField, Min(0.05f)] private float burstDuration = 0.6f;
        [SerializeField, Min(0.05f)] private float burstRadius = 0.6f;

        [Header("Stage 1 — Spirit Found Hint")]
        [Tooltip("Bottom hint shown while the spirit is idle AND within captureRangeMeters of the AR camera. Hidden when CaptureSequence starts.")]
        [SerializeField] private GameObject foundHintPanel;
        [SerializeField] private TextMeshProUGUI foundHintLabel;
        [Tooltip("Text shown when the spirit is in range AND inside the camera viewport (player is looking at it).")]
        [TextArea(2, 4)]
        [FormerlySerializedAs("foundHintText")]
        [SerializeField] private string onscreenHintText  = "앗 물방울 정령이다!\n터치해서 물방울 정령을 잡자!";
        [Tooltip("Text shown when the spirit is in range but OFF-screen (player needs to turn to find it).")]
        [TextArea(2, 4)]
        [SerializeField] private string offscreenHintText = "주변에 정령의 기운이 느껴집니다..";
        [Tooltip("AR-local in-range distance (meters) between the AR camera and the spawned spirit. Mirrors ProximityWatcher.captureRangeMeters so the hint logic stays consistent with the Lantern scene.")]
        [SerializeField, Min(0.5f)] private float captureRangeMeters = 10f;

        [Header("Stage 2 — Capturing Toast Text")]
        [Tooltip("TMP label inside the existing CaptureToast. Text is applied in OnEnable; lifecycle/timing unchanged.")]
        [SerializeField] private TextMeshProUGUI captureToastLabel;
        [TextArea(1, 3)]
        [SerializeField] private string capturingToastText = "잡는중...";

        [Header("Stage 3 — Capture Result Panel")]
        [Tooltip("Overlay shown after capture FX. If assigned, replaces the auto-return-to-Lantern step — user dismisses via Btn_Close or Btn_ResultArrow.")]
        [SerializeField] private GameObject captureResultPanel;
        [Tooltip("The lantern art behind the captured spirit. If the captured Spirit has a capturedSprite, this Image is swapped to that composite at runtime; otherwise its scene-authored sprite is preserved.")]
        [SerializeField] private Image lanternFrameImage;
        [SerializeField] private Image capturedSpiritImage;
        [SerializeField] private TextMeshProUGUI spiritNameLabel;
        [SerializeField] private Button captureResultCloseButton;
        [Tooltip("Optional second advance button — bottom-right arrow inside the card. Wired to the same handler as Btn_Close.")]
        [SerializeField] private Button captureResultArrowButton;
        [SerializeField] private TextMeshProUGUI captureDescLabel;
        [TextArea(2, 5)]
        [SerializeField] private string captureDescText = "증발할 뻔한 물방울을 잡았다!\n아직 몸이 흐릿흐릿하다.\n등불 안에서 천천히 식혀주자.";

        [Header("Stage 3 — Lantern-gated result image")]
        [Tooltip("화홍 lantern ONLY (SelectedLanternIndex==0): the 4 spirits, paired BY INDEX with capturedComposite. The captured spirit is matched here via Array.IndexOf, and capturedComposite at that same index is shown.")]
        [SerializeField] private Spirit[] compositeSpirits = new Spirit[4];
        [Tooltip("화홍 'lantern + spirit' composite art — one per compositeSpirits entry (same index). I assign these. Empty slot → falls back to plainLanternSprites[0].")]
        [SerializeField] private Sprite[] capturedComposite = new Sprite[4];
        [Tooltip("Plain selected-lantern art for NON-화홍 lanterns. Index 0=화홍, 1=방울, 2=수문. Index 0 is also the universal empty-slot fallback.")]
        [SerializeField] private Sprite[] plainLanternSprites = new Sprite[3];

        [Header("Stage 2 — Catching ('잡는중') sprite")]
        [Tooltip("Sprite shown ON the shrinking spirit DURING the 잡는중 capture animation, per spirit (paired BY INDEX with compositeSpirits: 0=Evaporate,1=Heated,2=Melting,3=Fallen). Replaces the idle billboard sprite for the shrink only. Empty slot → catchingSpriteShared → (if also empty) the original spawn sprite.")]
        [SerializeField] private Sprite[] catchingSprites = new Sprite[4];
        [Tooltip("Shared fallback 잡는중 sprite used when a spirit's catchingSprites slot is empty.")]
        [SerializeField] private Sprite catchingSpriteShared;

        #endregion

        #region Public API

        public Transform SpiritAnchor => spawned != null ? spawned.transform : null;
        public bool HasSpirit => spawned != null && !captured;

        #endregion

        #region Runtime state

        private GameObject spawned;
        private Vector3 anchorPosition;
        private Spirit target;

        // AR Foundation anchor the spirit is pinned to (drift-proof world-locking). When non-null,
        // the spirit is parented to this anchor and AF drives its world pose as tracking refines —
        // so we never write the spirit's position ourselves. Null = no anchor available (no
        // ARAnchorManager / unsupported / creation failed) → fixed-anchorPosition fallback.
        private ARAnchor spiritAnchor;

        // Human-readable result of the last anchoring attempt, shown in the on-screen overlay so
        // the anchored-vs-fallback path is visible on device without logcat.
        private string anchorStatus = "<no spirit yet>";

        // Two flags so we can tell "user has tapped, capture is queued" (`captured`) apart from
        // "the capture coroutine is currently running" (`capturing`). Together they prevent both
        // double-captures and re-entrancy from same-frame Touch + UI clicks.
        private bool captured;
        private bool capturing;

        // HUD diagnostics — read by OnGUI.
        private int tapCountSeen;
        private int tapHitsOnSpirit;
        private string lastTapPathHint = "<no tap yet>";
        private string lastCaptureMilestone = "<none>";
        private float lastTapDistPx = -1f;

        // Cached reference to the fullscreen UI tap-catcher this controller spawns in
        // OnEnable. Held so OnDisable can clean it up without calling GameObject.Find,
        // which trips Unity's "go.IsActive()" assertion during scene teardown.
        private GameObject tapCatcherGo;

        // Captured at OnEnable so PopulateCaptureResultPanel can restore the lantern art
        // when a spirit without a capturedSprite is captured AFTER a spirit with one.
        // Without these, the previous capture's composite would linger on screen.
        private Sprite lanternFrameDefaultSprite;
        private Color  lanternFrameDefaultColor = Color.white;
        private bool   capturedSpiritDefaultActive = true;

        #endregion

        #region Unity lifecycle

        private void OnEnable()
        {
            target = GameStateManager.Instance != null ? GameStateManager.Instance.ActiveTargetSpirit : null;
            Debug.Log($"[ARCaptureController][trace] OnEnable target={(target == null ? "<NULL>" : target.name)}  GSM.Instance={(GameStateManager.Instance == null ? "<NULL>" : "OK")}  GSM.ActiveTargetSpirit={(GameStateManager.Instance != null && GameStateManager.Instance.ActiveTargetSpirit != null ? GameStateManager.Instance.ActiveTargetSpirit.name : "<NULL>")}");

            // DEV override: force a specific spirit so all 4 can be tested without proximity or a
            // lantern selection. Overrides the spawned billboard AND the result-panel image (both
            // key off `target`). Set devSpiritIndex back to -1 for normal play.
            if (devSpiritIndex >= 0 && compositeSpirits != null
                && devSpiritIndex < compositeSpirits.Length && compositeSpirits[devSpiritIndex] != null)
            {
                target = compositeSpirits[devSpiritIndex];
                Debug.Log($"[ARCaptureController][trace] devSpiritIndex={devSpiritIndex} → forcing target={target.name} (overrides GSM.ActiveTargetSpirit).");
            }
            if (captureToast != null) captureToast.SetActive(false);
            if (captureResultPanel != null) captureResultPanel.SetActive(false);
            if (foundHintPanel != null) foundHintPanel.SetActive(false); // shown once SpawnSpirit completes

            // Snapshot the scene-authored lantern art so we can restore it between captures.
            // Only seeded the first time OnEnable runs in this scene-load — re-entering AR
            // between captures must not overwrite the cache with whatever the last
            // PopulateCaptureResultPanel left behind.
            if (lanternFrameImage != null && lanternFrameDefaultSprite == null)
            {
                lanternFrameDefaultSprite = lanternFrameImage.sprite;
                lanternFrameDefaultColor  = lanternFrameImage.color;
            }
            if (capturedSpiritImage != null)
            {
                capturedSpiritDefaultActive = capturedSpiritImage.gameObject.activeSelf;
            }

            // Apply the serialized text for each stage once at scene start. Inspector-edits
            // win — these strings are the defaults; the artist/designer can override per build.
            // foundHintLabel.text is driven per-frame in Update (onscreen vs offscreen variant).
            if (captureToastLabel != null) captureToastLabel.text = capturingToastText;

            if (captureResultCloseButton != null)
            {
                captureResultCloseButton.onClick.RemoveListener(OnCaptureResultClose);
                captureResultCloseButton.onClick.AddListener(OnCaptureResultClose);
            }
            if (captureResultArrowButton != null)
            {
                captureResultArrowButton.onClick.RemoveListener(OnCaptureResultClose);
                captureResultArrowButton.onClick.AddListener(OnCaptureResultClose);
            }

            captured = false;
            capturing = false;
            spawned = null;

            tapCountSeen = 0;
            tapHitsOnSpirit = 0;
            lastTapPathHint = "<no tap yet>";
            lastCaptureMilestone = "<none>";
            lastTapDistPx = -1f;

            // EnhancedTouch is the most reliable touch path on Android. Some devices/Unity
            // versions miss single-finger taps via Touchscreen.primaryTouch alone.
            if (!EnhancedTouchSupport.enabled)
            {
                try { EnhancedTouchSupport.Enable(); }
                catch (System.Exception e) { Debug.LogWarning($"[ARCaptureController] EnhancedTouchSupport.Enable failed: {e.Message}"); }
            }

            // Force a generous tap-forgiveness radius — the Inspector default of 120px is tight
            // for phone screens; 15% of screen height works for any device.
            tapForgivenessPixels = Mathf.Max(tapForgivenessPixels, Screen.height * 0.15f);

            EnsureFallbackCaptureButton();
            StartCoroutine(SpawnSpiritWhenCameraReady());

            Debug.Log($"[ARCaptureController] OnEnable. target={(target == null ? "<null>" : target.name)} forgive={tapForgivenessPixels:F0}px");
        }

        private void OnDisable()
        {
            // Tap-catcher is rebuilt on every OnEnable; clean up so leaving the scene doesn't
            // leave a stale GameObject behind that would also intercept Lantern-screen clicks.
            //
            // Use the cached reference instead of GameObject.Find. Find can trip an internal
            // "Assertion failed on expression: go.IsActive()" during scene teardown when the
            // controller is being disabled as part of unloading ARCaptureScene — the scene's
            // root objects are mid-deactivate and the global GO list isn't safe to scan.
            if (tapCatcherGo != null) Destroy(tapCatcherGo);
            tapCatcherGo = null;

            // Don't leak the spirit's AR anchor into the subsystem when leaving the scene.
            CleanupAnchor();
        }

        private void Update()
        {
            if (arCamera == null) arCamera = Camera.main;

            if (spawned != null && !captured)
            {
                // WORLD POSITION IS FIXED. anchorPosition is computed once in SpawnSpirit and the
                // spirit's transform.position is set there exactly once. We deliberately do NOT
                // rewrite position every frame anymore — so it can never be recomputed against the
                // moving camera and therefore can never "follow" it.
                //
                // Parent management:
                //   ANCHORED (spiritAnchor != null): the ARAnchor's transform IS the intended
                //     parent. AF moves that anchor as tracking refines and the spirit (its child)
                //     follows — that's the whole drift-proofing. We leave it parented to the anchor
                //     and never write its position. We only re-assert the parent if something
                //     external stole it away from the anchor.
                //   NOT ANCHORED (fallback): the spirit must sit at scene root, pinned to the fixed
                //     anchorPosition. If anything reparents it (which would drag it with that
                //     parent — the camera-follow bug), we detach back to root and re-pin.
                Transform desiredParent = spiritAnchor != null ? spiritAnchor.transform : null;
                if (spawned.transform.parent != desiredParent)
                {
                    if (desiredParent != null)
                    {
                        spawned.transform.SetParent(desiredParent, worldPositionStays: true);
                    }
                    else
                    {
                        Debug.LogWarning($"[ARCaptureController] spawned was re-parented to '{(spawned.transform.parent != null ? spawned.transform.parent.name : "<root>")}' — detaching to scene root and re-pinning to anchorPosition so it stays world-fixed.");
                        spawned.transform.SetParent(null, worldPositionStays: false);
                        spawned.transform.position = anchorPosition;
                    }
                }

                // Billboard: ROTATION ONLY. Align the sprite to the AR camera each frame so it
                // always faces the player. Position is never touched here.
                if (arCamera != null)
                {
                    spawned.transform.rotation = arCamera.transform.rotation;
                }

                // 1 Hz diagnostic: confirm on device (read with `adb logcat -s Unity`) that
                // anchorPosition is constant and spawned.position == anchorPosition + bob.
                // If `cameraPos` changes (device moving) while `anchor` stays constant and
                // `spirit` tracks `anchor`, the spirit is correctly world-fixed.
                //
                // Also reports the spirit's projected screen point: if (sx, sy) reads
                // outside [0..Screen.width] / [0..Screen.height] for many consecutive
                // seconds while the user can still see the sprite, that's the edge-
                // forgiveness branch in TryCaptureAt that's keeping captures possible.
                if (showDebugHud && Time.frameCount % 60 == 0 && arCamera != null)
                {
                    Vector3 sp = arCamera.WorldToScreenPoint(spawned.transform.position);
                    bool offscreen = sp.z <= 0f || sp.x < 0f || sp.x > Screen.width || sp.y < 0f || sp.y > Screen.height;
                    Debug.Log($"[ARCaptureController][trace] frame={Time.frameCount} spirit={spawned.transform.position} anchor={anchorPosition} cameraPos={arCamera.transform.position} parent={(spawned.transform.parent == null ? "<root>" : spawned.transform.parent.name)}  spiritScreen=({sp.x:F0},{sp.y:F0},z={sp.z:F1}) screen={Screen.width}x{Screen.height} offscreen={offscreen}");
                }

                // Stage 1 hint: visible only while the AR camera is within captureRangeMeters
                // of the spirit. Lets us mirror the Lantern scene's "in range / out of range"
                // semantics without a separate proximity component in AR.
                //
                // When in range, the label swaps between two messages depending on whether
                // the spirit is inside the camera's viewport: onscreen (you see it) vs
                // offscreen (you're close but need to turn to find it). Recomputed every
                // frame so RMB-drag camera rotation flips the message live.
                if (foundHintPanel != null && arCamera != null)
                {
                    float distToSpirit = Vector3.Distance(arCamera.transform.position, spawned.transform.position);
                    bool inRange = distToSpirit <= captureRangeMeters;
                    if (foundHintPanel.activeSelf != inRange) foundHintPanel.SetActive(inRange);

                    if (inRange && foundHintLabel != null)
                    {
                        Vector3 vp = arCamera.WorldToViewportPoint(spawned.transform.position);
                        bool onscreen = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
                        string desired = onscreen ? onscreenHintText : offscreenHintText;
                        // Avoid pushing the same string into the TMP every frame — that
                        // would trigger a needless mesh rebuild.
                        if (foundHintLabel.text != desired) foundHintLabel.text = desired;
                    }
                }
            }

            if (TryGetTap(out Vector2 screenPos))
            {
                tapCountSeen++;
                TryCaptureAt(screenPos, "Update/Touch");
            }
        }

#if UNITY_EDITOR
        // Mouse-look is applied in LateUpdate so it runs AFTER AR Foundation's
        // TrackedPoseDriver writes the camera pose in Update — otherwise the driver
        // would overwrite our manual rotation each frame.
        private float editorYaw;
        private float editorPitch;
        private bool editorMouseLookHeld;

        private void LateUpdate()
        {
            if (!editorMouseLookEnabled) return;
            if (arCamera == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            // RMB chosen so it never fights the fullscreen UI tap-catcher (which only consumes LMB).
            bool rmb = mouse.rightButton.isPressed;
            if (!rmb)
            {
                editorMouseLookHeld = false;
                return;
            }

            // First frame of a drag: seed yaw/pitch from the camera's current orientation so
            // mouse-look picks up wherever the pose driver last left the camera.
            if (!editorMouseLookHeld)
            {
                var e = arCamera.transform.rotation.eulerAngles;
                editorYaw = e.y;
                editorPitch = e.x;
                if (editorPitch > 180f) editorPitch -= 360f;
                editorMouseLookHeld = true;
            }

            Vector2 delta = mouse.delta.ReadValue();
            editorYaw   += delta.x * editorMouseLookSensitivity;
            editorPitch -= delta.y * editorMouseLookSensitivity;
            editorPitch  = Mathf.Clamp(editorPitch, -89f, 89f);
            arCamera.transform.rotation = Quaternion.Euler(editorPitch, editorYaw, 0f);
        }
#endif

        #endregion

        #region Spawn

        // AR Foundation may take 1–2 frames to install the AR Camera as Camera.main, and several
        // more before the AR session is actually TRACKING (i.e. the camera transform reports the
        // real device pose instead of the default origin pose). SpawnSpirit computes anchorPosition
        // from arCamera.transform.position, so spawning before tracking starts anchors the spirit
        // against a bogus pose — it shows at the wrong spot, then "jumps" when tracking kicks in and
        // the camera pose snaps to reality. We therefore gate the spawn on BOTH:
        //   (a) the AR camera being resolved, and
        //   (b) ARSession.state == SessionTracking (so the camera pose is final).
        // If no AR session exists at all (e.g. the scene is played directly without AR, or an
        // unsupported device), we fall back to spawning once the camera resolves so dev/editor
        // testing still works.
        private IEnumerator SpawnSpiritWhenCameraReady()
        {
            const int maxFrames = 300; // ~5s at 60fps
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (arCamera == null) arCamera = Camera.main;

                if (arCamera != null)
                {
                    var state = ARSession.state;

                    // Camera pose is real — spawn now at the correct anchor.
                    if (state == ARSessionState.SessionTracking)
                    {
                        SpawnSpirit();
                        yield break;
                    }

                    // No AR session is going to come up (none in scene / unsupported / not
                    // installed). There's no tracking to wait for, so spawn best-effort once the
                    // camera has resolved. Give a short grace window first in case the session is
                    // still booting (None/Initializing flip to SessionTracking within a few frames).
                    bool sessionUnavailable =
                        state == ARSessionState.Unsupported ||
                        state == ARSessionState.NeedsInstall ||
                        state == ARSessionState.Installing;
                    bool sessionAbsentAfterGrace =
                        frame >= 90 && (state == ARSessionState.None || state == ARSessionState.CheckingAvailability);

                    if (sessionUnavailable || sessionAbsentAfterGrace)
                    {
                        Debug.LogWarning($"[ARCaptureController] Spawning without tracking gate (ARSession.state={state}) — no live tracking to wait for.");
                        SpawnSpirit();
                        yield break;
                    }
                }

                yield return null;
            }

            // Tracking never arrived within the window. Spawn anyway so the player isn't stuck with
            // an empty scene — position may be slightly off but it won't visibly jump after this.
            Debug.LogWarning($"[ARCaptureController] AR tracking not reached within {maxFrames} frames (camera={(arCamera == null ? "null" : "ok")}, ARSession.state={ARSession.state}) — spawning best-effort.");
            if (arCamera == null) arCamera = Camera.main;
            if (arCamera != null) SpawnSpirit();
            else Debug.LogWarning("[ARCaptureController] Camera.main never resolved — spirit not spawned.");
        }

        private void SpawnSpirit()
        {
            if (arCamera == null) { Debug.LogWarning("[ARCaptureController] No AR camera; cannot spawn spirit."); return; }

            // Choose a horizontal direction. In dev mode, always in front so it's visible in the
            // editor's stationary simulator camera. On device, exclude the forward cone so the
            // player has to physically rotate to find the spirit.
            Vector3 fwd = arCamera.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            float yawDeg;
            float distance;
            if (spawnInFrontFixed)
            {
                // DEMO/DEBUG: directly ahead, fixed distance, EYE LEVEL (fwd has y zeroed, so the
                // spawn point is at the camera's height — no vertical offset). This guarantees the
                // spirit sits clearly in world space in front of the player rather than at a steep
                // angle / right on top of the camera, which is what produces the "stuck at the
                // screen edge" symptom.
                yawDeg = 0f;
                distance = frontSpawnDistance;
                anchorPosition = arCamera.transform.position + fwd * distance;
            }
            else
            {
                if (devSpawnInFront)
                {
                    yawDeg = Random.Range(-45f, 45f);
                }
                else
                {
                    float forbiddenHalf = Mathf.Clamp(forbiddenForwardConeDegrees, 0f, 89f);
                    float allowedSweep = 360f - 2f * forbiddenHalf;
                    yawDeg = forbiddenHalf + Random.value * allowedSweep;
                    if (Random.value < 0.5f) yawDeg = -yawDeg;
                }

                Vector3 dir = Quaternion.AngleAxis(yawDeg, Vector3.up) * fwd;
                distance = Random.Range(minSpawnDistance, maxSpawnDistance);
                anchorPosition = arCamera.transform.position + dir * distance + Vector3.up * spawnHeightOffset;
            }

            if (spiritPrefab != null)
            {
                spawned = Instantiate(spiritPrefab, anchorPosition, Quaternion.identity);
            }
            else if (target != null && target.sprite != null)
            {
                // 2D billboard branch — uses the same Sprite asset the result panel uses.
                // anchorPosition + bob + tap hit-test all key off transform.position, so the
                // existing capture-detection logic continues to work unchanged.
                spawned = new GameObject("PlaceholderSpirit");

                var sr = spawned.AddComponent<SpriteRenderer>();
                sr.sprite = target.sprite;
                // Cast the sprite's largest bound to billboardWorldSize meters in world space.
                var bs = sr.sprite.bounds.size;
                float maxDim = Mathf.Max(bs.x, bs.y);
                float scale = (maxDim > 0.0001f) ? (billboardWorldSize / maxDim) : billboardWorldSize;
                // spiritScale only shrinks the BASE size; the world-space placement is unchanged, so
                // perspective still makes it look bigger up close and smaller far away.
                spawned.transform.localScale = Vector3.one * scale * spiritScale;
            }
            else
            {
                // Fallback (target null OR target.sprite null): the original tinted-sphere
                // placeholder. Kept so the AR scene played directly with no target still
                // renders something visible and tappable.
                spawned = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                spawned.name = "PlaceholderSpirit";
                spawned.transform.localScale = Vector3.one * placeholderScale;
                Color tint = target != null ? target.accentColor : fallbackColor;
                PlaceholderMaterialFactory.Apply(spawned, tint);
            }

            // Lock the spawned spirit to scene root so nothing — XR Origin, AR session,
            // ARCameraBackground, etc. — can implicitly parent it and drag it around when the
            // device moves. Then write its world transform AFTER detaching, otherwise a
            // parent's transform applied later in the frame could shift its world position.
            if (spawned != null)
            {
                // Keep it hidden until the final world position is written below, then reveal it
                // exactly once. Guarantees zero frames rendered at the pre-finalized position even
                // if instantiation and the transform write are split across the spawn logic.
                spawned.SetActive(false);

                if (spawned.transform.parent != null)
                {
                    Debug.LogWarning($"[ARCaptureController] Spawned spirit had an implicit parent '{spawned.transform.parent.name}' — detaching to scene root so it stays world-fixed.");
                }
                spawned.transform.SetParent(null, worldPositionStays: false);
                spawned.transform.position = anchorPosition;
                if (arCamera != null) spawned.transform.rotation = arCamera.transform.rotation;

                // Final position is set and the camera is tracking — show it for the first and
                // only time, at its correct random anchor.
                spawned.SetActive(true);

                // Drift-proof world-locking: pin the spirit to an AR Foundation anchor at this
                // pose. If anchoring succeeds, AF owns the spirit's world position from here on
                // (we never write it again). If it fails, we fall back to the fixed-position
                // behavior above (spawned already sits at anchorPosition).
                TryAnchorSpirit();
            }

            // Make sure the camera's clip planes don't accidentally cull a 3-meter-away spirit.
            arCamera.nearClipPlane = Mathf.Min(arCamera.nearClipPlane, 0.05f);
            arCamera.farClipPlane = Mathf.Max(arCamera.farClipPlane, 100f);

            Debug.Log($"[ARCaptureController] Spawned at yaw {yawDeg:F0}°, distance {distance:F1}m, world={anchorPosition}.");
            // Trace the identity of what we actually instantiated so it can be compared
            // against the spirit the user thinks they picked.
            string tintHex = ColorUtility.ToHtmlStringRGB(target != null ? target.accentColor : fallbackColor);
            Debug.Log($"[ARCaptureController][trace] SpawnSpirit  target={(target == null ? "<NULL>" : target.name)}  displayName={(target == null ? "<n/a>" : target.displayName)}  accentColor=#{tintHex}.");

            // Stage 1 visibility is driven by Update's proximity check — don't force it on
            // here. If the spirit happened to spawn just past captureRangeMeters, the hint
            // stays hidden until the player walks closer.
        }

        // Drift-proof world-locking via AR Foundation (6.x). Creates an ARAnchor at the spirit's
        // current world pose and parents the spirit to it, so the AR subsystem keeps it pinned to
        // the real-world spot as tracking refines (loop closure / relocalization). On any failure —
        // no ARAnchorManager in the scene, anchors unsupported on the device, or registration
        // rejected — we leave the spirit unparented at its fixed anchorPosition (the existing
        // fallback) and the Update guard keeps it world-fixed there.
        private void TryAnchorSpirit()
        {
            spiritAnchor = null;
            if (spawned == null) return;

            var manager = FindAnyObjectByType<ARAnchorManager>();
            if (manager == null || !manager.enabled)
            {
                anchorStatus = "FALLBACK: no ARAnchorManager (fixed pos)";
                Debug.Log("[ARCaptureController] No enabled ARAnchorManager — spirit stays at fixed anchorPosition (no AR anchor, fallback world-locking).");
                return;
            }

            try
            {
                // Add the ARAnchor component to a dedicated GameObject at the spirit's pose. In
                // AF 6.x the component self-registers with the manager in its OnEnable (runs
                // synchronously here, since the GameObject is active); on failure it logs and
                // disables itself, which we detect via anchor.enabled.
                var anchorGo = new GameObject("SpiritAnchor");
                anchorGo.transform.SetPositionAndRotation(anchorPosition, Quaternion.identity);

                var anchor = anchorGo.AddComponent<ARAnchor>();
                if (anchor == null || !anchor.enabled)
                {
                    anchorStatus = "FALLBACK: anchor registration failed (fixed pos)";
                    Debug.LogWarning("[ARCaptureController] ARAnchor registration failed — falling back to fixed-position world-locking.");
                    Destroy(anchorGo);
                    return;
                }

                spiritAnchor = anchor;
                // worldPositionStays:true keeps the spirit exactly at anchorPosition; from now on
                // the anchor (not this script) owns its world pose.
                spawned.transform.SetParent(anchor.transform, worldPositionStays: true);
                anchorStatus = "ANCHORED (drift-proof)";
                Debug.Log($"[ARCaptureController] Spirit anchored to ARAnchor (drift-proof) at {anchorPosition}. trackableId={anchor.trackableId}.");
            }
            catch (System.Exception e)
            {
                anchorStatus = $"FALLBACK: anchor exception {e.GetType().Name} (fixed pos)";
                Debug.LogWarning($"[ARCaptureController] Anchor creation threw ({e.GetType().Name}: {e.Message}) — falling back to fixed-position world-locking.");
                spiritAnchor = null;
            }
        }

        // Destroys the spirit's AR anchor (if any). Called when the spirit is captured/destroyed
        // and on scene exit so we don't leak anchors into the subsystem.
        private void CleanupAnchor()
        {
            if (spiritAnchor != null)
            {
                if (spiritAnchor.gameObject != null) Destroy(spiritAnchor.gameObject);
                spiritAnchor = null;
            }
            anchorStatus = "<spirit captured / gone>";
        }

        #endregion

        #region Input — three-path tap detection

        // Tries (in order) EnhancedTouch.activeTouches → Touchscreen.touches[i].press →
        // Touchscreen.primaryTouch.press → Pointer.current.press. Each check reads the device
        // state directly and is independent of the EventSystem, so taps that get consumed by
        // UI on one path may still be visible on another.
        //
        // The fullscreen UI tap-catcher (see EnsureFallbackCaptureButton) is a separate fourth
        // path that goes through EventSystem and reports via OnFullscreenTap.
        private bool TryGetTap(out Vector2 screenPos)
        {
            screenPos = default;

            if (EnhancedTouchSupport.enabled)
            {
                var active = ETouch.activeTouches;
                for (int i = 0; i < active.Count; i++)
                {
                    if (active[i].phase == UnityEngine.InputSystem.TouchPhase.Began)
                    {
                        screenPos = active[i].screenPosition;
                        lastTapPathHint = $"EnhancedTouch[{i}]";
                        return true;
                    }
                }
            }

            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                for (int i = 0; i < touchscreen.touches.Count; i++)
                {
                    if (touchscreen.touches[i].press.wasPressedThisFrame)
                    {
                        screenPos = touchscreen.touches[i].position.ReadValue();
                        lastTapPathHint = $"Touchscreen.touches[{i}]";
                        return true;
                    }
                }
                if (touchscreen.primaryTouch.press.wasPressedThisFrame)
                {
                    screenPos = touchscreen.primaryTouch.position.ReadValue();
                    lastTapPathHint = "Touchscreen.primaryTouch";
                    return true;
                }
            }

            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                screenPos = pointer.position.ReadValue();
                lastTapPathHint = $"Pointer ({pointer.GetType().Name})";
                return true;
            }

            return false;
        }

        // Called from the fullscreen UI tap-catcher's IPointerClickHandler. Routed via the
        // controller (rather than the catcher calling TryCaptureAt directly) so all tap
        // bookkeeping lives in one place.
        internal void OnFullscreenTap(Vector2 screenPos)
        {
            tapCountSeen++;
            lastTapPathHint = "UI/PointerClick";
            TryCaptureAt(screenPos, "UI/PointerClick");
        }

        #endregion

        #region Capture — hit-test and animation

        // Hit-tests against the spirit's projected screen position with a finger-sized radius.
        //
        // Computes both bottom-left (Camera.WorldToScreenPoint convention) and top-left
        // (PointerEventData / OnGUI convention) candidates and uses whichever is closer —
        // the input system is inconsistent about which origin it uses on different paths.
        private void TryCaptureAt(Vector2 screenPos, string sourceTag)
        {
            if (captured)        { Debug.Log($"[ARCaptureController] [{sourceTag}] tap ignored: already captured."); return; }
            if (capturing)       { Debug.Log($"[ARCaptureController] [{sourceTag}] tap ignored: capture in progress."); return; }
            if (spawned == null) { Debug.Log($"[ARCaptureController] [{sourceTag}] tap ignored: no spirit yet."); return; }

            if (arCamera == null) arCamera = Camera.main;
            if (arCamera == null) { Debug.LogWarning($"[ARCaptureController] [{sourceTag}] no AR camera; cannot hit-test."); return; }

            Vector3 spiritScreen = arCamera.WorldToScreenPoint(spawned.transform.position);
            if (spiritScreen.z <= 0f)
            {
                lastTapDistPx = -1f;
                Debug.Log($"[ARCaptureController] [{sourceTag}] spirit is BEHIND the camera. Turn to face it.");
                return;
            }

            Vector2 spiritBL = new Vector2(spiritScreen.x, spiritScreen.y);
            Vector2 spiritTL = new Vector2(spiritScreen.x, Screen.height - spiritScreen.y);
            float distBL = Vector2.Distance(screenPos, spiritBL);
            float distTL = Vector2.Distance(screenPos, spiritTL);
            float distPx = Mathf.Min(distBL, distTL);
            lastTapDistPx = distPx;

            // Edge-case forgiveness: when the spirit projects near or past the screen edge
            // (which on device happens after the user has rotated the phone such that the
            // spirit is at the viewport boundary), the user can SEE the sprite's body but
            // its CENTER coord is off-screen — so taps directly on the visible sprite are
            // measured from the off-screen center and overshoot the normal forgiveness.
            //
            // When the projected center is within ~10% of any edge (or off-screen entirely),
            // bump the forgiveness so a tap anywhere near the sprite's actual visible area
            // still counts. Normal-case taps in the middle of the screen are unaffected.
            float edgeMarginPx = Mathf.Max(80f, Screen.height * 0.10f);
            bool nearOrOffEdge = spiritScreen.x < edgeMarginPx
                              || spiritScreen.x > Screen.width  - edgeMarginPx
                              || spiritScreen.y < edgeMarginPx
                              || spiritScreen.y > Screen.height - edgeMarginPx;
            float effectiveForgiveness = nearOrOffEdge
                ? Mathf.Max(tapForgivenessPixels, Screen.height * 0.4f)
                : tapForgivenessPixels;

            if (distPx > effectiveForgiveness)
            {
                Debug.Log($"[ARCaptureController] [{sourceTag}] MISS — {distPx:F0}px > {effectiveForgiveness:F0}px (nearEdge={nearOrOffEdge}, spiritScreen=({spiritScreen.x:F0},{spiritScreen.y:F0})).");
                return;
            }
            if (nearOrOffEdge)
            {
                Debug.Log($"[ARCaptureController] [{sourceTag}] HIT (edge-forgiveness) — {distPx:F0}px ≤ {effectiveForgiveness:F0}px, spiritScreen=({spiritScreen.x:F0},{spiritScreen.y:F0}).");
            }

            tapHitsOnSpirit++;
            Debug.Log($"[ARCaptureController] [{sourceTag}] HIT — {distPx:F0}px. Capturing.");
            StartCoroutine(CaptureSequence());
        }

        private IEnumerator CaptureSequence()
        {
            if (capturing) yield break;
            capturing = true;
            captured = true;

            // Stage 1 hint disappears the instant capture begins.
            if (foundHintPanel != null) foundHintPanel.SetActive(false);

            lastCaptureMilestone = "started";
            Debug.Log("[ARCaptureController] Capture: started.");

            // Stage 2 toast turns on BEFORE the fade so "잡는중..." is visible while the
            // spirit is actively shrinking — the toast and the fade run in sync, not back-
            // to-back. It then lingers for postCaptureToastSeconds after the spirit vanishes.
            if (captureToast != null) captureToast.SetActive(true);
            lastCaptureMilestone = "toast-on";

            if (spawned != null)
            {
                // Swap the billboard to the dedicated 잡는중 catching art (if assigned) BEFORE the
                // fade, so the shrinking spirit shows the catching image instead of the idle sprite.
                ApplyCatchingSprite();

                Vector3 burstPos = spawned.transform.position;

                // The burst is decorative — never let it kill the capture flow if it throws.
                try { SpawnBurst(burstPos); }
                catch (System.Exception e) { Debug.LogWarning($"[ARCaptureController] burst threw: {e.GetType().Name}: {e.Message}"); }
                lastCaptureMilestone = "burst";

                yield return AnimateFade();

                lastCaptureMilestone = "faded";
                if (spawned != null) Destroy(spawned);
                spawned = null;
                CleanupAnchor();
            }

            // Short post-fade linger, then toast off → result panel.
            yield return new WaitForSeconds(postCaptureToastSeconds);
            if (captureToast != null) captureToast.SetActive(false);
            lastCaptureMilestone = "toast-off";

            if (captureResultPanel != null)
            {
                PopulateCaptureResultPanel();
                captureResultPanel.SetActive(true);
                lastCaptureMilestone = "result-panel";
                Debug.Log("[ARCaptureController] Capture: showing result panel; awaiting Btn_Close.");
                capturing = false;
                yield break;
            }

            lastCaptureMilestone = "returning";
            Debug.Log("[ARCaptureController] Capture: returning to Lantern.");
            if (GameStateManager.Instance != null) GameStateManager.Instance.ReturnToLantern(target);
            else SceneManager.LoadScene("LanternPrototype");

            capturing = false;
        }

        private void PopulateCaptureResultPanel()
        {
            // Lantern-gated result art (demo rule):
            //   화홍 lantern (SelectedLanternIndex == 0) → composite "lantern + spirit" image,
            //     picked by spirit type: capturedComposite[ IndexOf(target in compositeSpirits) ].
            //   Any other lantern (방울/수문) → no composites exist → the plain selected-lantern
            //     image, plainLanternSprites[SelectedLanternIndex].
            //   Empty/unassigned slot anywhere → fall back to plainLanternSprites[0] (화홍 plain).
            int selIdx = GameStateManager.Instance != null ? GameStateManager.Instance.SelectedLanternIndex : 0;

            // ---- DIAGNOSTIC: why does the result show only the plain lantern? ----
            {
                bool gsmOk   = GameStateManager.Instance != null;
                int  matchSi = (compositeSpirits != null && target != null) ? System.Array.IndexOf(compositeSpirits, target) : -2;
                string compositeNames = compositeSpirits == null ? "<null array>"
                    : "[" + string.Join(", ", System.Array.ConvertAll(compositeSpirits, s => s == null ? "<null>" : s.displayName)) + "]";
                string capturedSlotState;
                if (matchSi >= 0 && capturedComposite != null && matchSi < capturedComposite.Length)
                    capturedSlotState = capturedComposite[matchSi] == null ? "<NULL sprite>" : capturedComposite[matchSi].name;
                else
                    capturedSlotState = "<index out of range>";

                Debug.Log(
                    "[ARCaptureController][DIAG] PopulateCaptureResultPanel @ catch time:\n" +
                    $"  1) SelectedLanternIndex = {selIdx} (GameStateManager.Instance {(gsmOk ? "present" : "NULL→defaulted to 0")}); HwarongLanternIndex = {HwarongLanternIndex}; will use composite branch = {selIdx == HwarongLanternIndex}\n" +
                    $"  2) target = {(target == null ? "<NULL>" : target.displayName)}; Array.IndexOf(compositeSpirits, target) = {matchSi}; compositeSpirits = {compositeNames}\n" +
                    $"  3) capturedComposite slot[{matchSi}] = {capturedSlotState}; capturedComposite.Length = {(capturedComposite == null ? -1 : capturedComposite.Length)}\n" +
                    $"  => branch taken = {(selIdx != HwarongLanternIndex ? "PLAIN-LANTERN (SelectedLanternIndex != 0)" : (matchSi < 0 ? "PLAIN-LANTERN fallback (IndexOf returned -1: target not found in compositeSpirits)" : (capturedSlotState == "<NULL sprite>" || capturedSlotState == "<index out of range>" ? "PLAIN-LANTERN fallback (composite slot resolved null)" : "COMPOSITE (capturedComposite hit)")))}");
            }
            // ---------------------------------------------------------------------

            Sprite chosen = null;
            if (selIdx == HwarongLanternIndex)
            {
                int si = (compositeSpirits != null && target != null) ? System.Array.IndexOf(compositeSpirits, target) : -1;
                if (si >= 0 && capturedComposite != null && si < capturedComposite.Length)
                {
                    chosen = capturedComposite[si];
                }
                Debug.Log($"[ARCaptureController][trace] result image: 화홍 lantern, spiritSlot={si}, composite={(chosen == null ? "<empty→fallback>" : chosen.name)}.");
            }
            else
            {
                if (plainLanternSprites != null && selIdx >= 0 && selIdx < plainLanternSprites.Length)
                {
                    chosen = plainLanternSprites[selIdx];
                }
                Debug.Log($"[ARCaptureController][trace] result image: non-화홍 lantern idx={selIdx}, plain={(chosen == null ? "<empty→fallback>" : chosen.name)}.");
            }

            // Universal empty-slot fallback → the 화홍 plain lantern (index 0).
            if (chosen == null && plainLanternSprites != null && plainLanternSprites.Length > 0)
            {
                chosen = plainLanternSprites[0];
            }

            if (lanternFrameImage != null)
            {
                if (chosen != null)
                {
                    lanternFrameImage.sprite = chosen;
                    lanternFrameImage.color  = Color.white;
                }
                else
                {
                    // Nothing assigned at all — keep the scene-authored default rather than blanking.
                    lanternFrameImage.sprite = lanternFrameDefaultSprite;
                    lanternFrameImage.color  = lanternFrameDefaultColor;
                }
            }

            // The composite / plain-lantern image is the whole result picture; the separate
            // spirit overlay is not used under this rule.
            if (capturedSpiritImage != null) capturedSpiritImage.gameObject.SetActive(false);

            if (spiritNameLabel != null)
            {
                spiritNameLabel.text = target != null ? target.displayName : "???";
            }
            if (captureDescLabel != null)
            {
                // Per-spirit description authored on the Spirit asset; falls back to the serialized
                // default only if the captured spirit (or its description) is missing.
                captureDescLabel.text = (target != null && !string.IsNullOrEmpty(target.description))
                    ? target.description
                    : captureDescText;
            }
        }

        // 화홍 lantern — the only lantern with dedicated composite art in the demo.
        private const int HwarongLanternIndex = 0;

        // Swaps the spawned billboard's SpriteRenderer to the 잡는중 catching art for the shrink.
        // Per-spirit (catchingSprites paired by index with compositeSpirits) → shared fallback →
        // leave the spawn sprite untouched. No-op for the 3D prefab / sphere fallback (no SpriteRenderer).
        private void ApplyCatchingSprite()
        {
            if (spawned == null) return;
            var sr = spawned.GetComponent<SpriteRenderer>();
            if (sr == null) return;

            Sprite catchSprite = null;
            int si = (compositeSpirits != null && target != null) ? System.Array.IndexOf(compositeSpirits, target) : -1;
            if (si >= 0 && catchingSprites != null && si < catchingSprites.Length)
            {
                catchSprite = catchingSprites[si];
            }
            if (catchSprite == null) catchSprite = catchingSpriteShared;

            if (catchSprite != null)
            {
                sr.sprite = catchSprite;
                Debug.Log($"[ARCaptureController][trace] 잡는중 catching sprite applied: slot={si}, sprite={catchSprite.name}.");
            }
        }

        private void OnCaptureResultClose()
        {
            bool hasGsm = GameStateManager.Instance != null;
            Debug.Log($"[ARCaptureController][trace] OnCaptureResultClose firing — target={(target == null ? "<NULL>" : target.name)}  GameStateManager.Instance={(hasGsm ? "OK" : "<NULL>")}.");
            if (captureResultPanel != null) captureResultPanel.SetActive(false);
            if (hasGsm)
            {
                GameStateManager.Instance.ReturnToLantern(target);
            }
            else
            {
                // CRITICAL: if we get here, the persistent GameStateManager singleton was lost
                // mid-AR session. The reward gate cannot run, so we cannot route to Reward even
                // in demo mode. Log loudly so this is obvious in the console.
                Debug.LogError("[ARCaptureController][trace] OnCaptureResultClose: GameStateManager.Instance is NULL — bypassing reward gate, loading LanternPrototype directly. This is the silent-fallthrough that would otherwise look like 'demo mode didn't fire'.");
                SceneManager.LoadScene("LanternPrototype");
            }
        }

        // Shrinks the spirit to zero scale and fades alpha to zero over captureDuration.
        // Each frame is wrapped in try/catch so a transient material/transform error doesn't
        // leave the coroutine stuck mid-fade.
        private IEnumerator AnimateFade()
        {
            float elapsed = 0f;
            Vector3 startScale = spawned != null ? spawned.transform.localScale : Vector3.one;

            Material mat = null;
            bool useBaseColor = false;
            Color startColor = Color.white;
            try
            {
                var renderer = spawned != null ? spawned.GetComponent<Renderer>() : null;
                mat = renderer != null ? renderer.material : null;
                useBaseColor = mat != null && mat.HasProperty("_BaseColor");
                if (useBaseColor) startColor = mat.GetColor("_BaseColor");
                else if (mat != null) startColor = mat.color;
            }
            catch (System.Exception e) { Debug.LogWarning($"[ARCaptureController] fade-init threw: {e.Message}"); }

            while (elapsed < captureDuration && spawned != null)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / captureDuration);

                try
                {
                    if (spawned != null) spawned.transform.localScale = startScale * (1f - k);
                    if (mat != null)
                    {
                        Color c = startColor; c.a = 1f - k;
                        if (useBaseColor) mat.SetColor("_BaseColor", c);
                        else mat.color = c;
                    }
                }
                catch (System.Exception e) { Debug.LogWarning($"[ARCaptureController] fade-frame threw: {e.Message}"); }

                yield return null;
            }
        }

        #endregion

        #region Burst — placeholder particle effect

        // Spawns N small spheres around the capture point that fly outward and fade.
        // Stand-in for a real particle system; the real art will replace this.
        private void SpawnBurst(Vector3 center)
        {
            for (int i = 0; i < burstCount; i++)
            {
                float angle = (i / (float)burstCount) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                float pitch = Random.Range(-0.3f, 0.3f);
                Vector3 dir = new Vector3(Mathf.Cos(angle), pitch, Mathf.Sin(angle)).normalized;

                var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                p.name = "BurstParticle";
                Destroy(p.GetComponent<Collider>());
                p.transform.position = center;
                p.transform.localScale = Vector3.one * 0.06f;

                Color tint = target != null ? target.accentColor : fallbackColor;
                PlaceholderMaterialFactory.Apply(p, tint);

                StartCoroutine(BurstParticle(p, dir));
            }
        }

        private IEnumerator BurstParticle(GameObject p, Vector3 dir)
        {
            float elapsed = 0f;
            Vector3 start = p.transform.position;

            var renderer = p.GetComponent<Renderer>();
            Material mat = renderer != null ? renderer.material : null;
            bool useBaseColor = mat != null && mat.HasProperty("_BaseColor");
            Color startColor = useBaseColor ? mat.GetColor("_BaseColor")
                                            : mat != null ? mat.color : Color.white;

            while (elapsed < burstDuration && p != null)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / burstDuration);

                p.transform.position = start + dir * (k * burstRadius);
                p.transform.localScale = Vector3.one * (0.06f * (1f - k));

                if (mat != null)
                {
                    Color c = startColor; c.a = 1f - k;
                    if (useBaseColor) mat.SetColor("_BaseColor", c);
                    else mat.color = c;
                }
                yield return null;
            }

            if (p != null) Destroy(p);
        }

        #endregion

        #region Fullscreen UI tap-catcher

        // Builds a fullscreen, near-invisible UI Image+Button that catches any tap not consumed
        // by another UI element (Photo, Back, etc.). Routes clicks through EventSystem +
        // InputSystemUIInputModule — the most battle-tested input path on Android.
        private void EnsureFallbackCaptureButton()
        {
            if (tapCatcherGo != null) { Destroy(tapCatcherGo); tapCatcherGo = null; }

            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[ARCaptureController] No Canvas in AR scene — fallback tap-catcher not installed.");
                return;
            }

            var go = new GameObject(
                "ARCapture_FullscreenTapCatcher",
                typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(canvas.transform, false);
            go.transform.SetAsFirstSibling(); // sit BEHIND Photo/Back so they receive their taps

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.color = new Color(0f, 0f, 0f, 0.001f);
            img.raycastTarget = true; // explicit — invisible image still needs raycasts

            var fwd = go.AddComponent<FullscreenTapForwarder>();
            fwd.target = this;
            tapCatcherGo = go;
        }

        // Forwards UI clicks to the controller. Lives as a nested class because it's tightly
        // coupled to ARCaptureController's lifecycle and only used here.
        private class FullscreenTapForwarder : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
        {
            public ARCaptureController target;
            public void OnPointerClick(UnityEngine.EventSystems.PointerEventData ev)
            {
                if (target != null) target.OnFullscreenTap(ev.position);
            }
        }

        #endregion

        #region Debug HUD (OnGUI)

        // On-device diagnostic overlay. Shows tap counts, capture state, and the last hit-test
        // distance. Useful for verifying input and capture behaviour without adb / logcat.
        // Gated by showDebugHud — leave this OFF for normal play; flip it on when you need to
        // see what input path tapped, or how many pixels off the spirit your tap was.
        private void OnGUI()
        {
            if (showAnchorStatusOverlay) DrawAnchorStatusOverlay();

            if (!showDebugHud) return;
            var rect = new Rect(20, Screen.height - 200, Screen.width - 40, 180);
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(20, Screen.height / 55),
                normal = { textColor = Color.white },
                richText = true,
            };

            string spawnedStr = spawned == null ? "<null>" : "OK";
            string capStr = captured ? "YES" : "no";
            string distStr = lastTapDistPx < 0f ? "<n/a>" : $"{lastTapDistPx:F0}px";
            string spiritScreenStr = "<n/a>";
            if (spawned != null && arCamera != null)
            {
                Vector3 sp = arCamera.WorldToScreenPoint(spawned.transform.position);
                spiritScreenStr = $"({sp.x:F0},{sp.y:F0},z={sp.z:F1})";
            }

            GUI.Label(rect,
                $"<b>AR Debug</b>  screen={Screen.width}x{Screen.height}  taps={tapCountSeen}  hits={tapHitsOnSpirit}\n" +
                $"spirit={spawnedStr}  screenPos={spiritScreenStr}  captured={capStr}\n" +
                $"lastTapVia: {lastTapPathHint}  dist: {distStr}  forgive: {tapForgivenessPixels:F0}px\n" +
                $"capturing: {capturing}  milestone: {lastCaptureMilestone}",
                style);
        }

        // Always-visible (when enabled) top banner reporting the world-locking path so the
        // anchored-vs-fallback question can be answered on device without logcat, plus the live
        // anchor tracking state / parent / camera distance to diagnose the "follows the view" bug.
        private void DrawAnchorStatusOverlay()
        {
            string ts = "<n/a>";
            string pend = "";
            if (spiritAnchor != null)
            {
                ts = spiritAnchor.trackingState.ToString();   // Tracking / Limited / None
                pend = spiritAnchor.pending ? " (pending)" : "";
            }

            float camDist = (spawned != null && arCamera != null)
                ? Vector3.Distance(arCamera.transform.position, spawned.transform.position)
                : -1f;
            string parentName = spawned == null
                ? "<no spirit>"
                : (spawned.transform.parent == null ? "<root>" : spawned.transform.parent.name);

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(20, Screen.height / 48),
                normal = { textColor = spiritAnchor != null ? Color.green : Color.yellow },
                richText = true,
                wordWrap = true,
            };
            var rect = new Rect(20, 30, Screen.width - 40, 140);
            GUI.Label(rect,
                $"<b>ANCHOR:</b> {anchorStatus}\n" +
                $"trackingState={ts}{pend}  parent={parentName}  camDist={(camDist < 0f ? "<n/a>" : camDist.ToString("F2") + "m")}",
                style);
        }

        #endregion
    }
}
