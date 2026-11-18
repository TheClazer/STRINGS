using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Strings
{
    /// <summary>Frame-exact capture at 30 fps (Time.captureFramerate) of what the seated player sees, with post-processing and the painterly pass.</summary>
    public class StringsRecorder : MonoBehaviour
    {
        public const string Dir = @"E:\Winter\metavr\logs\strings\frames";
        Camera _cam; RenderTexture _rt; Texture2D _tex; int _n; const int W = 1280, H = 720;

        void Start()
        {
            Debug.Log("[STR] recorder start");
            Directory.CreateDirectory(Dir); foreach (var f in Directory.GetFiles(Dir)) File.Delete(f);
            Time.captureFramerate = 30;
            var go = new GameObject("VideoCam"); _cam = go.AddComponent<Camera>(); _cam.fieldOfView = 80f; _cam.nearClipPlane = 0.03f; _cam.farClipPlane = 30f;
            _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = Color.black;
            var ud = go.AddComponent<UniversalAdditionalCameraData>(); ud.renderPostProcessing = true;
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32); _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            _cam.targetTexture = _rt; _cam.enabled = false;
        }

        void LateUpdate()
        {
            var head = Camera.main; if (head == null || _cam == null) return;
            float t = Time.unscaledTime;
            _cam.transform.SetPositionAndRotation(head.transform.position + new Vector3(Mathf.Sin(t * 0.31f) * 0.008f, Mathf.Sin(t * 0.9f) * 0.006f, 0f),
                head.transform.rotation * Quaternion.Euler(Mathf.Sin(t * 0.37f) * 0.4f, Mathf.Sin(t * 0.21f) * 0.5f, 0f));
            _cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); _tex.Apply(); RenderTexture.active = prev;
            File.WriteAllBytes($@"{Dir}\f{_n++:D5}.jpg", _tex.EncodeToJPG(90)); SEv.Frame = _n;
            if (_n % 300 == 0) Debug.Log($"[STR][VIDEO] {_n} frames");
        }
    }
}
