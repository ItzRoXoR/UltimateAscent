# Threats at ascent 24

Every value below is from PEAK 3.0.0 (read from the game code and the game data) and from
Ultimate Ascent 1.6.2's rules. Status amounts use the stamina bar's scale: **100 = the whole bar**. A rule
marked *(17)* comes from that ascent; ascent 24 contains every ascent up to 24. Ultimate Ascent's
numbers are what lands on the bar, ascent 14's boost included; below ascent 14 the same rules land
them without that boost, so they are always lower there. Distances are the game's own units,
called metres here as in the mod; the game's altitude display counts one unit as 1.6 m.

## Rules that apply to every threat at 24

| Rule | Game | Ultimate Ascent at 24 |
|---|---|---|
| How a status is added | Amounts collect until they reach a 2.5-point step; whole steps go on the bar, **the rest is dropped** (a single 6 adds 5). | Same. Exact targets that are not whole steps (a spider's 12) carry the rest over to the next bite, so they average out exactly. |
| Passing out | When statuses fill more than 99% of the bar for **5 s** (the faint timer; it runs back at the same speed when the bar drops below 99%). | Same timer, but passing out is **instant death** *(24)*. A checkpoint flag still saves you, for **10 curse** *(15)*; it puts back exactly the statuses you had when you planted it, but leaves you the petrification you have if that is more *(15)* (the game puts back that too). |
| Fall damage | ×2 from ascent 1. | Fall damage also makes you **60 drowsy** *(12)*; with your 20 curse *(19)* that is 80, so a fall that hurts for **20 or more** (less with other statuses on the bar) fills it → **fatal** *(24)* unless you cure it within the faint timer. Fall damage drops all your items, backpack included *(13)*. |
| Status strength | — | Cold and drowsiness ×1.3, heat ×1.5, spores ×1.65, poison ×1.75 *(14)*, on everything. The exact numbers below are tuned so that they land as given, this boost included. |
| Explosions | Knockback, a knock-down and the explosion's own status. | Every explosion: status ×2, knockback ×3–4, knock-down ×3 *(17)*. Only a cooked Lantern's or Faerie Lantern's burst and the Gloom ghost's burst also bring **drowsiness up to passing out** → at 24 being knocked down by one of them kills. (Mines and the Gloom ghost land exact amounts instead of ×2; a Shroomberry's spore burst and a cooked Toxic Drink's are not treated as explosions.) |
| Cooking | Campfires (the portable stove too), lava and explosions cook items; cooking takes a food's poison away. | Campfires, lava and explosions char what they cook **to ruin** on the first cooking *(17; the config's Hot Campfires switch can turn this off)*; cooking no longer removes poison *(15)*. |
| Food and items | As listed per item. | Hunger and extra stamina from food ×0.5, what status items cure ×0.6 *(15)*; what they cost you (a Napberry's sleep, a Scorchberry's heat) stays the game's, except the exact amounts in **Food** below *(14)*. |
| Touch hazards (anything that hurts on contact) | Own values. | Damage ×2, at least 2 cold/heat *(17)*; urchins, thorny vines, spiked fences, lava streams and petrifying stones have exact values (below). Climbing ice rock ×2; magma rock burns and petrifying stones petrify while you climb them. Status emitters that add a status (spore clouds, heat fields) ×4. |
| Recovery | Statuses heal over time after a short pause. | Heat heals 2.6× slower in the Caldera/Kiln (0.5 a second), drowsiness 2.5× slower in the Gloom/Citadel (1 a second) *(16)*; injuries **worsen** by 2 per minute, drowsiness does not heal at night (there is no night from the Caldera and the Gloom on), poison/spores make you stumble, heat drains stamina, cold slows you *(22)*; poison does not heal for 10 s after a tick bite *(17)*. |
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
| Fall damage after the launch | ×2 | ×2, and 60 drowsiness *(12)*: with the sting's 20 poison and your 20 curse → **fatal** *(24)* |

### Sea urchins
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Poison per touch | 10 | **40** |
| Repeat | at most once per second while touching | same |
| Knockback | 20 (100 while ragdolled) | **30** (150) |

### Crabs
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | none | the beach, **25** small red crabs (about a scorpion's size) spread along it from where the scouts wake up to its campfire, **seven in ten on the climb itself** (the cliff up to the campfire, spread all the way up it), the rest on the sand; never leaving the beach, not replaced once taken *(every ascent; below 17 half as strong)* |
| Getting about | — | crawl around the beach at **1 m/s** from spot to spot, resting a moment at each, scuttling sideways; a scout within **2 m** is chased at the same 1 m/s (a scout walks 2.5 m/s and sprints 5.2 m/s), one at random if more are that near, for at least **6 s**, then for as long as they stay within 2 m *(17)* |
| Pinch | — | facing the scout, the big claw rises and opens for 1 s, then snaps shut (the scorpion's glint and sparks), and 0.15 s after the snap, within 1.5 m, it lands: **10 injury**, and whatever you hold falls out of your hands; again after 2 s *(17)* |
| Picked up | — | like a scorpion, into your hands only (put away, it is dropped): held, it pinches you (10 injury) and falls out of your hands *(17)* |
| Eating one | — | only once cooked, as a cooked crab leg (darker than the live crab; it goes in a pocket or a backpack, like any food): 10 hunger and 10 extra stamina, 5 and 5 *(15)*; every way to cook it burns it *(17)*, and burnt it can still be eaten: no hunger or stamina, **35 poison** (the game's 20 for burnt food, with ascent 14's boost) |
| Sounds | — | a claw click now and then while someone is about (none while it goes for a scout or is held), a scuttle while it moves, one snap as it pinches *(17)* |

## Tropics

### Ticks
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | Tick spots on jungle weeds and monsteras, only in the Tropics (930–1,160 per Tropics map) | Same; tick spots anywhere else are removed |
| Spots with a tick | **1%** (≈10 per map) | **35%** (≈330–400 per map) |
| Ticks at once | 1, always on the right knee | **up to 5**, latching on where they touch you (like cacti), never on top of each other |
| Size | grows from nothing to full size over 5 min | **0.6 at once, 3× full size after 60 s** |
| Bite | every 29 s: 5 poison | **every 0.5 s: 2.5 poison** = 5 poison/s per tick, 25/s with five |
| Stops biting | after 10 bites if your poison is at least 50 | **never**: it bites until you pull it off |
| No poison healing | each bite adds a 30 s "no poison healing" effect that **the game never uses** | your poison does not heal for **10 s** after each bite |
| Removal | interact with it (it lands in your hand) | same; eating it restores **no hunger** *(15)*, and eaten raw it poisons for **5** *(15)*; any cooking chars it to ruin *(17)* |

### Bees
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Swarms | one per beehive | the same, plus **ambushes**: as every day and every night begins, each scout has a **50%** chance of one at a moment of their own within it (at random, at least 30 s from anyone else's, so never two at once), if they are in the Tropics then; the swarm appears 2–3 m away, angry, and chases that scout for at least 20 s however far they run (then up to 120 m from where it appeared); and **20 swarms without hives** hover over the peak's grassy hill (8 m apart), calm until a scout comes near, then angry like an ambush swarm, dispersing once they have lost you *(17)* |
| Poison | calm 3/s, angry 6/s, +5 when you enter the swarm (1 s cooldown) | **calm 10/s, angry 15/s**; +7.5 on entering (the game's 5 × 1.75 *(14)*) |
| Sting radius | 1.3 m; stops once your bar is full | **2 m**; same |
| Aggro / lose interest | attack within 5 m (7 m angry); give up beyond 6 m (7 m angry); never more than 20 m from the hive | the same distances to the scout, but a swarm chases up to **120 m** from its hive (an ambush swarm from where it appeared) |
| Anger | 8 s after the hive is grabbed, 12 s after it is destroyed | **50 s** (hive grabbed, hive destroyed, or an ambush) |
| Measured | — | an ambush on a scout with 70 stamina: bar full **3.1 s** after the first sting, **dead 8.1 s** after it |

## Tropics and Roots

### Snakes (Tropics)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | none | the Tropics, whatever the map *(every ascent; below 17 half as strong and chasing a third slower)*: **30 ground snakes**, **seven in ten of them on the climb itself** (the cliff up to the Tropics' campfire, spread all the way up it), the rest on the jungle floor, each where a scout can stand, on the ground or a rock of the cliff (never a tree or its crown) with plants near (a bush, weed, monstera, ivy, palm or small tree within 6 m) or among the game's grass blades, and **10 tree snakes** (a third as many) in the giant trees; they never leave the Tropics (a snake stops where their ground ends and gives up a scout who goes beyond it) |
| Tree snake (about 3.6 m, 28 cm thick) | — | sleeps curled up in a ball on a branch; when a scout comes within **5 m** it uncoils (0.8 s), strikes and chases at **3.2 m/s**; once the scout is **20 m** away it climbs back up its tree, winding round the trunk; a bite: **30 injury**, then for **2 minutes** a **4 s** collapse every **15–30 s** (the first 5–15 s after the bite; another bite starts the 2 minutes over, it does not stack); a poison drop above the stamina bar drains over those 2 minutes, and a First Aid Kit, Cure-All, Blowgun dart, Napberry, Medicinal Root or Remedy Fungus cloud ends it *(17)* |
| Ground snake (about 1.4 m, 11 cm thick) | — | never sleeps, slithers around within **40 m** of its spot (and 10 m above or below it) at **0.5 m/s**, up and down the cliffs too, winding along the rock (at most **1.5 m/s** there; it strikes only once off the rock, and a hit knocks it off); chases a scout it sees (as near as a scorpion notices one) at **3.6 m/s**, up to 20 m (a scout walks 2.5 m/s and sprints 5.2 m/s); a bite: **10 injury**, then **40 poison over 10 s** *(17)* |
| Attack | — | once it goes for a scout it keeps after them for at least **10 s**, however far they run (only then does 20 m make it give up); from 2.5 m it rears up (0.3 s) and jumps at the scout's legs, mouth open (bites within 1.5 m); then for **3 s** it slithers round the scout 3.5 m out, then goes for another scout within its noticing range if there is one, or the same scout again *(17)* |
| Fending off | — | cannot be picked up; a Stick (4 s), a Torch (2 s), a Stone thrown at 10 m/s or more (2 s) or a kick (1 s) knocks it back 1.5 m, and it curls up for that long; then it goes for the scout who stunned it (the first, if more did), when within its noticing range *(17)* |

### Waterfalls
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Falling water | hitboxes 6 m across and 9 m long, one after another down each stream; while any part of you is in one: knocked down again every physics tick, each part of you shoved away from the hitbox's anchor (the harder the further) and braked to 80% speed every tick, so you are held fast or flung | knocked down when it hits you (0.5 s), and meanwhile washed out of it at **6 m/s**, sideways where there is room (and down it at 2 m/s), else down the stream, pushed only as hard as that takes; still in it when you get up, knocked down again *(every ascent)* |
| Splash at the foot | about 11 m wide and tall: stepping anywhere into it knocks you down for 0.5 s and throws you 500 sideways (0.7-1.3×) | only within **3 m** of where the water lands, thrown **250** sideways *(every ascent)* |

### Exploding mushrooms (poison mines, bombs, spore mines)
Each mushroom bursts once, when a scout touches it or an item hits it at 5 m/s or more (100%).
The burst weakens with distance and does nothing below 20% strength. Poison and spore mines then
leave a cloud that bursts again every 0.5 s for 11 s, with the same falloff.

| | Game | Ultimate Ascent at 24 |
|---|---|---|
| **Poison mines**: Tropics only (`Jungle_SporeMushroom`, ≈100 per map) | 20 poison × falloff within 9.6 m (half strength at 7.5 m), no knockback; then 2.5 × falloff every 0.5 s for 11 s | **up to 50 poison**; then **up to 5 every 0.5 s** (10/s at the centre) for 11 s |
| **Bombs**: Tropics (≈100 per map) and Roots (≈130–175 per map, caves included) (`Jungle_SporeMushroomExplo`) | no status; knockback 1300 × falloff, knocked down 1 s × falloff, within 4.8 m (strength falls off linearly over 6 m) | knockback ×3–4, knocked down 3 s *(17)* |
| **Spore mines**: Roots only (`Forest_SporeFungus`, ≈240 per map, caves included) | 20 spores × falloff within 9.6 m; then 2.5 × falloff every 0.5 s for 11 s | **up to 45 spores**; then **up to 5 every 0.5 s** for 11 s |

## Roots

### Spore clouds
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Spores | 2.25/s within 11 m (only the nearest cloud counts), with a warning; ≈1.7/s on the bar after the 2.5-point steps | emitters ×4 *(17)*, spores ×1.65 *(14)* → ≈15/s nominal, **≈10/s** on the bar |
| Wind | paused while the Roots wind blows (+2 s) | same |

### Spiders
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Trigger | drop on you when you pass under them (up to 50 m below, 2 m wide) | same |
| Grab | Web +25 on the bar, knocked down 1 s, pulled up | **Web +40**, knocked down **1.5 s** |
| Poison | 5 every 0.8 s, starting 2 s after the grab | **12 every 0.8 s** (on the bar 10, 12.5, 12.5, 12.5, 12.5, …: 12 on average, 15/s) |
| Escape | each jump −5 web (5 jumps), only while you have stamina (each jump costs 5%, 7% from the game's ascent 6, and it does not come back while you keep jumping); a jump at most every 0.4 s, earlier presses lost; a teammate frees you | 8 jumps, **without stamina**, an early press counts as soon as the next jump can *(9)*; a teammate; or a **rescue grapple** that catches you pulls you out of the web |

### Mushroom zombies
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Spawn | each zombie spot keeps its zombie with 33%; **1 zombie** awake on the map at a time | **up to 12** at a time |
| Waking / chasing | wakes when you look at it within 20 m; chases from 10 m; sprints after 3 s within 10 m; lunges within 4 m | wakes from **30 m**, **chases at once** and **runs** all the way, as when it lunges |
| Bite | 10 injury, 10 spores + 0.5/s for 60 s (30), stunned 3 s | spores ×1.65 (bite 15 on the bar, then ≈0.8/s), stunned **4 s** |
| Lifetime | dies after 120 s awake | **300 s** |
| Ritual Dagger | can't be used on a zombie | stabbed in the back (as a teammate must be), a zombie dies after **8** stabs *(15)* (5 below 15, on every ascent) and leaves a skeleton; nobody gets anything, and the dagger is kept |
| At the peak | none | **10 zombies dressed as scouts** on the level ground of the peak's grassy hill (not the summit with the flares), each appearing asleep as a scout comes near, with the same rules *(17)*; one the game took away with nobody near comes back, a dead one does not |

### Beetles (root bugs)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | on the Roots walls | same |
| Attack | notices you within 14 m, bonks within 1.7 m after a 0.75 s wind-up, then rests 6 s; after a bonk every beetle leaves that scout alone for 4–6 s | same |
| Bonk | push 125 forward and 225 up (full strength within 0.4 m of the hit, fading out at 4 m), knocked down 1 s, no damage | push **1,250** forward (225 up), fading out at **6 m**, knocked down **10 s**, **10 injury** *(17)* |
| Fall damage after the throw | ×2 | ×2, and 60 drowsiness *(12)*: with the bonk's 10 injury and your 20 curse, a fall that hurts for 10 or more → **fatal** *(24)* |

### Bouncy mushrooms
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | the flat tops of the Roots' mushroom trees: red ones, and purple (spore) ones | same |
| Bounce | the farther you fall onto one, the higher it throws you (up to 7.5×); a bounce never causes fall damage; an open parasol stops the boost | same |
| Stamina per bounce | +10 | **+5** (red), **+2.5** (purple) *(17)* |
| Purple ones | 5 spores per touch, at most once a second per mushroom | ×2 *(17)*, ×1.65 *(14)* → **15 spores per touch** |

### Shroomberries (the Roots' mushrooms to eat)
Each of the five colours gets one random effect per run (at least one good and one bad colour) and a random
amount of extra stamina; most effects start 3 s after eating.

| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Eating one | 10 hunger, 5 spores, the colour's extra stamina and effect | **5 hunger** *(15)*, **15 spores** *(14)*; the extra stamina and the pick of effects are the game's |
| Good: infinite stamina | 4 s | same |
| Good: faster | walking +50%, climbing +150% for 5 s | walking +50%, climbing **+70%** (ascent 15's energy-drink rule) |
| Good: low gravity | 15 s | **8 s** *(15)* |
| Good: invincible | 10 s | **5 s**, blocking only 75% *(15)* |
| Good: healing | 15 hunger, 15 injury and 15 poison, and poison effects stop | **7.5** each *(15)*; poison effects stop |
| Bad: spores | +25 | **+30** *(15)* |
| Bad: blind | 60 s | **120 s** *(15)* |
| Bad: knocked down | 8 s | **12 s** *(15)* |
| Bad: numb | 60 s | **120 s** *(15)* |
| Bad: spore burst | the other scouts within 4.8 m are thrown back (1,300 × falloff) and knocked down 1 s × falloff; the eater only floats up | within **10 m**, knocked down **4 s** × falloff *(15)*; not an explosion, so no knock-out at 17 |

## Alpine

### Mountain goats
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | none | the Alps, up to **15**, alone or in pairs, along the whole way up (from where the Alps begin to the top of their climb), **seven in ten on the climb itself** (the Alps' cliffs up to their campfire, spread all the way up them), the rest on the snowfield *(every ascent; below 17 half as strong)* |
| Getting about | — | graze, look about and roam within **150 m** of their spot, up and down the cliffs (a ledge above or below, the foot of the cliff), never leaving the Alps (a goat stops where their ground ends, and does not charge a scout beyond it); walk slopes up to **80°** (climbing, the whole body along the cliff, the hooves on the rock) and leap from foothold to foothold, up to **3.5 m** up, **6 m** down and **8 m** forward, across gaps (the Roots' beetles crawl on surfaces up to 70°, but never leap); solid: scouts bump into them *(17)* |
| Charge | — | a scout within **20 m** and in sight: the goat decides at once, then every **20 s** while one is; three times in four it goes on grazing, the fourth it bleats, lowers its head and paws the ground for **1 s**, then charges at **7 m/s** (a scout walks 2.5 m/s and sprints 5.2 m/s), turning after the scout, but straight on in its last **5 m**: step aside then and it misses, skids on and shakes its head; it never runs off an edge, and gives up after 4 s or 30 m *(17)* |
| Ram | — | knocked down **2 s**, thrown **6–8 m** along the charge, **20 injury** *(17)* |
| Sounds | — | a bleat every 25–70 s (while a scout is within 60 m) and always right before a charge; hooves while they move; a ram lands with the Roots' beetles' hit, heavier and louder *(17)* |

### Geysers
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Trigger | **33%** chance each time someone steps on it | **80%** |
| Burst (within 4 m) | heat 30 × falloff (at least 30% strength), knockback 1500 × falloff, knocked down 1 s × falloff | heat ×2 ×1.5 → **up to 90**, knockback ×3–4, knocked down 3 s *(17)* |
| Warm spot | −5 cold/s within 3 m | −5 cold/s within **2 m** *(17)* |

### Ice rocks
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Touching (standing on one, bumping into one) | 1 cold per touch, at most once every 0.7 s; on the bar that is 2.5 every third touch (≈1.2/s) | 2 *(17)* × 1.3 *(14)* → **2.5 per touch** (≈3.6/s) |
| Climbing one | 1 cold every 0.3 s; on the bar 2.5 every 0.9 s (≈2.8/s) | 2 *(17)* × 1.3 *(14)* → **2.5 every 0.3 s** (≈8.3/s) |
| Time to a full bar | from the starting 10 curse: ≈32 s of climbing, ≈75 s standing on one | from the starting 20 curse: **≈10 s of climbing**, ≈22 s standing on one; 5 s later you pass out, which is **death** *(24)* |
| What cold does | fills the bar like any status | also slows walking and climbing, up to 35% slower at a full cold bar *(22)* |

### Napberry bushes (sleepy berries)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | ≈6 bushes per Alpine map | same |
| Hypnosis | while the berry on the bush is on your screen within 12 m and you are awake, it pulses twice every 3 s: 5 drowsiness per pulse at 12 m, up to 10 from 2 m or closer when you look straight at it | pulses **once a second**; 5 at 12 m, **12.5** up close (×1.3 *(14)*, which the 2.5-point steps drop at range) |
| Time to a full bar | from the starting 10 curse: ≈13 s up close, ≈26 s at 12 m; after the 5 s faint timer you fall asleep | from the starting 20 curse: **≈7 s up close**, ≈16 s at 12 m; 5 s later you pass out, which is **death** *(24)* |
| Stopping it | pick the berry, look away or move beyond 12 m | same |
| The berry as an item | can only be fed to another scout: clears all their statuses except curse, weight and crab, then +100 drowsiness, so they sleep it off | clears at most 50 of each status *(15)*, then **+200 drowsiness** *(14)*: that fills the bar on its own, so they pass out, which is **death** *(24)* |

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
| Thorns | pop out after 30 s solo / 120 s in multiplayer, or pull them out | thorns **in the face** pop out as in the game; thorns **on the body never pop out**, solo too: pull them out *(17)* |
| Cactus ball | +20 on the bar while one is stuck to you or in your hand | **+40**, held too |

### Scorpions
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Sting | within 1.5 m, every 3 s (a 2 s rest, then a 1 s wind-up), knockback 500 | within **1.7 m**, **every second** (the wind-up only) |
| Poison | 2.5, then poison over **15 s** adding enough to fill your bar (at least 50) | over **7.5 s** *(17)*, ×1.75 *(14)* → **bar full ≈4 s after a sting** → dead ≈9 s after it unless cured; more stings stack |
| Eating one | only once cooked: 20 hunger and 10 extra stamina, no poison | 10 hunger and 5 stamina *(15)*, **15 poison** *(15)*; but every way to cook it (campfire, portable stove, lava, explosion) chars it to ruin *(17)*, so at 24 a scorpion cannot be eaten |

### Dynamite
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | Mesa mineshafts; also an item (Legendary rarity) | same |
| Fuse | lights when you come within 5 m; 5 s | lights within **6 m**; **3 s** |
| In your hand when it blows | +25 injury | **+100** |
| Explosion (within 12 m, line of sight) | injury 30 × falloff, knockback 1600 × falloff, knocked down 1 s × falloff | injury ×2 (**up to 60**), knockback ×3–4, knocked down 3 s *(17)* |

### Tumbleweeds
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | spawners across the Mesa each send one every 5–30 s (mostly 20–30 s); a tumbleweed is about 2.7 m across and shrinks away after 15 s | same |
| Rolling | toward the nearest scout inside its cone (50–180° around one fixed direction, mostly about 50°), otherwise straight on; rolling force 5–10, random per tumbleweed | rolling force **×1.15**; the cone **10–240°** (mostly about 10°; from 180° every direction) *(17)* |
| Hit | power = speed × 0.05 (less while it shrinks), at most 1; from 0.2 power (4 m/s): knocked down 2 s × power, pushed 1000 × power, **1 thorn** (5 on the bar), 2 above 0.6 power; each scout at most once a second per tumbleweed | power **×1.5** (from 2.7 m/s, full power at 13.3 m/s, 3 thorns from 8 m/s) *(17)*: knocked down up to 2 s, **2 thorns**, 3 above 0.6 power; thorns on the body never pop out by themselves (in the face: the game's times) *(17)*; a push into a fall → fall damage → 60 drowsiness *(12)*: with 2 thorns (10) and your 20 curse, a fall that hurts for 10 or more → **fatal** *(24)* |

## Caldera

### Burning rocks (eruptions)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Frequency | while a scout is in the Caldera: every −5 to 15 s (a negative roll erupts again at once), anywhere ±155 × ±140 m | every −5 to 12 s, on the ground or the lava's surface where they land *(17)* (the game puts them all at 780 m, the lava's lowest level, so their heat never reached the ground around them) |
| The last stretch (the Caldera's statue → the campfire that opens the Kiln) | only the Caldera's eruptions above | **every 2–6 s** while a scout is on it, landing all over it, each away from the last few *(17)*; none within **20 m** of that campfire or the statue (the Caldera's other eruptions keep off too) |
| Each eruption | burns 35 s; heat zones on the column at 0, 15 and 27 m up, each a pair: a mild one (1/s) that grows to 15 m over the first 7 s and stays, and a strong one (15/s) that only grows to 15 m from about 20 s, holds 23–28 s and shrinks away by 31 s | each zone ×4 *(17)*, ×1.5 *(14)*, measured 4 m from the column: nothing for the first 3 s, then **6 heat/s** for each mild zone reaching you (usually 1–2) until about 21 s, then at its height (the strong zones: the ground one within 15 m, the one 15 m up within about 5 m of the column) **20 heat/s** however many reach you *(17)*, from about 21 to 30 s |

### Condors
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Speed | circle their nest, dive at a scout below and carry them off towards the nest for up to 5 s | **1.35× as fast**: circling, diving, carrying and flying back *(17)* |

### Lava (the lava river) and lava flows
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Heat above the river | up to 1.2 every 0.1 s at the surface, fading over 10 m (≈8/s on the bar) | ×1.5 *(14)* (≈12/s) |
| Touching the river | 25 injury + 25 heat (at most 1/s), pushed up | **100 injury and 200 heat** → bar full at once → **fatal** *(24)* |
| River height | rises and falls 6.3 m (779 → 785.3) every 72 s: low for about 30 s, rising for about 20 s, about 10 s at the top, falling for about 13 s | **rises and falls 14 m (779 → 793) every 61 s** *(18)*: low for about 21 s, rising for about 16 s, about 16 s near the top, falling for about 9 s; above the game's highest level for 28 s of each cycle; the same for everyone |
| Lava flows (streams) | 20 injury + 50 heat per touch (1/s), knockback 1000 | **50 injury + 100 heat** → bar full → **fatal** |
| Lava pillars | knockback 1000 + 10 heat (cliffs 5) | heat ×1.5 |
| Magma rocks (Caldera and Kiln) | 2.5 heat per touch while standing on them (every 0.75 s); heat field 1/s within 11 m; climbing them does nothing extra | touch **7.5** *(17, 14)*; field ×4 ×1.5 = 6/s nominal, **5/s** on the bar; **climbing them burns 2.5 heat every 0.3 s** (≈8.3/s), on top of touching, like ice rocks' cold |

## Kiln

| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Rising lava | starts after 180 s, reaches the top in 1,200 s | starts after **30 s**, rises in **700 s** *(9)* |
| Heat above the rising lava | up to 1.5 every 0.25 s, fading over 10 m (≈5/s on the bar) | ×1.5 *(14)*, lost to the 2.5-point steps (still ≈5/s) |
| Touching the lava | 25 injury + 25 heat (at most 1/s), pushed up | **100 injury and 200 heat** → **fatal** *(24)* |
| Burning (magma) rocks | as in the Caldera | as in the Caldera |
| Heat recovery | 1.3 a second, after 8 s | **0.5 a second** *(16)* |
| Hot ash | — | once a minute once the lava rises: 5 s of rumbling as the air thickens, then **3 heat a second** for 15 s (**45** in all) *(18)*, everywhere in the Kiln below its top (where the lava stops rising: it shows there but does not burn); within **3 m of a petrifying stone** it neither shows nor burns |
| Eruptions | none | **once a minute** a column of fire (the Caldera's eruption: heat ×4 ×1.5, the bar fills on the first tick) breaks out 18–32 m from one of the scouts *(17)*, taking turns with the hot ash, never with it, at most 10 s apart: 2–9.5 s after the ash clears, burning 30 s |

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
| Spawn | every 60–180 s (counted while no ghost is out) near a scout who is alone (nobody within 15 m), 30 m ahead (±10) | **every 30 s**, for the leading scout **alone or in a group** |
| Attack | rolls toward the nearest scout; arms within 1 m, explodes 2.1 s later (get 1.3 m away within 1.5 s to stop it); lives 30 s | explodes **1.5 s** after arming (no way back after ≈1.1 s); lives **50 s** |
| Defence | lit bell towers (11 m) repel it and burn it (3 s); a lit Faerie Lantern (7 m) the same; the Lantern and the Torch do nothing | lit bell towers and Faerie Lanterns push it back until it is out of blast range of the scouts they protect (their reach + 10 m), and **do not burn it**; it cannot arm while pushed. A **burning Torch** in a scout's hands does the same within 4.5 m of it *(9)* |
| Explosion (within 10 m) | **blind 20 s**, cold 35, knockback 1600 (tilted up), knocked down 4 s, landing damage capped at 7.5 for 8 s | **blind 50 s**, **60 cold**, knockback ×3–4, knocked down 12 s **and drowsiness up to passing out** → **fatal** *(24)* |

### Carnivorous plants (Venus fly traps)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Bite | catches an arm or leg: FlyTrap +30 on the bar, knocked down 0.5 s, pulled in | catches **any body part**: **+45**, knocked down **0.75 s** |
| Poison while held | 5 every second | **10 every second** |
| Escape | each jump −2.5 (12 jumps), only while you have stamina (as for webs); a teammate frees you | **18 jumps**, **without stamina**, early presses kept *(9)* (about 7 s of pressing) |
| Trap cooldown | 2 s | same |

### Sleep fog
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Gloom fog | 0.9 drowsiness/s (lit bell towers protect, and so does a lit Portable Stove within 7 m — but because of a game bug, for the scouts present when it was placed only after it burned out, and then for good) | **2.5 drowsiness/s** *(18)* (×1.3 *(14)* below 18); drowsiness heals at 1 a second in the Gloom (game: 2.5) *(16)*, at night too (there is no night here); the stove bug is fixed: a stove protects while it burns, **30 s** *(15)*, and not after |
| Early Worm (eaten) | removes 100 drowsiness, 5 hunger | removes **50** *(15)*, 2.5 hunger *(15)* |
| Early Worm (lying about) | stays where it lies | seen lying close by (within 15 m, in view), it brings a **raven** three times in four: it flies in, lands beside it and eats it *(17)* |

### Frogs
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Tongue | licks a scout in front of it (within 18 m, a 14° cone, line of sight; closer than 10 m it hops away instead), knocks them down 1 s and pulls them toward it for up to 1 s (until 5 m away); a frog licks at most every 12 s, and a scout is licked at most every 12 s | same, and when it lets go the pulled scout gets **10 poison** *(17)* |

## Citadel

### Arrow traps (also in the Gloom temple)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Shot | fires along its line up to 80 m at the first scout in it; 3 shots, 8 s apart, then empty for good; a swinging spike ball crossing its line sets it off too | **never runs out**, fires every **0.5 s**; spike balls no longer set it off *(9)* |
| Hit | 1 arrow = **7.5** on the bar (Arrow), knockback 30 | 1 arrow = **15**, knockback **60** |
| Pulling an arrow out | 12.5 injury | **25 injury** |

### Traps
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Spiked fences (Gloom days) | 2.5 injury per touch (at most 1/s) | **15** |
| Lava sphere (one per Gloom-day map) | heat up to 7.5 every 0.5 s within 3.9 m; touch 25 injury + 25 heat | heat ×1.5; touch **100 injury + 200 heat** → **fatal** |
| Floor spike traps (2,800) | step on → spikes ready after 0.3 s → hit 0.8 s after the step, within 1.2 m: **15 injury**, knockback 300, knocked down 0.2 s | the game builds this hit as an explosion: **30 injury**, knockback ×3–4, knocked down 0.6 s, the hit **0.53 s after the step** *(17)* |
| Wall saws (180) | run back and forth along their wall; first touch 15 injury (after 2 s without a hit), then 2.5 every 0.25 s while touching (10/s) | run **1.5× as fast**; first touch **30**, then the same |
| Central Tower saw | nothing: its damage is left empty in the game data | **7.5 per second** |
| Spike rollers (32) | sweep round at 15, 30 or 35° a second, their rollers turning on their axles; 5 injury per contact (up to 4/s), pushed away | sweep round **1.5× as fast** (the rollers keep the game's pace); **10** per contact |
| Ladders (69) | 3× as fast as climbing rock, 25% of its stamina | **1.2×** as fast, **90%** of its stamina |
| Swinging spike balls (156) | swing back and forth every 4.2 s; up to 25 injury (by swing speed), knockback up to 500, knocked down up to 0.2 s, 1 s cooldown | swing **1.5× as fast**; up to **60** |

### Mimic chests (trick luggage)
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | about one per Citadel map (Central Tower), with a chest of its own (it shows) | the same, looking like an **ordinary chest**, plus **35% of the ordinary chests in the Citadel**, picked anew each climb (they keep their loot, like the game's mimics) |
| Opening | releases sleeping gas for 10 s within 10 m: 5 drowsiness every 0.5 s (10/s) → 100 in 10 s | gas with **5 drowsiness + 5 poison + 5 spores every 0.5 s** (30/s in all) for 10 s; opening any suitcase also hurts for **10 injury** *(21)*; staying in the gas = passing out = **death** *(24)* |
| Rising gloom | starts after 150 s, rises in 1,200 s | starts after **30 s**, reaches the top in **700 s**, as the Kiln's lava *(9)* |

## Nadir

| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Healing | drowsiness, poison, heat and spores heal at their usual rates; blindness and numbness wear off as usual | all of them **half as fast** *(17)* |
| Gravity | as anywhere | **twice as strong** for the scouts, in the air and lying limp *(18)* |

## Anywhere

### The Scoutmaster
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Needs | at least 2 scouts | same; a Looker can send him after anyone, solo too |
| Picking a target | every 30 s a 10% chance: the highest scout, if more than 100 m above the next one and below 825 m | **also**: the host checks every 5 s and makes him hunt a scout who is 150 m ahead of someone with nobody within **35 m**, for **70 s** (a scout coming within 35 m calls him off) *(11, 17)* |
| Giving up | when another scout is within 15 m of his target, or the target is no longer the highest by 80 m | another scout within **35 m**, on every ascent |
| Chase | teleports to 50–70 m from the target when farther than 80 m (unseen, every 5 s+); grabs within 3 m | same |
| Throw | holds you 3.2 s, throws you toward the biggest drop (force 1500 × 0.7–1.0), knocked down 1 s, **30 injury** | same, plus **4 curse** per throw *(11)*; the fall damage after the throw makes you 60 drowsy *(12)*: with the throw's 30 injury and your 20 curse → **fatal** *(24)*; a death curses the survivors *(11)*: **10 / 7.5 / 2.5** with ≤2 / ≤4 / ≤6 scouts (the rule asks for 12/8/4; the bar drops the rest), and in bigger lobbies the 2 it asks for only lands, as 2.5, with that scout's next curse |

### The Looker
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Where | 20 hiding spots per map; in 5% of runs one of them shows a Looker | **every spot** shows one with **90%** (≈18 per map) |
| Watching it | it peeks out now and then and ducks away 6× sooner while watched; harmless | while it peeks out in the middle of your view within 40 m (nothing in between): **2.5 curse a second**, and a lollipop's **sugar rush** (infinite stamina) for 5 s, then not again for 10 s; not looking costs nothing |
| Leaving | for good, once the host has watched it for 3 s or anyone comes within 5 m | after **5 s** of your watching it leaves and **sends the Scoutmaster after you for 70 s**; within 5 m it just leaves |

### Clown chests
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Opening | clown loot | a trick instead of the 10 injury other suitcases deal *(21)*: **45 drowsiness, poison, spores, web or fly-trap grip, or 20 petrification** (each 2 in 13), or **100 s of blindness** (1 in 13); web and grip come off by jumping or a friend's help, as in the game |

### Bing Bong's evil force
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Warning | — | 10 s without anyone holding Bing Bong: he talks, line after line, in the voice he has once burned, heard by every scout; silent as soon as someone holds him, or when the crab comes *(20)* |
| Neglect | — | 15 s without anyone holding Bing Bong: 5 crab a second *(20)*, which only the **Cursed Skull** cures *(15)*; it leaves 10%, like everything else it clears. Cure-alls don't touch crab, and Pandora's Lunchbox can even add some *(15)* |

### Banana peels
| | Game | Ultimate Ascent at 24 |
|---|---|---|
| Slip | 2 s | **10 s**, launched 3× higher (like jellyfish); 3 s later your screen **darkens like the Scoutmaster's** and every sound is **muffled** (as in TFA's Ice Fog: sound effects dull and quieter, no music, voices duller) for 20 s (no Scoutmaster) |

## Food

### Poisonous food
The poisonous Button, Cluster and Bugle Shrooms look like the safe ones of the same name.

| Food | Game | Ultimate Ascent at 24 |
|---|---|---|
| Button Shroom (poisonous) | 10 hunger; after 10 s, 30 poison over 12 s | 5 hunger *(15)*; after 10 s, **150 poison** over 12 s *(14)* |
| Cluster Shroom (poisonous) | 5 hunger; after 10 s, 40 poison over 16 s | 2.5 hunger; after 10 s, **75** over 16 s |
| Bugle Shroom (poisonous) | 15 hunger; after 10 s, 20 poison over 8 s | 7.5 hunger; after 10 s, **120** over 8 s |
| Green Crispberry | 5 hunger; after 2 s, 10 poison over 4 s | 2.5 hunger; after 2 s, **40** over 4 s |
| Yellow Kingberry | 30 hunger; after 3 s, 25 poison over 10 s | 15 hunger; after 3 s, **150** over 10 s |
| Pink Berrynana | 15 hunger; after 2 s, 15 poison over 6 s | 7.5 hunger; after 2 s, **100** over 6 s |
| Green Kingberry | 30 hunger, 5 poison at once | 15 hunger, **35 poison at once** *(14)* |
| Scorchberry | 10 hunger, 25 heat at once | 5 hunger, **80 heat at once** *(14)* |
| Egg (Caldera, from a Big Egg in a nest) | 15 hunger raw, 30 cooked; no poison | 7.5 hunger *(15)*; eaten raw **5 poison** at once *(15)*; any cooking chars it to ruin *(17)*, so at 24 it is always eaten raw |
| Egg (Gloom, from a raven's Small Egg) | 7.5 hunger raw, 15 cooked; no poison | 2.5 hunger (half is 3.75; the bar drops the rest) *(15)*; raw **5 poison** *(15)*; same cooking as above |
| Several meals | one poison effect at a time: a second meal only adds time at the first one's rate; natural poison or spore recovery also cancels it | every meal runs on its own and lands its whole total *(14)*; only a cure (an antidote, the healing Shroomberry, a small poison or spore cure) or death stops it |
| Result | — | with 80 of the bar left after the starting curse, the four with 100 or more fill it alone → pass out → **death** *(24)* unless cured in time |

## Scout statue gems
The four gems from the scout statues can be used again and again while your petrification is under 100.
Petrification heals only 10 per campfire lighting *(17)*, and revives keep it *(19)*, a checkpoint flag's save too *(15)*.

| Gem | Game | Ultimate Ascent at 24 |
|---|---|---|
| Scout's Initiative (red) | you and every scout within 8 m make a super jump, then 3 s of low gravity; **7** petrification per use | within **6 m**, **2 s** of low gravity; **15** petrification *(15)* |
| Scout's Ambition (yellow) | infinite stamina for 8 s for every scout within 6 m; **30** petrification | **6 s**; **35** petrification *(15)* |
| Scout's Tenacity (blue) | heals up to 60 (injury, spores, poison, cold, heat, drowsiness, in that order) and 10 s of invincibility; costs half the healing in petrification, 10 to 30; works on a teammate (the user pays) | heals up to **40**, invincible **8 s** (blocking only 75%); costs 1.5× the healing, **20 to 60** *(15)* |
| Scout's Generosity (green) | clones the item you aim at within 8 m; **25** petrification, **45** for a mystical item | **35**, **60** for a mystical item *(15)*; the Warp Compass and the Toxic Drink cost a mystical item's price (from ascent 9 on) |

## Not on the list, but deadly at 24
| Threat | Game | Ultimate Ascent at 24 |
|---|---|---|
| Thorny vines (Tropics) | 10 poison per touch (at most 1/s), knockback 1000 that can cause fall damage | **60 poison per touch**, knockback **1500**; a fall from the knockback → fatal *(12, 24)* |
| Poison ivy (Tropics) | 3.5 poison per touch (2.5 on the bar), at most 1/s | at least 15 *(17)* × 1.75 → **25 per touch** |
| Mandrake (pulled out of the ground) | while anyone awake is within 10 m it screams for 3 s every 0.75–2 s: 5 drowsiness × falloff 8 times per scream (40 up close), only for players whose own sound is on; eaten raw: 10 hunger, numb 60 s; cooked: no scream, −25 curse | up to **75 per scream** *(17)* (the game's 40 at 14–16); players whose own sound is off get half, up to **37.5** *(17)* (none at 14–16, as in the game); eaten raw: 5 hunger, numb 60 s and **30 spores** *(15)*; cooked: −15 curse, but every way to cook it chars it to ruin *(17)* |
| Snowballs (Alpine) | holding one does nothing; thrown (or rolled) into a scout fast enough, it bonks them: knocked down 1 s and pushed | holding one chills **2.5/s** and it melts after **20 s** in the hands *(15)*; a bonk gives **30 cold** and knocks down **2 s** *(15)* |
| A teammate's Stone (beach, Tropics, Roots) | cut from the game | lies on the ground from ascent 9; hitting a scout at **10 m/s** or more (a throw charged a third of the way, or a fall from 5 m) knocks them down **2 s** and injures **20** *(17)*; it shatters on any impact that hard |
| A teammate's Stick (beach, Tropics, Roots) | cut from the game | lies on the ground from ascent 9; a swing (33% of the swinger's stamina) knocks down every scout and zombie a kick would reach (**2.5 m**, 55° ahead) for **4 s**, without injury |
| A teammate's Torch (the Mesa's Tomb; suitcases wherever the Lantern is) | a light: 2.5 cold a second off the scouts within 4.5 m while lit, no attack | lit, a swing (right mouse, 33% of the swinger's stamina, 4 s of the Torch's fuel) knocks down every scout and zombie a kick would reach (**2.5 m**, 55° ahead) for **2 s** and burns the scouts for **20** *(15)*; it warms **2 cold a second** *(15)* |
| A teammate's Blowgun | the dart clears your statuses (except curse), then +120 drowsiness: with the starting 10 curse you fall asleep | the clear keeps 25% of each status ("Nerfed Revives"; not a revive statue's) *(15)*, then **155 drowsiness** (120 × 1.3 *(14)*): with 20 curse that is 175 → pass out → **death** *(24)* |
| The sun | only the Mesa's sun is hot; sunscreen, an open parasol or a glider held overhead keep it off | hot everywhere except the Alpine, up to the Caldera and the Gloom *(23)*: outside the Mesa **0.5 heat/s** in direct sunlight (ascent 14's boost leaves the sun alone), the Mesa's midday sun burns **6 heat/s** *(18)* (the game's ×1.5 *(14)* below 18); whatever you shield yourself with roasts: clean for 5 s, heavy smoke for 15 s, then burned, and a burned item crumbles away after 5 s more (parasols 1.5× as long, the Mesa's sun twice as fast) — in the Mesa's midday sun from *(18)*, everywhere *(23)*; **sunscreen lets a quarter of the heat through** *(15)* |
