using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Strings.Editor
{
    /// <summary>
    /// Gesture robustness without a headset. Generates noisy, realistic RAW finger-curl curves (the same 0..1 joint-angle curl the
    /// headset path measures before calibration) and wrist paths for two hands, replays them through the real StringsInput.Step()
    /// at 72 Hz, and counts detected, missed and misfired gestures against the scripted ground truth.
    ///   Unity -batchmode -projectPath ...\RoomIsLying_v2 -executeMethod Strings.Editor.StringsGestureSim.Run -sim-label before
    ///   optional: -sim-sweep "a=1,b=2;a=3"   (sets public StringsInput fields per variant, for tuning)
    /// Model (all labelled simulator): white jitter on every curl (sigma 0.04 and 0.08), slow wander of the resting pose, the middle
    /// finger follows the index by 40% (and index follows middle 40%, ring follows middle 50%), slow and partial curls, resting
    /// half-curled hands with stretches and fidgets, fists formed with per-finger stagger, 1-3 frame tracking spikes, wrist position
    /// spikes and short tracking dropouts. Output: Submission/STRINGS/gesture_sim_&lt;label&gt;.txt and [STR][SIM] log lines.
    /// </summary>
    public static class StringsGestureSim
    {
        const float DT = 1f / 72f;
        const float Warm = 6f;              // calibration prefix: two open/fist cycles and the start curl, not scored

        enum G { Jab, Kick, Hook, Sweep, Guard, Hop, Cut }
        static readonly string[] GN = { "jab", "kick", "hook", "sweep", "guard", "hop", "cut" };

        class Ev { public G g; public float t0, rise, hold, fall, peak; public float End => t0 + rise + hold + fall; public bool excused; public bool hit; public float lat; }

        class Track
        {
            public int n; public float[][] curl = new float[10][]; public float[] pinchL, pinchR; public Vector3[] posL, posR; public bool[] trL, trR;
            public Track(int n) { this.n = n; for (int i = 0; i < 10; i++) curl[i] = new float[n]; pinchL = new float[n]; pinchR = new float[n]; posL = new Vector3[n]; posR = new Vector3[n]; trL = new bool[n]; trR = new bool[n]; }
        }

        class Replay : IHandSource
        {
            public Track tr; public int i;
            public void Read(HandSig l, HandSig r)
            {
                int k = Mathf.Min(i, tr.n - 1);
                l.tracked = tr.trL[k]; r.tracked = tr.trR[k];
                l.pos = tr.posL[k]; r.pos = tr.posR[k]; l.rot = r.rot = Quaternion.identity;
                l.thumb = tr.curl[0][k]; l.index = tr.curl[1][k]; l.middle = tr.curl[2][k]; l.ring = tr.curl[3][k]; l.pinky = tr.curl[4][k];
                r.thumb = tr.curl[5][k]; r.index = tr.curl[6][k]; r.middle = tr.curl[7][k]; r.ring = tr.curl[8][k]; r.pinky = tr.curl[9][k];
                l.pinch = tr.pinchL[k]; r.pinch = tr.pinchR[k];
            }
        }

        static float Gauss(System.Random r) { double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble(); return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2)); }
        static float U(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        static float Env(float t, float t0, float rise, float hold, float fall)
        {
            if (t < t0) return 0f; t -= t0; if (t < rise) return Smooth(t / rise); t -= rise; if (t < hold) return 1f; t -= hold; return t < fall ? 1f - Smooth(t / fall) : 0f;
        }

        // ------------------------------------------------------------------------------------------------ scenario generation
        // scenario: 0 rest (no intended gestures), 1 fight with brisk curls, 2 fight with slow first-timer curls, 3 weak partial curls
        static Track Generate(int scenario, float jitter, int seed, float dur, List<Ev> evs)
        {
            var rng = new System.Random(seed);
            int n = Mathf.CeilToInt((Warm + dur) / DT); var tr = new Track(n);
            // resting pose per finger (raw curl): thumb, index, middle, ring, pinky; natural cascade, more curl toward the pinky
            float[] restBase = { 0.25f, 0.32f, 0.38f, 0.45f, 0.48f };
            var rest = new float[10]; var wA = new float[10]; var wP = new float[10]; var wF = new float[10];
            for (int f = 0; f < 10; f++) { rest[f] = restBase[f % 5] + U(rng, -0.08f, 0.08f); wA[f] = U(rng, 0.03f, 0.07f); wP[f] = U(rng, 0f, 6.28f); wF[f] = U(rng, 0.09f, 0.25f); }

            // ---- gestures
            var all = new List<Ev>();
            // calibration prefix: two open/fist cycles on both hands, then a right index curl to start (as the start gate asks)
            for (int c = 0; c < 2; c++) { all.Add(new Ev { g = (G)100, t0 = 0.6f + c * 1.4f, rise = 0.35f, hold = 0.3f, fall = 0.45f, peak = 1f }); }
            all.Add(new Ev { g = G.Jab, t0 = 4.4f, rise = 0.2f, hold = 0.2f, fall = 0.3f, peak = 0.95f, excused = true });
            // stretches (open hand) and fidgets (slow partial curl of every finger) happen in every scenario
            for (float t = Warm + U(rng, 2f, 6f); t < Warm + dur - 3f; t += U(rng, 6f, 14f))
                all.Add(new Ev { g = (G)(rng.NextDouble() < 0.5 ? 101 : 102), t0 = t, rise = U(rng, 0.4f, 0.9f), hold = U(rng, 0.3f, 1.2f), fall = U(rng, 0.5f, 1.0f), peak = 0f });
            if (scenario > 0)
            {
                float t = Warm + 1.0f;
                while (t < Warm + dur - 2.5f)
                {
                    double p = rng.NextDouble(); G g = p < 0.19 ? G.Jab : p < 0.38 ? G.Kick : p < 0.55 ? G.Hook : p < 0.70 ? G.Sweep : p < 0.82 ? G.Guard : p < 0.93 ? G.Hop : G.Cut;
                    var e = new Ev { g = g };
                    if (g <= G.Sweep)
                    {
                        if (scenario == 1) { e.rise = U(rng, 0.10f, 0.20f); e.hold = U(rng, 0.05f, 0.15f); e.fall = U(rng, 0.15f, 0.3f); e.peak = U(rng, 0.88f, 1.0f); }
                        else if (scenario == 2) { e.rise = U(rng, 0.30f, 0.60f); e.hold = U(rng, 0.10f, 0.40f); e.fall = U(rng, 0.25f, 0.5f); e.peak = U(rng, 0.80f, 0.95f); }
                        else { e.rise = U(rng, 0.15f, 0.30f); e.hold = U(rng, 0.05f, 0.20f); e.fall = U(rng, 0.2f, 0.35f); e.peak = U(rng, 0.68f, 0.80f); }
                    }
                    else if (g == G.Guard) { e.rise = U(rng, 0.12f, scenario == 2 ? 0.4f : 0.25f); e.hold = U(rng, 0.6f, 1.5f); e.fall = U(rng, 0.2f, 0.35f); e.peak = U(rng, 0.9f, 1.0f); }
                    else if (g == G.Hop) { e.rise = U(rng, 0.10f, scenario == 2 ? 0.25f : 0.18f); e.hold = 0.02f; e.fall = 0.3f; e.peak = U(rng, 0.06f, 0.12f); }
                    else { e.rise = U(rng, 0.12f, 0.25f); e.hold = U(rng, 0.2f, 0.4f); e.fall = 0.2f; e.peak = U(rng, 0.92f, 1.0f); }
                    e.t0 = t; all.Add(e); evs.Add(e);
                    t = e.End + U(rng, 0.5f, scenario == 2 ? 1.6f : 1.1f);
                }
            }

            // ---- tracking imperfections: dropouts (per hand) and spikes
            var dropL = new bool[n]; var dropR = new bool[n];
            for (int h = 0; h < 2; h++) { var d = h == 0 ? dropL : dropR; for (int k = Mathf.CeilToInt(Warm / DT); k < n; k++) if (rng.NextDouble() < 0.0012) { int len = Mathf.RoundToInt(U(rng, 0.1f, 0.6f) / DT); for (int j = k; j < Mathf.Min(n, k + len); j++) d[j] = true; k += len; } }
            var spike = new float[10][]; for (int f = 0; f < 10; f++) { spike[f] = new float[n]; for (int k = 0; k < n; k++) if (rng.NextDouble() < 0.004) { float m = U(rng, 0.15f, 0.35f) * (rng.NextDouble() < 0.5 ? -1f : 1f); int len = rng.Next(1, 4); for (int j = k; j < Mathf.Min(n, k + len); j++) spike[f][j] = m; } }
            var pspike = new Vector3[2][]; for (int h = 0; h < 2; h++) { pspike[h] = new Vector3[n]; for (int k = 0; k < n; k++) if (rng.NextDouble() < 0.002) { var v = new Vector3(Gauss(rng), Gauss(rng), Gauss(rng)).normalized * U(rng, 0.03f, 0.06f); int len = rng.Next(1, 3); for (int j = k; j < Mathf.Min(n, k + len); j++) pspike[h][j] = v; } }

            // ---- steering path for the right wrist: smooth random walk inside +-12 cm, plus a small vertical bob
            Vector3 oR = new Vector3(0.12f, 0.83f, 0.2f), oL = new Vector3(-0.12f, 0.83f, 0.2f);
            Vector2 sp = Vector2.zero, sv = Vector2.zero; float lyBob = U(rng, 0f, 6f);

            for (int k = 0; k < n; k++)
            {
                float t = k * DT;
                var c = new float[10];
                for (int f = 0; f < 10; f++) c[f] = rest[f] + wA[f] * Mathf.Sin(t * wF[f] * 6.283f + wP[f]);
                float pinchL = 0.05f, pinchR = 0.05f, hopY = 0f;
                foreach (var e in all)
                {
                    float en = Env(t, e.t0, e.rise, e.hold, e.fall); if (en <= 0f) continue;
                    int code = (int)e.g;
                    if (code == 100) { for (int f = 0; f < 10; f++) c[f] = Mathf.Lerp(c[f], f % 5 == 0 ? 0.85f : 1f, en); }       // calibration fist
                    else if (code == 101) { for (int f = 0; f < 10; f++) c[f] = Mathf.Lerp(c[f], 0.06f, en); }                       // stretch: open hand
                    else if (code == 102) { for (int f = 0; f < 10; f++) c[f] += 0.25f * en; }                                         // fidget: slow partial curl
                    else if (e.g == G.Jab || e.g == G.Hook)
                    {
                        int b = e.g == G.Jab ? 5 : 0; float a = (e.peak - rest[b + 1]) * en;
                        c[b + 1] += a; c[b + 2] += 0.4f * a; c[b + 3] += 0.1f * a;                    // middle follows the index by 40%
                    }
                    else if (e.g == G.Kick || e.g == G.Sweep)
                    {
                        int b = e.g == G.Kick ? 5 : 0; float a = (e.peak - rest[b + 2]) * en;
                        c[b + 2] += a; c[b + 1] += 0.4f * a; c[b + 3] += 0.5f * a; c[b + 4] += 0.25f * a; // index 40%, ring 50%, pinky 25%
                    }
                    else if (e.g == G.Guard)
                    {
                        var r2 = new System.Random(seed * 31 + (int)(e.t0 * 100));
                        for (int f = 0; f < 10; f++) { float st = U(r2, 0f, 0.08f) + (f >= 5 ? U(r2, -0.05f, 0.1f) : 0f); float en2 = Env(t, e.t0 + st, e.rise, e.hold - st, e.fall); c[f] = Mathf.Lerp(c[f], f % 5 == 0 ? 0.8f : e.peak, en2); }
                        pinchL = Mathf.Max(pinchL, U(r2, 0.5f, 0.98f) * en);                          // a thumb wrapped over the index reads as a pinch
                    }
                    else if (e.g == G.Cut) { pinchL = Mathf.Max(pinchL, e.peak * en); c[1] += 0.2f * en; c[0] += 0.3f * en; }
                    else if (e.g == G.Hop) { float up = t < e.t0 + e.rise + e.hold ? en : en; hopY += e.peak * up; }
                }
                // steering walk (scenario 0 slower)
                float maxV = scenario == 0 ? 0.12f : 0.3f;
                sv += new Vector2(Gauss(rng), Gauss(rng)) * 0.6f * DT * 3f; sv -= sp * 1.5f * DT * 3f; sv = Vector2.ClampMagnitude(sv, maxV); sp += sv * DT; sp = Vector2.ClampMagnitude(sp, 0.12f);
                float bob = 0.01f * Mathf.Sin(t * 1.3f + lyBob);
                tr.posR[k] = oR + new Vector3(sp.x, bob + hopY, sp.y) + new Vector3(Gauss(rng), Gauss(rng), Gauss(rng)) * 0.002f + pspike[1][k];
                tr.posL[k] = oL + new Vector3(0f, 0.008f * Mathf.Sin(t * 0.9f), 0f) + new Vector3(Gauss(rng), Gauss(rng), Gauss(rng)) * 0.002f + pspike[0][k];
                for (int f = 0; f < 10; f++) tr.curl[f][k] = Mathf.Clamp01(c[f] + Gauss(rng) * jitter + spike[f][k]);
                tr.pinchL[k] = Mathf.Clamp01(pinchL + Gauss(rng) * 0.04f); tr.pinchR[k] = Mathf.Clamp01(pinchR + Gauss(rng) * 0.04f);
                tr.trL[k] = !dropL[k]; tr.trR[k] = !dropR[k];
            }
            // gestures overlapping a dropout of the hand they need are excused (not counted as intended or missed)
            foreach (var e in evs)
            {
                int k0 = Mathf.Max(0, Mathf.FloorToInt(e.t0 / DT) - 3), k1 = Mathf.Min(n - 1, Mathf.CeilToInt(e.End / DT));
                for (int k = k0; k <= k1 && !e.excused; k++)
                {
                    bool needL = e.g == G.Hook || e.g == G.Sweep || e.g == G.Cut || e.g == G.Guard, needR = e.g == G.Jab || e.g == G.Kick || e.g == G.Hop || e.g == G.Guard;
                    if ((needL && dropL[k]) || (needR && dropR[k])) e.excused = true;
                }
            }
            return tr;
        }

        // ------------------------------------------------------------------------------------------------ scoring
        class Score
        {
            public int[] intended = new int[7], hit = new int[7], miss = new int[7]; public float[] latSum = new float[7];
            public int wrongDuring, falseIdle, strikesInGuard, cutsInGuard, falseHop, falseCut; public float falseGuardSec, guardOnSec, guardWantSec, minutes;
            public void Add(Score o)
            {
                for (int i = 0; i < 7; i++) { intended[i] += o.intended[i]; hit[i] += o.hit[i]; miss[i] += o.miss[i]; latSum[i] += o.latSum[i]; }
                wrongDuring += o.wrongDuring; falseIdle += o.falseIdle; strikesInGuard += o.strikesInGuard; cutsInGuard += o.cutsInGuard; falseHop += o.falseHop; falseCut += o.falseCut;
                falseGuardSec += o.falseGuardSec; guardOnSec += o.guardOnSec; guardWantSec += o.guardWantSec; minutes += o.minutes;
            }
            public int Strikes(int[] a) => a[0] + a[1] + a[2] + a[3];
            public int Misfires => wrongDuring + falseIdle + strikesInGuard + falseHop + falseCut + cutsInGuard;
        }

        static Score RunOne(int scenario, float jitter, int seed, float dur, Dictionary<string, string> set)
        {
            var evs = new List<Ev>(); var tr = Generate(scenario, jitter, seed, dur, evs);
            var go = new GameObject("GestureSim"); var si = go.AddComponent<StringsInput>();
            foreach (var kv in set) Apply(si, kv.Key, kv.Value);
            var rp = new Replay { tr = tr }; si.Source = rp; si.SourceRaw = true;
            var s = new Score(); s.minutes = dur / 60f;
            int k0 = Mathf.CeilToInt(Warm / DT);
            for (int k = 0; k < tr.n; k++)
            {
                rp.i = k; si.Step(DT);
                if (k < k0) continue;
                float t = k * DT; var o = si.Out;
                // which gesture (if any) owns this moment: the window runs from the start of the gesture to 0.35 s after it ends
                Ev own = null; foreach (var e in evs) if (t >= e.t0 && t <= e.End + 0.35f) { own = e; break; }
                bool[] fired = { o.jab, o.kick, o.hook, o.sweep, false, o.hop, o.cut };
                for (int g = 0; g < 7; g++)
                {
                    if (!fired[g] || g == 4) continue;
                    if (own != null && (int)own.g == g && !own.hit) { own.hit = true; own.lat = t - own.t0; continue; }
                    if (own != null && own.excused) continue;
                    if (own != null && own.g == G.Guard) { if (g == 6) s.cutsInGuard++; else if (g < 4) s.strikesInGuard++; else s.falseHop++; continue; }
                    if (g == 5) s.falseHop++; else if (g == 6) s.falseCut++; else if (own != null) s.wrongDuring++; else s.falseIdle++;
                }
                bool wantGuard = own != null && own.g == G.Guard && t >= own.t0 + own.rise + 0.1f && t <= own.t0 + own.rise + own.hold;
                bool nearGuard = own != null && own.g == G.Guard;
                if (wantGuard) { s.guardWantSec += DT; if (o.guard) s.guardOnSec += DT; }
                if (o.guard && !nearGuard) s.falseGuardSec += DT;
            }
            foreach (var e in evs)
            {
                if (e.excused) continue; int g = (int)e.g; s.intended[g]++;
                if (e.g == G.Guard) continue;
                if (e.hit) { s.hit[g]++; s.latSum[g] += e.lat; } else s.miss[g]++;
            }
            UnityEngine.Object.DestroyImmediate(go);
            return s;
        }

        static void Apply(StringsInput si, string name, string val)
        {
            var f = typeof(StringsInput).GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) { Debug.LogWarning("[STR][SIM] no field " + name); return; }
            if (f.FieldType == typeof(float)) f.SetValue(si, float.Parse(val, System.Globalization.CultureInfo.InvariantCulture));
            else if (f.FieldType == typeof(bool)) f.SetValue(si, val == "1" || val == "true");
            else if (f.FieldType == typeof(int)) f.SetValue(si, int.Parse(val));
        }

        static readonly string[] ScN = { "rest (no gestures)", "fight, brisk curls", "fight, slow curls", "weak partial curls" };

        static string Table(Dictionary<string, string> set, int trials, float dur, out int totalMis, out int totalMiss)
        {
            var sb = new StringBuilder(); totalMis = 0; totalMiss = 0;
            sb.AppendLine("| jitter | scenario | min | strikes hit/intended | missed | wrong or extra strike during a gesture | false strike at rest | strike fired while making a fist | guard held (% of fist time) | false guard s/min | hops hit/intended | false hops | cuts hit/intended | cut while making a fist | false cuts | mean strike latency ms | jab / kick / hook / sweep hit |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (float jit in new[] { 0.04f, 0.08f })
                for (int sc = 0; sc < 4; sc++)
                {
                    var tot = new Score();
                    for (int s = 0; s < trials; s++) tot.Add(RunOne(sc, jit, 1000 * sc + 17 * s + (jit > 0.05f ? 500 : 0) + 3, dur, set));
                    int sh = tot.Strikes(tot.hit), si = tot.Strikes(tot.intended), sm = tot.Strikes(tot.miss);
                    float lat = sh > 0 ? (tot.latSum[0] + tot.latSum[1] + tot.latSum[2] + tot.latSum[3]) / sh * 1000f : 0f;
                    totalMis += tot.Misfires; totalMiss += sm + tot.miss[5] + tot.miss[6];
                    sb.AppendLine($"| {jit:0.00} | {ScN[sc]} | {tot.minutes:0} | {sh}/{si} | {sm} | {tot.wrongDuring} | {tot.falseIdle} | {tot.strikesInGuard} | {(tot.guardWantSec > 0 ? 100f * tot.guardOnSec / tot.guardWantSec : 0f):0}% ({tot.intended[4]} fists) | {tot.falseGuardSec / Mathf.Max(0.01f, tot.minutes):0.0} | {tot.hit[5]}/{tot.intended[5]} | {tot.falseHop} | {tot.hit[6]}/{tot.intended[6]} | {tot.cutsInGuard} | {tot.falseCut} | {lat:0} | {tot.hit[0]}/{tot.intended[0]} {tot.hit[1]}/{tot.intended[1]} {tot.hit[2]}/{tot.intended[2]} {tot.hit[3]}/{tot.intended[3]} |");
                }
            return sb.ToString();
        }

        public static void Run()
        {
            string label = SArgs.Get("-sim-label", "run"); int trials = SArgs.GetI("-sim-trials", 8); float dur = SArgs.GetF("-sim-dur", 90f);
            var variants = new List<Dictionary<string, string>> { new Dictionary<string, string>() };
            string sweep = SArgs.Get("-sim-sweep");
            if (!string.IsNullOrEmpty(sweep))
                foreach (var v in sweep.Split(';'))
                {
                    var d = new Dictionary<string, string>();
                    foreach (var kv in v.Split(',')) { var p = kv.Split('='); if (p.Length == 2) d[p[0].Trim()] = p[1].Trim(); }
                    variants.Add(d);
                }
            var sb = new StringBuilder();
            sb.AppendLine("# STRINGS gesture robustness, simulator (label: " + label + ")");
            sb.AppendLine("Measured in simulator/headless: synthetic raw curl curves replayed through the real StringsInput.Step() at 72 Hz. Not measured on headset.");
            sb.AppendLine($"Trials per row: {trials} x {dur:0} s, plus a 6 s calibration prefix (two open/fist cycles and the start curl) that is not scored. Generated {DateTime.Now:yyyy-MM-dd HH:mm}.");
            sb.AppendLine();
            foreach (var v in variants)
            {
                string name = v.Count == 0 ? "defaults" : string.Join(", ", v.Keys.Select2(k => k + "=" + v[k]));
                var tbl = Table(v, trials, dur, out int mis, out int miss);
                sb.AppendLine("## " + name + $"  (total misfires {mis}, total missed {miss})"); sb.AppendLine(); sb.Append(tbl); sb.AppendLine();
                Debug.Log($"[STR][SIM] {label} [{name}] misfires={mis} missed={miss}\n{tbl}");
            }
            try { Directory.CreateDirectory(@"E:\Winter\metavr\Submission\STRINGS"); File.WriteAllText($@"E:\Winter\metavr\Submission\STRINGS\gesture_sim_{label}.txt", sb.ToString()); } catch (Exception e) { Debug.LogError("[STR][SIM] write failed " + e.Message); }
            Debug.Log("[STR][SIM] DONE " + label);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static IEnumerable<string> Select2(this IEnumerable<string> src, Func<string, string> f) { foreach (var s in src) yield return f(s); }
    }
}
