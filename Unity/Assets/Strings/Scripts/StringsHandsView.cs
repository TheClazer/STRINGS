using UnityEngine;

namespace Strings
{
    /// <summary>
    /// Draws a pair of hands from the same HandSig values the game decodes (wrist position and per-finger curl). On the headset the Meta
    /// tracked-hand mesh is shown instead; this view makes the autopilot's synthetic hands, and so the recorded demo, show exactly what the
    /// game reads: wrist = steering, finger curls = punches, kicks and guard.
    /// </summary>
    public class SHandsView : MonoBehaviour
    {
        class H { public Transform root; public Transform[][] seg = new Transform[5][]; }
        H _l, _r; Material _skin;

        static Transform Cube(Transform p, string n, Vector3 pos, Vector3 sc, Material m, PrimitiveType t = PrimitiveType.Cube)
        {
            var g = GameObject.CreatePrimitive(t); g.name = n; Destroy(g.GetComponent<Collider>()); g.transform.SetParent(p, false);
            g.transform.localPosition = pos; g.transform.localScale = sc; g.GetComponent<Renderer>().sharedMaterial = m; return g.transform;
        }

        H Build(string n, bool left)
        {
            var h = new H(); h.root = new GameObject(n).transform; h.root.SetParent(transform, false);
            Cube(h.root, "Palm", Vector3.zero, new Vector3(0.085f, 0.022f, 0.09f), _skin);
            Cube(h.root, "Forearm", new Vector3(0f, -0.004f, -0.19f), new Vector3(0.06f, 0.05f, 0.3f), _skin);
            float side = left ? -1f : 1f;
            float[] x = { 0.05f * side, -0.032f * side, -0.011f * side, 0.011f * side, 0.032f * side };   // thumb, index, middle, ring, pinky
            float[] len = { 0.04f, 0.044f, 0.048f, 0.044f, 0.036f };
            for (int i = 0; i < 5; i++)
            {
                var piv = new GameObject("F" + i).transform; piv.SetParent(h.root, false); piv.localPosition = new Vector3(x[i], 0f, i == 0 ? 0.0f : 0.045f);
                var s1 = Cube(piv, "s1", new Vector3(0f, 0f, len[i] * 0.5f), new Vector3(0.016f, 0.016f, len[i]), _skin);
                var piv2 = new GameObject("J2").transform; piv2.SetParent(piv, false); piv2.localPosition = new Vector3(0f, 0f, len[i]);
                Cube(piv2, "s2", new Vector3(0f, 0f, len[i] * 0.45f), new Vector3(0.014f, 0.014f, len[i] * 0.9f), _skin);
                h.seg[i] = new[] { piv, piv2 };
            }
            return h;
        }

        void Pose(H h, HandSig s, bool left)
        {
            h.root.gameObject.SetActive(s.tracked);
            if (!s.tracked) return;
            h.root.position = s.pos; h.root.localScale = Vector3.one * 0.7f;
            h.root.rotation = Quaternion.identity;
            float[] c = { s.thumb, s.index, s.middle, s.ring, s.pinky };
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.Clamp01(c[i]);
                if (i == 0) { h.seg[i][0].localRotation = Quaternion.Euler(0f, (left ? 1f : -1f) * (-10f - 40f * a), 0f); h.seg[i][1].localRotation = Quaternion.Euler(0f, 0f, 0f); }
                else { h.seg[i][0].localRotation = Quaternion.Euler(a * 75f, 0f, 0f); h.seg[i][1].localRotation = Quaternion.Euler(a * 85f, 0f, 0f); }
            }
        }

        // headset: the player's own tracked hands (the SDK hand mesh), visible only while that hand is tracked, never a frozen hand
        OVRHand[] _ovr = new OVRHand[0]; Renderer[][] _ovrR = new Renderer[0][]; float _scan;
        void ShowTrackedHands()
        {
            _scan -= Time.unscaledDeltaTime;
            if (_scan <= 0f)
            {
                _scan = _ovr.Length >= 2 ? 3f : 0.5f;
                _ovr = FindObjectsByType<OVRHand>(FindObjectsSortMode.None); _ovrR = new Renderer[_ovr.Length][];
                for (int i = 0; i < _ovr.Length; i++) _ovrR[i] = _ovr[i].GetComponentsInChildren<Renderer>(true);
            }
            bool autopilot = StringsInput.I != null && StringsInput.I.Source != null;
            for (int i = 0; i < _ovr.Length; i++)
            {
                if (_ovr[i] == null) { _scan = 0f; continue; }
                bool on = !autopilot && _ovr[i].IsTracked;
                foreach (var r in _ovrR[i]) if (r != null && r.enabled != on) r.enabled = on;
            }
        }

        void LateUpdate()
        {
            ShowTrackedHands();
            if (StringsInput.I == null) return;   // drawn for the autopilot and, since the skinned hand meshes are removed from the scene, for real tracked hands too
            if (_l == null)
            {
                var g = FindFirstObjectByType<StringsGame>(); if (g == null || g.lit == null) return;   // Shader.Find for URP Lit fails in the player; copy the game's lit material
                _skin = new Material(g.lit); _skin.SetColor("_BaseColor", new Color(0.72f, 0.5f, 0.4f)); _skin.SetFloat("_Smoothness", 0.35f);
                _l = Build("HandL", true); _r = Build("HandR", false);
            }
            Pose(_l, StringsInput.I.L, true); Pose(_r, StringsInput.I.R, false);
        }
    }
}
