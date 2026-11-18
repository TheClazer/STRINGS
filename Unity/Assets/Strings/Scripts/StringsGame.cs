using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Strings
{
    /// <summary>
    /// STRINGS: the marionette tournament. Best-of-three duels on a small round stage on your table; push the rival's puppet off the edge.
    /// A ladder of puppeteers, each with a different style. Finger curls throw punches and kicks, two fists guard, and a pinch cuts the
    /// rival's strings when your meter is full. After 18 seconds the stage starts to shrink so every round ends.
    /// </summary>
    public class StringsGame : MonoBehaviour
    {
        public Material lit, unlit; public Font font;
        public SPuppet Player { get; private set; }
        public SPuppet Rival { get; private set; }
        public bool Finished { get; private set; }
        public int MatchesWon { get; private set; }
        public readonly List<string> Log = new List<string>();
        public int RoundsPlayed, RingOuts, Cuts, Hits, WaveHitsYou, WaveClearedYou, WaveHitsRival, WaveClearedRival;

        class Opp { public string name, intro; public Color body, accent; public float aggr, guard, react, dodgy, speed, power, hop; public bool boss; }
        readonly Opp[] _ladder =
        {
            new Opp { name = "PIP", intro = "a_vs_pip", body = new Color(0.25f, 0.62f, 0.78f), accent = new Color(0.95f, 0.85f, 0.35f), aggr = 0.22f, guard = 0.05f, react = 0.55f, dodgy = 0.1f, speed = 0.55f, power = 0.8f, hop = 0f },
            new Opp { name = "MADAME RUE", intro = "a_vs_rue", body = new Color(0.55f, 0.2f, 0.62f), accent = new Color(0.95f, 0.55f, 0.75f), aggr = 0.45f, guard = 0.6f, react = 0.34f, dodgy = 0.3f, speed = 0.8f, power = 0.95f, hop = 0.1f },
            new Opp { name = "IRON BRUNO", intro = "a_vs_bruno", body = new Color(0.45f, 0.5f, 0.52f), accent = new Color(0.85f, 0.35f, 0.15f), aggr = 0.6f, guard = 0.25f, react = 0.4f, dodgy = 0.05f, speed = 0.62f, power = 1.35f, hop = 0f },
            new Opp { name = "SILK", intro = "a_vs_silk", body = new Color(0.9f, 0.85f, 0.5f), accent = new Color(0.2f, 0.7f, 0.55f), aggr = 0.7f, guard = 0.3f, react = 0.2f, dodgy = 0.9f, speed = 1.0f, power = 1.0f, hop = 0.5f },
            new Opp { name = "THE PUPPETMASTER", intro = "a_vs_master", body = new Color(0.12f, 0.1f, 0.14f), accent = new Color(0.85f, 0.15f, 0.2f), aggr = 0.7f, guard = 0.35f, react = 0.22f, dodgy = 0.5f, speed = 0.95f, power = 1.2f, hop = 0.4f, boss = true },
            new Opp { name = "YOUR RIVAL", intro = null, body = new Color(0.2f, 0.7f, 0.65f), accent = new Color(0.95f, 0.95f, 0.95f), aggr = 0.5f, guard = 0.3f, react = 0.35f, dodgy = 0.3f, speed = 0.8f, power = 1f, hop = 0.1f },
        };

        TextMesh _banner, _nameP, _nameR, _timer, _hint;
        Transform _platform; readonly List<Transform> _pipP = new List<Transform>(), _pipR = new List<Transform>();
        Transform _meterP, _meterR; Material _pipOn, _pipOff, _meterMat, _meterMatR; static readonly Color MeterCol = new Color(0.4f, 0.9f, 1f), MeterFull = new Color(1f, 0.85f, 0.25f);
        bool _cutTipShown, _bossBeaten, _meterWasFull; readonly Dictionary<Color, Material> _sparkMats = new Dictionary<Color, Material>();
        static bool RealHands => StringsInput.I != null && StringsInput.I.Source == null && !Application.isBatchMode;
        public bool InWorkshop; SBrain _rivalBrain; Camera _cam; int _opp; float _hintUntil; Transform _wave;

        Material Mk(Material b, Color c, float s = 0.2f, float emit = 0f)
        {
            var m = new Material(b); m.SetColor("_BaseColor", c); if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", s);
            if (emit > 0f && m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * emit); }
            return m;
        }

        GameObject Prim(PrimitiveType t, string n, Transform par, Vector3 pos, Vector3 sc, Material m, bool collider = false)
        {
            var g = GameObject.CreatePrimitive(t); g.name = n; if (!collider) Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(par, false); g.transform.position = pos; g.transform.localScale = sc; g.GetComponent<Renderer>().sharedMaterial = m; return g;
        }

        TextMesh Text(string name, string s, float size, Color c, Vector3 pos, bool face = true)
        {
            var t = RoomIsLying.WorldText.Create(name, transform, s, size, c); t.transform.position = pos;
            RoomIsLying.WorldText.AddBacking(t);
            if (face) t.gameObject.AddComponent<RoomIsLying.FaceCamera>();
            return t;
        }

        // ------------------------------------------------------------------------------------------------ stage
        void BuildStage()
        {
            Vector3 c = SStage.Center;
            var wood = Mk(lit, new Color(0.18f, 0.1f, 0.06f), 0.45f);
            var velvet = Mk(lit, new Color(0.55f, 0.07f, 0.1f), 0.1f);
            var gold = Mk(lit, new Color(0.9f, 0.68f, 0.28f), 0.6f);
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            // the table the stage sits on (the player rests their elbows on the real one)
            Prim(PrimitiveType.Cube, "Table", transform, new Vector3(0f, c.y - 0.035f, c.z), new Vector3(1.5f, 0.04f, 0.95f), wood);
            // the round platform: the arena (collider is a convex mesh so the edge is a real edge)
            var plat = GameObject.CreatePrimitive(PrimitiveType.Cylinder); plat.name = "Platform"; Destroy(plat.GetComponent<Collider>());
            plat.transform.SetParent(transform, false); plat.transform.position = new Vector3(c.x, c.y - 0.015f, c.z);
            plat.transform.localScale = new Vector3(SStage.Radius * 2f, 0.015f, SStage.Radius * 2f); plat.GetComponent<Renderer>().sharedMaterial = Mk(lit, new Color(0.62f, 0.42f, 0.24f), 0.5f);
            var mc = plat.AddComponent<MeshCollider>(); mc.sharedMesh = plat.GetComponent<MeshFilter>().sharedMesh; mc.convex = true;
            mc.material = new PhysicsMaterial { dynamicFriction = 0.15f, staticFriction = 0.2f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
            _platform = plat.transform;
            var rim = Prim(PrimitiveType.Cylinder, "Rim", _platform, new Vector3(c.x, c.y - 0.012f, c.z), new Vector3(1.04f, 1.0f, 1.04f), Mk(unlit, new Color(1f, 0.7f, 0.25f), 0.1f));
            rim.transform.SetParent(_platform, true); rim.transform.localScale = new Vector3(1.05f, 0.6f, 1.05f); rim.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            // curtains, painted backdrop and proscenium
            for (int i = 0; i < 5; i++)
                Prim(PrimitiveType.Cube, "Curtain" + i, transform, new Vector3(c.x - 0.8f + i * 0.4f, c.y + 0.55f, c.z + 0.62f + (i % 2) * 0.03f), new Vector3(0.42f, 1.25f, 0.03f), velvet);
            Prim(PrimitiveType.Cube, "ArchTop", transform, new Vector3(c.x, c.y + 1.25f, c.z + 0.58f), new Vector3(2.0f, 0.14f, 0.1f), gold);
            Prim(PrimitiveType.Sphere, "Moon", transform, new Vector3(c.x, c.y + 0.62f, c.z + 0.6f), new Vector3(0.38f, 0.38f, 0.02f), Mk(unlit, new Color(1f, 0.93f, 0.7f)));
            for (int s = -1; s <= 1; s += 2)
            {
                Prim(PrimitiveType.Cylinder, "Column", transform, new Vector3(c.x + s * 0.95f, c.y + 0.55f, c.z + 0.55f), new Vector3(0.08f, 0.62f, 0.08f), gold);
                Prim(PrimitiveType.Cube, "Wing", transform, new Vector3(c.x + s * 0.72f, c.y + 0.45f, c.z + 0.3f), new Vector3(0.03f, 0.9f, 0.55f), velvet);
            }
            // lighting: a warm key over the stage, cool rim lights from behind, almost no ambient (the room is dark, the stage glows)
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.1f, 0.08f, 0.12f);
            RenderSettings.fog = false;
            MakeLight("Key", LightType.Spot, new Color(1f, 0.82f, 0.55f), 1.8f, c + new Vector3(0f, 1.0f, -0.15f), Quaternion.Euler(88f, 0f, 0f), 3f, 62f);
            MakeLight("RimL", LightType.Spot, new Color(0.35f, 0.55f, 1f), 3.5f, c + new Vector3(-0.9f, 0.55f, 0.5f), Quaternion.LookRotation(c - (c + new Vector3(-0.9f, 0.55f, 0.5f))), 3f, 60f);
            MakeLight("RimR", LightType.Spot, new Color(1f, 0.4f, 0.55f), 3.5f, c + new Vector3(0.9f, 0.55f, 0.5f), Quaternion.LookRotation(c - (c + new Vector3(0.9f, 0.55f, 0.5f))), 3f, 60f);
            MakeLight("Fill", LightType.Point, new Color(1f, 0.75f, 0.5f), 0.35f, c + new Vector3(0f, 0.35f, -0.5f), Quaternion.identity, 2.5f, 0f);
            // audience: dark rows with glinting eyes
            var dark = Mk(lit, new Color(0.02f, 0.018f, 0.025f), 0.1f); var eye = Mk(unlit, new Color(1f, 0.85f, 0.5f));
            var rng = new System.Random(7);
            for (int row = 0; row < 3; row++)
                for (int i = 0; i < 18; i++)
                {
                    float a = Mathf.Lerp(-62f, 62f, i / 17f) * Mathf.Deg2Rad, rad = 1.55f + row * 0.35f;
                    Vector3 p = new Vector3(Mathf.Sin(a) * rad * 1.2f, 0.2f + row * 0.28f, c.z + 0.1f + Mathf.Cos(a) * rad * 0.55f + 0.3f);
                    if (p.z < c.z + 0.5f) continue;
                    var b = Prim(PrimitiveType.Sphere, "Aud", transform, p, new Vector3(0.22f, 0.26f, 0.22f), dark);
                    if (rng.NextDouble() < 0.55) { Prim(PrimitiveType.Sphere, "E", b.transform, p + new Vector3(-0.04f, 0.04f, -0.1f), Vector3.one * 0.025f, eye); Prim(PrimitiveType.Sphere, "E", b.transform, p + new Vector3(0.04f, 0.04f, -0.1f), Vector3.one * 0.025f, eye); }
                }
        }

        void MakeLight(string n, LightType t, Color col, float inten, Vector3 pos, Quaternion rot, float range, float spot)
        {
            var g = new GameObject(n); g.transform.SetParent(transform, false); g.transform.SetPositionAndRotation(pos, rot);
            var l = g.AddComponent<Light>(); l.type = t; l.color = col; l.intensity = inten; l.range = range; if (t == LightType.Spot) { l.spotAngle = spot; l.innerSpotAngle = spot * 0.5f; }
            l.shadows = (t == LightType.Spot && n == "Key") ? LightShadows.Soft : LightShadows.None;
        }

        void BuildHud()
        {
            Vector3 c = SStage.Center;
            _banner = Text("Banner", "", 0.075f, new Color(1f, 0.9f, 0.55f), c + new Vector3(0f, 0.47f, 0.25f));
            _nameP = Text("NameP", "YOU", 0.03f, new Color(1f, 0.55f, 0.5f), c + new Vector3(-0.42f, 0.3f, 0.3f));
            _nameR = Text("NameR", "", 0.03f, new Color(0.6f, 0.8f, 1f), c + new Vector3(0.42f, 0.3f, 0.3f));
            _timer = Text("Timer", "", 0.035f, Color.white, c + new Vector3(0f, 0.27f, 0.25f));
            _hint = Text("Hint", "", 0.022f, new Color(0.95f, 0.95f, 0.85f), c + new Vector3(0f, 0.37f, 0.25f));
            _pipOn = Mk(unlit, new Color(1f, 0.8f, 0.2f)); _pipOff = Mk(unlit, new Color(0.25f, 0.2f, 0.15f));
            for (int i = 0; i < 2; i++)
            {
                _pipP.Add(Prim(PrimitiveType.Sphere, "PipP", transform, c + new Vector3(-0.455f + i * 0.07f, 0.245f, 0.3f), Vector3.one * 0.035f, _pipOff).transform);
                _pipR.Add(Prim(PrimitiveType.Sphere, "PipR", transform, c + new Vector3(0.385f + i * 0.07f, 0.245f, 0.3f), Vector3.one * 0.035f, _pipOff).transform);
            }
            _meterMat = Mk(unlit, MeterCol); _meterMatR = Mk(unlit, MeterCol);
            _meterP = Prim(PrimitiveType.Cube, "MeterP", transform, c + new Vector3(-0.42f, 0.205f, 0.3f), new Vector3(0.012f, 0.014f, 0.012f), _meterMat).transform;
            _meterR = Prim(PrimitiveType.Cube, "MeterR", transform, c + new Vector3(0.42f, 0.205f, 0.3f), new Vector3(0.012f, 0.014f, 0.012f), _meterMatR).transform;
        }

        // ------------------------------------------------------------------------------------------------ flow
        void Start() { StartCoroutine(Main()); }

        IEnumerator Main()
        {
            if (SAudio.I == null) gameObject.AddComponent<SAudio>();
            for (int i = 0; i < 40 && Camera.main == null; i++) yield return null;
            _cam = Camera.main; Debug.Log("[STR] game main: camera=" + (_cam != null ? _cam.name : "NONE"));
            if (font != null) RoomIsLying.WorldText.Configure(font);
            BuildStage(); Debug.Log("[STR] stage built"); BuildHud(); Debug.Log("[STR] hud built");
            Player = SPuppet.Create("YOU", new Color(0.85f, 0.2f, 0.2f), new Color(0.98f, 0.82f, 0.3f), lit, unlit, true);
            Rival = SPuppet.Create("RIVAL", Color.blue, Color.white, lit, unlit, false);
            Player.foe = Rival; Rival.foe = Player;
            Player.Hit += OnHit; Rival.Hit += OnHit;
            _rivalBrain = Rival.gameObject.AddComponent<SBrain>(); _rivalBrain.me = Rival;
            _rivalBrain.OnMove = v => Rival.moveTarget = v; _rivalBrain.OnStrike = l => Rival.Strike(l); _rivalBrain.OnGuard = g => Rival.guard = g;
            _rivalBrain.OnCut = () => { if (Rival.TryCut()) { Cuts++; Log.Add("rival cut your strings"); SAudio.Say("a_cut"); } }; _rivalBrain.OnHop = () => Rival.Hop();
            Player.ResetTo(SStage.Center + new Vector3(-0.2f, 0, 0)); Rival.ResetTo(SStage.Center + new Vector3(0.2f, 0, 0));
            Player.Active = Rival.Active = false;
            ApplyChoices(SavedChoices());

            Debug.Log("[STR] puppets ready");
            yield return new WaitForSeconds(1.0f);
            SAudio.Music("music_menu");
            if (StringsInput.I != null && StringsInput.I.Source == null && !Application.isBatchMode)
            {
                // start menu + calibration: rest both hands, open and curl the fingers a few times (the game learns your range), then curl INDEX to begin
                float tw = 0f; while (!StringsInput.I.BothTracked && tw < 600f) { _hint.text = "PUT THE CONTROLLERS DOWN\nREST YOUR FOREARMS ON THE TABLE, BOTH HANDS IN VIEW"; tw += Time.unscaledDeltaTime; yield return null; }
                _hint.text = "MAKE A FIST AND OPEN IT TWICE\nTHEN CURL YOUR RIGHT INDEX FINGER TO START"; _hintUntil = 0f;
                // no dead end: a clearer hint after 20 s, and the game starts by itself after 40 s with hands in view
                float tc = 0f; while (tc < 40f) { tc += Time.unscaledDeltaTime; if (tc > 2.5f && StringsInput.I.Out.jab) break; if (tc > 20f && tc - Time.unscaledDeltaTime <= 20f) _hint.text = "CURL YOUR RIGHT INDEX FINGER, LIKE PULLING A TRIGGER\nTHE MATCH STARTS BY ITSELF IN A MOMENT"; yield return null; }
                Debug.Log("[STR] start gate passed after " + tc.ToString("0.0") + " s");
                _hint.text = ""; SAudio.Sfx("bell"); StringsInput.I.Recenter();
            }
            SAudio.Say("a_welcome"); Banner("STRINGS", 3.2f); yield return new WaitForSeconds(3.6f);
            while (true)
            {
            MatchesWon = 0; _bossBeaten = false;
            var order = new List<int>();
            if (SArgs.Get("-str-ladder") != null) foreach (var t in SArgs.Get("-str-ladder").Split(',')) { if (int.TryParse(t, out int li)) order.Add(Mathf.Clamp(li, 0, _ladder.Length - 1)); }
            else if (SArgs.Has("-str-start") || SArgs.Has("-str-count")) { int st = Mathf.Clamp(SArgs.GetI("-str-start", 0), 0, 4), mx = Mathf.Clamp(SArgs.GetI("-str-count", 5), 1, 5); for (int k = st; k < mx; k++) order.Add(k); }
            else order.AddRange(new[] { 0, 5, 1, 2, 3, 4 });     // first fight straight away (PIP, the easiest), the Workshop comes after it
            if (order.Count == 0) order.Add(0);
            int attempts = 0, pos = 0;
            while (pos < order.Count)
            {
                _opp = order[pos]; var o = _ladder[_opp];
                // the Workshop (dress your puppet, design YOUR RIVAL) opens before the designed rival's match, once the player knows how to fight
                if (_opp == 5 && attempts == 0 && !SArgs.Has("-str-noworkshop")) yield return Workshop();
                ApplyOpp(o);
                // (the designed rival has no announcer line of its own: it used to borrow Pip's "the clumsy apprentice, Pip!")
                Banner("VS " + o.name, 2.2f); if (o.intro != null) SAudio.Say(o.intro); if (o.boss) SAudio.Music("music_boss"); else SAudio.Music("music_fight"); yield return new WaitForSeconds(2.6f);
                int pw = 0, rw = 0, round = 1;
                while (pw < 2 && rw < 2)
                {
                    yield return Round(round, pw, rw, w => { if (w == Player) pw++; else rw++; });
                    SetPips(pw, rw); round++; yield return new WaitForSeconds(1.0f);
                }
                if (pw >= 2) { MatchesWon++; Log.Add($"match {o.name}: WON {pw}-{rw}"); Banner(_ladder[_opp].boss ? "PUPPETMASTER DEFEATED" : "YOU WIN", 2.4f); SAudio.StopMusic(); SAudio.Music("music_win", false); SAudio.Say("a_matchwin"); if (o.boss) _bossBeaten = true; pos++; attempts = 0; }
                else { Log.Add($"match {o.name}: LOST {pw}-{rw}"); Banner("DEFEATED", 2.4f); SAudio.StopMusic(); SAudio.Music("music_lose", false); SAudio.Say("a_matchlose"); if (++attempts >= 2 || _opp == 5) { pos++; attempts = 0; } else Log.Add("retry " + o.name); }
                SetPips(0, 0); yield return new WaitForSeconds(2.8f);
            }
            SAudio.StopMusic();
            if (_bossBeaten) { SAudio.Music("music_win", false); Banner("CHAMPION", 5f); SAudio.Say("a_champion"); }
            else { SAudio.Music("music_lose", false); Banner("TOURNAMENT OVER  -  " + MatchesWon + (MatchesWon == 1 ? " WIN" : " WINS"), 5f); }
            Log.Add($"tournament over: matches won {MatchesWon}, rounds {RoundsPlayed}, ring-outs {RingOuts}, cuts {Cuts}, hits {Hits}, shockwaves you hit/cleared {WaveHitsYou}/{WaveClearedYou}, rival hit/cleared {WaveHitsRival}/{WaveClearedRival}");
            yield return new WaitForSeconds(5f);
            Finished = true;
            if (!RealHands) yield break;
            _hint.text = "CURL YOUR RIGHT INDEX TO PLAY AGAIN"; _hintUntil = 0f; SAudio.Music("music_menu");
            while (!StringsInput.I.Out.jab) yield return null;
            _hint.text = ""; SAudio.Sfx("bell");
            }
        }

        static readonly Color[] _bodies = { new Color(0.85f, 0.2f, 0.2f), new Color(0.2f, 0.45f, 0.9f), new Color(0.25f, 0.7f, 0.35f), new Color(0.6f, 0.25f, 0.75f), new Color(0.95f, 0.5f, 0.15f), new Color(0.15f, 0.15f, 0.18f) };
        static readonly string[] _bodyN = { "RED", "BLUE", "GREEN", "PURPLE", "ORANGE", "BLACK" };
        static readonly Color[] _hats = { new Color(0.98f, 0.82f, 0.3f), new Color(0.95f, 0.95f, 0.95f), new Color(0.9f, 0.2f, 0.5f), new Color(0.3f, 0.85f, 0.85f), new Color(0.6f, 0.9f, 0.3f), new Color(0.85f, 0.55f, 0.2f) };
        static readonly string[] _hatN = { "GOLD", "WHITE", "PINK", "CYAN", "LIME", "COPPER" };
        static readonly string[] _lvl = { "LOW", "MEDIUM", "HIGH" };
        static readonly float[] _aggrV = { 0.25f, 0.5f, 0.75f }, _guardV = { 0.05f, 0.3f, 0.6f }, _speedV = { 0.55f, 0.8f, 1.0f };

        void RecolorP(SPuppet p, Color body, Color accent)
        {
            foreach (var r in p.GetComponentsInChildren<MeshRenderer>())
            {
                var m = r.sharedMaterial; if (m == null || !m.HasProperty("_BaseColor")) continue;
                if (r.name == "Torso" || r.transform.parent != null && (r.transform.parent.name == "ArmL" || r.transform.parent.name == "ArmR") && r.name == "Capsule") m.SetColor("_BaseColor", body);
                else if (r.name == "Belt" || r.name == "Hat" || r.name == "Brim") m.SetColor("_BaseColor", accent);
            }
        }

        /// <summary>The puppet workshop: dress your puppet and DESIGN YOUR RIVAL (aggression, guard, speed), all with finger curls. Saved between sessions.</summary>
        int[] SavedChoices()
        {
            int[] v = { PlayerPrefs.GetInt("s_body", 0), PlayerPrefs.GetInt("s_hat", 0), PlayerPrefs.GetInt("s_aggr", 1), PlayerPrefs.GetInt("s_guard", 1), PlayerPrefs.GetInt("s_speed", 1) };
            if (SArgs.Has("-str-auto")) v = new[] { 0, 0, 1, 1, 1 };
            int[] mod = { _bodies.Length, _hats.Length, 3, 3, 3 }; for (int i = 0; i < 5; i++) v[i] = Mathf.Clamp(v[i], 0, mod[i] - 1);
            return v;
        }

        /// <summary>Saved Workshop choices apply from the first second (the Workshop itself now comes after the first match).</summary>
        void ApplyChoices(int[] v)
        {
            RecolorP(Player, _bodies[v[0]], _hats[v[1]]);
            var c = _ladder[5]; c.aggr = _aggrV[v[2]]; c.guard = _guardV[v[3]]; c.speed = _speedV[v[4]]; c.react = 0.5f - 0.12f * v[2];
        }

        IEnumerator Workshop()
        {
            int[] v = SavedChoices();
            var hintPos = _hint.transform.position; float hintSize = _hint.characterSize; _hint.characterSize = hintSize * 1.25f; _hint.transform.position = SStage.Center + new Vector3(0f, 0.40f, 0.0f); /* about +-23 deg wide at 0.62 m, above the puppets' heads */ int row = 0; InWorkshop = true; Player.Active = Rival.Active = false; SAudio.Music("music_menu");
            float idle = 0f; bool dirty = true;
            while (true)
            {
                if (dirty)
                {
                    dirty = false; RecolorP(Player, _bodies[v[0]], _hats[v[1]]);
                    // START is the first row: one index curl and the next match begins; the rest is optional
                    string[] names = { "START THE NEXT MATCH", "YOUR BODY   " + _bodyN[v[0]], "YOUR HAT   " + _hatN[v[1]], "RIVAL AGGRESSION   " + _lvl[v[2]], "RIVAL GUARD   " + _lvl[v[3]], "RIVAL SPEED   " + _lvl[v[4]] };
                    var sb = new System.Text.StringBuilder("WORKSHOP: DESIGN YOUR NEXT RIVAL\nRIGHT INDEX = CHANGE    MIDDLE = NEXT ROW\n\n");
                    for (int i = 0; i < names.Length; i++) sb.Append(i == row ? "> " : "   ").Append(names[i]).Append('\n');
                    _hint.text = sb.ToString(); _hintUntil = 0f;
                }
                yield return null;
                var inp = StringsInput.I.Out; idle += Time.unscaledDeltaTime;
                if (idle > 25f) { Log.Add("workshop: started by itself after 25 s idle"); break; }   // nobody is choosing: do not hold the player here
                if (inp.kick) { row = (row + 1) % 6; SAudio.Sfx("tick"); idle = 0f; dirty = true; }
                else if (inp.jab)
                {
                    if (row == 0) break;
                    int[] mod = { _bodies.Length, _hats.Length, 3, 3, 3 }; v[row - 1] = (v[row - 1] + 1) % mod[row - 1]; SAudio.Sfx("hit_soft", 0.6f); idle = 0f; dirty = true;
                }
            }
            PlayerPrefs.SetInt("s_body", v[0]); PlayerPrefs.SetInt("s_hat", v[1]); PlayerPrefs.SetInt("s_aggr", v[2]); PlayerPrefs.SetInt("s_guard", v[3]); PlayerPrefs.SetInt("s_speed", v[4]); PlayerPrefs.Save();
            ApplyChoices(v);
            _hint.text = ""; _hint.transform.position = hintPos; _hint.characterSize = hintSize; InWorkshop = false; SAudio.Sfx("bell");
            Log.Add($"workshop: body {_bodyN[v[0]]}, hat {_hatN[v[1]]}, rival aggression {_lvl[v[2]]}, guard {_lvl[v[3]]}, speed {_lvl[v[4]]}");
        }

        void ApplyOpp(Opp o)
        {
            _nameR.text = o.name; Rival.pname = o.name;
            RecolorRival(o);
            _rivalBrain.aggression = o.aggr; _rivalBrain.guardiness = o.guard; _rivalBrain.reaction = o.react; _rivalBrain.dodgy = o.dodgy; _rivalBrain.hopChance = o.hop;
            Rival.speed = o.speed; Rival.powerMul = o.power;
        }

        void RecolorRival(Opp o)
        {
            foreach (var r in Rival.GetComponentsInChildren<MeshRenderer>())
            {
                var m = r.sharedMaterial; if (m == null || !m.HasProperty("_BaseColor")) continue;
                if (r.name == "Torso" || r.transform.parent != null && (r.transform.parent.name == "ArmL" || r.transform.parent.name == "ArmR") && r.name == "Capsule") m.SetColor("_BaseColor", o.body);
                else if (r.name == "Belt" || r.name == "Hat" || r.name == "Brim") m.SetColor("_BaseColor", o.accent);
            }
        }

        void SetPips(int p, int r)
        {
            for (int i = 0; i < 2; i++) { _pipP[i].GetComponent<Renderer>().sharedMaterial = i < p ? _pipOn : _pipOff; _pipR[i].GetComponent<Renderer>().sharedMaterial = i < r ? _pipOn : _pipOff; }
        }

        void Banner(string s, float sec) { _banner.text = s; StartCoroutine(ClearBanner(s, sec)); }
        IEnumerator ClearBanner(string s, float sec) { yield return new WaitForSecondsRealtime(sec); if (_banner.text == s) _banner.text = ""; }

        static readonly string[] Tips =
        {
            "MOVE YOUR RIGHT HAND TO STEER  -  PUSH THE RIVAL OFF THE EDGE\nRIGHT INDEX CURL = PUNCH     RIGHT MIDDLE CURL = KICK",
            "LEFT INDEX CURL = HOOK     LEFT MIDDLE CURL = SWEEP\nCLOSE BOTH FISTS = GUARD",
            "FLICK YOUR RIGHT HAND UP = HOP\nFILL THE BLUE METER, THEN PINCH YOUR LEFT HAND TO CUT THE STRINGS",
        };

        /// <summary>Hands out of view (or the system menu is open): freeze the fight instead of letting the rival push a puppet nobody is steering.</summary>
        IEnumerator WaitForHands()
        {
            Time.timeScale = 0f; _hint.text = "PAUSED\nHOLD BOTH HANDS OVER THE TABLE TO CARRY ON"; _hintUntil = 0f;
            Debug.Log("[STR] paused: waiting for both hands");
            while (!StringsInput.I.BothTracked || !_hasFocus) yield return null;
            yield return new WaitForSecondsRealtime(0.7f);
            _focusLost = false; StringsInput.I.Recenter(); _hint.text = ""; Time.timeScale = 1f;
        }

        // the system menu, the Meta button or taking the headset off: pause the fight on return instead of letting the rival push on
        bool _focusLost, _hasFocus = true;
        void OnApplicationFocus(bool has) { _hasFocus = has; if (!has) _focusLost = true; }
        void OnApplicationPause(bool paused) { _hasFocus = !paused; if (paused) _focusLost = true; }

        IEnumerator Round(int n, int pw, int rw, System.Action<SPuppet> done)
        {
            RoundsPlayed++;
            SStage.Radius = 0.42f; _platform.localScale = new Vector3(SStage.Radius * 2f, 0.015f, SStage.Radius * 2f);
            Player.ResetTo(SStage.Center + new Vector3(-0.2f, 0, 0)); Rival.ResetTo(SStage.Center + new Vector3(0.2f, 0, 0));
            Player.Active = Rival.Active = false; _timer.text = "";
            bool fin = pw == 1 && rw == 1; string rn = fin ? "FINAL ROUND" : "ROUND " + n;
            Banner(rn, 1.6f); SAudio.Say(fin ? "a_roundfinal" : "a_round" + Mathf.Min(n, 3)); yield return new WaitForSeconds(1.9f);
            SAudio.Sfx("tick"); Banner("3", 0.6f); yield return new WaitForSeconds(0.55f); SAudio.Sfx("tick"); Banner("2", 0.6f); yield return new WaitForSeconds(0.55f); SAudio.Sfx("tick"); Banner("1", 0.6f); yield return new WaitForSeconds(0.55f); SAudio.Sfx("bell");
            if (RealHands) StringsInput.I.Recenter();     // wherever the right hand rests now is the middle of the stage
            _focusLost = false;
            if (RoundsPlayed <= Tips.Length) { _hint.text = Tips[RoundsPlayed - 1]; _hintUntil = Time.time + 10f; }
            Banner("FIGHT!", 1.0f); SAudio.Say("a_fight");
            Player.Active = Rival.Active = true;
            float t = 0f, nextWave = 2.5f, lost = 0f; bool sd = false; SPuppet loser = null; bool boss = _ladder[_opp].boss;
            while (loser == null)
            {
                if (RealHands) { lost = StringsInput.I.R.tracked ? 0f : lost + Time.unscaledDeltaTime; if (lost > 0.6f || _focusLost || !_hasFocus) { lost = 0f; yield return WaitForHands(); } }
                t += Time.deltaTime;
                if (t > 18f) { SStage.Radius = Mathf.Max(0.24f, 0.42f - (t - 18f) * 0.014f); _platform.localScale = new Vector3(SStage.Radius * 2f, 0.015f, SStage.Radius * 2f); }
                if (t > 18f && !sd) { sd = true; SAudio.Say("a_sudden_death"); }
                _timer.text = t > 18f ? "SUDDEN DEATH" : "";
                if (boss && t > nextWave && SStage.WaveRadius < 0f) { nextWave = t + 5f; StartCoroutine(Wave()); }
                if (Player.Fallen || Rival.Fallen) loser = Player.Fallen && (!Rival.Fallen || Player.Pos.y < Rival.Pos.y) ? Player : Rival;
                else if (t > 75f) { float dp = (Player.Pos - SStage.Center).magnitude, dr = (Rival.Pos - SStage.Center).magnitude; loser = dp > dr ? Player : Rival; }
                yield return null;
            }
            RingOuts++; Player.Active = Rival.Active = false; SStage.WaveRadius = -1f; if (_wave != null) _wave.gameObject.SetActive(false);
            SAudio.Sfx("fall"); SAudio.Say("a_ringout"); Banner("RING OUT!", 1.8f);
            Time.timeScale = 0.25f; yield return new WaitForSecondsRealtime(0.9f); Time.timeScale = 1f;
            var winner = loser == Player ? Rival : Player; winner.wins++;
            SAudio.Sfx(winner == Player ? "cheer" : "gasp");
            Log.Add($"round {n} vs {Rival.pname}: {(winner == Player ? "YOU" : Rival.pname)} wins by ring-out at {t:F1}s");
            SAudio.Say(winner == Player ? "a_point_you" : "a_point_foe");
            yield return new WaitForSeconds(1.3f);
            done(winner);
        }

        IEnumerator Wave()
        {
            if (_wave == null)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); g.name = "Shockwave"; Destroy(g.GetComponent<Collider>());
                g.GetComponent<Renderer>().sharedMaterial = Mk(unlit, new Color(1f, 0.25f, 0.2f)); _wave = g.transform;
            }
            SStage.WaveId++;
            _wave.position = SStage.Center + Vector3.up * 0.006f; _wave.localScale = new Vector3(0.04f, 0.004f, 0.04f);
            _wave.gameObject.SetActive(true); SAudio.Sfx("whoosh"); Banner("JUMP!", 1.4f);
            yield return new WaitForSeconds(0.9f);                       // warning: the ring is already visible at the centre
            // the ring hits a puppet once, at the moment its front passes the puppet, and only if the puppet is on the floor then:
            // a hop up to about 0.3 s early clears it. It shoves hard (like a heavy hit) but does not throw a puppet off by itself.
            float r = 0.02f; bool doneP = false, doneR = false;
            while (r < SStage.Radius + 0.1f && Player.Active)
            {
                float prev = r; r += Time.deltaTime * SStage.WaveSpeed; SStage.WaveRadius = r;
                _wave.position = SStage.Center + Vector3.up * 0.006f; _wave.localScale = new Vector3(r * 2f, 0.004f, r * 2f);
                WaveHit(Player, prev, r, ref doneP); WaveHit(Rival, prev, r, ref doneR);
                yield return null;
            }
            SStage.WaveRadius = -1f; if (_wave != null) _wave.gameObject.SetActive(false);
        }

        void WaveHit(SPuppet p, float prev, float r, ref bool done)
        {
            if (done) return;
            Vector3 d = p.Pos - SStage.Center; d.y = 0f; float m = d.magnitude;
            if (m <= prev || m > r) return;
            done = true;
            if (p.Pos.y < SStage.TopY + 0.03f) { p.Shock(d, 1.3f); p.wobble = Mathf.Min(1.6f, p.wobble + 0.3f); Burst(p.Pos, new Color(1f, 0.3f, 0.2f), 14); if (p == Player) WaveHitsYou++; else WaveHitsRival++; }
            else if (p == Player) WaveClearedYou++; else WaveClearedRival++;
        }

        void OnHit(SPuppet victim, SPuppet by, int limb, float power)
        {
            Hits++;
            Burst(victim.Pos + Vector3.up * 0.1f, power > 1.4f ? new Color(1f, 0.6f, 0.2f) : new Color(1f, 0.95f, 0.6f), power > 1.4f ? 16 : 9);
            if (power > 1.2f) StartCoroutine(HitStop(0.06f));
        }

        // a hit-stop only restores the time scale it set itself, so it never cuts the ring-out slow motion short or ends a hands-lost pause
        const float HitStopScale = 0.08f;
        IEnumerator HitStop(float s) { if (Time.timeScale < 0.99f) yield break; Time.timeScale = HitStopScale; yield return new WaitForSecondsRealtime(s); if (Mathf.Approximately(Time.timeScale, HitStopScale)) Time.timeScale = 1f; }

        void Burst(Vector3 p, Color col, int count)
        {
            var g = new GameObject("Spark"); g.transform.position = p; var ps = g.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.duration = 0.5f; m.loop = false; m.startLifetime = 0.35f; m.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.7f); m.startSize = 0.012f; m.startColor = col; m.gravityModifier = 0.5f; m.simulationSpace = ParticleSystemSimulationSpace.World;
            var e = ps.emission; e.rateOverTime = 0; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.01f;
            var r = g.GetComponent<ParticleSystemRenderer>(); if (!_sparkMats.TryGetValue(col, out var sm)) { sm = Mk(unlit, col); _sparkMats[col] = sm; } r.sharedMaterial = sm; r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play(); Destroy(g, 1.2f);
        }

        // ------------------------------------------------------------------------------------------------ player input
        /// <summary>Full hand travel steers to 90% of the stage radius: a drifting hand cannot walk the puppet off the edge on its own;
        /// ring-outs come from being knocked out (it was 105%, so a hand 24 cm off-centre walked the puppet off the stage).</summary>
        public const float SteerReach = 0.9f;

        void Update()
        {
            SEv.Now = Time.time;
            if (Player == null || StringsInput.I == null) return;
            var inp = StringsInput.I.Out; Vector3 c = SStage.Center;
            if (StringsInput.I.R.tracked || StringsInput.I.Source != null)
                Player.moveTarget = c + new Vector3(inp.move.x, 0f, inp.move.y) * SStage.Radius * SteerReach;
            Player.guard = inp.guard; if (inp.hop) { Player.Hop(); SAudio.Sfx("hop", 0.6f); }
            if (inp.jab) Player.Strike(0); if (inp.hook) Player.Strike(1); if (inp.kick) Player.Strike(2); if (inp.sweep) Player.Strike(3);
            if (inp.cut && Player.TryCut()) { Cuts++; Log.Add("you cut the rival's strings"); SAudio.Say("a_cut"); }
            if (_hint != null && _hintUntil > 0f && Time.time > _hintUntil) { _hint.text = ""; _hintUntil = 0f; }
            // meters
            if (_meterP != null) { float sp = Mathf.Clamp01(Player.special); _meterP.localScale = new Vector3(0.012f + sp * 0.2f, 0.014f, 0.012f); _meterMat.SetColor("_BaseColor", sp >= 1f ? Color.Lerp(MeterFull, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f)) : MeterCol); }
            if (_meterR != null) { float sp = Mathf.Clamp01(Rival.special); _meterR.localScale = new Vector3(0.012f + sp * 0.2f, 0.014f, 0.012f); _meterMatR.SetColor("_BaseColor", sp >= 1f ? MeterFull : MeterCol); }
            bool full = Player.special >= 1f; if (full && !_meterWasFull && Player.Active) SAudio.Sfx("meter_full", 0.7f); _meterWasFull = full;
            if (Player.Active && Player.special >= 1f && !_cutTipShown && _hint != null && _hint.text == "") { _cutTipShown = true; _hint.text = "YOUR METER IS FULL  -  PINCH YOUR LEFT THUMB AND INDEX TO CUT THE STRINGS"; _hintUntil = Time.time + 7f; }
        }
    }
}
