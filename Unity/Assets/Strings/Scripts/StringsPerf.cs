using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace Strings
{
    /// <summary>
    /// Headset only. Holds the frame rate: foveated rendering on, and if the smoothed frame time stays over the display's budget the render
    /// resolution steps down (1.0 -> 0.85 -> 0.72) and, at the last step, bloom and the other camera effects go. It only ever steps down,
    /// so the picture never flickers between levels. What it measures and does is logged ("[STR][PERF]" in logcat).
    /// </summary>
    public class SPerf : MonoBehaviour
    {
        public static int Level; public static float AvgMs;
        static readonly float[] Scale = { 1f, 0.85f, 0.72f };
        float _ema, _budget = 1f / 72f, _t, _warm = 6f; int _over;

        void Start()
        {
            try
            {
                OVRManager.foveatedRenderingLevel = OVRManager.FoveatedRenderingLevel.High; OVRManager.useDynamicFoveatedRendering = true;
                float hz = OVRPlugin.systemDisplayFrequency; if (hz > 30f) _budget = 1f / hz;
            }
            catch (System.Exception e) { Debug.LogWarning("[STR][PERF] headset settings: " + e.Message); }
            _ema = _budget; Debug.Log("[STR][PERF] budget " + (_budget * 1000f).ToString("0.0") + " ms");
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime; if (dt > 0.25f) return;            // a pause or a load, not a slow frame
            if (_warm > 0f) { _warm -= dt; return; }                                // start-up is not representative
            _ema += (dt - _ema) * 0.05f; _t += dt; if (_t < 2f) return; _t = 0f; AvgMs = _ema * 1000f;
            if (_ema > _budget * 1.15f) _over++; else _over = 0;
            if (_over >= 2 && Level < Scale.Length - 1) { Level++; _over = 0; Apply(); }
        }

        void Apply()
        {
            XRSettings.eyeTextureResolutionScale = Scale[Level];
            if (Level == Scale.Length - 1 && Camera.main != null) { var ud = Camera.main.GetComponent<UniversalAdditionalCameraData>(); if (ud != null) ud.renderPostProcessing = false; }
            Debug.Log("[STR][PERF] quality level " + Level + ": render scale " + Scale[Level] + " (" + AvgMs.ToString("0.0") + " ms avg, budget " + (_budget * 1000f).ToString("0.0") + " ms)");
        }
    }
}
