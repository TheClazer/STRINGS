using System.Collections;
using UnityEngine;

namespace RoomIsLying
{
    /// <summary>In-world text without any asset dependencies (built-in font + TextMesh).</summary>
    public static class WorldText
    {
        static Font _font;

        /// <summary>Use the font referenced from the scene (guaranteed to be in the build).</summary>
        public static void Configure(Font font) { if (font != null) _font = font; }

        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        /// <summary>Dark translucent unlit material (the board panel), shared by every text backing.</summary>
        public static Material PanelMaterial;

        /// <summary>Greedy word wrap so no caption line runs wider than a comfortable reading arc.</summary>
        public static string Wrap(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var outLines = new System.Text.StringBuilder();
            foreach (var para in text.Split('\n'))
            {
                int col = 0;
                foreach (var word in para.Split(' '))
                {
                    if (col > 0 && col + 1 + word.Length > maxChars) { outLines.Append('\n'); col = 0; }
                    else if (col > 0) { outLines.Append(' '); col++; }
                    outLines.Append(word); col += word.Length;
                }
                outLines.Append('\n');
            }
            return outLines.ToString().TrimEnd('\n');
        }

        /// <summary>Thin dark outline around a text mesh (no box): the text stays on a transparent background and still reads on any surface.
        /// (Name kept so every existing call keeps working; padding and opacity are ignored.)</summary>
        public static void AddBacking(TextMesh tm, float padding = 0f, float opacity = 0f)
        {
            if (tm == null || tm.GetComponent<TextOutline>() != null) return;
            tm.gameObject.AddComponent<TextOutline>().Init(tm);
        }

        /// <param name="lineHeightMetres">approximate height of one text line in the world</param>
        public static TextMesh Create(string name, Transform parent, string text, float lineHeightMetres, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, TextAlignment align = TextAlignment.Center)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = Font;
            tm.fontSize = 64;
            tm.characterSize = lineHeightMetres / (64f * 0.1f);
            tm.text = text;
            tm.anchor = anchor;
            tm.alignment = align;
            tm.color = color;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return tm;
        }
    }

    /// <summary>Four offset dark copies of a TextMesh behind it, following its text and fade: a cheap soft outline.</summary>
    public class TextOutline : MonoBehaviour
    {
        TextMesh _tm; TextMesh[] _copies;

        public void Init(TextMesh tm)
        {
            _tm = tm;
            float d = tm.characterSize * 0.55f;                       // outline thickness scales with the text
            Vector2[] offs = { new Vector2(d, d), new Vector2(-d, d), new Vector2(d, -d), new Vector2(-d, -d) };
            _copies = new TextMesh[offs.Length];
            for (int i = 0; i < offs.Length; i++)
            {
                var go = new GameObject("Outline" + i);
                go.transform.SetParent(tm.transform, false);
                go.transform.localPosition = new Vector3(offs[i].x, offs[i].y, 0.002f);
                var c = go.AddComponent<TextMesh>();
                c.font = tm.font; c.fontSize = tm.fontSize; c.characterSize = tm.characterSize; c.anchor = tm.anchor; c.alignment = tm.alignment;
                c.text = tm.text; c.color = new Color(0f, 0f, 0f, 0f);
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = tm.font.material; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                _copies[i] = c;
            }
        }

        void LateUpdate()
        {
            if (_tm == null || _copies == null) return;
            float a = _tm.color.a * 0.85f;
            foreach (var c in _copies)
            {
                if (c == null) continue;
                if (c.text != _tm.text) c.text = _tm.text;
                c.color = new Color(0f, 0f, 0f, a);
            }
        }
    }

    /// <summary>
    /// Soft head-following caption for every spoken or sound-only event (accessibility).
    /// Eases toward a comfortable spot so it never locks rigidly to the head.
    /// </summary>
    public class CaptionBar : MonoBehaviour
    {
        TextMesh _tm;
        float _alpha, _target;
        Coroutine _routine;

        public string Current => _tm != null ? _tm.text : "";
        public Vector3 TextSize => _tm != null ? _tm.GetComponent<MeshRenderer>().bounds.size : Vector3.zero;

        void Awake()
        {
            // ~5 cm line height at 1 m (the comfort floor is 4 cm), on a dark panel
            _tm = WorldText.Create("CaptionText", transform, "", 0.043f, new Color(1f, 1f, 1f, 0f));
            WorldText.AddBacking(_tm, 0.04f, 0.72f);
        }

        public void Show(string text, float seconds = 3.5f)
        {
            if (_tm == null) return;
            _tm.text = WorldText.Wrap(text, 34);
            VideoCapture.Event("caption", text);
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Fade(seconds));
        }

        IEnumerator Fade(float seconds)
        {
            _target = 1f;
            yield return new WaitForSeconds(seconds);
            _target = 0f;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 f = cam.transform.forward; f.y = 0f;
            f = f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
            Vector3 goal = cam.transform.position + f * 0.85f + Vector3.down * 0.27f;
            transform.position = Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-3f * Time.deltaTime));
            Vector3 look = transform.position - cam.transform.position; look.y = 0f;
            if (look.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);

            _alpha = Mathf.MoveTowards(_alpha, _target, Time.deltaTime * 3f);
            var c = _tm.color; c.a = _alpha; _tm.color = c;
        }
    }

    /// <summary>A floating text card that fades itself out.</summary>
    public class FadingCard : MonoBehaviour
    {
        TextMesh _tm;
        float _life;

        public static FadingCard Spawn(Vector3 pos, string text, float lineHeight, float seconds, Color color)
        {
            var go = new GameObject("Card");
            go.transform.position = pos;
            var c = go.AddComponent<FadingCard>();
            c._tm = WorldText.Create("CardText", go.transform, text, lineHeight, color, TextAnchor.MiddleCenter, TextAlignment.Left);
            WorldText.AddBacking(c._tm, 0.05f, 0.78f);
            c._life = seconds;
            go.AddComponent<BillboardY>();
            return c;
        }

        void Update()
        {
            _life -= Time.deltaTime;
            float a = Mathf.Clamp01(Mathf.Min(_life / 1.5f, 1f));
            var col = _tm.color; col.a = a; _tm.color = col;
            if (_life <= 0f) Destroy(gameObject);
        }
    }
}
