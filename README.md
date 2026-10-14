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
| **11** | 13DDA | **Rule zero is stricter.** A scout who is 150 m ahead of someone and has nobody within 15 m is hunted by the Scoutmaster for 30 s (70 s from ascent 17; regrouping calls him off). Every death curses the survivors (12/8/4/2 curse for ≤2/≤4/≤6/more players), being thrown adds 4 curse. **Eating a scout** poisons you for 50 (28.6 below ascent 15, without its boost) and curses you for 25 (game: 10 curse). |
| **12** | 13DDA + More Ascents | **Falling and helping make you drowsy.** Fall damage makes you **80 drowsy** (61.5 below ascent 15, without its boost). Pulling someone up adds 5 drowsiness per second. Switching items mid-air no longer cancels a fall. |
| **13** | More Ascents | **Butter fingers.** Taking fall damage drops every item, including your backpack. |
| **14** | 13DDA + new | **Items are worse.** Ailment amounts on this row are what lands from ascent 15 on; at 14 itself they land without ascent 15's boost (poison ÷1.75, spores ÷1.65, heat ÷1.5, cold and drowsiness ÷1.3), so 14 always gets less. Food and extra stamina ×0.5, **eating a tick restores no hunger**, what items cure ×0.6 (what they cost you stays the game's), cure-alls remove at most half a bar, multi-use items lose 2 uses per use, rope ×0.75, **rope spools** hold half the rope (25 segments, at most 20 in one rope; game: 50/40), chain 30 m, weak balloons, a carried **Balloon** takes only 2% of the bar off your weight and a **Balloon Bunch** 8% (2.5% and 12.5%), slower energy drinks (the **Energy Drink** speeds up walking by only 30%; game: 50%; other speed boosts climb only +70%), the **Big Lollipop**'s crash is 85 drowsiness over 12 s (65 at 14; game: 33 over 10 s), cooking no longer removes poison or spores, **eating a scorpion costs 15 poison**, eating a **raw egg or tick** 5 poison (the Caldera's and the Gloom's eggs), an open parasol pulls you *down* — except the **Lightweight Parasol** (the Roots' parasol, renamed and weightless from ascent 9, and in custom runs with its own switch on the Custom Run screen), which still slows your fall — gliders use 1.5× stamina, letting go of a **Rescue Claw** makes you fall (you keep your items), the cannon burns (+50 heat) and the "cannon glitch" knocks everyone out, skeletons are fragile, invincibility only blocks 75%, revives keep 25% of your statuses ("Nerfed Revives"). A **Heat Pack** warms for 20 s (60), **sunscreen** keeps off only 75% of the sun's heat (all of it), the **Scoutmaster's Bugle** sends him after you for 180 s (120), a **Bounce Fungus** pad gives back 2.5 stamina (10), thrown **Shelf, Cloud and Warp Fungus** fly as if twice as heavy, a **Magic Bean**'s vine grows at most 15 m (20) and standing on it sticks 2.5 thorns a second into you (each 2.5 falls out after 30 s), placed **pitons** crack like the Alps' pitons but 30% sturdier (1.3–6.5 s of hanging, then five cracks). A **snowball** that bonks a scout gives 30 cold and knocks them down for 2 s (no cold, 1 s); holding one chills 2.5 a second. The Gloom's **Early Worm** removes only 50 drowsiness (100). Whatever you feed the **Jetpack** gives 35% less fuel. A **Remedy Fungus** cloud lasts 8 s (15), the **Anti-Zooka**'s bubble is 8 m across and lasts 40 s (10 m, 60 s), a **Portable Stove** burns 30 s (60). The **Book of Bones** costs 35 curse net when it makes you a skeleton (25), and a teammate revived with it gets 20 curse (15). The **Ancient Idol** weighs half the bar. A raw **Mandrake** gives 30 spores; cooked it removes 15 curse (25). A **checkpoint flag** that saves you curses you for 10. The **Cursed Skull** is the only cure for crab status (cure-alls such as the Book of Bones leave it) and keeps 10% of every status instead of 25% ("Nerfed Revives"; game: none). The **Ritual Dagger** turns a teammate into a zombie instead of killing them. **Pandora's Lunchbox** can hand out crab status too. **Scout statue gems** cost more and do less: Initiative 15 petrification per use (game: 7), reaches 6 m (8) and gives 2 s of low gravity (3); Ambition 6 s of infinite stamina (8) for 35 petrification (30); Tenacity heals up to 40 (60) and gives 5 s of invincibility (10) for 20–60 petrification (10–30); Generosity costs 35 petrification, 60 for a mystical item (25/45). **Shroomberries:** low gravity 8 s (15), invincibility 5 s (10), the healing one restores 7.5 hunger, injury and poison (15 each), +30 spores (+25; the game's 25 at 14), blind and numb 120 s (60), knocked down 12 s (8); the spore burst throws back the scouts within 10 m (4.8 m) and knocks them down for up to 4 s (1 s). |
| **15** | 13DDA | **Ailments apply more:** cold and drowsiness ×1.3, heat ×1.5, **spores ×1.65**, poison ×1.75, on everything (the Blowgun's dart: 155 drowsiness instead of 120). Rules with exact numbers (ascent 17 and others) are tuned so that their numbers land on the bar exactly, this boost included; below 15 the same rules land them without the boost, so they are always lower there. **Poisonous food** lands exact totals over the game's own delay and duration: Button Shroom 150 poison, Cluster Shroom 75, Bugle Shroom 120, Green Crispberry 40, Yellow Kingberry 150, Pink Berrynana 100 (game: 30, 40, 20, 10, 25, 15); Green Kingberry 35 at once (5); Shroomberries 15 spores (5). Every meal lands in full; only a cure or death stops it. A fed **Napberry** gives 200 drowsiness (100), a **Scorchberry** 80 heat (25); the **Energy Drink**'s crash is 50 drowsiness (25). |
| **16** | 13DDA + More Ascents | **Heat heals 3× slower** in the Caldera and the Kiln (drowsiness heals as slowly in the Gloom and the Citadel). **Campfires are extremely hot:** anything cooked on a campfire, over lava or by an explosion is charred to ruin (burned) on the very first cooking cycle. Lighting a campfire takes only **10 petrification** off the scouts beside it (game: 20). |
| **17** | 13DDA + new | **Hazards are more dangerous.** Numbers are what lands on your bar (ascent 15 included). *Beach:* jellyfish 20 poison per sting and a 10 s slip, 3× higher; sea urchins 40 poison. *Tropics:* thorny vines 60 poison; urchins and vines push 1.5× harder. **Ticks** live only in the Tropics, on **35%** of the tick spots (game: 1%); up to **5** latch on like cacti where they touch you, are visible at once and swell to 3× size within a minute; each bites for **2.5 poison every 0.5 s until pulled off**, and your poison does not heal for **10 s** after a bite. **Bees** sting for **10 poison/s** (calm) or **15/s** (angry) within **2 m**, stay angry **50 s** and chase up to **120 m** from their hive; at the middle of every in-game day and every night each scout in the Tropics has a **30%** chance of a **bee ambush** by an angry swarm that follows them up to 120 m from where it appeared. **Poison mines** up to 50 poison and **spore mines** (Roots) up to 45 spores, then their cloud adds up to 5 every 0.5 s for 11 s. *Roots:* beetles deal **10 injury**, hit 10× harder and knock you down for 10 s; the bouncy mushrooms give back 5 stamina per bounce (the purple ones 2.5, and 15 spores per touch). Spiders 12 poison every 0.8 s and a web of 40; a rescue grapple pulls a webbed scout out. Up to **12 mushroom zombies** wake at 30 m and chase at once (normal speed), stun 4 s, live 300 s. *Alpine:* geysers erupt on 80% of steps; their warm spot only reaches 2 m (game: 3 m). Napberries pulse every second. Flash plants blind up to **180 s** (less with distance). *Mesa:* cacti stick to any body part with two thorns; thorns pop out after 120 s alone and never by themselves with others. Scorpions sting every second within 1.7 m and pump their poison in twice as fast. Dynamite lights within 6 m, has a 3 s fuse and deals 100 injury if it blows up in your hand. **Tumbleweeds** roll 15% faster, hit twice as hard (full power from 10 m/s) and stick one more thorn (2, or 3 on a strong hit). *Caldera and Kiln:* lava 100 injury + 200 heat; lava streams 50 injury + 100 heat; climbing magma rock burns 2.5 heat every 0.3 s (like ice rocks' cold); eruptions more often. Petrifying stones 4 per touch, 8 per second while you climb them. *Gloom:* the big ghost comes every 50 s for anyone (not only loners), lives 50 s, explodes 1.5 s after arming for 60 cold and 80 s of blindness; lit bell towers push it out of blast range without burning it. Fly traps 10 poison/s and grab any body part. A frog's tongue pull poisons for 10. *Citadel:* arrow traps never run out and fire every 0.5 s (15 per arrow, 25 to pull it out); spiked fences 15; wall saws 30 on the first hit; the Central Tower's saw 7.5/s; spike rollers 10; spiked balls up to 60. Mimic chests release gas: 5 drowsiness, 5 poison and 5 spores every 0.5 s, and every second Citadel chest is a mimic. *Anywhere:* a **Mandrake**'s scream gives up to 75 drowsiness (game: 55); the Scoutmaster's hunts last 70 s; slipping on a banana peel darkens your screen like the Scoutmaster for 20 s. **Lookers** show up on 25% of their spots; watching one curses you for 2.5/s and gives you a 5 s sugar rush (then not again for 10 s); staring at it for 5 s makes it leave and sends the Scoutmaster after you for 70 s (walking up to it only makes it leave). Explosions double their status, knock you back 3–4× as hard and knock you out; every other damaging surface ×2; beetles ×10; spore and heat emitters ×4. **Burned items burn you:** a freshly burned item (cooked 3+ times), held or worn as a backpack, burns like a campfire for its first **10 s**. |
| **18** | 13DDA + new | **Weather is more extreme:** stronger wind storms, slippery rain that pushes you down, snowstorms that chill (or scorch by day outside the Alpine), tornadoes every 20–120 s that pull 4× as hard. The **Caldera's lava rises and falls** like a tide (synchronised for everyone). The fog turns **poisonous** and 3× as strong. **Cold nights chill 1.5× as fast.** In the **Alpine** the nights are longer: the **first and last quarter of the day are dark** too, with the same night cold. **The Mesa's midday sun roasts sun shields:** whatever you shield yourself with there (an open parasol, a glider held overhead, any held item that blocks the sun) stays clean for 5 s, smokes heavily for 15 s and is then fully burned (parasols 1.5× longer; the Mesa's sun roasts twice as fast, so 10 s, parasols 15 s). |
| **19** | More Ascents + new | **No more revive statues:** the statues at the end of each part of the climb (they revive the dead, or hand out an artifact if nobody died) are gone. Every other way back to life still works (scout effigies, skeletons, the Nadir), but **every revive adds 15 curse** (also the ones that give no status in the game) and **no revive cures petrification** any more (game: −75), so a scout who turned to stone stays doomed. Scouts start with **20 curse** (game: 10 from ascent 7). |
| **20** | More Ascents | **Bing Bong calls upon an evil force if not given attention.** Somebody must hold Bing Bong — holding it keeps the evil away for as long as it is held (in More Ascents the timer only reset when Bing Bong was picked up or put away); after 10 s without, Bing Bong starts talking, line after line, in the voice he has once burned, for every scout to hear (he falls silent as soon as someone holds him); after 15 s he falls silent and everyone gains crab status, which only the **Cursed Skull** cures (not cure-alls; Pandora's Lunchbox can even add more, see 14). **A burned Bing Bong never counts** (cooked 3+ times; from ascent 16 one campfire cooking burns it), and while freshly burned it burns the hand that holds it, like any burned item. |
| **21** | 13DDA | **Suitcases are painful:** small 6, big 10, epic 16 injury (statues 20 + 6 curse). The game fills status bars in steps of 2.5, so 6 shows as 5 and 16 as 15. **Clown chests** play a trick instead: 45 drowsiness, poison, spores, web or fly-trap grip, or 20 petrification (equally likely), or, half as often, 100 s of blindness; web and grip come off as usual (jumping, a friend's help). |
| **22** | 13DDA | **Ailments have unique effects:** poison and spores make you stumble, injuries slowly worsen, weight pulls you down faster, drowsiness does not heal at night (from the Caldera and the Gloom on there is no night), heat drains stamina while sprinting/climbing, cold slows walking and climbing. |
| **23** | 13DDA + More Ascents + new | **The sun is hot everywhere except the Alpine**, where it does nothing (the Mesa keeps its own sun; sunscreen still protects, letting a quarter of the heat through from ascent 14). **Sun protection burns everywhere** (from 18 only in the Mesa's midday sun): whatever you shield yourself with against direct sunlight (an open parasol, a glider held overhead, or any held item that blocks the sun) protects you but roasts instead, only while it shields you: it stays clean for **5 s**, then **smokes heavily** for **15 s**, and is then **fully burned at once**, skipping the cooking stages, and stays in your hands. An open **parasol** lasts **1.5× longer** (7.5 s + 22.5 s); the **Mesa's sun roasts twice as fast** (10 s, parasols 15 s). In the shade it stops smoking and cools down. |
| **24** | More Ascents | **Going unconscious is instant death** (a checkpoint flag still saves you). Note: fall damage alone makes you 80 drowsy (12), which with your 20 curse (19) fills the bar — so a fall kills. |
| **25** | 13DDA | **All weather everywhere, at slightly less effect.** Every run has **rain, wind and snowstorms**, whatever biome variants the map rolled (13DDA could only ever give you rain *or* wind), plus **extra tornadoes** roaming near the scouts. Storms start 30 s after waking up and keep away from the Kiln and beyond. With **True Final Ascent's Apocalypse mode** the weather is TFA's: its Apocalypse weather runs as TFA runs it (plus the tornadoes). |

**Climbing Chalk.** The game has an unused Climbing Chalk; from ascent 9 it is a real item, a rare find
wherever the **Energy Drink** turns up (the same luggage, the same odds). Using it takes 2 s while you
rub it into your hands; for **16 s, climbing** (walls, ropes and vines) **costs 75% of its stamina**
(from ascent 14: **85% for 12 s**). A cloud of chalk dust bursts from your hands and slowly settles,
your hands turn chalk white for everyone to see (gloves included), and a chalked hand shows above the
right end of the stamina bar while the effect lasts. It has a chalk-bag model, an icon and
its own sound; on every ascent it shows its bag instead of the game's placeholder cube. In a **custom
run** it has its own switch on the Custom Run screen.

## Choosing an ascent

Open the boarding pass at the airport and use the arrows, like vanilla.

* **Unlocking:** ascent 9 unlocks once you have beaten vanilla ascent 8; beating ascent *N* unlocks *N + 1*.
  The game's own Steam stat stops at 8; progress beyond is saved in the mod's config.
  Set **Unlock All Ascents** to skip that. Ascents 9–25 give no reward (no sash promotion).
* **Wrap-around:** left from Tenderfoot jumps straight to your highest unlocked ascent, right from the
  end of the list goes back to Tenderfoot.
* **Default Ascent:** pick which ascent the boarding pass shows when you first open it.
* With **True Final Ascent**, its FINAL ASCENT entry stays right after your highest ascent. In FINAL
  ASCENT, TFA's own difficulty dropdown offers ascents 9–25 too.

## Multiplayer

Everybody needs **the same version** of the mod. The boarding pass refuses to start ascents 9–25 and
names who is missing it. The ascent itself is the game's own value, so it reaches every player the
vanilla way (also late joiners and campfire saves). The host's settings for *Nerfed Revives* and
*Curse On Teammate Death* apply to everyone.

## Configuration (`BepInEx/config/UltimateAscent.cfg`)

| Section | Setting | Default | |
|---|---|---|---|
| Boarding Pass | Default Ascent | 0 | -1 = Tenderfoot, 0 = Peak (vanilla), 1–25 = that ascent (capped at your highest unlocked) |
| Boarding Pass | Wrap Around | true | The arrows wrap around |
| Unlocks | Unlock All Ascents | false | All of 9–25 selectable |
| Unlocks | Highest Unlocked Ascent | 0 | Your progress beyond 8 (kept by the mod) |
| Big Lobbies | Nerfed Revives | true | Ascent 14 revive/cure residue (host) |
| Big Lobbies | Curse On Teammate Death | true | Ascent 11 death curse (host) |
| Debug | Verbose Logging | false | Extra log lines |

## Compatibility

* **True Final Ascent 2.8.1** — supported (see above). TFA's Final Ascent map stacks all biomes; the mod
  uses TFA's own stage tracking there, moves its Caldera lava tide with TFA's Caldera offset,
  and leaves TFA's fog, weather and network keys alone. Its run records, daily board and Continue panel
  show ascents 9–25 by name.
  * **Apocalypse mode:** TFA decides how hard its own events are, so the ascent rules leave them alone.
    Whatever TFA's events do to a scout lands as TFA gives it: no ascent 15 boost on their statuses,
    their faints stay faints at ascent 24, falls they cause bring no ascent 12 drowsiness and no
    ascent 13 drops, TFA's own cures work in full. What they spawn (the blood moon's creatures, the
    cactus rain, the poison fog's mines, supply drops) keeps TFA's numbers, without ascents 10, 17 and
    21. During TFA's Blackout and Watch, ascent 11's Scoutmaster hunt steps aside; the Sleep Fog's
    Early Worms wake you fully. While an Apocalypse weather runs, the sun rules rest (ascent 23's
    heat, the roasting of ascents 18 and 23, ascent 14's leaky sunscreen), the way TFA rests More
    Ascents' sun. Once a status is on your bar it is yours, wherever it came from: how it heals and
    what it does (ascents 16 and 22) stay the ascent's.
* **Item Stats 0.6.1** and **Effect Preview 1.2.0** — supported: they show what items do on the ascent
  you climb (half food, weaker cures, partial cure-alls, ascent 15's boost and exact poison totals, the
  rules' crashes, weights and durations, suitcase pain, cannibalism, campfire petrification, Jetpack
  fuel) instead of the game's numbers. From ascent 16 Effect Preview shows no cooking stages, since
  cooking ruins the food at once.
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
can knock the test scout out); `-KeepApocalypse` keeps it and checks that TFA's events stay TFA's
(and, at ascent 25, that the weather is left to TFA).

## Credits

* **pooblives** — 13 Downright Dastardly Ascents (rules and much of the code this is based on).
* **Memnant** — 13 Downright Dastardly Ascents *Continued* (the PEAK 2.6 port, its fixes, and the
  embedded snowstorm visuals extracted from the game).
* **asdfmovie402** — More Ascents.
* **CptPICHU** — True Final Ascent (compatibility only; no TFA code is included).
* PEAK by Aggro Crab & Landfall.

Neither 13DDA nor More Ascents ships with a license; the Continued port explicitly states that no
re-license was granted. The embedded snowstorm bundle contains PEAK's own visual assets.
