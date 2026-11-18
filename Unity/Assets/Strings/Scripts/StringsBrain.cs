using System;
using UnityEngine;

namespace Strings
{
    /// <summary>
    /// The decision-making of a puppeteer. The rival uses it directly on its puppet; the autopilot uses the same brain but routes its
    /// decisions through synthetic hands (AutoHands), so the real input decoding is exercised end to end.
    /// </summary>
    public class SBrain : MonoBehaviour
    {
        public SPuppet me;
        public float reaction = 0.28f, aggression = 0.5f, guardiness = 0.15f, dodgy = 0.2f, hopChance = 0f;
        public Action<int> OnStrike; public Action<Vector3> OnMove; public Action<bool> OnGuard; public Action OnCut; public Action OnHop;
        float _t, _guardUntil;

        float _hopCool; int _waveSeen = -1; bool _waveTry;
        /// <summary>Chance of jumping the boss shockwave in time (one roll per wave, so a puppeteer sometimes misjudges it).</summary>
        public float waveSkill = 0.7f;
        void Update()
        {
            if (me == null || me.foe == null || !me.Active) return;
            _hopCool -= Time.deltaTime;
            if (SStage.WaveRadius > 0f)
            {
                if (_waveSeen != SStage.WaveId) { _waveSeen = SStage.WaveId; _waveTry = UnityEngine.Random.value < waveSkill; }
                Vector3 fc = me.Pos - SStage.Center; fc.y = 0f;
                float ahead = fc.magnitude - SStage.WaveRadius;                  // >0 while the ring is still coming
                if (_waveTry && _hopCool <= 0f && ahead > 0f && ahead < SStage.WaveSpeed * 0.2f) { OnHop?.Invoke(); _hopCool = 0.7f; }
            }
            _t -= Time.deltaTime; if (_t > 0f) return;
            _t = reaction * UnityEngine.Random.Range(0.7f, 1.3f);
            var foe = me.foe; Vector3 c = SStage.Center; Vector3 mp = me.Pos, fp = foe.Pos;
            Vector3 toF = fp - mp; toF.y = 0f; float d = toF.magnitude;
            Vector3 fromC = mp - c; fromC.y = 0f; float edge = fromC.magnitude / Mathf.Max(0.05f, SStage.Radius);
            Vector3 foeFromC = fp - c; foeFromC.y = 0f; float foeEdge = foeFromC.magnitude / Mathf.Max(0.05f, SStage.Radius);
            Vector3 target;
            if (edge > 0.7f) target = c + fromC.normalized * 0.08f;                                  // too close to the edge: come back
            else if (foeEdge > 0.55f && d < 0.5f) target = fp - foeFromC.normalized * 0.17f;         // rival near its edge: stand inside it and shove
            else if (d > 0.27f) target = fp - toF.normalized * 0.19f;                                  // approach
            else target = mp + Quaternion.Euler(0, UnityEngine.Random.value < 0.5f ? 90 : -90, 0) * toF.normalized * 0.12f * dodgy;
            OnMove?.Invoke(target);

            bool foeAtk = foe.IsAttacking;
            if (foeAtk && UnityEngine.Random.value < guardiness) { OnGuard?.Invoke(true); _guardUntil = Time.time + 0.45f; }
            else if (Time.time > _guardUntil) OnGuard?.Invoke(false);
            if (foeAtk && UnityEngine.Random.value < hopChance) OnHop?.Invoke();
            if (d < 0.26f && Time.time > _guardUntil && UnityEngine.Random.value < aggression)
            {
                float r = UnityEngine.Random.value; int limb = r < 0.4f ? 0 : (r < 0.65f ? 1 : (r < 0.9f ? 2 : 3));
                OnStrike?.Invoke(limb);
            }
            if (me.special >= 1f && d < 0.42f) OnCut?.Invoke();
        }
    }

    /// <summary>Synthetic hands for the autopilot: moves the right wrist and pulses finger curls, exactly like a person would.</summary>
    public class AutoHands : IHandSource
    {
        public Vector2 move; public bool guard;
        public Vector3 originR = new Vector3(0.12f, 0.83f, 0.2f), originL = new Vector3(-0.12f, 0.83f, 0.2f);
        float _jab, _hook, _kick, _sweep, _pinch;
        public void Jab() { if (_jab <= 0f) _jab = 0.32f; }
        public void Hook() { if (_hook <= 0f) _hook = 0.32f; }
        public void Kick() { if (_kick <= 0f) _kick = 0.32f; }
        public void Sweep() { if (_sweep <= 0f) _sweep = 0.32f; }
        public void Pinch() { if (_pinch <= 0f) _pinch = 0.4f; }
        float _hop; public void Hop() { if (_hop <= 0f) _hop = 0.3f; }
        static float Pulse(float t) { if (t <= 0f) return 0f; return t > 0.18f ? Mathf.Clamp01((0.32f - t) / 0.08f) : Mathf.Clamp01(t / 0.18f); }

        public void Read(HandSig l, HandSig r)
        {
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            _jab -= dt; _hook -= dt; _kick -= dt; _sweep -= dt; _pinch -= dt; _hop -= dt;
            l.tracked = r.tracked = true;
            float hy = _hop > 0f ? (_hop > 0.22f ? Mathf.Clamp01((0.3f - _hop) / 0.08f) : Mathf.Clamp01(_hop / 0.22f)) * 0.14f : 0f;
            r.pos = originR + new Vector3(move.x, hy, move.y) / 1f; r.pos = originR + new Vector3(move.x / 4.2f, hy, move.y / 4.2f); r.rot = Quaternion.identity;
            l.pos = originL; l.rot = Quaternion.identity;
            // a guard is two real fists: every finger curls (the decoder must not read the index/middle part of a fist as a strike)
            float g = guard ? 1f : 0f;
            r.index = Mathf.Max(Pulse(_jab), g); r.middle = Mathf.Max(Pulse(_kick), g); l.index = Mathf.Max(Pulse(_hook), g); l.middle = Mathf.Max(Pulse(_sweep), g);
            l.ring = l.pinky = r.ring = r.pinky = g; l.thumb = r.thumb = 0.1f;
            l.pinch = _pinch > 0f ? Mathf.Clamp01((0.4f - _pinch) / 0.1f) : 0f; r.pinch = 0f;
        }
    }
}
