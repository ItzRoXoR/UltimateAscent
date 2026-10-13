# Ultimate Ascent

Seventeen extra ascents for **PEAK** (ascents **9–25**) on the game's own boarding pass. They combine
**13 Downright Dastardly Ascents** (pooblives, via the *Continued* port by Memnant) and
**More Ascents** (asdfmovie402), add a few new rules, and work together with **True Final Ascent**.

Built and checked against **PEAK 2.6.b** (Unity 6000.3.15f1) with BepInEx 5.4.23.

Ascents are cumulative, like the vanilla ones: ascent *N* contains every vanilla ascent and every rule
of ascents 9 to *N*.

## The ascents

| Ascent | Source | Rule |
|---|---|---|
| **9** | 13DDA (trimmed) | The fog moves **2× as fast** and starts after **20 s** instead of waiting for you to move on (on the beach too), **until the Caldera or the Gloom**; from there on the fog keeps the game's pace (with True Final Ascent's FINAL ASCENT: until its Caldera). The **Kiln's lava** starts after **30 s** and rises in **700 s**; the **Citadel's gloom** starts after **30 s** and, like the **Nadir's ghosts**, rises **2.5× as fast**. |
| **10** | More Ascents + new | **Big and Explorer's luggage** have a **50% chance to hold only one item**. |
| **11** | 13DDA | **Rule zero is stricter.** A scout who is 150 m ahead of someone and has nobody within 15 m is hunted by the Scoutmaster for 30 s (70 s from ascent 17; regrouping calls him off). Every death curses the survivors (12/8/4/2 curse for ≤2/≤4/≤6/more players), being thrown adds 4 curse. **Eating a scout** poisons you for 50 and curses you for 25 (game: 10 curse). |
| **12** | 13DDA + More Ascents | **Falling and helping make you drowsy.** Fall damage makes you **80 drowsy**. Pulling someone up adds 5 drowsiness per second. Switching items mid-air no longer cancels a fall. |
| **13** | More Ascents | **Butter fingers.** Taking fall damage drops every item, including your backpack. |
| **14** | 13DDA + new | **Items are worse.** Food and extra stamina ×0.5, **eating a tick restores no hunger**, healing ×0.6, cure-alls remove at most half a bar, multi-use items lose 2 uses per use, rope ×0.75, chain 30 m, weak balloons, slower energy drinks, lollipops 3× drowsier, poisonous food far worse and cooking no longer removes poison, an open parasol pulls you *down*, gliders use 1.5× stamina, letting go of a rescue hook drops you and your items, the cannon burns (+50 heat) and the "cannon glitch" knocks everyone out, skeletons are fragile, invincibility only blocks 75%, revives keep 25% of your statuses ("Nerfed Revives"). |
| **15** | 13DDA | **Ailments apply more:** cold and drowsiness ×1.3, heat ×1.5, **spores ×1.65**, poison ×1.75, on everything. Rules with exact numbers (ascent 17 and others) are tuned so that their numbers land on the bar exactly, this boost included. |
| **16** | 13DDA + More Ascents | **Heat heals 3× slower** in the Caldera and the Kiln (drowsiness heals as slowly in the Gloom and the Citadel). **Campfires are extremely hot:** anything cooked on a campfire is charred to ruin (burned) on the very first cooking cycle. Lighting a campfire takes only **10 petrification** off the scouts beside it (game: 20). |
| **17** | 13DDA + new | **Hazards are more dangerous.** Numbers are what lands on your bar (ascent 15 included). *Beach:* jellyfish 20 poison per sting and a 10 s slip, 3× higher; sea urchins 40 poison. *Tropics:* thorny vines 60 poison; urchins and vines push 1.5× harder. **Ticks** live only in the Tropics, on **35%** of the tick spots (game: 1%); up to **5** latch on like cacti where they touch you, are visible at once and swell to 3× size within a minute; each bites for **10 poison every 0.5 s until pulled off**, and your poison does not heal for **10 s** after a bite. **Bees** sting for **10 poison/s** (calm) or **15/s** (angry) within 1.5 m and stay angry **35 s**; at the middle of every in-game day and every night each scout in the Tropics has a **30%** chance of a **bee ambush** by an angry swarm that follows them up to 40 m from where it appeared. **Poison mines** up to 60 poison and **spore mines** (Roots) up to 55 spores, then their cloud adds up to 5 every 0.5 s for 11 s. *Roots:* spiders 12 poison every 0.8 s and a web of 40; a rescue grapple pulls a webbed scout out. Up to **12 mushroom zombies** wake at 30 m and chase at once (normal speed), stun 4 s, live 300 s. *Alpine:* geysers erupt on 80% of steps; their warm spot is 2 m wide and removes 15 cold/s. Napberries pulse every second. Flash plants blind up to **180 s** (less with distance). *Mesa:* cacti stick to any body part with two thorns; thorns pop out after 120 s alone and never by themselves with others. Scorpions sting every second within 1.7 m and pump their poison in twice as fast. Dynamite lights within 6 m, has a 3 s fuse and deals 100 injury if it blows up in your hand. *Caldera and Kiln:* lava 100 injury + 200 heat; lava streams 50 injury + 100 heat; climbing magma rock burns 2.5 heat every 0.3 s (like ice rocks' cold); eruptions more often. Petrifying stones 4 per touch, 8 per second while you climb them. *Gloom:* the big ghost comes every 50 s for anyone (not only loners), lives 50 s, explodes 1.5 s after arming for 60 cold and 80 s of blindness; lit bell towers push it out of blast range without burning it. Fly traps 10 poison/s and grab any body part. *Citadel:* arrow traps never run out and fire every 0.5 s (15 per arrow, 25 to pull it out); spiked fences 15; wall saws 30 on the first hit; the Central Tower's saw 7.5/s; spike rollers 10; spiked balls up to 60. Mimic chests release gas: 5 drowsiness, 5 poison and 5 spores every 0.5 s, and every second Citadel chest is a mimic. *Anywhere:* the Scoutmaster's hunts last 70 s; slipping on a banana peel darkens your screen like the Scoutmaster for 20 s. **Lookers** show up on 15% of their spots; watching one curses you for 5/s and gives you a 5 s sugar rush (then not again for 10 s); staring at it for 5 s makes it leave and sends the Scoutmaster after you for 70 s (walking up to it only makes it leave). Explosions double their status, knock you back 3–4× as hard and knock you out; every other damaging surface ×2; beetles ×10; spore and heat emitters ×4. **Burned items burn you:** a freshly burned item (cooked 3+ times), held or worn as a backpack, burns like a campfire for its first **10 s**. |
| **18** | 13DDA + new | **Weather is more extreme:** stronger wind storms, slippery rain that pushes you down, snowstorms that chill (or scorch by day outside the Alpine), tornadoes every 20–120 s that pull 4× as hard. The **Caldera's lava rises and falls** like a tide (synchronised for everyone). The fog turns **poisonous** and 3× as strong. **Cold nights chill 1.5× as fast.** In the **Alpine** the nights are longer: the **first and last quarter of the day are dark** too, with the same night cold. |
| **19** | More Ascents + new | **No more revive statues:** the statues at the end of each part of the climb (they revive the dead, or hand out an artifact if nobody died) are gone. Every other way back to life still works (scout effigies, skeletons, the Nadir), but **every revive adds 15 curse** (also the ones that give no status in the game) and **no revive cures petrification** any more (game: −75), so a scout who turned to stone stays doomed. Scouts start with **20 curse** (game: 10 from ascent 7). |
| **20** | More Ascents | **Bing Bong calls upon an evil force if not given attention.** Somebody must hold Bing Bong — holding it keeps the evil away for as long as it is held (in More Ascents the timer only reset when Bing Bong was picked up or put away); after 10 s without, the world shakes, after 15 s everyone gains crab status, which nothing cures (not cure-alls, not Pandora's lunchbox). **A burned Bing Bong never counts** (cooked 3+ times; from ascent 16 one campfire cooking burns it), and while freshly burned it burns the hand that holds it, like any burned item. |
| **21** | 13DDA | **Suitcases are painful:** small 6, big 10, epic 16 injury (statues 20 + 6 curse). The game fills status bars in steps of 2.5, so 6 shows as 5 and 16 as 15. **Clown chests** play a trick instead: 45 drowsiness, poison, spores, web or fly-trap grip, or 20 petrification (equally likely), or, half as often, 100 s of blindness; web and grip come off as usual (jumping, a friend's help). |
| **22** | 13DDA | **Ailments have unique effects:** poison and spores make you stumble, injuries slowly worsen, weight pulls you down faster, drowsiness does not heal at night (from the Caldera and the Gloom on there is no night), heat drains stamina while sprinting/climbing, cold slows walking and climbing. |
| **23** | 13DDA + More Ascents + new | **The sun is hot everywhere except the Alpine**, where it does nothing (the Mesa keeps its own sun; sunscreen still protects). **Sun protection burns:** whatever you shield yourself with against direct sunlight (an open parasol, a glider held overhead, or any held item that blocks the sun) protects you but roasts instead, only while it shields you: it **smokes** like a backpack on hot rock, and when its time is up it is **fully burned at once**, skipping the cooking stages, and stays in your hands. That takes 30 s of sunshine; an open **parasol** lasts **1.5× longer** (45 s); the **Mesa's sun roasts twice as fast** (15 s, parasols 22.5 s). In the shade it stops smoking and cools down. |
| **24** | More Ascents | **Going unconscious is instant death** (a checkpoint flag still saves you). Note: fall damage alone makes you 80 drowsy (12), which with your 20 curse (19) fills the bar — so a fall kills. |
| **25** | 13DDA | **All weather everywhere, at slightly less effect.** Every run has **rain, wind and snowstorms**, whatever biome variants the map rolled (13DDA could only ever give you rain *or* wind), plus **extra tornadoes** roaming near the scouts. Storms start 30 s after waking up and keep away from the Kiln and beyond. With **True Final Ascent's Apocalypse mode**, two Apocalypse weathers run at once instead (plus the tornadoes). |

## Choosing an ascent

Open the boarding pass at the airport and use the arrows, like vanilla.

* **Unlocking:** ascent 9 unlocks once you have beaten vanilla ascent 8; beating ascent *N* unlocks *N + 1*.
  The game's own Steam stat stops at 8; progress beyond is saved in the mod's config.
  Set **Unlock All Ascents** to skip that.
* **Wrap-around:** left from Tenderfoot jumps straight to your highest unlocked ascent, right from the
  end of the list goes back to Tenderfoot.
* **Default Ascent:** pick which ascent the boarding pass shows when you first open it.
* With **True Final Ascent**, its FINAL ASCENT entry stays right after your highest ascent. In FINAL
  ASCENT, TFA's own difficulty dropdown offers ascents 9–25 too.

## Multiplayer

Everybody needs **the same version** of the mod. The boarding pass refuses to start ascents 9–25 and
names who is missing it. The ascent itself is the game's own value, so it reaches every player the
vanilla way (also late joiners and campfire saves). The host's settings for *Nerfed Revives*,
*Curse On Teammate Death* and *Double Apocalypse Weather* apply to everyone.

## Configuration (`BepInEx/config/UltimateAscent.cfg`)

| Section | Setting | Default | |
|---|---|---|---|
| Boarding Pass | Default Ascent | 0 | -1 = Tenderfoot, 0 = Peak (vanilla), 1–25 = that ascent (capped at your highest unlocked) |
| Boarding Pass | Wrap Around | true | The arrows wrap around |
| Unlocks | Unlock All Ascents | false | All of 9–25 selectable |
| Unlocks | Highest Unlocked Ascent | 0 | Your progress beyond 8 (kept by the mod) |
| Big Lobbies | Nerfed Revives | true | Ascent 14 revive/cure residue (host) |
| Big Lobbies | Curse On Teammate Death | true | Ascent 11 death curse (host) |
| Compatibility | Double Apocalypse Weather | true | Ascent 25 + TFA Apocalypse: two events at once (host) |
| Debug | Verbose Logging | false | Extra log lines |

## Compatibility

* **True Final Ascent 2.8.1** — supported (see above). TFA's Final Ascent map stacks all biomes; the mod
  uses TFA's own stage tracking there, moves its Caldera lava tide with TFA's Caldera offset,
  and leaves TFA's fog, weather and network keys alone.
* **More Ascents** and **13 Downright Dastardly Ascents** (incl. Continued) — *incompatible* (they would
  fight over the same ascent slots); BepInEx refuses to load Ultimate Ascent next to them.
* Every rule is its own Harmony patch: if a game update breaks one, only that rule switches off
  (logged), the rest keeps working.

## Installing

Copy `UltimateAscent.dll` into `BepInEx/plugins/UltimateAscent/` of your r2modman profile (or the
game folder), or import the packaged zip in r2modman. Requires BepInExPack for PEAK.

## Building

```
dotnet build src/UltimateAscent/UltimateAscent.csproj -c Release
```

Paths default to `C:\Program Files (x86)\Steam\steamapps\common\PEAK` and the r2modman profile
`FinalAscent`; override with `-p:PeakDir=...` / `-p:ProfileDir=...`.
`tools/build.ps1` builds, runs the patch checker and writes the Thunderstore zip to `artifacts/`.
`tools/deploy.ps1 -Profile <name>` copies the DLL into an r2modman profile.

`tools/PatchCheck` statically verifies every Harmony patch against the installed game (target exists,
not ambiguous, every injected parameter name matches).

`tools/smoketest.ps1 [-Clean] [-Profile <name>] [-Ascent N] [-KeepApocalypse]` tests the mod inside the
game: it installs a temporary test plugin (`tools/SmokeTest`), starts PEAK through Steam, plays a solo
run on its own (boarding pass, today's map, jumps to the later segments), checks the rules from inside
the game, quits and removes the plugin again. Results land in `artifacts/smoketest/`. `-Clean` uses a
throwaway profile with nothing but BepInEx and Ultimate Ascent (no True Final Ascent, no other mods).
Otherwise True Final Ascent's Apocalypse mode is switched off for that session only (its random weather
can knock the test scout out); `-KeepApocalypse` keeps it and checks ascent 25's second Apocalypse
weather instead.

## Credits

* **pooblives** — 13 Downright Dastardly Ascents (rules and much of the code this is based on).
* **Memnant** — 13 Downright Dastardly Ascents *Continued* (the PEAK 2.6 port, its fixes, and the
  embedded snowstorm visuals extracted from the game).
* **asdfmovie402** — More Ascents.
* **CptPICHU** — True Final Ascent (compatibility only; no TFA code is included).
* PEAK by Aggro Crab & Landfall.

Neither 13DDA nor More Ascents ships with a license; the Continued port explicitly states that no
re-license was granted. The embedded snowstorm bundle contains PEAK's own visual assets.
