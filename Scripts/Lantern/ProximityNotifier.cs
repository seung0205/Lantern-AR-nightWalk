using UnityEngine;
using Locackthon.Data;
#if UNITY_ANDROID && !UNITY_EDITOR
using Unity.Notifications.Android;
#endif
#if UNITY_IOS && !UNITY_EDITOR
using System.Collections;
using Unity.Notifications.iOS;
#endif

namespace Locackthon.Lantern
{
    // Fires an OS-level local notification (with vibration) when ProximityWatcher reports
    // that a spirit has entered capture range. The notification surfaces on the lock
    // screen and vibrates the device even if the screen is off, because it goes through
    // the platform notification center, not Unity's UI.
    //
    // Why this works with the screen off:
    //   - Application.runInBackground = true keeps GpsService + ProximityWatcher polling
    //     while the screen is asleep but the app is still resident in memory (the typical
    //     "phone in lantern" case).
    //   - Once OnSpiritEntersCaptureRange fires, we hand off to AndroidNotificationCenter /
    //     iOSNotificationCenter, which the OS displays regardless of our app's UI state.
    //   - The Android channel is registered with Importance.High + EnableVibration, so
    //     the OS handles vibration; no Handheld.Vibrate call is needed on device.
    //
    // ProximityWatcher's enter/leave is edge-triggered, so this won't spam while the
    // player lingers in range — only on the out → in transition.
    //
    // Wire-up (LanternPrototype scene):
    //   1) Add this component to the same GameObject as ProximityWatcher.
    //   2) Drag that ProximityWatcher into the "watcher" field in the Inspector.
    [DisallowMultipleComponent]
    public class ProximityNotifier : MonoBehaviour
    {
        [Header("Inputs")]
        [SerializeField] private ProximityWatcher watcher;

        [Header("Notification Text")]
        [SerializeField] private string title = "로티니 GO";
        [SerializeField] private string subtitle = "주변에 정령의 기운이 느껴집니다..";
        [Tooltip("Legacy fallback. Kept for inspector compatibility; body text is now a fixed Korean string.")]
#pragma warning disable CS0414 // assigned-but-never-used — SerializeField, retained so previously saved values don't disappear from the Inspector.
        [SerializeField] private string fallbackBody = "Lift the lantern and look around.";
#pragma warning restore CS0414

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string ChannelId = "spirit_proximity";
#endif

        private void Awake()
        {
            // Keep the proximity poll alive when the screen sleeps but the app is still
            // resident — this is the "phone in lantern, screen off" case.
            Application.runInBackground = true;

            if (watcher == null)
            {
                Debug.LogWarning("[ProximityNotifier] No ProximityWatcher assigned — no notifications will fire.");
                return;
            }
            watcher.OnSpiritEntersCaptureRange.AddListener(HandleEnter);
        }

        private void OnDestroy()
        {
            if (watcher != null)
                watcher.OnSpiritEntersCaptureRange.RemoveListener(HandleEnter);
        }

        private void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            RegisterAndroidChannel();
#elif UNITY_IOS && !UNITY_EDITOR
            StartCoroutine(RequestIOSAuthorization());
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void RegisterAndroidChannel()
        {
            var channel = new AndroidNotificationChannel
            {
                Id = ChannelId,
                Name = "Spirit Proximity",
                Description = "Alerts when a night spirit is nearby",
                // Importance.High = heads-up banner + lock-screen + vibration by default.
                Importance = Importance.High,
                EnableLights = true,
                EnableVibration = true,
                LockScreenVisibility = LockScreenVisibility.Public,
                VibrationPattern = new long[] { 0, 400, 200, 400 },
            };
            AndroidNotificationCenter.RegisterNotificationChannel(channel);
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        private IEnumerator RequestIOSAuthorization()
        {
            using (var req = new AuthorizationRequest(
                AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound,
                registerForRemoteNotifications: false))
            {
                while (!req.IsFinished) yield return null;
            }
        }
#endif

        private void HandleEnter(Spirit spirit)
        {
            string body = "주변에 정령의 기운이 느껴집니다..";

            Debug.Log($"[ProximityNotifier] Firing notification for '{(spirit != null ? spirit.displayName : "<null>")}'.");

#if UNITY_ANDROID && !UNITY_EDITOR
            var n = new AndroidNotification
            {
                Title = title,
                Text  = body,
                SmallIcon = "lantern_small",   // white-on-transparent placeholder; Android tints it.
                LargeIcon = "lantern_large",   // 스튜디오로니티 로고 (full-color), shown at right of the notification row.
                ShouldAutoCancel = true,
                FireTime = System.DateTime.Now,
            };
            AndroidNotificationCenter.SendNotification(n, ChannelId);
#elif UNITY_IOS && !UNITY_EDITOR
            var n = new iOSNotification
            {
                Identifier = $"spirit_proximity_{System.DateTime.UtcNow.Ticks}",
                Title = title,
                Subtitle = subtitle,
                Body = body,
                ShowInForeground = true,
                ForegroundPresentationOption =
                    PresentationOption.Alert | PresentationOption.Sound | PresentationOption.Badge,
                CategoryIdentifier = "spirit_proximity",
                ThreadIdentifier = "spirit_proximity",
                // iOS time-interval triggers require >= 1s; this is the lock-screen path.
                Trigger = new iOSNotificationTimeIntervalTrigger
                {
                    TimeInterval = new System.TimeSpan(0, 0, 1),
                    Repeats = false,
                },
            };
            iOSNotificationCenter.ScheduleNotification(n);
#else
            // Editor / desktop fallback — Handheld.Vibrate is a no-op here, so just log.
            Debug.Log($"[ProximityNotifier] (Editor) Would notify: \"{title}\" — {body}");
#endif
        }
    }
}
