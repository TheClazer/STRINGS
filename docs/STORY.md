# The Puppet Has No Buttons

*The story behind STRINGS, a seated marionette tournament you play with nothing but your hands.*

---

## The first thing a puppeteer learns

Watch someone work a marionette and you notice something odd: there is no interface. No trigger, no stick, no menu. The wrist tilts and the puppet leans. A finger lifts and an arm swings. Hundreds of years before anyone said "hand tracking", puppeteers had already solved the problem of turning small, precise hand movements into a character's whole body.

That is where STRINGS began. Most hand-tracked games treat your hands as a replacement for a controller: you pinch where you would have pulled a trigger. I wanted the opposite. I wanted a game where hands were not a substitute for buttons but the only thing that could possibly work, a game that would make no sense with a controller in your fist.

A marionette is that game. So I put one on your table.

## The stage

You sit down. In front of you is a small round stage on a dark wooden table, about the width of your two forearms. Red velvet curtains hang behind it. A pale moon is painted on the backdrop, a gold arch frames the top, and in the dark beyond the footlights there is an audience you can't quite see: rows of shadowed heads, and here and there a pair of eyes catching the light.

On the stage stand two puppets, each hanging from a wooden control bar by six strings. One of them is yours.

Your right wrist is the bar. Move it and your puppet walks. Curl your right index finger and it throws a punch; curl the middle finger and it kicks. Your left hand is the other half of the act: the index finger hooks, the middle finger trips. Close both fists and your puppet raises its guard. Flick your wrist upward and it hops.

A round is won the old-fashioned way: you push the other puppet off the edge of the stage. Best of three. After eighteen seconds the stage begins to shrink, because a puppet show that never ends is not a show.

## The troupe

Every tournament needs rivals, and STRINGS has a ladder of them. Each one plays differently, because each one is driven by a different temperament in the same decision-making code.

**Pip** comes first. Pip is the apprentice: slow to react, gentle with the punches, almost never guarding. Pip exists so that your first fight is a fight you can win while you are still discovering that your fingers are fists.

**Madame Rue** is the wall. She guards more than any other rival on the ladder, and she punishes the player who swings without thinking.

**Iron Bruno** is heavy. He is not fast, but when he lands a hit your puppet flies.

**Silk** is the opposite of Bruno: quick, evasive, always sidestepping, and fond of hopping out of trouble.

At the top waits **the Puppetmaster**, in black and crimson. The Puppetmaster sends shockwaves rippling out across the stage. You see the ring leave the centre; you have a moment; you flick your wrist and hop. Miss it and you are thrown.

Between the first match and the rest of the ladder, the curtain opens on the **Puppet Workshop**. Here you dress your own puppet, choosing a body colour and a hat. Then you do something stranger: you design an opponent. You set its aggression, its guard and its speed, and it joins the tournament as **Your Rival**. All of it is done with the same finger curls you fight with, and the workshop remembers your choices the next time you sit down.

The characters' personalities live in how they fight, not in cutscenes. There is no dialogue to read. The announcer calls the matches, the audience cheers or gasps, and the puppets tell the rest.

## The cut

Every fight game needs a moment people talk about afterwards. In STRINGS it is the cut.

Each puppet has a meter that fills as its hits land. When yours is full, a pinch of your left thumb and index finger cuts your rival's strings. The rival puppet collapses on the spot, slumped and helpless for a couple of seconds, its strings hanging loose. It is the only move in the game that is not a curl, so it can never happen by accident, and the first time a player does it on purpose they usually laugh out loud.

## The hard part: a fight without buttons

Here is the honest truth about building a hands-only fighting game: hands never stop moving.

A controller is silent until you press it. A tracked hand is never silent. Fingers tremble. A resting hand sits half-curled. When you curl your index finger, your middle finger follows along. When you close a fist to guard, all four fingers curl at once, and a naive game reads that as a punch, a kick, a hook and a trip in the same instant.

The first version of the gesture decoder was exactly that naive. To find out how bad it was without a headset, I built a gesture simulator: it replays realistic, noisy finger curves through the game's real input code. Trembling hands. Lazy, slow curls. A middle finger that drags along with the index. Hands resting on the table doing nothing at all. Then it counts every punch that fired when it shouldn't have and every punch that didn't fire when it should.

The first decoder produced **20,638 misfires** across the test set. Hands resting on the table, doing nothing, threw **3,597 false strikes** in twelve simulated minutes.

So I rebuilt it. A strike now needs a real, deliberate curl that rises through a threshold, holds for a short confirmation window, and then has to be released before it can fire again. A closed fist is recognised as a fist and locks out the strikes it would otherwise trigger. Each player's own range is learned while they play, so a small hand and a large hand both reach "fully curled".

The same simulator, same noise, same test set: **176 misfires** in total, and **5 false strikes** from resting hands in twelve minutes, down from 3,597.

There was a price, and I chose to pay it. A strike now lands about a fifth of a second after you begin a brisk curl, a little slower than before, and the simulator catches between 85 and 88 percent of deliberate curls instead of almost all of them. The old decoder "caught" more only because it was firing all the time. A game that punches when you don't mean it to is a game nobody trusts. A game that occasionally asks you to curl a little more firmly is a game you learn.

## The other hard part: rounds that lasted one second

The second surprise came from the autopilot, a test player that fights entire tournaments through synthetic hands so the game can be played thousands of times without a person in the headset.

The logs were full of rounds that ended after one second. Sometimes less. A single kick from the starting position was enough to slide a puppet clean off the stage. Technically a victory. Emotionally, nothing.

The fix was to make a ring-out something you earn. Early hits now shove rather than launch. Each hit a puppet takes makes it wobble a little more, and it is the accumulated wobble that finally sends it over the edge. After that change, across three full tournaments, the median round lasted 7.5 seconds, and most rounds fell between three and twenty seconds: long enough for a comeback, short enough that you always want one more.

## The first five minutes

The most important design decision in STRINGS was cutting things out of the opening.

Earlier versions started in the workshop, asking you to choose a hat before you had thrown a single punch. That is backwards. Now you raise your hands, the bell rings, and within about twenty seconds you are fighting Pip, with a single line of text above the stage telling you the one thing you need right now: steer with your right hand, curl to punch. The next round teaches the left hand. The round after that teaches the hop and the cut. You never read a manual; the game teaches you a move at the moment you can use it, and gets out of the way.

Real life gets the same respect. If a hand slips out of view, or you press the Meta button, or you take the headset off, the fight freezes and waits for both of your hands to come back. Nobody loses a round because they reached for a drink. And when the tournament ends, one finger curl starts it again.

## Built for a seat, a table and two hands

STRINGS asks for very little. You don't stand. You don't turn around. Everything happens within the reach of your forearms resting on a table, the way a real puppeteer works above a small stage. No controllers, no microphone, no room scan, no passthrough, no account. The app asks for hand tracking and nothing else.

It is built in Unity 6 with the Meta XR SDK on OpenXR. Finger curl comes from the joint angles of the tracked hand skeleton, so it measures what your fingers are doing, not how big your hand is. The rivals and the autopilot share the same decision code, which is how the game could be played through to the end hundreds of times before it was ever handed to a person. The announcer, the crowd, the sound effects and the music were generated with ElevenLabs, and AI coding assistants helped with the engineering.

## What is real today

I'd rather you trust this page than be impressed by it, so here is exactly where things stand.

| What | Status |
|---|---|
| The full tournament, all rivals, the workshop, the cut, the shockwave hop, pause and play again | Plays end to end in headless runs, with no errors across three full tournaments |
| Misfire numbers (20,638 → 176) | Measured in the gesture simulator on synthetic hands, not on real hands |
| Round lengths (median 7.5 s) | Measured from autopilot tournaments |
| The demo video | Recorded frame by frame in the Unity editor with the autopilot's synthetic hands driving the same input code; labelled as simulator footage |
| Feel on a real Quest, frame rate on device | To be measured on the headset; this page will only ever claim what has been measured |

## Why this, why now

There is a particular moment I built STRINGS around. You curl a finger, a little wooden arm swings on a stage in front of you, and for a second your brain forgets which one is the puppet.

That moment only exists when there is nothing in your hands. That is the whole point.

Sit down. Raise your hands. The bell is about to ring.
