using System.Collections.Generic;
using UnityEngine;

namespace Strings
{
    public class HandSig
    {
        public bool tracked; public Vector3 pos; public Quaternion rot = Quaternion.identity;
        public float thumb, index, middle, ring, pinky, pinch;     // finger curl 0 (open) .. 1 (closed); pinch strength 0..1
    }

    public interface IHandSource { void Read(HandSig left, HandSig right); }

    /// <summary>
    /// Hands in, one Intent out. Finger curl comes from the tracked skeleton (angle of the finger joints), so it is independent of how
    /// big the hand is. An autopilot or the gesture simulator can stand in for the hands (IHandSource) and goes through exactly the same
    /// decoding (Step).
    ///   RIGHT hand: wrist position = steer the puppet; index curl = jab; middle curl = kick; quick lift of the wrist = hop.
    ///   LEFT hand: index curl = hook; middle curl = sweep (trip); thumb-index pinch = CUT the rival's strings when the meter is full.
    ///   Both fists = guard.
    /// How a strike is told apart from noise (tuned in the gesture simulator, Assets/Editor/StringsGestureSim.cs):
    ///   - each curl is calibrated to the player's own range, but the range can never collapse (minimum span, bounded drift), so a hand
    ///     resting for a minute does not become hair-trigger;
    ///   - a light low-pass removes frame-to-frame tracking jitter;
    ///   - a strike fires when the finger crosses strikeOn AND has risen by minRise within riseWindow (a real curl, fast or slow, not a
    ///     drifting rest pose); it re-arms only after the finger opens below strikeRearm (hysteresis), so jitter cannot repeat it;
    ///   - the strike is confirmed a few tens of milliseconds later, and dropped if the pinky is curling too (a fist is forming: that is
    ///     a guard, not a punch); if index and middle both crossed, only the one that rose more fires.
    /// </summary>
    public class StringsInput : MonoBehaviour
    {
        public static StringsInput I;
        public readonly HandSig L = new HandSig(), R = new HandSig();
        public IHandSource Source;
        /// <summary>True when Source writes raw joint-angle curls (as the headset path measures them) that still need calibrating.</summary>
        public bool SourceRaw;
        public readonly Intent Out = new Intent();
        public bool BothTracked => L.tracked && R.tracked;
        public float steerGain = 4.2f;      // stage radii per metre of hand travel
        public Vector3 origin; bool _haveOrigin;

        // ---- tunables (simulator-tuned; see the class summary)
        public float strikeOn = 0.62f, strikeRearm = 0.45f, rearmDrop = 0.25f, minRise = 0.3f, watchTime = 0.25f, riseWindow = 0.6f, confirm = 0.08f, sustainDrop = 0.12f;
        public float fistPinkyRise = 0.3f, fistSum = 0.65f, fistOtherRatio = 0.75f, fistRatioPinky = 0.12f, fistPinkyLow = 0.2f, fistAbs = 0.7f;
        public float guardOn = 0.72f, guardOff = 0.5f, guardFingers = 0.6f;
        public float smoothTau = 0.035f;
        public float minSpan = 0.5f, adaptTau = 600f, loCap = 0.25f, hiFloor = 0.8f;
        public float hopRise = 0.05f, hopWindow = 0.2f, hopConfirm = 0.035f;
        public float pinchOn = 0.85f, pinchRearm = 0.5f;

        OVRHand[] _hands = new OVRHand[0]; float _scan;
        readonly Dictionary<OVRHand, Dictionary<OVRSkeleton.BoneId, Transform>> _bones = new Dictionary<OVRHand, Dictionary<OVRSkeleton.BoneId, Transform>>();
        readonly Dictionary<OVRHand, OVRSkeleton> _skel = new Dictionary<OVRHand, OVRSkeleton>();
        readonly Dictionary<OVRHand, bool> _xr = new Dictionary<OVRHand, bool>();
        float _invalidT; bool _warnedInvalid; readonly HashSet<OVRSkeleton> _repaired = new HashSet<OVRSkeleton>();
        static readonly System.Reflection.FieldInfo DataProv = typeof(OVRSkeleton).GetField("_dataProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.MethodInfo SetType = typeof(OVRSkeleton).GetMethod("SetSkeletonType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        /// <summary>A tracked hand whose skeleton still has no data: its type or data source did not match the SDK's hand skeleton version
        /// when it woke up (see StringsBoot.AlignHandSkeletons). Give it the OVRHand's type and the OVRHand as its source, once.</summary>
        static void Repair(OVRHand h, OVRSkeleton sk)
        {
            try
            {
                var want = ((OVRSkeleton.IOVRSkeletonDataProvider)h).GetSkeletonType(); var had = sk.GetSkeletonType();
                if (want != had && SetType != null) SetType.Invoke(sk, new object[] { want });
                bool noSource = DataProv != null && DataProv.GetValue(sk) == null; if (noSource) DataProv.SetValue(sk, h);
                Debug.LogWarning("[STR] hand " + h.name + ": skeleton had no data; type " + had + " -> " + sk.GetSkeletonType() + (noSource ? ", data source set to the OVRHand" : ""));
            }
            catch (System.Exception e) { Debug.LogWarning("[STR] hand skeleton repair failed: " + e.Message); }
        }
        readonly float[] _lo = { 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f }, _hi = { 0.9f, 0.9f, 0.9f, 0.9f, 0.9f, 0.9f, 0.9f, 0.9f, 0.9f, 0.9f };
        float _dt = 1f / 72f, _now;

        /// <summary>Raw curl -> 0..1 in this player's range. Drops/raises the ends at once on a new extreme, relaxes them only slowly, and keeps
        /// the range at least minSpan wide with bounded ends, so the mapping can never shrink onto a resting hand's jitter.</summary>
        float Nrm(int i, float raw)
        {
            float k = Mathf.Clamp01(_dt / Mathf.Max(1f, adaptTau));
            _lo[i] = raw < _lo[i] ? raw : Mathf.Min(loCap, _lo[i] + (raw - _lo[i]) * k);
            _hi[i] = raw > _hi[i] ? raw : Mathf.Max(hiFloor, _hi[i] + (raw - _hi[i]) * k);
            float lo = _lo[i], hi = Mathf.Max(_hi[i], lo + minSpan);
            return Mathf.Clamp01((raw - lo) / (hi - lo));
        }

        /// <summary>Per-finger decoding state: a short history (for "how much did it rise lately"), the arm/re-arm latch and a pending strike.</summary>
        class Fin
        {
            const int N = 128;
            readonly float[] _v = new float[N], _t = new float[N]; int _i;
            public float s, peak; public bool armed = true; public float pendT = -1f, pendRise, watchT = -1f;
            public void Reset(float v, float rearm) { for (int k = 0; k < N; k++) _t[k] = -999f; s = v; peak = v; armed = v < rearm; pendT = -1f; watchT = -1f; }
            public void Push(float t, float v) { _t[_i] = t; _v[_i] = v; _i = (_i + 1) % N; }
            public float Min(float since) { float m = 2f; for (int k = 0; k < N; k++) if (_t[k] >= since && _v[k] < m) m = _v[k]; return m > 1.5f ? s : m; }
        }
        readonly Fin[] _f = new Fin[10];                // left thumb..pinky = 0..4, right = 5..9
        readonly float[] _scratch = new float[5];
        readonly float[] _hy = new float[64], _ht = new float[64]; int _hyi; float _hopCd, _hopPendT = -1f, _hopBase;
        bool _rWas, _lWas, _guard, _pinchArmed = true; float _pinchPendT = -1f;

        void Awake() { I = this; }

        public void Recenter() { _haveOrigin = false; }

        static bool IsLeft(OVRHand h) { for (var t = h.transform; t != null; t = t.parent) if (t.name.Contains("Left")) return true; return false; }

        static float Angle(Transform a, Transform b, Transform c)
        {
            if (a == null || b == null || c == null) return 0f;
            return Vector3.Angle(b.position - a.position, c.position - b.position);
        }

        float Curl(OVRHand h, OVRSkeleton.BoneId b1, OVRSkeleton.BoneId b2, OVRSkeleton.BoneId b3, OVRSkeleton.BoneId tip)
        {
            if (!_bones.TryGetValue(h, out var d)) return 0f;
            d.TryGetValue(b1, out var t1); d.TryGetValue(b2, out var t2); d.TryGetValue(b3, out var t3); d.TryGetValue(tip, out var tt);
            float a = Angle(t1, t2, t3) + Angle(t2, t3, tt);
            return Mathf.Clamp01((a - 12f) / 120f);
        }

        /// <summary>Headset path: raw joint-angle curls from the tracked skeleton (calibrated later in NrmHand, same as a raw Source).</summary>
        void Read(OVRHand h, HandSig s)
        {
            s.tracked = h.IsTracked;
            if (!s.tracked) return;
            if (!_skel.TryGetValue(h, out var sk) || sk == null) { sk = h.GetComponent<OVRSkeleton>(); _skel[h] = sk; }
            if (sk == null || !sk.IsDataValid || sk.Bones == null)
            {
                s.tracked = false; _invalidT += _dt;
                if (_invalidT > 1f && sk != null && !_repaired.Contains(sk)) { _repaired.Add(sk); Repair(h, sk); }
                if (_invalidT > 2f && !_warnedInvalid) { _warnedInvalid = true; Debug.LogWarning("[STR] hand " + h.name + " is tracked but its skeleton has no data (skeleton " + (sk != null ? sk.GetSkeletonType().ToString() : "missing") + ", runtime hand skeleton version " + OVRPlugin.HandSkeletonVersion + ")"); }
                return;
            }
            if (!_bones.TryGetValue(h, out var d) || d.Count == 0)
            {
                d = new Dictionary<OVRSkeleton.BoneId, Transform>();
                foreach (var b in sk.Bones) d[b.Id] = b.Transform;
                _bones[h] = d;
                _xr[h] = sk.GetSkeletonType().IsOpenXRHandSkeleton();
                Debug.Log("[STR] hand " + h.name + ": skeleton " + sk.GetSkeletonType() + ", " + d.Count + " bones, reading " + (_xr[h] ? "XRHand_* (OpenXR)" : "Hand_* (OVR)") + " joint ids");
            }
            // the joint ids depend on the hand skeleton version the SDK runs (OculusRuntimeSettings: OpenXR in this project); both are supported
            if (_xr.TryGetValue(h, out bool xr) && xr)
            {
                d.TryGetValue(OVRSkeleton.BoneId.XRHand_Wrist, out var wx);
                s.pos = wx != null ? wx.position : h.transform.position; s.rot = wx != null ? wx.rotation : h.transform.rotation;
                s.index = Curl(h, OVRSkeleton.BoneId.XRHand_IndexProximal, OVRSkeleton.BoneId.XRHand_IndexIntermediate, OVRSkeleton.BoneId.XRHand_IndexDistal, OVRSkeleton.BoneId.XRHand_IndexTip);
                s.middle = Curl(h, OVRSkeleton.BoneId.XRHand_MiddleProximal, OVRSkeleton.BoneId.XRHand_MiddleIntermediate, OVRSkeleton.BoneId.XRHand_MiddleDistal, OVRSkeleton.BoneId.XRHand_MiddleTip);
                s.ring = Curl(h, OVRSkeleton.BoneId.XRHand_RingProximal, OVRSkeleton.BoneId.XRHand_RingIntermediate, OVRSkeleton.BoneId.XRHand_RingDistal, OVRSkeleton.BoneId.XRHand_RingTip);
                s.pinky = Curl(h, OVRSkeleton.BoneId.XRHand_LittleProximal, OVRSkeleton.BoneId.XRHand_LittleIntermediate, OVRSkeleton.BoneId.XRHand_LittleDistal, OVRSkeleton.BoneId.XRHand_LittleTip);
                s.thumb = Curl(h, OVRSkeleton.BoneId.XRHand_ThumbMetacarpal, OVRSkeleton.BoneId.XRHand_ThumbProximal, OVRSkeleton.BoneId.XRHand_ThumbDistal, OVRSkeleton.BoneId.XRHand_ThumbTip);
                s.pinch = h.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                return;
            }
            d.TryGetValue(OVRSkeleton.BoneId.Hand_WristRoot, out var wr);
            s.pos = wr != null ? wr.position : h.transform.position; s.rot = wr != null ? wr.rotation : h.transform.rotation;
            s.index = Curl(h, OVRSkeleton.BoneId.Hand_Index1, OVRSkeleton.BoneId.Hand_Index2, OVRSkeleton.BoneId.Hand_Index3, OVRSkeleton.BoneId.Hand_IndexTip);
            s.middle = Curl(h, OVRSkeleton.BoneId.Hand_Middle1, OVRSkeleton.BoneId.Hand_Middle2, OVRSkeleton.BoneId.Hand_Middle3, OVRSkeleton.BoneId.Hand_MiddleTip);
            s.ring = Curl(h, OVRSkeleton.BoneId.Hand_Ring1, OVRSkeleton.BoneId.Hand_Ring2, OVRSkeleton.BoneId.Hand_Ring3, OVRSkeleton.BoneId.Hand_RingTip);
            s.pinky = Curl(h, OVRSkeleton.BoneId.Hand_Pinky1, OVRSkeleton.BoneId.Hand_Pinky2, OVRSkeleton.BoneId.Hand_Pinky3, OVRSkeleton.BoneId.Hand_PinkyTip);
            s.thumb = Curl(h, OVRSkeleton.BoneId.Hand_Thumb1, OVRSkeleton.BoneId.Hand_Thumb2, OVRSkeleton.BoneId.Hand_Thumb3, OVRSkeleton.BoneId.Hand_ThumbTip);
            s.pinch = h.GetFingerPinchStrength(OVRHand.HandFinger.Index);
        }

        void NrmHand(HandSig s, bool left)
        {
            int b = left ? 0 : 5;
            s.thumb = Nrm(b + 0, s.thumb); s.index = Nrm(b + 1, s.index); s.middle = Nrm(b + 2, s.middle); s.ring = Nrm(b + 3, s.ring); s.pinky = Nrm(b + 4, s.pinky);
        }

        /// <summary>Low-pass the five curls of a hand in place and record them in the per-finger history.</summary>
        void SmoothHand(HandSig s, bool left, bool reentered)
        {
            int b = left ? 0 : 5; float a = smoothTau <= 0f ? 1f : 1f - Mathf.Exp(-_dt / smoothTau);
            var raw = _scratch; raw[0] = s.thumb; raw[1] = s.index; raw[2] = s.middle; raw[3] = s.ring; raw[4] = s.pinky;
            for (int k = 0; k < 5; k++)
            {
                var f = _f[b + k];
                if (reentered) f.Reset(raw[k], strikeRearm); else f.s += (raw[k] - f.s) * a;
                f.Push(_now, f.s); raw[k] = f.s;
            }
            s.thumb = raw[0]; s.index = raw[1]; s.middle = raw[2]; s.ring = raw[3]; s.pinky = raw[4];
        }

        /// <summary>Arms, detects and confirms the two strike fingers (index, middle) of one hand. Returns 0 none, 1 index, 2 middle.</summary>
        int Strikes(bool left)
        {
            int b = left ? 0 : 5; var ix = _f[b + 1]; var md = _f[b + 2]; var rg = _f[b + 3]; var pk = _f[b + 4];
            float since = _now - riseWindow;
            Arm(ix, since); Arm(md, since);
            if (ix.pendT < 0f && md.pendT < 0f) return 0;
            // the candidate is the finger that rose more; the other one is its neighbour (which follows it partly in a real strike)
            bool useIx = ix.pendT >= 0f && (md.pendT < 0f || ix.pendRise >= md.pendRise);
            var tg = useIx ? ix : md; var ot = useIx ? md : ix;
            if (_guard) { ix.pendT = md.pendT = -1f; return 0; }
            if (_now - tg.pendT < confirm - 1e-4f) return 0;
            // decided once, at the end of the confirm time: is this a fist forming (a guard), not a single-finger strike?
            //  - the pinky and the neighbouring strike finger rose a lot together, or
            //  - both strike fingers rose about equally while the pinky followed (a whole-hand curl), or
            //  - the pinkies of BOTH hands are rising (two fists = guard), or the hand is already a fist.
            // A kick drags the index along by about 40% and the pinky by about 25%; that alone does not count as a fist.
            float tRise = tg.s - tg.Min(since), oRise = ot.s - ot.Min(since), pRise = pk.s - pk.Min(since);
            var opk = _f[(left ? 5 : 0) + 4]; float opRise = opk.s - opk.Min(since);
            bool fist = pRise + oRise >= fistSum || (oRise >= fistOtherRatio * tRise && pRise >= fistRatioPinky)
                        || (pRise >= fistPinkyLow && opRise >= fistPinkyLow) || (rg.s >= fistAbs && pk.s >= fistAbs && ot.s >= fistAbs);
            ix.pendT = md.pendT = -1f;
            if (fist) return 0;
            if (tg.s < strikeOn - sustainDrop) return 0;               // gone again within the confirm time: a tracking spike, not a curl
            return useIx ? 1 : 2;
        }

        void Arm(Fin f, float since)
        {
            // re-arm once the finger has opened again: below strikeRearm, or rearmDrop below its peak (a middle finger often rests near 0.45)
            if (!f.armed) { f.peak = Mathf.Max(f.peak, f.s); if (f.s < strikeRearm || f.s < f.peak - rearmDrop) f.armed = true; }
            if (!f.armed || f.pendT >= 0f) return;
            if (f.s < strikeOn) { f.watchT = -1f; return; }
            // above the threshold: a strike once it has risen minRise within riseWindow. A finger that rests high (middle) crosses the
            // threshold early in its curl, so the rise is judged for up to watchTime after the crossing; a slow drift never gets there.
            float rise = f.s - f.Min(since);
            if (rise >= minRise) { f.pendT = _now; f.pendRise = rise; f.armed = false; f.peak = f.s; f.watchT = -1f; return; }
            if (f.watchT < 0f) f.watchT = _now;
            else if (_now - f.watchT > watchTime) { f.armed = false; f.peak = f.s; f.watchT = -1f; }   // drifted up, not a curl: open again first
        }

        bool Fist(HandSig s, bool on) { float g = on ? guardOff : guardOn, gf = on ? guardFingers * 0.7f : guardFingers; return s.ring >= g && s.pinky >= g && s.index >= gf && s.middle >= gf; }

        void Update() { Step(Mathf.Max(Time.unscaledDeltaTime, 1e-4f)); }

        /// <summary>One decoding step. Update calls it every frame; the gesture simulator calls it directly with a fixed dt.</summary>
        public void Step(float dt)
        {
            _dt = dt; _now += dt;
            if (_f[0] == null) for (int k = 0; k < 10; k++) { _f[k] = new Fin(); _f[k].Reset(0f, strikeRearm); }
            bool raw = Source == null || SourceRaw;
            if (Source != null) Source.Read(L, R);
            else
            {
                _scan -= dt;
                if (_scan <= 0f) { _scan = _hands.Length >= 2 ? 3f : 0.5f; _hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None); }
                L.tracked = false; R.tracked = false;
                foreach (var h in _hands) { if (h == null) { _scan = 0f; continue; } if (IsLeft(h)) Read(h, L); else Read(h, R); }
            }
            if (raw) { if (L.tracked) NrmHand(L, true); if (R.tracked) NrmHand(R, false); }

            // a hand that has just come back into view must not fire a hop or a strike from the jump between its old and new pose
            bool rIn = R.tracked && !_rWas, lIn = L.tracked && !_lWas;
            if (R.tracked) SmoothHand(R, false, rIn);
            if (L.tracked) SmoothHand(L, true, lIn);
            if (rIn) { for (int k = 0; k < _ht.Length; k++) _ht[k] = -999f; _hopCd = 0.3f; _hopPendT = -1f; }
            if (lIn) { _pinchArmed = L.pinch < pinchRearm; _pinchPendT = -1f; }
            if (!R.tracked) { _f[6].pendT = _f[7].pendT = -1f; _hopPendT = -1f; }
            if (!L.tracked) { _f[1].pendT = _f[2].pendT = -1f; _pinchPendT = -1f; }
            _rWas = R.tracked; _lWas = L.tracked;

            var o = Out;
            o.move = Vector2.zero; o.jab = o.hook = o.kick = o.sweep = o.cut = o.guard = o.hop = false;

            // guard first (it blocks strikes): both fists, with hysteresis
            _guard = L.tracked && R.tracked && Fist(L, _guard) && Fist(R, _guard);
            o.guard = _guard;

            if (R.tracked)
            {
                if (!_haveOrigin) { origin = R.pos; _haveOrigin = true; }
                Vector3 d = R.pos - origin;
                o.move = Vector2.ClampMagnitude(new Vector2(d.x, d.z) * steerGain, 1f);
                int s = Strikes(false); o.jab = s == 1; o.kick = s == 2;
                // hop: the wrist rose hopRise within hopWindow, and is still up hopConfirm later (a one-frame tracking jump is not a hop)
                _hopCd -= dt; _ht[_hyi] = _now; _hy[_hyi] = R.pos.y; _hyi = (_hyi + 1) % _hy.Length;
                float lowY = R.pos.y; for (int k = 0; k < _hy.Length; k++) if (_ht[k] >= _now - hopWindow && _hy[k] < lowY) lowY = _hy[k];
                if (_hopPendT < 0f && _hopCd <= 0f && R.pos.y - lowY >= hopRise) { _hopPendT = _now; _hopBase = lowY; }
                else if (_hopPendT >= 0f && _now - _hopPendT >= hopConfirm - 1e-4f) { if (R.pos.y - _hopBase >= hopRise * 0.8f) { o.hop = true; _hopCd = 0.5f; } _hopPendT = -1f; }
            }
            if (L.tracked)
            {
                int s = Strikes(true); o.hook = s == 1; o.sweep = s == 2;
                // cut: pinch with hysteresis, confirmed like a strike (a fist wraps the thumb over the index and reads as a pinch)
                if (!_pinchArmed && L.pinch < pinchRearm) _pinchArmed = true;
                if (_pinchArmed && _pinchPendT < 0f && L.pinch >= pinchOn) { _pinchArmed = false; _pinchPendT = _now; }
                if (_pinchPendT >= 0f)
                {
                    var pk = _f[4]; bool fist = _guard || (pk.s - pk.Min(_now - riseWindow)) >= fistPinkyRise || (L.ring >= fistAbs && L.pinky >= fistAbs);
                    if (fist) _pinchPendT = -1f;
                    else if (_now - _pinchPendT >= confirm - 1e-4f) { o.cut = L.pinch >= pinchOn - 0.15f; _pinchPendT = -1f; }
                }
            }
        }
    }
}
