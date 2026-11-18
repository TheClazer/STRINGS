using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Strings
{
    /// <summary>Starts the support pieces. Flags: -str-auto (autopilot plays through synthetic hands), -str-video (frame-exact recording).
    /// Runs first (execution order -1000) so it can line the hand skeletons up with the SDK before OVRSkeleton.Awake looks for its data.</summary>
    [DefaultExecutionOrder(-1000)]
    public class StringsBoot : MonoBehaviour
    {
        void Awake()
        {
            Debug.Log("[STR] boot awake; flags auto=" + SArgs.Has("-str-auto") + " video=" + SArgs.Has("-str-video") + " nopaint=" + SArgs.Has("-str-nopaint") + " noalign=" + SArgs.Has("-str-noalign"));
            if (SArgs.Has("-str-nopaint")) SetPainterly(false);
            // Measured on a Quest 3S (2026-11-18): with camera post-processing on, URP threw a NullReferenceException in
            // VolumeManager.OverrideData every frame in the player, even with a correctly saved profile. Last Frequency runs with
            // post-processing off and does not. On the headset, switch the camera's post-processing and the scene volumes off
            // (-str-postfx keeps them on, for testing); the painterly pass is a renderer feature and is not affected.
            if (Application.isMobilePlatform && !SArgs.Has("-str-postfx"))
            {
                foreach (var v in FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None)) v.enabled = false;
                foreach (var cd in FindObjectsByType<UniversalAdditionalCameraData>(FindObjectsInactive.Include, FindObjectsSortMode.None)) cd.renderPostProcessing = false;
                Debug.Log("[STR] post-processing off on the headset");
            }
            if (!SArgs.Has("-str-noalign")) AlignHandSkeletons();
            Debug.Log("[STR] stage boot-aligned");
            if (GetComponent<StringsInput>() == null) gameObject.AddComponent<StringsInput>();
            if (GetComponent<SAudio>() == null) gameObject.AddComponent<SAudio>();
            if (SArgs.Has("-str-video")) gameObject.AddComponent<StringsRecorder>();
            if (SArgs.Has("-str-auto")) gameObject.AddComponent<StringsAuto>();
            gameObject.AddComponent<SHandsView>();
            if (Application.isMobilePlatform) gameObject.AddComponent<SPerf>();
        }

        /// <summary>Start-up checkpoints for device crash triage: the first frames and the first seconds, so a crash log shows how far start-up got.</summary>
        IEnumerator Start()
        {
            for (int i = 1; i <= 3; i++) { yield return null; Debug.Log("[STR] stage frame " + i); }
            yield return new WaitForSecondsRealtime(1f); Debug.Log("[STR] stage 1s");
            yield return new WaitForSecondsRealtime(4f); Debug.Log("[STR] stage 5s");
        }

        /// <summary>Turns the painterly full-screen pass on the URP renderer on or off at run time (-str-nopaint switches it off for a device A/B test).</summary>
        public static void SetPainterly(bool on)
        {
            try
            {
                var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
                var list = urp != null && f != null ? f.GetValue(urp) as ScriptableRendererData[] : null; int n = 0;
                if (list != null) foreach (var rd in list) if (rd != null) foreach (var feat in rd.rendererFeatures) if (feat != null && feat.name == "Painterly") { feat.SetActive(on); n++; }
                Debug.Log("[STR] painterly pass " + (on ? "on" : "off") + " (" + n + " feature" + (n == 1 ? "" : "s") + ")");
            }
            catch (System.Exception e) { Debug.LogWarning("[STR] painterly toggle failed: " + e.Message); }
        }

        static readonly FieldInfo SkelType = typeof(OVRSkeleton).GetField("_skeletonType", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// The project runs the OpenXR hand skeleton (OculusRuntimeSettings). OVRHand reports XRHandLeft/Right, and OVRSkeleton only takes
        /// an OVRHand as its data source when its own serialized type matches; OVRHand fixes the type only in the editor (OnValidate). If
        /// they disagree, the skeleton never gets data and the game never sees a hand. So before any OVRSkeleton.Awake, set each hand
        /// skeleton's type to the one its OVRHand provides, and log what was found. StringsInput.Repair covers the case where Awake ran first.
        /// </summary>
        public static void AlignHandSkeletons()
        {
            foreach (var sk in FindObjectsByType<OVRSkeleton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var hand = sk.GetComponentInParent<OVRHand>(true);
                if (hand == null) continue;
                var want = ((OVRSkeleton.IOVRSkeletonDataProvider)hand).GetSkeletonType();
                var had = sk.GetSkeletonType();
                if (want != had && want != OVRSkeleton.SkeletonType.None && SkelType != null) SkelType.SetValue(sk, want);
                Debug.Log("[STR] hand skeleton " + sk.name + ": serialized " + had + ", OVRHand provides " + want + (want != had ? " -> set to " + want : " (match)") + "; runtime version " + OVRPlugin.HandSkeletonVersion);
            }
        }
    }
}
