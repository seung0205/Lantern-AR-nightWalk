using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace Locackthon.Gps
{
    // Single source of truth for the player's coordinate.
    //
    // Two modes:
    //   useDevMode = true  → returns the simulatedLatitude/Longitude values, mutable at
    //                        runtime via SetSimulatedCoordinate. DevModeSpiritSelector uses
    //                        this to teleport the player onto a chosen spirit.
    //   useDevMode = false → reads the real device GPS via Input.location, requesting
    //                        Android FineLocation permission first.
    //
    // ProximityWatcher reads CoordinateChanged + DistanceMetersTo each poll. Switch to real
    // GPS by toggling useDevMode in the Inspector. Set false for production builds.
    public class GpsService : MonoBehaviour
    {
        [Header("Mode")]
        [Tooltip("If true, the service uses the simulated coordinate below instead of the device GPS. Use this for indoor testing.")]
        // SET TO FALSE FOR PRODUCTION BUILD
        [FormerlySerializedAs("devMode")]
        [SerializeField] private bool useDevMode = true;

        [Header("Dev Mode Simulation")]
        [Tooltip("Latitude used when Dev Mode is on. Default: Hwaseong Haenggung area, Suwon.")]
        [SerializeField] private double simulatedLatitude = 37.2839;
        [Tooltip("Longitude used when Dev Mode is on. Default: Hwaseong Haenggung area, Suwon.")]
        [SerializeField] private double simulatedLongitude = 127.0153;

        [Header("Real GPS")]
        [Tooltip("Desired GPS accuracy in meters when running on a real device.")]
#pragma warning disable CS0414 // assigned-but-never-used in editor (only consumed inside UNITY_ANDROID/UNITY_IOS blocks below)
        [SerializeField] private float desiredAccuracyMeters = 5f;
        [Tooltip("Minimum distance in meters before a GPS update fires.")]
        [SerializeField] private float updateDistanceMeters = 1f;
#pragma warning restore CS0414

        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        public bool HasFix { get; private set; }
        public bool IsDevMode => useDevMode;

        public event Action<double, double> CoordinateChanged;

        private void OnEnable()
        {
            if (useDevMode)
            {
                SetCoordinate(simulatedLatitude, simulatedLongitude);
                HasFix = true;
                Debug.Log($"[GpsService] Dev Mode ON. Simulated coord = ({simulatedLatitude:F6}, {simulatedLongitude:F6}). HasFix=true.");
            }
            else
            {
                Debug.Log("[GpsService] Dev Mode OFF — starting real device GPS.");
                StartCoroutine(StartRealGps());
            }
        }

        private IEnumerator StartRealGps()
        {
#if UNITY_ANDROID || UNITY_IOS
#if UNITY_ANDROID
            // Android 6.0+ requires a runtime permission grant before Input.location.isEnabledByUser
            // returns true. Without this, real GPS would fail silently on every modern Android device.
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Debug.Log("[GpsService] Requesting Android FineLocation permission…");
                Permission.RequestUserPermission(Permission.FineLocation);
                float waited = 0f;
                while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation) && waited < 10f)
                {
                    yield return new WaitForSeconds(0.25f);
                    waited += 0.25f;
                }
                if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                {
                    Debug.LogWarning("[GpsService] FineLocation permission denied or not granted within 10s — aborting real GPS.");
                    yield break;
                }
            }
#endif
            // iOS surfaces the system permission prompt automatically when Input.location.Start is
            // called, provided NSLocationWhenInUseUsageDescription is set in Info.plist (Unity does
            // this from Player Settings → iOS → Other Settings → Location Usage Description).

            if (!Input.location.isEnabledByUser)
            {
                Debug.LogWarning("[GpsService] Location services are disabled by the user.");
                yield break;
            }

            Input.location.Start(desiredAccuracyMeters, updateDistanceMeters);
            int wait = 20;
            while (Input.location.status == LocationServiceStatus.Initializing && wait > 0)
            {
                yield return new WaitForSeconds(1f);
                wait--;
            }

            if (Input.location.status != LocationServiceStatus.Running)
            {
                Debug.LogWarning($"[GpsService] Failed to start location service: {Input.location.status}");
                yield break;
            }

            HasFix = true;
            while (Input.location.status == LocationServiceStatus.Running)
            {
                LocationInfo data = Input.location.lastData;
                SetCoordinate(data.latitude, data.longitude);
                yield return new WaitForSeconds(0.5f);
            }
#else
            Debug.Log("[GpsService] Real GPS is not supported on this platform; remaining in Dev Mode behavior.");
            yield break;
#endif
        }

        public void SetSimulatedCoordinate(double latitude, double longitude)
        {
            simulatedLatitude = latitude;
            simulatedLongitude = longitude;
            if (useDevMode)
            {
                SetCoordinate(latitude, longitude);
            }
        }

        private void SetCoordinate(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
            CoordinateChanged?.Invoke(latitude, longitude);
        }

        public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusMeters = 6371000.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
                       * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return earthRadiusMeters * c;
        }

        public double DistanceMetersTo(double targetLatitude, double targetLongitude)
            => DistanceMeters(Latitude, Longitude, targetLatitude, targetLongitude);
    }
}
