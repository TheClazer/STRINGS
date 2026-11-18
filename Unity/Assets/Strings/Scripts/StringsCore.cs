using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Strings
{
    public static class SArgs
    {
        static string[] _a;
        static string[] A => _a ?? (_a = System.Environment.GetCommandLineArgs());
        public static bool Has(string f) { foreach (var a in A) if (a == f) return true; return false; }
        public static string Get(string f, string d = null) { for (int i = 0; i < A.Length - 1; i++) if (A[i] == f) return A[i + 1]; return d; }
        public static float GetF(string f, float d) => float.TryParse(Get(f), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;
        public static int GetI(string f, int d) => int.TryParse(Get(f), out var v) ? v : d;
    }

    /// <summary>Timestamped event log (frame, kind, id): the voice and sound-effect track for the demo video is built from it.</summary>
    public static class SEv
    {
        public static readonly List<string> L = new List<string>();
        public static int Frame; public static float Now;
        /// <summary>Only the autopilot and the video render keep this log; on the headset it would only grow and allocate.</summary>
        public static readonly bool On = SArgs.Has("-str-auto") || SArgs.Has("-str-video");
        public static void Add(string kind, string id, float dur = 0f) { if (On) L.Add($"{Frame}\t{Now:F2}\t{kind}\t{id}\t{dur:F2}"); }
        public static void Flush()
        {
            try { Directory.CreateDirectory(@"E:\Winter\metavr\logs\strings"); File.WriteAllLines(@"E:\Winter\metavr\logs\strings\events.tsv", L); } catch { }
        }
    }

    /// <summary>Plays Resources/StringsAudio/&lt;id&gt; (announcer) and sfx_&lt;id&gt; (effects), and logs them for the video mix.</summary>
    public class SAudio : MonoBehaviour
    {
        public static SAudio I; AudioSource _voice, _sfx, _music;
        void Awake() { I = this; _voice = gameObject.AddComponent<AudioSource>(); _sfx = gameObject.AddComponent<AudioSource>(); }
        public static void Say(string id)
        {
            SEv.Add("say", id);
            var c = Resources.Load<AudioClip>("StringsAudio/" + id);
            if (c != null && I != null) { I._voice.Stop(); I._voice.PlayOneShot(c, 1f); }
        }
        public static void Music(string id, bool loop = true, float vol = 0.45f)
        {
            SEv.Add("music", id);
            if (I == null) return;
            var c = Resources.Load<AudioClip>("StringsAudio/" + id);
            if (I._music == null) I._music = I.gameObject.AddComponent<AudioSource>();
            if (c == null) return;
            I._music.clip = c; I._music.loop = loop; I._music.volume = vol; I._music.Play();
        }
        public static void StopMusic() { SEv.Add("music", "stop"); if (I != null && I._music != null) I._music.Stop(); }

        public static void Sfx(string id, float vol = 1f)
        {
            SEv.Add("sfx", id);
            var c = Resources.Load<AudioClip>("StringsAudio/sfx_" + id);
            if (c != null && I != null) I._sfx.PlayOneShot(c, vol);
        }
    }

    /// <summary>One decoded intent per frame, from hands (or the autopilot standing in for hands).</summary>
    public class Intent
    {
        public Vector2 move;            // normalised -1..1 on the stage plane (x right, y away from you)
        public bool jab, hook, kick, sweep, cut, guard, hop;
    }
}
