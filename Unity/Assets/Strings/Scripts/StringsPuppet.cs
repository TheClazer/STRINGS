using System.Collections;
using UnityEngine;

namespace Strings
{
    public static class SStage
    {
        public static Vector3 Center = new Vector3(0f, 0.8f, 0.62f);
        public static float WaveRadius = -1f;          // >0 while the boss shockwave is travelling
        public static int WaveId;                      // increments for every new shockwave
        public const float WaveSpeed = 0.4f;           // metres per second the ring expands
        public static float Radius = 0.42f;            // current (it shrinks in sudden death)
        public static float TopY => Center.y;
    }

    /// <summary>
    /// A marionette on the stage. A capsule body that the control bar pulls around with spring-like steering, four swinging limbs, and
    /// six strings drawn up to the bar. Strikes land after the wind-up (a short delay) and knock the rival back; the more it has been
    /// hit (wobble) the further it flies. Cutting its strings makes it collapse and lose control for a couple of seconds.
    /// </summary>
    public class SPuppet : MonoBehaviour
    {
        public string pname = "Puppet"; public bool isPlayer; public SPuppet foe;
        public Rigidbody rb; public Vector3 moveTarget; public bool guard;
        public float wobble, stun, stagger, special, guardEnergy = 1f; public int wins;
        public bool Active = true;
        public float speed = 0.85f, powerMul = 1f;
        public float LastStrike { get; private set; } = -9f;
        public System.Action<SPuppet, SPuppet, int, float> Hit;

        Transform _vis, _head, _armL, _armR, _legL, _legR, _bar;
        Material _body, _accent, _stringMat, _flashMat; LineRenderer[] _strings;
        readonly float[] _swing = new float[4], _cd = new float[4];
        float _walk, _flash, _guardBroken; Vector3 _barPos; Vector3[] _attach = new Vector3[6]; Transform[] _attachT = new Transform[6];
        static readonly float[] Cd = { 0.42f, 0.55f, 0.65f, 0.8f }, Power = { 1.0f, 1.35f, 1.7f, 1.0f };

        /// <summary>Knock-back scale for strikes. At 1.0 a single kick from the starting spot slid a puppet off the stage (rounds lasted 1-3 s);
        /// lower, a ring-out has to be earned: early hits shove, and the wobble they build is what finally sends a puppet over the edge.</summary>
        public static float Knock = SArgs.GetF("-str-knock", 0.55f);

        public bool Fallen => rb != null && rb.position.y < SStage.TopY - 0.1f;
        public bool IsAttacking => Time.time - LastStrike < 0.3f;
        public Vector3 Pos => rb.position;

        public static SPuppet Create(string name, Color body, Color accent, Material lit, Material unlit, bool player)
        {
            var go = new GameObject("Puppet_" + name);
            var p = go.AddComponent<SPuppet>(); p.pname = name; p.isPlayer = player;
            p.Build(body, accent, lit, unlit);
            return p;
        }

        Material Mk(Material baseM, Color c, float smooth = 0.25f)
        {
            var m = new Material(baseM); m.SetColor("_BaseColor", c); if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth); return m;
        }

        Transform Limb(string name, Vector3 pivot, float len, float r, Material m, Material endM)
        {
            var piv = new GameObject(name).transform; piv.SetParent(_vis, false); piv.localPosition = pivot;
            var c = GameObject.CreatePrimitive(PrimitiveType.Capsule); Destroy(c.GetComponent<Collider>()); c.transform.SetParent(piv, false);
            c.transform.localPosition = new Vector3(0, -len * 0.5f, 0); c.transform.localScale = new Vector3(r * 2f, len * 0.5f, r * 2f); c.GetComponent<Renderer>().sharedMaterial = m;
            var e = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(e.GetComponent<Collider>()); e.transform.SetParent(piv, false);
            e.transform.localPosition = new Vector3(0, -len, 0); e.transform.localScale = Vector3.one * r * 2.6f; e.GetComponent<Renderer>().sharedMaterial = endM;
            return piv;
        }

        void Build(Color body, Color accent, Material lit, Material unlit)
        {
            rb = gameObject.AddComponent<Rigidbody>(); rb.mass = 0.5f; rb.linearDamping = 0.15f; rb.angularDamping = 5f; rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            var cap = gameObject.AddComponent<CapsuleCollider>(); cap.radius = 0.045f; cap.height = 0.17f; cap.center = new Vector3(0, 0.085f, 0);
            cap.material = new PhysicsMaterial { dynamicFriction = 0.15f, staticFriction = 0.2f, bounciness = 0.05f, frictionCombine = PhysicsMaterialCombine.Minimum };
            _body = Mk(lit, body); _accent = Mk(lit, accent); _stringMat = Mk(unlit, new Color(0.95f, 0.92f, 0.85f, 1f)); _flashMat = Mk(unlit, Color.white);
            var skin = Mk(lit, new Color(0.93f, 0.8f, 0.62f), 0.15f); var dark = Mk(lit, new Color(0.08f, 0.06f, 0.06f), 0.4f);
            _vis = new GameObject("Vis").transform; _vis.SetParent(transform, false);
            void Prim(PrimitiveType t, string n, Vector3 pos, Vector3 sc, Material m, Transform par = null)
            {
                var g = GameObject.CreatePrimitive(t); Destroy(g.GetComponent<Collider>()); g.name = n; g.transform.SetParent(par != null ? par : _vis, false);
                g.transform.localPosition = pos; g.transform.localScale = sc; g.GetComponent<Renderer>().sharedMaterial = m;
            }
            Prim(PrimitiveType.Capsule, "Torso", new Vector3(0, 0.105f, 0), new Vector3(0.085f, 0.045f, 0.065f), _body);
            Prim(PrimitiveType.Cube, "Belt", new Vector3(0, 0.072f, 0), new Vector3(0.088f, 0.012f, 0.068f), _accent);
            _head = new GameObject("Head").transform; _head.SetParent(_vis, false); _head.localPosition = new Vector3(0, 0.185f, 0);
            Prim(PrimitiveType.Sphere, "Skull", Vector3.zero, new Vector3(0.075f, 0.08f, 0.075f), skin, _head);
            Prim(PrimitiveType.Sphere, "EyeL", new Vector3(-0.018f, 0.006f, 0.034f), Vector3.one * 0.014f, dark, _head);
            Prim(PrimitiveType.Sphere, "EyeR", new Vector3(0.018f, 0.006f, 0.034f), Vector3.one * 0.014f, dark, _head);
            Prim(PrimitiveType.Cylinder, "Hat", new Vector3(0, 0.05f, 0), new Vector3(0.052f, 0.022f, 0.052f), _accent, _head);
            Prim(PrimitiveType.Cylinder, "Brim", new Vector3(0, 0.032f, 0), new Vector3(0.088f, 0.004f, 0.088f), _accent, _head);
            _armL = Limb("ArmL", new Vector3(-0.058f, 0.14f, 0), 0.072f, 0.013f, _body, skin);
            _armR = Limb("ArmR", new Vector3(0.058f, 0.14f, 0), 0.072f, 0.013f, _body, skin);
            _legL = Limb("LegL", new Vector3(-0.024f, 0.062f, 0), 0.07f, 0.0165f, _accent, dark);
            _legR = Limb("LegR", new Vector3(0.024f, 0.062f, 0), 0.07f, 0.0165f, _accent, dark);
            // attachment points for the six strings: head, hands, knees, back
            _attachT[0] = _head; _attachT[1] = _armL; _attachT[2] = _armR; _attachT[3] = _legL; _attachT[4] = _legR; _attachT[5] = _vis;
            _strings = new LineRenderer[6];
            for (int i = 0; i < 6; i++)
            {
                var lg = new GameObject("String" + i); lg.transform.SetParent(transform, false);
                var lr = lg.AddComponent<LineRenderer>(); lr.positionCount = 3; lr.widthMultiplier = 0.0016f; lr.sharedMaterial = _stringMat; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _strings[i] = lr;
            }
            _bar = new GameObject("Bar").transform; _bar.SetParent(transform, false);
            var barM = Mk(lit, accent, 0.3f);
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(bg.GetComponent<Collider>()); bg.transform.SetParent(_bar, false); bg.transform.localScale = new Vector3(0.13f, 0.008f, 0.012f); bg.GetComponent<Renderer>().sharedMaterial = barM;
            var bg2 = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(bg2.GetComponent<Collider>()); bg2.transform.SetParent(_bar, false); bg2.transform.localScale = new Vector3(0.012f, 0.008f, 0.08f); bg2.GetComponent<Renderer>().sharedMaterial = barM;
        }

        public void ResetTo(Vector3 pos)
        {
            rb.linearVelocity = Vector3.zero; transform.position = new Vector3(pos.x, SStage.TopY + 0.004f, pos.z); rb.position = transform.position;
            stun = stagger = wobble = special = 0f; guardEnergy = 1f; _guardBroken = 0f; guard = false; Active = true;
            for (int i = 0; i < 4; i++) { _swing[i] = 0f; _cd[i] = 0f; }
            moveTarget = pos; _barPos = new Vector3(pos.x, SStage.TopY + 0.62f, pos.z);
            if (foe != null) { Vector3 d = foe.rb.position - rb.position; d.y = 0; if (d.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(d); }
        }

        // ----------------------------------------------------------------------------------------------- actions
        public bool Strike(int limb)
        {
            if (!Active || stun > 0f || _cd[limb] > 0f || foe == null) return false;
            _cd[limb] = Cd[limb]; _swing[limb] = 1f; LastStrike = Time.time;
            SAudio.Sfx("whoosh", 0.45f);
            StartCoroutine(Land(limb));
            return true;
        }

        IEnumerator Land(int limb)
        {
            yield return new WaitForSeconds(0.08f);
            if (foe == null || !Active || stun > 0f) yield break;
            Vector3 d = foe.rb.position - rb.position; d.y = 0f; float dist = d.magnitude;
            float reach = limb < 2 ? 0.21f : 0.24f;
            if (dist > reach || (dist > 0.001f && Vector3.Angle(transform.forward, d) > 62f)) yield break;
            float p = Power[limb] * powerMul * (1f + foe.wobble * 0.9f);
            bool blocked = foe.guard;
            if (blocked) p *= 0.3f;
            foe.TakeHit(d.normalized * p, limb, this, blocked);
            special = Mathf.Min(1f, special + (blocked ? 0.05f : 0.2f));
        }

        public void TakeHit(Vector3 impulse, int limb, SPuppet by, bool blocked)
        {
            rb.AddForce(impulse * Knock, ForceMode.VelocityChange);
            wobble = Mathf.Min(1.6f, wobble + (blocked ? 0.05f : 0.22f)); stagger = blocked ? 0.15f : (limb == 3 ? 0.75f : 0.35f); _flash = 0.15f;
            SAudio.Sfx(blocked ? "hit_soft" : (impulse.magnitude > 1.4f ? "hit_hard" : "hit_soft"), blocked ? 0.5f : 0.9f);
            Hit?.Invoke(this, by, limb, impulse.magnitude);
        }

        public bool TryCut()
        {
            if (!Active || stun > 0f || special < 1f || foe == null) return false;
            Vector3 d = foe.rb.position - rb.position; d.y = 0f;
            if (d.magnitude > 0.5f) return false;
            special = 0f; foe.stun = 2.6f; foe.wobble += 0.5f; foe.rb.AddForce(d.normalized * 0.5f, ForceMode.VelocityChange);
            SAudio.Sfx("snap"); SEv.Add("cut", pname + ">" + foe.pname);
            return true;
        }

        public void Shock(Vector3 dir, float power)
        {
            if (!Active) return;
            rb.AddForce(dir.normalized * power, ForceMode.VelocityChange); stagger = 0.5f; _flash = 0.15f; SAudio.Sfx("hit_hard", 0.8f);
        }

        public void Hop() { if (Active && stun <= 0f && rb.position.y < SStage.TopY + 0.02f) rb.AddForce(Vector3.up * 1.7f, ForceMode.VelocityChange); }

        // ----------------------------------------------------------------------------------------------- physics
        void FixedUpdate()
        {
            if (rb == null) return;
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < 4; i++) _cd[i] -= dt;
            stun -= dt; stagger -= dt; wobble = Mathf.Max(0f, wobble - dt * 0.18f); _flash -= dt; _guardBroken -= dt;
            if (guard && guardEnergy > 0f && _guardBroken <= 0f) guardEnergy -= dt / 2.4f; else guardEnergy = Mathf.Min(1f, guardEnergy + dt / 3.5f);
            if (guard && guardEnergy <= 0f) { guard = false; _guardBroken = 1.2f; SAudio.Sfx("snap", 0.4f); }
            if (_guardBroken > 0f) guard = false;

            if (!Active) return;
            Vector3 to = moveTarget - rb.position; to.y = 0f;
            float sp = speed * (guard ? 0.45f : 1f);
            Vector3 desired = stun > 0f ? Vector3.zero : Vector3.ClampMagnitude(to * 7f, sp);
            Vector3 v = rb.linearVelocity; v.y = 0f;
            float gain = stun > 0f ? 0.7f : (stagger > 0f ? 1.6f : 9f);
            if (rb.position.y > SStage.TopY + 0.03f) gain *= 0.2f;      // airborne: little control
            rb.AddForce((desired - v) * gain, ForceMode.Acceleration);
            if (foe != null && stun <= 0f)
            {
                Vector3 fd = foe.rb.position - rb.position; fd.y = 0f;
                if (fd.sqrMagnitude > 1e-6f) rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(fd), 0.3f));
            }
        }

        // ----------------------------------------------------------------------------------------------- animation and strings
        void LateUpdate()
        {
            if (_vis == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < 4; i++) _swing[i] = Mathf.Max(0f, _swing[i] - dt / 0.28f);
            Vector3 lv = transform.InverseTransformDirection(rb.linearVelocity); float spd = new Vector2(lv.x, lv.z).magnitude;
            _walk += spd * 14f * dt;
            bool slump = stun > 0f;
            float leanF = Mathf.Clamp(lv.z * 22f, -18f, 18f), leanS = Mathf.Clamp(-lv.x * 22f, -18f, 18f);
            Quaternion targetLean = slump ? Quaternion.Euler(28f, 0, 0) : Quaternion.Euler(leanF + (guard ? 12f : 0f), 0, leanS);
            _vis.localRotation = Quaternion.Slerp(_vis.localRotation, targetLean, 12f * dt);
            _vis.localPosition = new Vector3(0, slump ? -0.012f : Mathf.Abs(Mathf.Sin(_walk)) * 0.004f * Mathf.Clamp01(spd * 3f), 0);

            float aR = Strk(_swing[0]) * 125f - 12f, aL = Strk(_swing[1]) * 125f - 12f, kR = Strk(_swing[2]) * 80f, kL = Strk(_swing[3]) * 95f;
            if (guard) { aR = 85f; aL = 85f; }
            if (slump) { aR = -35f; aL = -35f; kR = -10f; kL = -10f; }
            float stride = Mathf.Sin(_walk) * 26f * Mathf.Clamp01(spd * 3f);
            _armR.localRotation = Quaternion.Euler(-aR + (guard ? 0 : -stride * 0.5f), 0, -6f); _armL.localRotation = Quaternion.Euler(-aL + (guard ? 0 : stride * 0.5f), 0, 6f);
            _legR.localRotation = Quaternion.Euler(-kR + stride, 0, 0); _legL.localRotation = Quaternion.Euler(-kL - stride, 0, 0);
            _head.localRotation = Quaternion.Slerp(_head.localRotation, slump ? Quaternion.Euler(40f, 0, 15f) : Quaternion.Euler(guard ? 20f : 0f, 0, 0), 10f * dt);
            // a hit squashes the body for a moment
            float sq = _flash > 0f ? 1f + 0.35f * (_flash / 0.15f) : 1f; _vis.localScale = new Vector3(sq, 2f - sq, sq);

            // control bar and strings
            Vector3 barGoal = new Vector3(moveTarget.x, SStage.TopY + 0.62f + (slump ? 0.25f : 0f), moveTarget.z);
            _barPos = Vector3.Lerp(_barPos, barGoal, 1f - Mathf.Exp(-9f * dt));
            _bar.position = _barPos; _bar.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            Vector3[] off = { new Vector3(0, 0, 0.0f), new Vector3(-0.06f, 0, 0.02f), new Vector3(0.06f, 0, 0.02f), new Vector3(-0.04f, 0, -0.03f), new Vector3(0.04f, 0, -0.03f), new Vector3(0, 0, -0.04f) };
            for (int i = 0; i < 6; i++)
            {
                Vector3 top = _bar.TransformPoint(off[i]);
                Vector3 att = i == 5 ? _vis.TransformPoint(new Vector3(0, 0.15f, -0.03f)) : (i == 0 ? _head.TransformPoint(new Vector3(0, 0.04f, 0)) : _attachT[i].TransformPoint(new Vector3(0, i < 3 ? -0.07f : -0.045f, 0)));
                var lr = _strings[i]; lr.SetPosition(0, top); lr.SetPosition(2, att);
                lr.SetPosition(1, slump ? Vector3.Lerp(top, att, 0.5f) + Vector3.down * 0.1f + transform.right * ((i % 2 == 0) ? 0.03f : -0.03f) : (top + att) * 0.5f);
            }
        }

        static float Strk(float t) { return Mathf.Sin(Mathf.Clamp01(1f - t) * Mathf.PI) * (t > 0f ? 1f : 0f); }
    }
}
