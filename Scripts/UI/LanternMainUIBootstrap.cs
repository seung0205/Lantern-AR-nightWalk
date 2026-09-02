using UnityEngine;
using UnityEngine.UI;
using Locackthon.Core;
using Locackthon.Data;
using Locackthon.Gps;
using Locackthon.Lantern;

namespace Locackthon.UI
{
    /// Wires runtime behaviour for the LanternPrototype main-screen UI. The Canvas,
    /// Panel_QuestBanner, Img_Map, Btn_OpenCollection and Btn_CatchSpirit are authored
    /// directly in the scene. This script only attaches the catch-spirit click handler;
    /// Btn_OpenCollection is wired by CollectionBookBootstrap.externalOpenButton.
    public class LanternMainUIBootstrap : MonoBehaviour
    {
        [SerializeField] private Button catchSpiritButton;

        [Header("Map overlay")]
        [SerializeField] private Button openMapButton;   // the Button on Img_Map (minimap)
        [SerializeField] private GameObject panelMap;     // fullscreen Panel_Map overlay
        [SerializeField] private Button closeMapButton;   // Btn_CloseMap (X)

        private void Awake()
        {
            if (catchSpiritButton != null) catchSpiritButton.onClick.AddListener(OnCatchClicked);
            else Debug.LogWarning("[LanternMainUIBootstrap] catchSpiritButton is not assigned.");

            if (openMapButton != null) openMapButton.onClick.AddListener(OpenMap);
            else Debug.LogWarning("[LanternMainUIBootstrap] openMapButton is not assigned.");

            if (closeMapButton != null) closeMapButton.onClick.AddListener(CloseMap);
            else Debug.LogWarning("[LanternMainUIBootstrap] closeMapButton is not assigned.");

            // Map overlay always starts hidden.
            if (panelMap != null) panelMap.SetActive(false);
            else Debug.LogWarning("[LanternMainUIBootstrap] panelMap is not assigned.");
        }

        public void OpenMap()
        {
            if (panelMap != null) panelMap.SetActive(true);
        }

        public void CloseMap()
        {
            if (panelMap != null) panelMap.SetActive(false);
        }

        public void OnCatchClicked()
        {
            // Real-device behaviour mirrors the deactivated OpenCameraButton: only enter AR
            // when a spirit is actually inside capture range.
            var watcher = FindFirstObjectByType<ProximityWatcher>();
            Spirit nearestInRange = watcher != null ? watcher.NearestInRange : null;
            Spirit lastSelected   = GameStateManager.Instance != null ? GameStateManager.Instance.LastSelectedSpirit : null;
            Spirit anyWatched     = watcher != null ? watcher.AnyWatchedSpirit : null;

            Spirit target = nearestInRange;
            string source = "watcher.NearestInRange";

            // DEV mode fallback chain. When GpsService is in Dev Mode, drop the strict
            // in-range gate so desk testing works:
            //   2nd choice: the DEV-picked spirit (GameStateManager.LastSelectedSpirit)
            //   3rd choice: the proximity watcher's first watched entry
            //   4th choice (NEW): the first non-template spirit in the SpiritRegistry — this
            //                     guarantees AR ALWAYS receives a valid target when the player
            //                     hits Btn_CatchSpirit without having pressed DEV first, even
            //                     if the scene's ProximityWatcher has no watched entries.
            // Real GPS mode never enters this branch — the strict in-range gate stays.
            string registryFallbackPick = "<not-attempted>";
            if (target == null)
            {
                var gps = FindFirstObjectByType<GpsService>();
                if (gps != null && gps.IsDevMode)
                {
                    if (lastSelected != null) { target = lastSelected; source = "GSM.LastSelectedSpirit (DEV fallback)"; }
                    else if (anyWatched != null) { target = anyWatched; source = "watcher.AnyWatchedSpirit (DEV fallback)"; }
                    else
                    {
                        var registry = CollectionDatabase.Instance != null ? CollectionDatabase.Instance.Registry : null;
                        if (registry != null)
                        {
                            for (int i = 0; i < registry.All.Count; i++)
                            {
                                var s = registry.All[i];
                                if (s == null) continue;
                                // All 4 spirit assets are real, catchable spirits now (the old
                                // Spirit_Template was repurposed and renamed to Spirit_Evaporate),
                                // so there is no template entry to skip.
                                target = s;
                                source = $"SpiritRegistry[{i}]='{s.name}' (DEV final fallback)";
                                registryFallbackPick = s.name;
                                break;
                            }
                            if (target == null) registryFallbackPick = "<no non-template entry>";
                        }
                        else
                        {
                            registryFallbackPick = "<registry unavailable>";
                        }
                    }
                }
            }

            Debug.Log($"[LanternMainUIBootstrap][trace] OnCatchClicked  NearestInRange={(nearestInRange==null?"<null>":nearestInRange.name)}  LastSelectedSpirit={(lastSelected==null?"<null>":lastSelected.name)}  AnyWatchedSpirit={(anyWatched==null?"<null>":anyWatched.name)}  registryFallback={registryFallbackPick}  →  picked={(target==null?"<NULL>":target.name)}  source={source}.");

            if (target == null)
            {
                // Hard refusal: never call EnterARCapture(null), which would silently bail
                // and leave the user stuck on the Lantern screen with no feedback. Logging
                // at error level so this is loud in the console.
                Debug.LogError("[LanternMainUIBootstrap] Btn_CatchSpirit: no spirit available from ANY source (proximity, DEV selection, registry). Refusing to enter AR with a null target.");
                return;
            }
            if (GameStateManager.Instance == null)
            {
                Debug.LogWarning("[LanternMainUIBootstrap] No GameStateManager in scene; cannot enter AR.");
                return;
            }
            GameStateManager.Instance.EnterARCapture(target);
        }
    }
}
