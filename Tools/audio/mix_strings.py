"""Mix the STRINGS demo: frames + event log (logs/strings/events.tsv) -> Submission/strings_demo_v2.mp4.   python -I mix_strings.py
The footage is the autopilot run rendered frame-exact in the Unity editor (StringsRecorder, 30 fps). The video opens a moment before the
first FIGHT so the first ten seconds show the hands and the puppet answering them; the welcome banner before it is skipped. A corner label
says what the footage is. Nothing is re-ordered or invented: every frame and sound comes from the same run."""
import os, subprocess, sys
ROOT = "E:/Winter/metavr"
EV = ROOT + "/logs/strings/events.tsv"; FR = ROOT + "/logs/strings/frames"
AU = ROOT + "/RoomIsLying_v2/Assets/Resources/StringsAudio"; OUT = ROOT + "/Submission/strings_demo_v2.mp4"
if os.path.abspath(OUT) == os.path.abspath(ROOT + "/Submission/strings_demo.mp4"): sys.exit("refusing to overwrite the old video")
FPS = 30.0
nframes = len([f for f in os.listdir(FR) if f.endswith(".jpg")]); full = nframes / FPS
ev = []
for l in open(EV, encoding="utf-8"):
    p = l.rstrip("\n").split("\t")
    if len(p) >= 4: ev.append((int(p[0]) / FPS, p[2], p[3]))
def first(kind, i, after=0.0):
    for t, k, x in ev:
        if k == kind and x == i and t >= after: return t
    return None
t_fight0 = first("say", "a_fight") or 12.0
START = max(0.0, t_fight0 - 2.2)                      # open on the countdown "2, 1, FIGHT!"
LIMIT = 178.0                                         # judges stop at 3:00; keep a margin
total = min(full - START, LIMIT)
ev = [(t - START, k, i) for t, k, i in ev if t - START < total]
inputs, filt, labels = [], [], []
def add(path, t, vol, dur=None, skip=0.0):
    i = len(inputs) // 2; inputs.extend(["-i", os.path.basename(path)]); ms = int(max(0.0, t) * 1000)
    f = f"[{i+1}:a]"
    f += f"atrim=start={skip:.2f}" + (f":end={skip + dur:.2f}" if dur else "") + ",asetpts=PTS-STARTPTS,"
    f += f"volume={vol}"
    if dur: f += f",afade=t=out:st={max(0, dur - 1.0):.2f}:d=1.0"
    f += f",adelay={ms}|{ms}[a{i}]"; filt.append(f); labels.append(f"[a{i}]")
music = [(t, i) for t, k, i in ev if k == "music"]
for n, (t, i) in enumerate(music):
    if i == "stop": continue
    end = next((t2 for t2, i2 in music[n+1:]), total); end = min(end, total)
    skip = max(0.0, -t); dur = end - max(t, 0.0)
    p = f"{AU}/{i}.mp3"
    if dur > 0.5 and os.path.exists(p): add(p, t, 0.22, dur, skip)
for t, k, i in ev:
    if t < 0: continue
    if k == "say": p, v = f"{AU}/{i}.mp3", 1.6
    elif k == "sfx": p, v = f"{AU}/sfx_{i}.mp3", 0.9
    else: continue
    if os.path.exists(p): add(p, t, v)
fc = ";".join(filt) + ";" + "".join(labels) + f"amix=inputs={len(labels)}:normalize=0:dropout_transition=0,alimiter=limit=0.95[m]"
def firstT(kind, i, after=-1e9):
    for t, k, x in ev:
        if k == kind and x == i and t >= after: return t
    return None
t_f1 = firstT("say", "a_fight", 0.0) or 2.2
t_cut = firstT("say", "a_cut", 0.0)
t_ws = firstT("music", "music_menu", 5.0)             # the Workshop opens after the first match
t_boss = firstT("say", "a_vs_master", 0.0)
t_end = firstT("say", "a_champion", 0.0) or firstT("music", "music_lose", (t_boss or 0.0) + 1.0)
CAPS = [
 (0.0, 9.5, "STRINGS: you are the puppeteer. Your wrist steers, finger curls punch and kick. Hands only."),
 (t_f1 + 8.0, t_f1 + 17.0, "Right index curl = punch. Right middle = kick. Left index = hook. Left middle = trip. Two fists = guard."),
 (t_f1 + 18.0, t_f1 + 27.0, "Push the rival off the round stage. Best of three. After 18 seconds the stage shrinks."),
]
if t_cut is not None: CAPS.append((t_cut - 0.5, t_cut + 6.0, "Meter full: pinch your left thumb and index to CUT the rival's strings."))
if t_ws is not None: CAPS.append((t_ws + 0.5, t_ws + 10.0, "After the first match: the Workshop. Dress your puppet and design your next rival, with finger curls."))
if t_boss is not None: CAPS.append((t_boss, t_boss + 9.0, "The Puppetmaster sends shockwaves: flick your right wrist up to hop over them."))
t_end = t_end if (t_end is not None and t_end + 6.0 <= total) else total - 6.5
CAPS.append((t_end, t_end + 6.0, "This excerpt: Pip, the Workshop, your designed rival, the Puppetmaster. The full ladder has six rivals."))
# keep captions from overlapping each other
CAPS.sort(); fixed = []
for a, b, txt in CAPS:
    if fixed and a < fixed[-1][1]: a = fixed[-1][1] + 0.2
    if b - a > 2.0 and a < total: fixed.append((a, min(b, total), txt))
def esc(t): return t.replace("\\", "").replace("'", "").replace(":", " -").replace(",", ";")
FONT = "fontfile='C\\:/Windows/Fonts/arial.ttf'"
dt = ["drawtext=" + FONT + ":text='" + esc("Simulator footage (Unity editor). Autopilot hands, decoded by the same gesture code as on the headset.") + "':fontsize=15:fontcolor=white@0.85:box=1:boxcolor=0x000000@0.45:boxborderw=6:x=14:y=12"]
for a, b, txt in fixed:
    dt.append("drawtext=" + FONT + ":text='" + esc(txt) + "':fontsize=22:fontcolor=white:box=1:boxcolor=0x000000@0.55:boxborderw=10:x=(w-text_w)/2:y=h-70:enable='between(t,%.2f,%.2f)'" % (a, b))
VF = ",".join(dt)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
FS = ROOT + "/logs/strings/filter.txt"; open(FS, "w").write(fc)
cmd = ["ffmpeg", "-y", "-loglevel", "error", "-framerate", "30", "-start_number", str(int(round(START * FPS))), "-i", FR + "/f%05d.jpg"] + inputs + ["-filter_complex_script", FS, "-vf", VF, "-map", "0:v", "-map", "[m]",
       "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", "-c:a", "aac", "-b:a", "192k", "-t", f"{total:.2f}", OUT]
r = subprocess.run(cmd, cwd=AU)
print("done" if r.returncode == 0 else "ffmpeg failed", OUT, f"start {START:.1f}s of {full:.0f}s rendered, length {total:.1f}s", len(labels), "audio events")
for a, b, txt in fixed: print(f"  caption {a:6.1f}-{b:6.1f}  {txt}")
