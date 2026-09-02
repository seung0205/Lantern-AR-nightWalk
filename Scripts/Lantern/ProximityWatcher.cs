using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Locackthon.Data;
using Locackthon.Gps;

namespace Locackthon.Lantern
{
    // Polls the player's GPS distance to each watched spirit on a fixed interval and fires
    // enter/leave events when a spirit crosses captureRangeMeters. Listened to by
    // OpenCameraButton — that's how the button appears or hides as the player walks.
    //
    // SetWatched(Spirit) replaces the watched list at runtime; DEV mode uses this to gate the
    // Open Camera button on the user's currently selected spirit.
    public class ProximityWatcher : MonoBehaviour
    {
        [System.Serializable] public class SpiritEvent : UnityEvent<Spirit> { }

        [Header("Inputs")]
        [SerializeField] private GpsService gps;
        [SerializeField] private List<Spirit> watched = new List<Spirit>();

        [Header("Capture Range")]
        [Tooltip("Distance in meters at which a spirit is considered 'in range' and the AR capture button should appear.")]
        [SerializeField, Min(1f)] private float captureRangeMeters = 10f;

        [Header("Polling")]
        [Tooltip("How often (seconds) to recompute distance to each watched spirit.")]
        [SerializeField, Min(0.05f)] private float pollSeconds = 0.5f;

        [Header("Events")]
        public SpiritEvent OnSpiritEntersCaptureRange;
        public SpiritEvent OnSpiritLeavesCaptureRange;

        public Spirit NearestInRange { get; private set; }
        public float NearestDistanceMeters { get; private set; } = float.PositiveInfinity;
        public Spirit AnyWatchedSpirit => watched.Count > 0 ? watched[0] : null;

        // Replace the watched list at runtime. DEV mode uses this to swap which spirit the
        // Open Camera button is gated on, without editing the scene's serialized list.
        public void SetWatched(Spirit single)
        {
            watched.Clear();
            if (single != null) watched.Add(single);
            // Force an immediate re-poll so the OpenCameraButton picks up the change without
            // waiting `pollSeconds`.
            timer = 0f;
            // Reset the in-range state so OnSpiritEntersCaptureRange fires for the new spirit.
            if (NearestInRange != null && NearestInRange != single)
            {
                var prev = NearestInRange;
                NearestInRange = null;
                OnSpiritLeavesCaptureRange?.Invoke(prev);
            }
        }

        private float timer;

        private void Start()
        {
            Debug.Log($"[ProximityWatcher] Start. gps={(gps == null ? "NULL" : "OK")}, watched.Count={watched.Count}, captureRangeMeters={captureRangeMeters}, pollSeconds={pollSeconds}.");
        }

        private void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = pollSeconds;
            Poll();
            Debug.Log($"[ProximityWatcher] Poll: gps.HasFix={(gps != null && gps.HasFix)}, nearestDist={NearestDistanceMeters:F1}m, inRange={(NearestInRange != null ? NearestInRange.displayName : "<none>")}.");
        }

        private void Poll()
        {
            if (gps == null || !gps.HasFix) return;

            Spirit closest = null;
            double bestDist = double.PositiveInfinity;
            foreach (var s in watched)
            {
                if (s == null) continue;
                double d = gps.DistanceMetersTo(s.latitude, s.longitude);
                if (d < bestDist) { bestDist = d; closest = s; }
            }
            NearestDistanceMeters = (float)bestDist;

            Spirit prev = NearestInRange;
            NearestInRange = (closest != null && bestDist <= captureRangeMeters) ? closest : null;

            if (NearestInRange != prev)
            {
                if (NearestInRange != null)
                {
                    Debug.Log($"[ProximityWatcher] ENTER capture range: '{NearestInRange.displayName}' at {bestDist:F1}m");
                    OnSpiritEntersCaptureRange?.Invoke(NearestInRange);
                }
                else if (prev != null)
                {
                    Debug.Log($"[ProximityWatcher] LEAVE capture range: '{prev.displayName}' (now {bestDist:F1}m away)");
                    OnSpiritLeavesCaptureRange?.Invoke(prev);
                }
            }
        }
    }
}
