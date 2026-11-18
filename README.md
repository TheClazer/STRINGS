# STRINGS

**Puppet duels played with your bare hands.** A seated, hands-only VR tournament for Meta Quest: steer a marionette with your real hands and knock rival puppeteers off the stage.

Entry for the Meta VR Start Developer Competition 2026, Gaming track.

- Privacy policy: https://theclazer.github.io/STRINGS/privacy.html
- The story behind the game: [docs/STORY.md](docs/STORY.md)

## How to play
Sit at a table, put the headset down, and keep your controllers aside: STRINGS uses hand tracking only.

| Hand | Gesture | Move |
|---|---|---|
| Right | move wrist | steer your puppet |
| Right | quick upward wrist flick | hop (dodge the Puppetmaster's shockwaves) |
| Right | curl index finger | punch |
| Right | curl middle finger | kick |
| Left | curl index finger | hook |
| Left | curl middle finger | trip |
| Both | close both fists | guard |
| Left | pinch thumb and index (meter full) | cut the rival's strings |

Push the rival off the round stage to win a round, best of three. After 18 seconds the stage starts to shrink. After the first match, the Puppet Workshop lets you dress your puppet and design your next rival with finger curls. The ladder: Pip, your designed rival, Madame Rue, Iron Bruno, Silk, then the Puppetmaster. If a hand leaves the tracking view, the fight pauses until both hands are back.

## Install (judges)
Use the Meta Horizon release channel invite link from the Devpost page and choose **Install on headset**. To sideload instead: `adb install STRINGS_v3f_signed.apk` (from the judges' zip), then open it from Library > Unknown Sources.

## Repository layout
```
Unity/Assets/Strings/          game scripts, scene, materials
  Scripts/StringsInput.cs      finger-curl and wrist gesture decoding (OVRHand / OVRSkeleton)
  Scripts/StringsGame.cs       stage, rounds, ladder, workshop, pause on hand loss
  Scripts/StringsBrain.cs      rival AI and the synthetic-hands autopilot
Unity/Assets/Editor/           scene builder, APK build, gesture simulator, manifest strip
Unity/Assets/Resources/StringsAudio/   announcer, effects and music (ElevenLabs)
Unity/Assets/Shared/           two small text helpers (WorldText, FaceCamera)
Unity/Packages/manifest.json   package versions
Tools/audio/                   audio generation and video mix scripts (read ELEVENLABS_API_KEY from the environment)
docs/                          privacy page (GitHub Pages) and story
```

## Building
Unity 6000.0.85f2 with the Meta XR SDK (`com.meta.xr.sdk.all`) and OpenXR, Android target, hand tracking set to HandsOnly. Copy `Unity/Assets` and `Unity/Packages/manifest.json` into a Unity 6 project, use the menu **Strings > Build Scene**, then **Strings > Build Quest APK**.

## Built with
Unity 6, C#, Meta XR SDK, OpenXR, Meta Quest hand tracking, ElevenLabs (announcer voice, sound effects and music).

## Honest status
The gesture decoder was tuned in a headless gesture simulator that replays noisy finger curves through the real input code, and full tournaments were played headless by the autopilot. The build launches on a Quest 3S; gesture accuracy with real hands has not yet been measured.

## Author
Suchit SM (smsuchit123@gmail.com)
