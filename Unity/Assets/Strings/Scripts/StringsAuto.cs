using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Strings
{
    /// <summary>The autopilot: plays the tournament with the same brain the rivals use, but only through synthetic hands.</summary>
    public class StringsAuto : MonoBehaviour
    {
        StringsGame _g; readonly AutoHands _hands = new AutoHands(); SBrain _brain; Transform _rig; float _t0;

        IEnumerator Start()
        {
            _g = GetComponent<StringsGame>();
            _t0 = Time.realtimeSinceStartup;
            while (_g == null || _g.Player == null || StringsInput.I == null) { _g = GetComponent<StringsGame>(); yield return null; }
            StringsInput.I.Source = _hands;
            _brain = _g.Player.gameObject.AddComponent<SBrain>(); _brain.me = _g.Player;
            _brain.aggression = SArgs.GetF("-str-aggr", 0.55f); _brain.reaction = SArgs.GetF("-str-react", 0.3f); _brain.guardiness = SArgs.GetF("-str-guard", 0.3f); _brain.dodgy = 0.3f;
            _brain.OnMove = v => { Vector3 c = SStage.Center; _hands.move = Vector2.ClampMagnitude(new Vector2(v.x - c.x, v.z - c.z) / (SStage.Radius * StringsGame.SteerReach), 1f); };
            _brain.OnStrike = l => { if (l == 0) _hands.Jab(); else if (l == 1) _hands.Hook(); else if (l == 2) _hands.Kick(); else _hands.Sweep(); };
            _brain.OnGuard = g => _hands.guard = g; _brain.OnCut = () => _hands.Pinch(); _brain.OnHop = () => _hands.Hop();
            _rig = Camera.main != null ? Camera.main.transform.root : null;
            StartCoroutine(WorkshopScript());
            float timeout = SArgs.GetF("-str-timeout", 1500f);
            while (!_g.Finished)
            {
                if (Time.realtimeSinceStartup - _t0 > timeout) { Debug.Log("[STR][AUTO] TIMEOUT"); break; }
                yield return null;
            }
            Finish();
        }

        // scripted visit to the workshop: dress the puppet, then design a stronger rival, all with finger curls
        IEnumerator WorkshopScript()
        {
            while (!_g.InWorkshop) { if (_g.Finished) yield break; yield return null; }
            yield return new WaitForSeconds(2.6f);
            foreach (char c in "KJJKJKKKKJ")     // rows: START, body, hat, aggression, guard, speed (START first): body +2, hat +1, then back to START
            {
                if (c == 'J') _hands.Jab(); else _hands.Kick();
                yield return new WaitForSeconds(1.1f);
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main; if (cam == null || _rig == null) return;
            Vector3 aim = SStage.Center + new Vector3(Mathf.Sin(Time.time * 0.3f) * 0.03f, 0.06f + Mathf.Sin(Time.time * 0.21f) * 0.01f, 0f);
            Vector3 dir = aim - cam.transform.position; if (dir.sqrMagnitude < 1e-4f) return;
            Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Quaternion camLocal = Quaternion.Inverse(_rig.rotation) * cam.transform.rotation;
            _rig.rotation = Quaternion.RotateTowards(_rig.rotation, want * Quaternion.Inverse(camLocal), 60f * Time.unscaledDeltaTime);
        }

        void Finish()
        {
            SEv.Flush();
            Debug.Log("[STR][AUTO] LOG:\n" + string.Join("\n", _g.Log));
            Debug.Log($"[STR][AUTO] RESULT finished={_g.Finished} matchesWon={_g.MatchesWon} rounds={_g.RoundsPlayed} ringouts={_g.RingOuts} cuts={_g.Cuts} hits={_g.Hits} realSeconds={Time.realtimeSinceStartup - _t0:F0}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(_g.Finished ? 0 : 2);
#else
            Application.Quit();
#endif
        }
    }
}
