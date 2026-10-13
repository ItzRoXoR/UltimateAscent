# Threats at ascent 24

Every value below is from PEAK 2.6.b (read from the game code and the game data) and from
Ultimate Ascent 1.0.0. Status amounts use the stamina bar's scale: **100 = the whole bar**. A rule
marked *(17)* comes from that ascent; ascent 24 contains every ascent up to 24. Ultimate Ascent's
numbers are what lands on the bar, ascent 15's boost included.

## Rules that apply to every threat at 24

| Rule | Game | Ultimate Ascent at 24 |
|---|---|---|
| How a status is added | Amounts collect until they reach a 2.5-point step; whole steps go on the bar, **the rest is dropped** (a single 6 adds 5). | Same. Exact targets that are not whole steps (a spider's 12) carry the rest over to the next bite, so they average out exactly. |
| Passing out | When statuses fill more than 99% of the bar for **5 s** (the faint timer; it runs back at the same speed when the bar drops below 99%). | Same timer, but passing out is **instant death** *(24)*. A checkpoint flag still saves you. |
| Fall damage | ×2 from ascent 1. | Fall damage also makes you **80 drowsy** *(12)*; with your 20 curse *(19)* that fills the bar → **fatal** *(24)* unless you cure it within the faint timer. Fall damage drops all your items, backpack included *(13)*. |
| Status strength | — | Cold and drowsiness ×1.3, heat ×1.5, spores ×1.65, poison ×1.75 *(15)*, on everything. The exact numbers below are tuned so that they land as given, this boost included. |
| Explosions | Knockback, a knock-down and the explosion's own status. | Every explosion: status ×2, knockback ×3–4, knock-down ×3 **and drowsiness up to passing out** *(17)* → at 24 being knocked down by an explosion kills. (Mines and the Gloom ghost land exact amounts instead of ×2.) |
| Touch hazards (anything that hurts on contact) | Own values. | Damage ×2, at least 2 cold/heat *(17)*; urchins, thorny vines, spiked fences, lava streams and petrifying stones have exact values (below). Climbing ice rock ×2; magma rock burns and petrifying stones petrify while you climb them. Status emitters that add a status (spore clouds, heat fields) ×4. |
| Recovery | Statuses heal over time after a short pause. | Heat heals 3× slower in the Caldera/Kiln, drowsiness as slowly in the Gloom/Citadel *(16)*; injuries **worsen** by 2 per minute, drowsiness does not heal at night (there is no night from the Caldera and the Gloom on), poison/spores make you stumble, heat drains stamina, cold slows you *(22)*; poison does not heal for 10 s after a tick bite *(17)*. |
| Revives | Scout statues at the end of each part of the climb revive the dead (or hand out an artifact if nobody died); a revive adds 15 curse *(game ascent 7)* and takes 75 petrification off. | **No revive statues** *(19)*; every other revive (scout effigy, skeleton, the Nadir) adds **15 curse**, also the ones that give the game's no status, and **keeps petrification** *(19)*. |
| Curse | Every scout starts with 10 curse *(game ascent 7)*, so 90 of the bar is left for everything else. | **20 curse** at the start *(19)*, so 80 is left; every death curses the survivors; eating a scout costs 50 poison and 25 curse *(11)*. |

"Falloff" below is the game's explosion falloff: full strength at the centre, weaker outward, and
nothing below the minimum factor. "Knocked down" is ragdoll time.

## Beach

### Jellyfish
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Poison per sting | 5 | **20** |
| Knock-down | 2 s, launched (hip 1500 up, feet 200, head −300) | **10 s**, launched **3× higher** (hip 4500) |
| Cooldown | 3 s | 3 s |
| Fall damage after the launch | ×2 | ×2, and 80 drowsiness *(12)* → **fatal** *(24)* |

### Sea urchins
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Poison per touch | 10 | **40** |
| Repeat | at most once per second while touching | same |
| Knockback | 20 (100 while ragdolled) | **30** (150) |

## Tropics

### Ticks
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | Tick spots on jungle weeds and monsteras, only in the Tropics (930–1,160 per Tropics map) | Same; tick spots anywhere else are removed |
| Spots with a tick | **1%** (≈10 per map) | **35%** (≈330–400 per map) |
| Ticks at once | 1, always on the right knee | **up to 5**, latching on where they touch you (like cacti), never on top of each other |
| Size | grows from nothing to full size over 5 min | **0.6 at once, 3× full size after 60 s** |
| Bite | every 29 s: 5 poison | **every 0.5 s: 10 poison** = 20 poison/s per tick, 100/s with five |
| Stops biting | after 10 bites if your poison is at least 50 | **never**: it bites until you pull it off |
| No poison healing | each bite adds a 30 s "no poison healing" effect that **the game never uses** | your poison does not heal for **10 s** after each bite |
| Removal | interact with it (it lands in your hand) | same; eating it restores **no hunger** *(14)* |

### Bees
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Swarms | one per beehive | the same, plus **ambushes**: at the middle of every day and every night each scout standing in the Tropics has a **30%** chance; the swarm appears 2.5 m away, angry |
| Poison | calm 3/s, angry 6/s, +5 when you enter the swarm (1 s cooldown) | **calm 10/s, angry 15/s**; +5 on entering |
| Sting radius | 1.3 m; stops once your bar is full | **1.5 m**; same |
| Aggro / lose interest | attack within 5 m (7 m angry); give up beyond 6 m (7 m angry); never more than 20 m from the hive | same; an ambush swarm follows its scout up to **40 m** from where it appeared |
| Anger | 8 s after the hive is grabbed, 12 s after it is destroyed | **35 s** (hive grabbed, hive destroyed, or an ambush) |
| Measured | — | an ambush on a scout with 70 stamina: bar full **3.1 s** after the first sting, **dead 8.1 s** after it |

## Tropics and Roots

### Exploding mushrooms (poison mines, bombs, spore mines)
Each mushroom bursts once, when a scout touches it or an item hits it at 5 m/s or more (100%).
The burst weakens with distance and does nothing below 20% strength. Poison and spore mines then
leave a cloud that bursts again every 0.5 s for 11 s, with the same falloff.

| | Game | Ultimate Ascent at 24 |
|---|---|---|
| **Poison mines**: Tropics only (`Jungle_SporeMushroom`, ≈100 per map) | 20 poison × falloff within 9.6 m (half strength at 7.5 m), no knockback; then 2.5 × falloff every 0.5 s for 11 s | **up to 60 poison**; then **up to 5 every 0.5 s** (10/s at the centre) for 11 s |
| **Bombs**: Tropics (≈100 per map) and Roots (≈130–175 per map, caves included) (`Jungle_SporeMushroomExplo`) | no status; knockback 1300 × falloff, knocked down 1 s × falloff, within 4.8 m (strength falls off linearly over 6 m) | knockback ×3–4, knocked down 3 s **and drowsiness up to passing out** *(17)* → **fatal within 4.8 m** *(24)* |
| **Spore mines**: Roots only (`Forest_SporeFungus`, ≈240 per map, caves included) | 20 spores × falloff within 9.6 m; then 2.5 × falloff every 0.5 s for 11 s | **up to 55 spores**; then **up to 5 every 0.5 s** for 11 s |

## Roots

### Spore clouds
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Spores | 2.25/s within 11 m (only the nearest cloud counts), with a warning; ≈1.7/s on the bar after the 2.5-point steps | emitters ×4 *(17)*, spores ×1.65 *(15)* → ≈15/s nominal, **≈10/s** on the bar |
| Wind | paused while the Roots wind blows (+2 s) | same |

### Spiders
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Trigger | drop on you when you pass under them (up to 50 m below, 2 m wide) | same |
| Grab | Web +25 on the bar, knocked down 1 s, pulled up | **Web +40**, knocked down **1.5 s** |
| Poison | 5 every 0.8 s, starting 2 s after the grab | **12 every 0.8 s** (on the bar 10, 12.5, 12.5, 12.5, 12.5, …: 12 on average, 15/s) |
| Escape | each jump −5 web (5 jumps); a teammate frees you | 8 jumps; a teammate; or a **rescue grapple** that catches you pulls you out of the web |

### Mushroom zombies
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Spawn | each zombie spot keeps its zombie with 33%; **1 zombie** awake on the map at a time | **up to 12** at a time |
| Waking / chasing | wakes when you look at it within 20 m; chases from 10 m; sprints after 3 s within 10 m; lunges within 4 m | wakes from **30 m** and **chases at once**, at its normal speed |
| Bite | 10 injury, 10 spores + 0.5/s for 60 s (30), stunned 3 s | spores ×1.65 (bite 15 on the bar, then ≈0.8/s), stunned **4 s** |
| Lifetime | dies after 120 s awake | **300 s** |

## Alpine

### Geysers
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Trigger | **33%** chance each time someone steps on it | **80%** |
| Burst (within 4 m) | heat 30 × falloff (at least 30% strength), knockback 1500 × falloff, knocked down 1 s × falloff | heat ×2 ×1.5 → **up to 90**, knockback ×3–4, knocked down 3 s **and drowsiness up to passing out** → **fatal** *(24)* |
| Warm spot | −5 cold/s within 3 m | **−15 cold/s within 2 m** |

### Ice rocks
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Touching (standing on one, bumping into one) | 1 cold per touch, at most once every 0.7 s; on the bar that is 2.5 every third touch (≈1.2/s) | 2 *(17)* × 1.3 *(15)* → **2.5 per touch** (≈3.6/s) |
| Climbing one | 1 cold every 0.3 s; on the bar 2.5 every 0.9 s (≈2.8/s) | 2 *(17)* × 1.3 *(15)* → **2.5 every 0.3 s** (≈8.3/s) |
| Time to a full bar | from the starting 10 curse: ≈32 s of climbing, ≈75 s standing on one | from the starting 20 curse: **≈10 s of climbing**, ≈22 s standing on one; 5 s later you pass out, which is **death** *(24)* |
| What cold does | fills the bar like any status | also slows walking and climbing, up to 35% slower at a full cold bar *(22)* |

### Napberry bushes (sleepy berries)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | ≈6 bushes per Alpine map | same |
| Hypnosis | while the berry on the bush is on your screen within 12 m and you are awake, it pulses twice every 3 s: 5 drowsiness per pulse at 12 m, up to 10 from 2 m or closer when you look straight at it | pulses **once a second**; 5 at 12 m, **12.5** up close (×1.3 *(15)*, which the 2.5-point steps drop at range) |
| Time to a full bar | from the starting 10 curse: ≈13 s up close, ≈26 s at 12 m; after the 5 s faint timer you fall asleep | from the starting 20 curse: **≈7 s up close**, ≈16 s at 12 m; 5 s later you pass out, which is **death** *(24)* |
| Stopping it | pick the berry, look away or move beyond 12 m | same |
| The berry as an item | can only be fed to another scout: clears all their statuses except curse, weight and crab, then +100 drowsiness, so they sleep it off | clears at most 50 of each status, then +77.5 drowsiness (100 × 0.6 × 1.3) *(14, 15)*; with their 20 curse that is 97.5, so almost anything else on their bar makes them pass out, which is **death** *(24)* |

### Flash plants (blue flowers)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | ≈60 per Alpine map | same |
| Trigger | bursts once when a scout walks into it (100%); blinds everyone in range, not only the one who touched it | same |
| Flash (within 3.9 m) | blind for 45 s × falloff: 45 s up close, ≈37 s at 2 m, ≈30 s at 3 m, 13.5 s at the edge; no damage, knockback or knock-down; a second flash restarts the timer | blind for **180 s** × falloff: 180 s up close, ≈146 s at 2 m, ≈119 s at 3 m, 54 s at the edge |

## Mesa

### Cacti
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Cactus | sticks an arm or leg (not head/torso) for up to 4 s, **1 thorn** = 5 on the bar | sticks **any body part**, **2 thorns** = 10 |
| Thorns | pop out after 30 s solo / 120 s in multiplayer, or pull them out | **120 s** solo; in multiplayer they **never pop out**: pull them out |
| Cactus ball | +20 on the bar while one is stuck to you | **+40** |

### Scorpions
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Sting | within 1.5 m, every 3 s (a 2 s rest, then a 1 s wind-up), knockback 500 | within **1.7 m**, **every second** (the wind-up only) |
| Poison | 2.5, then poison over **15 s** adding enough to fill your bar (at least 50) | over **7.5 s** *(17)*, ×1.75 *(15)* → **bar full ≈4 s after a sting** → dead ≈9 s after it unless cured; more stings stack |

### Dynamite
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | Mesa mineshafts; also an item (Legendary rarity) | same |
| Fuse | lights when you come within 5 m; 5 s | lights within **6 m**; **3 s** |
| In your hand when it blows | +25 injury | **+100** |
| Explosion (within 12 m, line of sight) | injury 30 × falloff, knockback 1600 × falloff, knocked down 1 s × falloff | injury ×2 (**up to 60**), knockback ×3–4, knocked down 3 s **and drowsiness up to passing out** → **fatal within ≈11.5 m** *(24)* |

## Caldera

### Burning rocks (eruptions)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Frequency | while a scout is in the Caldera: every −5 to 15 s (a negative roll erupts again at once), anywhere ±155 × ±140 m | every −5 to 12 s *(17)* |
| Each eruption | burns 35 s: 3 heat zones of 15/s each within 15 m (+3 × 1/s) → **≈45 heat/s** on the bar close by | heat zones ×4 *(17)*, heat ×1.5 *(15)* → **≈285 heat/s**; the bar fills on the first heat tick (within 0.5 s) |

### Lava (the lava river) and lava flows
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Heat above the river | up to 1.2 every 0.1 s at the surface, fading over 10 m (≈8/s on the bar) | ×1.5 *(15)* (≈12/s) |
| Touching the river | 25 injury + 25 heat (at most 1/s), pushed up | **100 injury and 200 heat** → bar full at once → **fatal** *(24)* |
| River height | fixed | **rises and falls ±8 m about once a minute** *(18)*, the same for everyone |
| Lava flows (streams) | 20 injury + 50 heat per touch (1/s), knockback 1000 | **50 injury + 100 heat** → bar full → **fatal** |
| Lava pillars | knockback 1000 + 10 heat (cliffs 5) | heat ×1.5 |
| Magma rocks (Caldera and Kiln) | 2.5 heat per touch while standing on them (every 0.75 s); heat field 1/s within 11 m; climbing them does nothing extra | touch **7.5** *(17, 15)*; field ×4 ×1.5 = 6/s nominal, **5/s** on the bar; **climbing them burns 2.5 heat every 0.3 s** (≈8.3/s), on top of touching, like ice rocks' cold |

## Kiln

| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Rising lava | starts after 180 s, reaches the top in 1,200 s | starts after **30 s**, rises in **700 s** *(9)* |
| Heat above the rising lava | up to 1.5 every 0.25 s, fading over 10 m (≈5/s on the bar) | ×1.5 *(15)*, lost to the 2.5-point steps (still ≈5/s) |
| Touching the lava | 25 injury + 25 heat (at most 1/s), pushed up | **100 injury and 200 heat** → **fatal** *(24)* |
| Burning (magma) rocks | as in the Caldera | as in the Caldera |
| Heat recovery | normal | **3× slower** *(16)* |

### Petrifying stones (crystals and petrified rock, in the Kiln and elsewhere)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Touching | +2 petrify points (of 100) per second | **+4** per second |
| Climbing | nothing extra: the stones' climbing effect is switched off in the game data, only touching counts | **+8** per second while you climb one |
| At 100 | you turn to stone (after the 5 s faint timer) | same; a revive no longer cures it, and there are no revive statues *(19)* |
| Healing | lighting a campfire removes 20; a revive removes 75 | lighting a campfire removes **10** *(16)*; revives remove **nothing** *(19)* |

## Gloom

### The big ghost
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Spawn | every 60–180 s (counted while no ghost is out) near a scout who is alone (nobody within 15 m), 30 m ahead (±10) | **every 50 s**, for the leading scout **alone or in a group** |
| Attack | rolls toward the nearest scout; arms within 1 m, explodes 2.1 s later (get 1.3 m away within 1.5 s to stop it); lives 30 s | explodes **1.5 s** after arming (no way back after ≈1.1 s); lives **50 s** |
| Defence | lit bell towers (11 m) repel it and burn it (3 s) | lit bell towers push it back until it is out of blast range of the scouts they protect (11 + 10 m), and **do not burn it**; it cannot arm while pushed |
| Explosion (within 10 m) | **blind 20 s**, cold 35, knockback 1600 (tilted up), knocked down 4 s, landing damage capped at 7.5 for 8 s | **blind 80 s**, **60 cold**, knockback ×3–4, knocked down 12 s **and drowsiness up to passing out** → **fatal** *(24)* |

### Carnivorous plants (Venus fly traps)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Bite | catches an arm or leg: FlyTrap +30 on the bar, knocked down 0.5 s, pulled in | catches **any body part**: **+45**, knocked down **0.75 s** |
| Poison while held | 5 every second | **10 every second** |
| Escape | each jump −2.5 (12 jumps); a teammate frees you | **18 jumps** |
| Trap cooldown | 2 s | same |

### Sleep fog
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Gloom fog | 0.9 drowsiness/s (lit bell towers protect) | ×1.3 *(15)*; drowsiness heals 3× slower in the Gloom *(16)*, at night too (there is no night here) |

## Citadel

### Arrow traps (also in the Gloom temple)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Shot | fires along its line up to 80 m at the first scout in it; 3 shots, 8 s apart, then empty for good | **never runs out**, fires every **0.5 s** |
| Hit | 1 arrow = **7.5** on the bar (Arrow), knockback 30 | 1 arrow = **15**, knockback **60** |
| Pulling an arrow out | 12.5 injury | **25 injury** |

### Traps
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Spiked fences (Gloom days) | 2.5 injury per touch (at most 1/s) | **15** |
| Lava sphere (one per Gloom-day map) | heat up to 7.5 every 0.5 s within 3.9 m; touch 25 injury + 25 heat | heat ×1.5; touch **100 injury + 200 heat** → **fatal** |
| Floor spike traps (2,800) | step on → 0.3 s → 1 s → hit within 1.2 m: **15 injury**, knockback 300, knocked down 0.2 s | the game builds this hit as an explosion: **30 injury**, knockback ×3–4, knocked down 0.6 s **and drowsiness up to passing out** → **fatal** *(24)* |
| Wall saws (180) | first touch 15 injury (after 2 s without a hit), then 2.5 every 0.25 s while touching (10/s) | first touch **30**, then the same |
| Central Tower saw | nothing: its damage is left empty in the game data | **7.5 per second** |
| Spike rollers (32) | 5 injury per contact (up to 4/s), pushed away | **10** per contact |
| Swinging spike balls (156) | up to 25 injury (by swing speed), knockback up to 500, knocked down up to 0.2 s, 1 s cooldown | up to **60** |

### Mimic chests (trick luggage)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | about one per Citadel map (Central Tower) | the same, plus **every second ordinary chest in the Citadel** (they keep their loot, like the game's mimics) |
| Opening | releases sleeping gas for 10 s within 10 m: 5 drowsiness every 0.5 s (10/s) → 100 in 10 s | gas with **5 drowsiness + 5 poison + 5 spores every 0.5 s** (30/s in all) for 10 s; opening any suitcase also hurts for **10 injury** *(21)*; staying in the gas = passing out = **death** *(24)* |
| Rising gloom | starts after 150 s, rises in 1,200 s | starts after **30 s**, rises **2.5× as fast** (480 s) *(9)* |

## Anywhere

### The Scoutmaster
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Needs | at least 2 scouts | same; a Looker can send him after anyone, solo too |
| Picking a target | every 30 s a 10% chance: the highest scout, if more than 100 m above the next one and below 825 m | **also**: the host checks every 5 s and makes him hunt a scout who is 150 m ahead of someone with nobody within 15 m, for **70 s** (regrouping calls him off) *(11, 17)* |
| Chase | teleports to 50–70 m from the target when farther than 80 m (unseen, every 5 s+); grabs within 3 m | same |
| Throw | holds you 3.2 s, throws you toward the biggest drop (force 1500 × 0.7–1.0), knocked down 1 s, **30 injury** | same, plus **4 curse** per throw *(11)*; the fall damage after the throw makes you 80 drowsy *(12)* → **fatal** *(24)*; a death curses the survivors (12/8/4/2 with ≤2/≤4/≤6/more scouts) *(11)* |

### The Looker
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | 20 hiding spots per map; in 5% of runs one of them shows a Looker | **every spot** shows one with **15%** (≈3 per map) |
| Watching it | it peeks out now and then and ducks away 6× sooner while watched; harmless | while it peeks out in the middle of your view within 40 m (nothing in between): **5 curse a second**, and a lollipop's **sugar rush** (infinite stamina) for 5 s, then not again for 10 s; not looking costs nothing |
| Leaving | for good, once the host has watched it for 3 s or anyone comes within 5 m | after **5 s** of your watching it leaves and **sends the Scoutmaster after you for 70 s**; within 5 m it just leaves |

### Clown chests
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Opening | clown loot | a trick instead of the 10 injury other suitcases deal *(21)*: **45 drowsiness, poison, spores, web or fly-trap grip, or 20 petrification** (each 2 in 13), or **100 s of blindness** (1 in 13); web and grip come off by jumping or a friend's help, as in the game |

### Bing Bong's evil force
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Neglect | — | 15 s without anyone holding Bing Bong: 5 crab a second *(20)*, which **nothing cures**: not cure-alls, not Pandora's lunchbox |

### Banana peels
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Slip | 2 s | **10 s**, launched 3× higher (like jellyfish), and your screen **darkens like the Scoutmaster's** for 20 s (no Scoutmaster) |

## Not on the list, but deadly at 24
| Threat | Game | Ultimate Ascent at 24 |
|---|---|---|
| Thorny vines (Tropics) | 10 poison per touch (at most 1/s), knockback 1000 that can cause fall damage | **60 poison per touch**, knockback **1500**; a fall from the knockback → fatal *(12, 24)* |
| Poison ivy (Tropics) | 3.5 poison per touch (2.5 on the bar), at most 1/s | at least 15 *(17)* × 1.75 → **25 per touch** |
| Beetles (Roots) | bonk within 4 m (force 125, 225 up), knocked down 1 s | force ×10, within 6 m, knocked down **10 s**; a landing with fall damage → fatal |
