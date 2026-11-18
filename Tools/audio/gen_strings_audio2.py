"""STRINGS audio pass 2: music, extra sfx, extra announcer lines. Cached; key never printed. Stops on first API error."""
import json, os, re, sys, subprocess, tempfile, urllib.request, urllib.error
OUT = r"E:\Winter\metavr\RoomIsLying_v2\Assets\Resources\StringsAudio"
UA = "Strings-Entry/1.0"
LINES = {
 "a_vs_master": "And now, the one who pulls every string. The Puppetmaster!",
 "a_sudden_death": "Sudden death! The stage is shrinking!",
 "a_three": "Three!", "a_two": "Two!", "a_one": "One!",
 "a_guard_break": "Guard broken!", "a_perfect": "Flawless victory!",
 "a_tryagain": "Try again. The strings are waiting.",
}
SFX = {  # id: (prompt, seconds, loop)
 "hop": ("a small wooden doll hopping once, light wooden tap on a stage floor", 0.6, False),
 "guard": ("a sharp wooden clack of puppet arms crossing to block", 0.6, False),
 "meter_full": ("a bright magical chime, rising sparkle, power fully charged", 1.5, False),
 "tick": ("a single dry clock tick, countdown", 0.5, False),
 "bell": ("a boxing ring bell ringing, one clear strike with ring-out", 2.0, False),
 "crowd_loop": ("a small theatre audience murmuring quietly and rustling, soft ambience, steady, no cheering", 20.0, True),
}
STYLE_END = " Instrumental only, no vocals."
MUSIC = {  # id: (prompt, ms, kind)
 "music_menu": ("Calm, slightly eerie music box melody with soft sustained strings, dark fairground lullaby, slow waltz feel, spacious and steady, no big climax, consistent energy throughout." + STYLE_END, 60000, "loop"),
 "music_fight": ("Driving tense circus-noir: snare and tom percussion, plucked pizzicato strings, staccato low cello ostinato, urgent cabaret energy, steady intensity throughout, 130 bpm." + STYLE_END, 60000, "loop"),
 "music_boss": ("Darker heavier circus-noir for a final boss: deep pounding drums, distorted low plucked strings, ominous brass stabs, menacing minor-key ostinato, relentless and steady intensity, 120 bpm." + STYLE_END, 60000, "loop"),
 "music_win": ("Short triumphant orchestral fanfare sting with bright strings and brass and a final resolved chord, ends cleanly." + STYLE_END, 5000, "sting"),
 "music_lose": ("Short sad sting: slack detuned plucked strings sagging downward, a mournful descending phrase, then silence." + STYLE_END, 5000, "sting"),
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
KEY = key()
def api(path, body=None, binary=False):
    req = urllib.request.Request("https://api.elevenlabs.io" + path, data=json.dumps(body).encode() if body is not None else None, headers={"xi-api-key": KEY, "Content-Type": "application/json", "User-Agent": UA})
    try: r = urllib.request.urlopen(req, timeout=900).read()
    except urllib.error.HTTPError as e: sys.exit(f"ElevenLabs error {e.code}: {e.read()[:300]!r}")
    return r if binary else json.loads(r)
def dur(p):
    return float(subprocess.check_output(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", p]).decode().strip())
def ff(*a):
    subprocess.check_call(["ffmpeg", "-y", "-v", "error", *a])
def make_loop(raw, dst, xf=1.5, br="112k"):
    L = dur(raw)
    fc = (f"[0]atrim={xf}:{L-xf},asetpts=PTS-STARTPTS[b];"
          f"[0]atrim={L-xf}:{L},asetpts=PTS-STARTPTS,afade=t=out:d={xf}[t];"
          f"[0]atrim=0:{xf},asetpts=PTS-STARTPTS,afade=t=in:d={xf}[h];"
          f"[t][h]amix=inputs=2:duration=longest:normalize=0[x];[b][x]concat=n=2:v=0:a=1[o]")
    ff("-i", raw, "-filter_complex", fc, "-map", "[o]", "-ac", "2", "-b:a", br, dst)
def make_sting(raw, dst, maxlen=5.5):
    L = min(dur(raw), maxlen)
    ff("-i", raw, "-t", str(L), "-af", f"afade=t=out:st={L-0.6}:d=0.6", "-b:a", "128k", dst)
os.makedirs(OUT, exist_ok=True)
tmp = tempfile.mkdtemp()
def todo(n): return not os.path.exists(os.path.join(OUT, n + ".mp3"))
for k, (p, ms, kind) in MUSIC.items():
    if not todo(k): continue
    raw = os.path.join(tmp, k + "_raw.mp3")
    open(raw, "wb").write(api("/v1/music?output_format=mp3_44100_192", {"prompt": p, "music_length_ms": ms, "force_instrumental": True}, binary=True))
    dst = os.path.join(OUT, k + ".mp3")
    (make_loop if kind == "loop" else make_sting)(raw, dst); print("made", k, round(dur(dst), 1))
for k, (p, s, loop) in SFX.items():
    n = "sfx_" + k
    if not todo(n): continue
    raw = os.path.join(tmp, n + "_raw.mp3")
    open(raw, "wb").write(api("/v1/sound-generation?output_format=mp3_44100_128", {"text": p, "duration_seconds": s, "prompt_influence": 0.5}, binary=True))
    dst = os.path.join(OUT, n + ".mp3")
    if loop: make_loop(raw, dst, xf=1.0, br="96k")
    else: ff("-i", raw, "-af", "afade=t=out:st=%.2f:d=0.15" % max(0, dur(raw) - 0.15), "-b:a", "128k", dst)
    print("made", n, round(dur(dst), 1))
voices = None
for k, text in LINES.items():
    if not todo(k): continue
    if voices is None:
        voices = api("/v1/voices")["voices"]
        vid = next(v["voice_id"] for v in voices if v["name"].lower().startswith("charlie"))
    open(os.path.join(OUT, k + ".mp3"), "wb").write(api(f"/v1/text-to-speech/{vid}?output_format=mp3_44100_128", {"text": text, "model_id": "eleven_turbo_v2_5", "voice_settings": {"stability": 0.35, "similarity_boost": 0.8, "style": 0.7}}, binary=True)); print("made", k)
print("done")
