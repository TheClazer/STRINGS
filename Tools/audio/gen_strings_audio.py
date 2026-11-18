"""ElevenLabs audio for STRINGS: announcer lines (a_*.mp3) and sound effects (sfx_*.mp3) into Resources/StringsAudio. Cached; key read at run time, never printed."""
import json, os, re, sys, urllib.request, urllib.error
OUT = r"E:\Winter\metavr\RoomIsLying_v2\Assets\Resources\StringsAudio"
UA = "Strings-Entry/1.0"
LINES = {
 "a_welcome": "Ladies and gentlemen, puppeteers and ghosts! Welcome to the Marionette Tournament!",
 "a_vs_pip": "First, the clumsy apprentice. Pip!",
 "a_vs_rue": "Next, she never drops her guard. Madame Rue!",
 "a_vs_bruno": "Now, he hits like a falling piano. Iron Bruno!",
 "a_vs_silk": "And the champion no one has ever caught. Silk!",
 "a_round1": "Round one!", "a_round2": "Round two!", "a_round3": "Round three!", "a_roundfinal": "Final round!",
 "a_fight": "Fight!", "a_ringout": "Ring out!", "a_cut": "The strings are cut!",
 "a_point_you": "Point to you!", "a_point_foe": "Point to your rival!",
 "a_matchwin": "You win the match!", "a_matchlose": "Defeated. The strings go slack.",
 "a_champion": "Champion of the strings!",
}
SFX = {
 "whoosh": ("a fast whoosh of a wooden puppet arm swinging through air", 0.8),
 "hit_soft": ("a dull wooden thud, two toy wooden dolls bumping", 0.6),
 "hit_hard": ("a hard wooden crack and a small impact, one wooden puppet smacking another", 0.9),
 "snap": ("a taut string snapping with a sharp twang", 1.0),
 "fall": ("a small wooden doll falling a long way into darkness, fading whistle then a faint clatter", 2.5),
 "cheer": ("a small theatre audience cheering and clapping loudly", 3.0),
 "gasp": ("a small theatre audience gasping then groaning", 2.0),
}
def key():
    v = os.environ.get("ELEVENLABS_API_KEY")
    if v: return v.strip()
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k:
            v, _ = winreg.QueryValueEx(k, "ELEVENLABS_API_KEY")
            if v: return v.strip()
    except Exception: pass
    for line in open(r"E:\Winter\meta\.env", encoding="utf-8", errors="ignore"):
        m = re.match(r"\s*ELEVENLABS_API_KEY\s*=\s*(.+)", line)
        if m: return m.group(1).strip().strip('"').strip("'")
    sys.exit("no key")
def api(path, body=None, binary=False):
    req = urllib.request.Request("https://api.elevenlabs.io" + path, data=json.dumps(body).encode() if body is not None else None, headers={"xi-api-key": key(), "Content-Type": "application/json", "User-Agent": UA})
    try: r = urllib.request.urlopen(req, timeout=180).read()
    except urllib.error.HTTPError as e: sys.exit(f"ElevenLabs error {e.code}: {e.read()[:200]!r}")
    return r if binary else json.loads(r)
voices = api("/v1/voices")["voices"]
def pick(prefs):
    for p in prefs:
        for v in voices:
            if v["name"].lower().startswith(p): return v["voice_id"], v["name"]
    return voices[0]["voice_id"], voices[0]["name"]
vid, vname = pick(["charlie", "callum", "brian", "george"])
print("announcer:", vname, "| lines:", len(LINES), "| sfx:", len(SFX))
os.makedirs(OUT, exist_ok=True); made = 0
for k, text in LINES.items():
    d = os.path.join(OUT, k + ".mp3")
    if os.path.exists(d): continue
    open(d, "wb").write(api(f"/v1/text-to-speech/{vid}?output_format=mp3_44100_128", {"text": text, "model_id": "eleven_turbo_v2_5", "voice_settings": {"stability": 0.35, "similarity_boost": 0.8, "style": 0.7}}, binary=True)); made += 1
for k, (text, dur) in SFX.items():
    d = os.path.join(OUT, "sfx_" + k + ".mp3")
    if os.path.exists(d): continue
    open(d, "wb").write(api("/v1/sound-generation?output_format=mp3_44100_128", {"text": text, "duration_seconds": dur, "prompt_influence": 0.5}, binary=True)); made += 1
print("generated", made, "files; total:", len(os.listdir(OUT)))
