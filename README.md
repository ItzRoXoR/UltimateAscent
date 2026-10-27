# Ultimate Ascent

Seventeen extra ascents for **PEAK** (ascents **9–25**) on the game's own boarding pass. They combine
**13 Downright Dastardly Ascents** (pooblives, via the *Continued* port by Memnant) and
**More Ascents** (asdfmovie402), add a few new rules, and work together with **True Final Ascent**.

Built and checked against **PEAK 3.0.0** (Unity 6000.3.15f1) with BepInEx 5.4.23.

Ascents are cumulative, like the vanilla ones: ascent *N* contains every vanilla ascent and every rule
of ascents 9 to *N*.

## The ascents

| Ascent | Source | Rule |
|---|---|---|
| **9** | 13DDA (trimmed) | The fog moves **2× as fast** and starts after **20 s** instead of waiting for you to move on (on the beach too), **until the Caldera or the Gloom**; from there on the fog keeps the game's pace (with True Final Ascent's FINAL ASCENT: until its Caldera). The **Kiln's lava** and the **Citadel's gloom** start after **30 s** and reach the top in **700 s**; the **Nadir's ghosts** rise **2.5× as fast**. |
| **10** | More Ascents + new | **Big and Explorer's luggage** have a **50% chance to hold only one item**. |
| **11** | 13DDA | **Rule zero is stricter.** A scout who is 150 m ahead of someone and has nobody within 35 m is hunted by the Scoutmaster for 30 s (70 s from ascent 17; a scout coming within 35 m calls him off). Every death curses the survivors (12/8/4/2 curse for ≤2/≤4/≤6/more players), being thrown adds 4 curse. **Eating a scout** poisons you for 50 (28.6 below ascent 14, without its boost) and curses you for 25 (game: 10 curse). |
| **12** | 13DDA + More Ascents | **Falling and helping make you drowsy.** Fall damage makes you **60 drowsy** (46.2 below ascent 14, without its boost): any fall the game hurts you for, a landing or a slide down a wall into a ledge, however you came to fall (jumping, a jetpack, a glider, balloons, a wall or tree you grabbed, True Final Ascent's Apocalypse events); other damage (suitcases, traps, mimics) never counts. Pulling someone up makes you **10 drowsy a second** (7.7 below ascent 14, without its boost). Switching items mid-air no longer cancels a fall. |
| **13** | More Ascents | **Butter fingers.** Taking fall damage (the same falls as at 12) drops every item, including your backpack. The dropped items pass through you until you are clear of them, so your tumbling body cannot fling them away (the game's own drop could). |
| **14** | 13DDA | **Ailments apply more:** cold and drowsiness ×1.3, heat ×1.5, **spores ×1.65**, poison ×1.75, on everything but the night's own cold and the scorching sun's own heat, which land as they come (the Blowgun's dart: 155 drowsiness instead of 120). Rules with exact numbers (ascent 17 and others) are tuned so that their numbers land on the bar exactly, this boost included; below 14 the same rules land them without the boost, so they are always lower there. **Poisonous food** lands exact totals over the game's own delay and duration: Button Shroom 150 poison, Cluster Shroom 75, Bugle Shroom 120, Green Crispberry 40, Yellow Kingberry 150, Pink Berrynana 100 (game: 30, 40, 20, 10, 25, 15); Green Kingberry 35 at once (5); Shroomberries 15 spores (5). Every meal lands in full; only a cure or death stops it. A fed **Napberry** gives 200 drowsiness (100), a **Scorchberry** 80 heat (25); the **Energy Drink**'s crash is 50 drowsiness (25). |
| **15** | 13DDA + new | **Items are worse.** Ailment amounts on this row are what lands on your bar, ascent 14's boost included. Food and extra stamina ×0.5, **eating a tick restores no hunger**, what items cure ×0.6 (what they cost you stays the game's), cure-alls remove at most half a bar, multi-use items lose 2 uses per use (the Climbing Chalk's three last two uses), rope ×0.75, **rope spools** hold half the rope (25 segments, at most 20 in one rope; game: 50/40), chain 30 m, the **Rope Cannon** and **Anti-Rope Cannon** reach 25 m (game: 30), the **Rescue Claw** reaches 20 m level or up and 30 m down (game: 30/40), weak balloons, a carried **Balloon** takes only 2% of the bar off your weight and a **Balloon Bunch** 8% (2.5% and 12.5%), slower speed boosts (the **Energy Drink** keeps the game's walking +50% and climbing +150%, but they last **6 s** (cooked **8 s**) from the moment you drink it, where the game holds its clock for the first 3 s unless you climb; other speed boosts walk at most +60% and climb at most +70%, the **Cherry Lollipop**'s walks +30%), the **Big Lollipop**'s crash is 85 drowsiness over 12 s (game: 33 over 10 s), **Fortified Milk** makes you invincible for 12 s, cooked 20 s (15 s, 25 s), cooking no longer removes poison or spores, **eating a scorpion costs 15 poison**, eating a **raw egg or tick** 5 poison (the Caldera's and the Gloom's eggs), the **Parasol** wears out: it slows your fall as in the game, but after **20 s of gliding** in all (open, in the air) it is used up, its use bar showing what is left — except the **Lightweight Parasol** (the Roots' parasol, renamed, recoloured dark blue, turquoise and mint with its own icon, weightless below ascent 15, from 15 weighing 5% of the bar like the Parasol; in custom runs weightless, with its own switch on the Custom Run screen; clown chests hold it too, 0.5% of what they pick), which never wears out — gliders use 1.5× stamina, letting go of a **Rescue Claw** makes you fall (you keep your items), the cannon burns (+50 heat) and the "cannon glitch" knocks everyone out, skeletons are fragile, invincibility only blocks 75%, revives keep 25% of your statuses (a revive statue clears them all and gives the game's revive curse and hunger, but takes no petrification off: game −75). A **Heat Pack** warms for 20 s (60), **sunscreen** keeps off only 75% of the sun's heat (all of it) and of ascent 18's snowstorm heat by day, the **Scoutmaster's Bugle** sends him after you for 180 s (120), a **Bounce Fungus** pad gives back 2.5 stamina (10), thrown **Shelf and Cloud Fungus** fly as if 1.5× as heavy and the **Warp Fungus** as if 1.35× (the throw is that much slower), a **Magic Bean**'s vine grows at most 15 m (20) and standing on it or climbing it sticks 2.5 thorns into you every 3 s (each 2.5 falls out after 30 s), placed **pitons** crack like the Alps' pitons but 30% sturdier (1.3–6.5 s of hanging, then five cracks). A **snowball** that bonks a scout gives 30 cold and knocks them down for 2 s (no cold, 1 s); holding one chills 2.5 a second, and it **melts after 20 s in your hands** (its use bar shows what is left; stashed or on the ground it keeps). A **Wonderberry** gives 5 s of infinite stamina (10), a **Green Clusterberry** removes 15 poison (30). The Gloom's **Early Worm** removes only 50 drowsiness (100). Whatever you feed the **Jetpack** gives 35% less fuel. A lit **Lantern** or **Torch** takes 2 cold a second off the scouts within 4.5 m (2.5), a lit **Faerie Lantern** 2.5 a second of each of cold, injury, poison, drowsiness and heat (5). A **Remedy Fungus** cloud lasts 8 s (14), the **Anti-Zooka**'s bubble is 8 m across and lasts 40 s (10 m, 60 s), a **Portable Stove** burns 30 s (60). The **Book of Bones** costs 35 curse net when it makes you a skeleton (25), and a teammate revived with it gets 20 curse (14). The **Ancient Idol** weighs half the bar. A raw **Mandrake** gives 30 spores; cooked it removes 15 curse (25). A **checkpoint flag** that saves you curses you for 10 and leaves you the petrification you have (game: back to what you had when you planted it). The **Cursed Skull** is the only cure for crab status (cure-alls such as the Book of Bones leave it) and keeps 10% of every status instead of 25% (game: none). The **Ritual Dagger** turns a teammate into a zombie instead of killing them, and a zombie takes **8** stabs to kill (5 below 15). The **Toxic Drink** makes you 3× as fast for 7 s, then poisons you for 8 a second for 5 s. A burning **Torch**'s hit burns for 20 (10). **Pandora's Lunchbox** can hand out crab status too, at half the share it rolls (as the game rolls its curse). **Scout statue gems** cost more and do less: Initiative 15 petrification per use (game: 7), reaches 6 m (8) and gives 2 s of low gravity (3); Ambition 6 s of infinite stamina (8) for 35 petrification (30); Tenacity heals up to 40 (60) and gives 8 s of invincibility (10) for 20–60 petrification (10–30); Generosity costs 35 petrification, 60 for a mystical item (25/45). **Shroomberries:** low gravity 8 s (14), invincibility 5 s (10), the healing one restores 7.5 hunger, injury and poison (15 each), +30 spores (+25), blind and numb 120 s (60), knocked down 12 s (8); the spore burst throws back the scouts within 10 m (4.8 m) and knocks them down for up to 4 s (1 s). |
| **16** | 13DDA + More Ascents | **Heat heals 2.6× slower** in the Caldera and the Kiln (0.5 a second; game: 1.3); **drowsiness heals 2.5× slower** in the Gloom and the Citadel (1 a second; game: 2.5). |
| **17** | 13DDA + new | **Hazards are more dangerous.** Numbers are what lands on your bar (ascent 14 included). *Beach:* jellyfish 20 poison per sting and a 10 s slip, 3× higher; sea urchins 40 poison. *Tropics:* Thorny vines 60 poison; urchins and vines push 1.5× harder. **Ticks** live only in the Tropics, on **35%** of the tick spots (game: 1%); up to **5** latch on like cacti where they touch you, are visible at once and swell to 3× size within a minute; each bites for **2.5 poison every 0.5 s until pulled off**, and your poison does not heal for **10 s** after a bite. **Bees** sting for **10 poison/s** (calm) or **15/s** (angry) within **2 m**, stay angry **50 s** and chase up to **120 m** from their hive; as every in-game day and every night begins, each scout has a **50%** chance of a **bee ambush** at a moment of their own within it (at random, at least 30 s from anyone else's, so never two at once; only if they are in the Tropics then) by an angry swarm that appears 2–3 m from them and chases them for at least 20 s however far they run, then up to 120 m from where it appeared. On **the peak's grassy hill** (after the Kiln or the Citadel, below the summit with the flares) **ten zombies dressed as scouts** wait asleep until a scout comes near (yellow in the fairy outfit and flower hat, or lime in a green tee with the sprout hat), and **twenty bee swarms without hives** hover over it; a swarm a scout comes near turns angry and chases them like an ambush swarm, then disperses once it has lost them. **Poison mines** up to 50 poison and **spore mines** (Roots) up to 45 spores, then their cloud adds up to 5 every 0.5 s for 11 s. *Roots:* beetles deal **10 injury**, hit 10× harder and knock you down for 10 s; the bouncy mushrooms give back 5 stamina per bounce (the purple ones 2.5, and 15 spores per touch). Spiders 12 poison every 0.8 s and a web of 40. Up to **12 mushroom zombies** wake at 30 m and chase at once, always running, stun 4 s, live 300 s. Awake zombies (the peak's too) **share out the scouts**: every scout gets one first, the rest go for the scout with the fewest after them (the nearest of those); a zombie keeps its scout for at least 10 s and only hunts within 100 m. *Alpine:* Geysers erupt on 80% of steps; their warm spot only reaches 2 m (game: 3 m). Napberries pulse every second. Flash plants blind up to **180 s** (less with distance). *Mesa:* cacti stick to any body part with two thorns; a cactus ball puts 40 on your bar while it sticks to you or you hold it (20); thorns in the face pop out at the game's times (30 s alone, 120 s with others); thorns on the body never pop out by themselves. Scorpions sting every second within 1.7 m and pump their poison in twice as fast. Dynamite lights within 6 m, has a 3 s fuse and deals 100 injury if it blows up in your hand. **Tumbleweeds** roll 15% faster, steer for scouts within a cone of 10–240° (game: 50–180°), hit 1.5× as hard (full power from 13.3 m/s) and stick one more thorn (2, or 3 on a strong hit). *Caldera and Kiln:* lava 100 injury + 200 heat; lava streams 50 injury + 100 heat; climbing magma rock burns 2.5 heat every 0.3 s (like ice rocks' cold); **condors** fly 1.35× as fast (circling, diving at you and carrying you off); eruptions more often and on the ground or the lava's surface where they land (the game's start at the lava's lowest level and never reached anyone), burning 20 heat a second at their height, and the **Caldera's last stretch is ablaze**: from its statue up to the campfire that opens the Kiln an eruption breaks out every 2–6 s while you are on it, all over the stretch (none within 20 m of the campfire or the statue, where the Caldera's other eruptions keep off too); in the **Kiln** too: once a minute a column of fire breaks out 18–32 m from one of the scouts in it, taking turns with the Kiln's ash (18), never with it, at most 10 s apart: 2–9.5 s after the ash clears, burning 30 s. Petrifying stones 4 per touch, 8 per second while you climb them. *Gloom:* the big ghost comes every 30 s for anyone (not only loners), lives 50 s, explodes 1.5 s after arming for 60 cold and 50 s of blindness; lit bell towers push it out of blast range without burning it (so does a burning Torch in a scout's hands). Three times in four, a **raven** comes for an Early Worm you see lying close by: it flies in, lands beside it, eats it (with the sound a scout makes finishing food) and flies off (pick it up first to keep it). Fly traps 10 poison/s and grab any body part. A frog's tongue pull poisons for 10. *Citadel:* arrow traps never run out and fire every 0.5 s (15 per arrow, 25 to pull it out), and their green warning line shows only its first half metre out of the opening; spiked fences 15; wall saws 30 on the first hit; the Central Tower's saw 7.5/s; spike rollers 10; spiked balls up to 60; spike rollers sweep round, spiked balls swing and wall saws run along their walls 1.5× as fast, rollers turn on their axles and saw blades wobble 1.5× as fast too, and pressure plates fire 1.5× as soon (0.53 s after the step; game: 0.8 s). **Ladders** help less: climbing one is 1.2× as fast as rock (game: 3×) and costs 90% of rock's stamina (25%). Mimic chests release gas: 5 drowsiness, 5 poison and 5 spores every 0.5 s, and look like ordinary chests; **35%** of the Citadel's other chests are mimics too (picked anew each climb). *Nadir:* drowsiness, poison, heat and spores heal half as fast, and blindness and numbness wear off half as fast. *Anywhere:* a **Mandrake**'s scream gives up to 75 drowsiness (game: 40), and half of that to players who can't hear it (sound-effects or master volume at 0), whom the game spares; the Scoutmaster's hunts last 70 s; 3 s after you slip on a banana peel your screen darkens like the Scoutmaster's and every sound is muffled (as in True Final Ascent's Ice Fog) for 20 s. **Lookers** show up on 90% of their spots; watching one curses you for 2.5/s and gives you a 5 s sugar rush (then not again for 10 s); staring at it for 5 s makes it leave and sends the Scoutmaster after you for 70 s (walking up to it only makes it leave). Explosions double their status and knock you back 3–4× as hard; a cooked Lantern's or Faerie Lantern's burst and the ghost's burst also knock you out; every other damaging surface ×2; beetles ×10; spore and heat emitters ×4. A thrown **Stone** injures for 20 (10 below 17). **Burned items burn you:** a freshly burned item (cooked 3+ times), held or worn as a backpack, burns like a campfire for its first **10 s**. **Campfires are extremely hot** (More Ascents): anything cooked on a campfire, over lava or by an explosion is charred to ruin (burned) on the very first cooking cycle (the config's *Hot Campfires* switch turns this off). Lighting a campfire takes only **10 petrification** off the scouts beside it (game: 20). |
| **18** | 13DDA + new | **Weather is more extreme:** stronger wind storms, slippery rain that pulls you down harder while you are in the air under open sky (about twice the pull of a normal jump, three times in the Tropics), snowstorms that chill (or scorch by day outside the Alpine, a heat only sunscreen keeps off, not a parasol or a glider; while a snowstorm blows on you, no sun burns through it), tornadoes every 20–120 s that spin, drag and lift a scout they catch 2.5× as hard. The **Mesa's midday sun** burns **6 heat a second** and the **Gloom's fog** makes you **2.5 drowsy a second**. The **Caldera's lava rises and falls** like a tide, 14 m (the game's own rise and fall: about 6 m), synchronised for everyone. **Hot ash fills the Kiln** once a minute once its lava rises: the ground rumbles and the air thickens for 5 s, then the ash burns **3 heat a second** for 15 s (45 in all) and clears (the same moment for everyone). It does not burn at the top of the Kiln (as high as its lava rises, or higher), and within **3 m of a petrifying stone** it neither shows nor burns. The fog turns **poisonous** and 3× as strong. **Cold nights chill 1.5× as fast.** In the **Alpine** the nights are longer: the **first and last quarter of the day are dark** too, with the same night cold. In the **Nadir** gravity pulls **twice as hard**. **The Mesa's midday sun roasts sun shields:** whatever you shield yourself with there (an open parasol, a glider held overhead, any held item that blocks the sun) stays clean for 5 s, smokes heavily for 15 s and is then fully burned (parasols 1.5× longer; the Mesa's sun roasts twice as fast, so 10 s, parasols 15 s). |
| **19** | More Ascents + new | **No more revive statues:** the statues at the end of each part of the climb (they revive the dead, or hand out an artifact if nobody died) are gone. Every other way back to life still works (scout effigies, skeletons, the Nadir), but **every revive adds 15 curse** (also the ones that give no status in the game) and **no revive cures petrification** any more (game: −75), so a scout who turned to stone stays doomed. Scouts start with **20 curse** (game: 10 from ascent 7). |
| **20** | More Ascents | **Bing Bong calls upon an evil force if not given attention.** Somebody must hold Bing Bong — holding it keeps the evil away for as long as it is held (in More Ascents the timer only reset when Bing Bong was picked up or put away); after 10 s without, Bing Bong starts talking, line after line, in the voice he has once burned, for every scout to hear (he falls silent as soon as someone holds him); after 15 s he falls silent and everyone gains crab status, which only the **Cursed Skull** cures (not cure-alls; Pandora's Lunchbox can even add more, see 14). **A burned Bing Bong never counts** (cooked 3+ times; from ascent 17 one campfire cooking burns it), and while freshly burned it burns the hand that holds it, like any burned item. |
| **21** | 13DDA | **Suitcases are painful:** small 6, big 10, epic 16 injury (statues 20 + 6 curse). The game fills status bars in steps of 2.5, so 6 shows as 5 and 16 as 15. **Clown chests** play a trick instead: 45 drowsiness, poison, spores, web or fly-trap grip, or 20 petrification (equally likely), or, half as often, 100 s of blindness; web and grip come off as usual (jumping, a friend's help). |
| **22** | 13DDA + new | **Ailments have unique effects:** injuries slowly worsen, weight pulls you down faster, drowsiness does not heal at night (from the Caldera and the Gloom on there is no night), cold slows walking and climbing. And episodes, more often the more of that ailment you have (about every 30 s with a full bar, none below 10): **spores** numb you for 20 s and then you burst in a Shroomberry's spore cloud, weaker than its own (scouts within 5 m knocked down for up to 1 s, a little spores); from **70 cold** you are now and then **knocked down**, shivering where you land, for 5 s; **heat** makes you **dizzy** for 6 s (the view closes in dark from the edges and sways, fading in and out, stamina comes back half as fast); **poison**: eating while poisoned may bring **nausea** (25 poison: one time in two; 50: always): that food and anything eaten in the next 3 s restores no hunger, and you stand bent over, hands on your knees, while your stomach growls. |
| **23** | 13DDA + More Ascents + new | **The sun is hot everywhere except the Alpine**, up to the Caldera and the Gloom (from there on it is the game's sun again); in the Alpine it does nothing (the Mesa keeps its own sun; sunscreen still protects, letting a quarter of the heat through from ascent 15; no sun burns through a snowstorm, see 18). **Sun protection burns wherever that sun is hot** (from 18 only in the Mesa's midday sun): whatever you shield yourself with against direct sunlight (an open parasol, a glider held overhead, or any held item that blocks the sun) protects you but roasts instead, only while it shields you: it stays clean for **5 s**, then **smokes heavily** for **15 s**, and is then **fully burned at once**, skipping the cooking stages. A burned item still shields you, but smokes and **crumbles away after 5 s** more in the sun (7.5 s for a parasol, 2.5 s in the Mesa's sun). An open **parasol** lasts **1.5× longer** (7.5 s + 22.5 s); the **Mesa's sun roasts twice as fast** (10 s, parasols 15 s). In the shade it stops smoking and cools down. |
| **24** | More Ascents | **Going unconscious is instant death** (a checkpoint flag still saves you). Note: fall damage makes you 60 drowsy (12), which with your 20 curse (19) fills 80% of the bar — so a fall that hurts for 20 or more kills (less does it if other statuses are on your bar). |
| **25** | 13DDA | **All weather everywhere, at slightly less effect.** Every run has **rain, wind and snowstorms**, whatever biome variants the map rolled (13DDA could only ever give you rain *or* wind), plus **extra tornadoes** roaming near the scouts. Storms start 30 s after waking up and keep away from the Kiln and beyond. In the Caldera and the Gloom (no day or night there) storms bring neither cold nor heat, and the wind and the snowstorm never blow together (the later one waits). With **True Final Ascent's Apocalypse mode** TFA's weather events run as TFA runs them; in the breaks between them this ascent's rain, wind and snowstorms run as above, and the tornadoes roam throughout. |

**Climbing Chalk.** The game has an unused Climbing Chalk; on every ascent it is a real item, found
wherever the **Energy Drink** turns up (the same luggage) but as rarely as the **Big Lollipop**. It
has **three uses** (two from ascent 15, which takes two uses per use). Each use takes 1 s while you
rub it into your hands; for **20 s, climbing** (walls, ropes and vines) **costs 60% of its stamina**
(from ascent 15: **75% for 15 s**), and every further use starts that time over. After each use a
cloud of chalk dust bursts from your hands and slowly settles; your hands turn chalk white for
everyone to see (gloves included), and a chalked hand shows above the right end of the stamina bar
while the effect lasts. It has a chalk-bag model (held upright like the Granola Bar), an icon and its own sound; on every ascent it
shows its bag instead of the game's placeholder cube, drawn with the game's own item shader, so it
is lit like the other items (in the shade too) and lights up when you look at it. Cooking darkens
it like any other item but gives it none of the food changes (no extra stamina, no poison), and a
**burned** chalk (cooked three times) can no longer be used. In a **custom run** it has its own
switch on the Custom Run screen; in regular runs the config's *New Items* switch keeps it out.

**Auto-Parachute.** The game's cut Auto-Parachute is back, as something to **wear in the backpack slot
(slot 4)** instead of a backpack. On every ascent it turns up where the **Jetpack** does, as rarely. Worn,
it opens by itself after 1.5 s of falling (the game's own parachute) and saves you from the fall. It
holds **three charges**, shown on its slot; from ascent 15 a save takes two, so it saves twice. With
the last charge it is gone; dropped, it keeps its charges. In a **custom run** it has its own switch
on the Custom Run screen; in regular runs the config's *New Items* switch keeps it out.

**Vanilla Wafers.** The game's unused second look of the Scout Cookies (a box of vanilla wafers),
under a name of their own: on every ascent, half of the Scout Cookies that spawn are these. Each of their
4 bites restores **5 hunger** and gives **20 extra stamina** (the Scout Cookies: 10 and 10); cooked like
the Scout Cookies, a bite is 10 hunger and 30 extra stamina (ascent 15 halves both). In a **custom run** they have their own
switch (after the Scout Cookies'): with both on they take half of the Scout Cookies, with the Scout
Cookies' switch off all of them.

**Cherry Lollipop.** On every ascent half of the Big Lollipops that spawn are the Cherry Lollipop instead: the
Big Lollipop in pink and white. Eaten, it gives the Big Lollipop's **infinite stamina and the Energy Drink's
speed boost at once**, for **6 s** (4 s from ascent 15), from the first lick (no 3 s hold); then the Big
Lollipop's drowsiness follows and **20 more**, on every ascent, with the ascents' changes on the 20 too (from
ascent 15 the Big Lollipop's of that ascent and 20 more: 85 + 26 over 12 s), not the
Energy Drink's crash. Cooked, like
the Big Lollipop, no drowsiness follows. In a **custom run** it has its own switch on
the Custom Run screen (after the Big Lollipop's): with both on it takes half of the Big Lollipops, with the
Big Lollipop's switch off all of them.

**Toxic Drink.** A new energy drink in a purple can. It turns up only in **Ancient Luggage** and
in the **Ancient Statues** (as rarely as the Faerie Lantern), on every ascent. Drunk, it makes you walk
and climb **4 times as fast for 10 s** (from ascent 15: 3 times for 7 s), counted from the first sip (the
Energy Drink holds its time for 3 s unless you climb); when the boost is over it
poisons you for **4 a second for 5 s** (from ascent 15: 8 a second). It also cools you by **60 heat** (the Energy Drink: 30; from ascent 15, as every
cure, 60% of it: 36) and wakes you up like the Energy Drink (from ascent 15 by at most 80 drowsiness); it sounds like the Energy Drink sped up 1.5 times. **Cooked**, it works like a cooked Energy Drink: it bursts when you drink it, and no
poison follows (the burst knocks you down and pushes you, as the cooked Energy Drink's does, but ascent
17 does not take it for an explosion). Its can is tinted the way a cooked Energy Drink's is, so the label
does not glare. Scout's Generosity clones it at a mystical item's cost. In a **custom run** it has its own
switch among the Custom Run screen's mystical items; in regular runs the config's *New Items* switch keeps it out.

**Lanterns.** From ascent 9 the Lantern turns up in the suitcases of every part of the climb up to the
Caldera and the Gloom (the game: only the Tropics' and the Alpine's), as rarely as before (Epic).

**Torch.** The game's Torch (three hang in the Mesa's Tomb) turns up in suitcases on every ascent wherever
the Lantern does, half as often. It is a weapon too: lit, the second use button (right
mouse) swings it like the Stick (33% stamina) and knocks down every scout and zombie it reaches for **2 s**;
the scouts it hits take **10 heat** (20 from ascent 15), zombies none. A swing burns 4 s of its 40 s of fuel,
so a full Torch gives 10 swings, 40 s of light or a mix; unlit it cannot swing. Lit, it warms like the
Lantern: 2.5 cold a second off the scouts within 4.5 m (2 from ascent 15). Lit in your hands it keeps the
Gloom's ghost away like a lit bell tower: a ghost hunting you or anyone within 4.5 m of it is pushed back out
of blast range and cannot arm (below ascent 17 the push also burns it, as the bell towers do). The Lantern
does not keep ghosts away; the Faerie Lantern does, within 7 m (the game's own). In a **custom run** it has its
own switch on the Custom Run screen (after the Lantern's) and swings there too; in regular runs the
config's *New Items* switch keeps it out of suitcases.

**Wild foods.** Three of the game's cut foods grow in the wild on every ascent (and in custom runs),
where the game's own foods of their kind grow: the **Wonderberry** (20 hunger and 10 s of infinite
stamina, no crash afterwards) on 0.2% of the berry bushes that grow Crispberries, the **Green
Clusterberry** (10 hunger, removes 30 poison) on thorny vines as often as each of the other
Clusterberries, and the **Weird Shroom** (5 hunger, the game's 10; you glow for 60 s; the game's glow was broken and
works again) in mushroom patches as often as the Cluster Shroom.

**Stones and sticks.** On every ascent (and in custom runs) up to **20 Stones** and **20 Sticks** lie on
the ground on the beach, and as many again in the Tropics or the Roots, near the spots where mushrooms
and berry bushes grow. A **Stone** thrown hard at a scout (or dropped on one from 5 m) knocks them down
for **2 s** and injures them for **10** (20 from ascent 17); it **shatters on any hard impact**, with a
rock smash and a small gray dust cloud (on a scout or a zombie too, with no coconut bonk). Swinging a **Stick** (the use button: a two-handed
chop over the head) costs **33% stamina** and knocks down for **4 s**, without hurting them, every scout
and zombie a kick would reach in front of you; out of stamina you can't swing.

**Ritual Dagger on zombies.** On every ascent the Ritual Dagger also works on mushroom zombies, the
Roots' and scouts who turned into zombies. As on a teammate, you have to stab them **in the back**
(from any side once they have passed out). A zombie dies after **5 stabs** (**8** from ascent 15) and,
like the game's zombies, is gone 10 s later, leaving a skeleton. Stabbing a zombie gives nobody a
cure or extra stamina, and you keep the dagger; the stab that kills it takes **15 crab** off you (exactly
15, from ascent 15 too, where nothing else but the Cursed Skull cures crab). Every stab that counts is heard
by everyone: the game's sound of a scout taking a hit, at the zombie (the scout who stabs hears it at their own
ears, as loud as their own hits).

**Warp Compass.** The game's cut Warp Compass is back on every ascent. It turns up only in **Ancient
Luggage** and in the **Ancient Statues** at the end of each part of the climb (where the Ritual Dagger
comes from), as rarely as the dagger, and, as in the game, never in solo, where there is no one to
warp to (from ascent 19 the statues are gone, so only Ancient Luggage has it). It has **one use**: it
warps you to a random teammate more than 12 m away who has stood on solid ground for a second (not
climbing, hanging on a rope or vine, falling, flying or being carried), onto free ground beside them.
If there is no such teammate, or no free ground beside one, nothing happens and you keep the use; a
line in the middle of the screen (in the lettering of the game's "THE LAVA RISES", without its sound or
swing) says why: no one to warp to, everyone too close, no one on solid ground, or no room to land. Scout's
Generosity clones it at a mystical item's cost. In a **custom run** it has its own switch among the Custom Run screen's mystical items; in regular runs the
config's *New Items* switch keeps it out.

The wild foods, the Stones and the Sticks never spawn in suitcases, so the Custom Run screen has no
switch for them: in custom runs too only the config's *New Items* switch keeps them out.

**New creatures.** With the config's *New Mobs* on (the host's), three new creatures live in regular runs on
every ascent; custom runs have a switch for each among the Custom Run screen's threats, in its biome's group
(on by default). The numbers here are ascent 17's: below it all they do is half as strong
(half the injury, poison and knockback, knocked down or paralysed half as long) and snakes chase a third
slower. Seven in ten of the crabs, the ground snakes and the goats live on the climb itself (the cliffs up to
the campfire, spread all the way up them, halfway up too), the rest on the way to it. *Beach:* **crabs**, 25 small red ones along the beach and up its
cliff, crawl around at 1 m/s from spot to spot and
chase any scout within 2 m for at least 6 s: a pinch deals 10 injury and knocks what you hold out of your
hands; you can pick one up (into your hands only: it pinches you and drops) or cook it into a crab leg you can
pocket (burnt at 17, it can still be eaten but only poisons: 35). *Tropics:* **snakes**, 30 on the ground and
10 in the trees: these sleep curled up in a ball on a branch of the giant trees (about 3.6 m long, 28 cm
thick), uncoiling when a scout comes within 5 m, striking and chasing until the scout is 20 m away, then
climbing back up, winding round the trunk; the rest (about 1.4 m long, 11 cm thick) live where a scout can stand, on the ground or a rock of the cliff
(halfway up the Tropics' climb its ledges are rocks; never a tree) with plants near (a bush, weed, monstera,
palm or small tree) or among the game's grass; they slither slowly around, up and down the cliffs too (winding along the
rock), and chase any scout they see, as scorpions do (a sprinting scout outruns them; on a cliff they crawl
after you but strike only once off it). Both keep after you for at least
10 s however far you run in the Tropics (they never leave them), rear up and jump at you, mouth open and fangs
out, then slither round you for 3 s before striking again (another scout nearby, if there is one); they
slither through the grass and rattle while they hunt. A ground snake's bite: 10 injury, then 40 poison over 10
s; a tree snake's: 30 injury, then for 2 minutes you collapse for 4 s every 15–30 s (another bite starts the 2
minutes over; a poison drop above the stamina bar drains as that time runs out; a First Aid Kit, Cure-All,
Blowgun dart, Napberry, Medicinal Root or Remedy Fungus cloud ends it). Snakes cannot be picked up; a Stick (4
s), a Torch (2 s), a thrown Stone (2 s) or a kick (1 s) pushes one back and stuns it, and then it goes for
whoever stunned it first. *Alps:* **mountain goats**, up to 15, alone or in pairs, live the whole way up the
Alps and roam up to 150 m from their spot (never leaving the Alps), walking slopes up to 80° up or down
(climbing with their hooves on the rock) and leaping from foothold to foothold, up to 8 m forward across a gap,
so they come down the cliffs they climb too; they are solid and bleat now and then; a goat with a scout within 20 m (in sight)
decides every 20 s whether to charge them (one time in four): it bleats, lowers its head and paws the ground
for 1 s, then charges at 7 m/s, straight on in its last 5 m (step aside then and it misses); its ram knocks
you down for 2 s and throws you 6–8 m, with 20 injury.

## Choosing an ascent

Open the boarding pass at the airport and use the arrows, like vanilla.

* **Unlocking:** ascent 9 unlocks once you have beaten vanilla ascent 8; beating ascent *N* unlocks *N + 1*.
  The game's own Steam stat stops at 8; progress beyond is saved in the mod's config.
  Set **Unlock All Ascents** to skip that. Ascents 9–25 give no reward (no sash promotion).
* **Wrap-around:** left from Tenderfoot jumps to the end of the list (True Final Ascent's FINAL ASCENT
  if installed, otherwise your highest unlocked ascent), right from the end of the list goes back to
  Tenderfoot.
* **Default Ascent:** pick which ascent the boarding pass shows when you first open it.
* **Map:** at the bottom right of the ascent card, the map's name in a blue outline with **<** and **>** beside it
  picks any map of the game's current rotation (the maps it goes through, one a day). It opens on today's map,
  **TODAY'S MAP (9)**; the arrows go by number (left **MAP 8**, right **MAP 10**), round at the ends, today's map
  keeping its name. Hold an arrow down to run through the maps, faster and faster; the die after the arrows picks
  a map at random. Your pick stays for the rest of the day, between climbs and restarts, and goes back to today's
  map when the day changes. The host's pick is the map everyone climbs; guests do not see the arrows. TFA's FINAL
  ASCENT keeps its own map.
* **Custom runs:** the last of the Custom Run screen's gameplay settings, **Ascent**, is off by default; a click
  steps through Tenderfoot, Peak and the ascents up to your highest unlocked, a right click back. With an ascent
  picked, the gameplay settings above it are ignored (they show faded) and the run plays by that ascent's
  modifiers, the game's and this mod's, with the host's config switches. The screen's threats and items still
  decide what is there: a threat switched off (crabs, scorpions, bees…) stays away on any ascent, also where the
  ascent adds its own (ascent 17's bee ambushes, the peak's zombies and bees, the Gloom's crows, the Citadel's
  mimics, ascent 25's tornadoes). The screen's **Eruptions** switch (after The Lava Rises, in any custom run): off,
  the volcano never erupts, neither the game's eruptions in the Caldera nor ascent 17's on its last stretch and in the
  Kiln; the Kiln's hot ash (18) comes only once its lava rises, so it stays away with The Lava Rises off. It stays a
  custom run: it unlocks and counts nothing. The boarding pass names the picked ascent, and so does the top right of
  the screen during the climb, under "Custom".
* With **True Final Ascent**, its FINAL ASCENT entry stays right after your highest ascent. In FINAL
  ASCENT, TFA's own difficulty dropdown offers ascents 9–25 too.

## Challenges and cosmetics

Two boxes in the boarding pass's header, **Blind Mode** and **Hot Mode**, are each player's own (in
multiplayer every player ticks their own) and work on every ascent:

* **Blind Mode:** all climb long your screen stays as dark as the Scoutmaster makes it at his closest
  (without his sound). When he really comes, it gets no darker.
* **Hot Mode:** ascent 23's sun on any ascent below 23, where that sun would scorch (not in the Alps, not
  from the Caldera and the Gloom on): it incinerates whatever you hold up against it just the same, and heats
  you at 65% of its strength. On ascent 23 and higher the box shows ticked and fixed: that ascent already has it.

Three cosmetics the game made but never offered are in the passport, each unlocked by a challenge
(clicking one while it is locked tells how):

* **VR Headset:** reach the peak with Blind Mode on for the whole climb.
* **Orange Spacesuit** and **Orange Helmet:** reach the peak wearing the Astronaut suit and the Astronaut
  helmet together for the whole climb, with Hot Mode on (or on ascent 23 or higher).

Custom runs and the Nadir do not count. The run's statistics show a new cosmetic as a small icon where the
badges go. These cosmetics have no Scout badge and leave the badge ribbon alone. What you unlocked, which of
them you wear and your two boxes are kept in the mod's own save next to the game's (`UltimateAscent/Save.txt`),
not in Steam; players without the mod see the game's default outfit and hat in their place.

## Multiplayer

Everybody needs **the same version** of the mod. The boarding pass refuses to start ascents 9–25 (a custom run
on one of them too) and names who is missing it. The ascent itself is the game's own value, so it reaches every player the
vanilla way (also late joiners and campfire saves). The host's ascent switches and its Hot Campfires and Cold
Nights switches (see Configuration) apply to everyone. During a climb, the line under the ascent at the top right
says **ALL MODIFIERS ON** when every switch that ascent takes in is on (the host's), or which are off
(**MODIFIERS OFF: 14, COLD NIGHTS**); from ascent 5, in custom runs only with an ascent picked.

## Configuration (`BepInEx/config/UltimateAscent.cfg`)

| Section | Setting | Default | |
|---|---|---|---|
| Boarding Pass | Default Ascent | 0 | -1 = Tenderfoot, 0 = Peak (vanilla), 1–25 = that ascent (capped at your highest unlocked) |
| Boarding Pass | Wrap Around | true | The arrows wrap around |
| Boarding Pass | Boarding Pass Extras | true | The Blind Mode and Hot Mode boxes and the map picker on the boarding pass. Off: none of them shows, both modes stay off (your ticks are kept) and every climb is on today's map. Each player's own |
| Unlocks | Unlock All Ascents | false | All of 9–25 selectable |
| Unlocks | Highest Unlocked Ascent | 0 | Your progress beyond 8 (kept by the mod) |
| Ascents | Ascent 9 … Ascent 25 | true | Switches that ascent's own rule on or off; the other ascents keep theirs (ascent 25 with *Ascent 20* off plays without Bing Bong's rule). The host's switches apply to everyone |
| Items | New Items | true | The mod's new items spawn in regular runs on every ascent: the Climbing Chalk, the Lightweight Parasol (the Roots parasol), the Auto-Parachute, the Warp Compass, the Toxic Drink, the Vanilla Wafers (half of the Scout Cookies), the Cherry Lollipop (half of the Big Lollipops), the Torch in suitcases (half as often as the Lantern), the wild Wonderberry, Green Clusterberry and Weird Shroom, and the Stones and Sticks on the ground. Off: none of them spawns in regular runs; the regular Parasol spawns wherever the Lightweight Parasol would, the Scout Cookies and the Big Lollipop keep all their spawns, and Torches hang only in the Mesa's Tomb. Custom runs keep their own switches for the items on the Custom Run screen (the Cherry Lollipop and the Vanilla Wafers have their own there); the wild foods, Stones and Sticks (not on that screen) follow this switch there too. The host's switch applies to everyone |
| Items | Six-Slot Backpack | true | Single-player only, on every ascent: the Backpack holds six items, with four pouches on its sides and its own icon. Off: the game's four-slot Backpack, as in multiplayer. A change applies to backpacks that appear after it |
| Items | Six-Slot Backpack in Multiplayer | false | Multiplayer only, on every ascent: the Backpack by the crashed plane at the start of the beach is a Six-Slot Backpack (six items, four pouches, its own icon); every other backpack stays the game's. Scout's Generosity clones it only for 100 petrification, which is death. The host's switch counts |
| Ascents | Ascent 8 | true | The game's own ascent 8: from it the flares at the peak no longer call the helicopter and a climb is won only in the Nadir. Off: the flares call the helicopter again on every ascent from 8 (the Nadir can still be visited). The host's switch counts |
| Items | More Wild Food in Multiplayer | false | Multiplayer only, on every ascent: every berry bush, palm, vine and mushroom patch grows half as much food again (beside the game's own). The host's switch counts |
| Ascents | Hot Campfires | true | From ascent 17 anything cooked on a campfire, over lava or by an explosion is charred to ruin on the first cooking cycle. Off: cooking goes as in the game on every ascent; the rest of ascent 17 stays (lighting a campfire still takes off only 10 petrification). First in the [Ascents] list. The host's switch applies to everyone |
| Ascents | Cold Nights | true | The night's own cold (the game's rule from ascent 5; harsher from 18). Off: the night itself no longer makes you cold, on every ascent; cold from anything else (storms, snow, water, the Alpine) stays. Custom runs keep their own Cold Night setting, unless they pick an ascent. The host's switch applies to everyone |
| Mobs | New Mobs | true | The mod's new creatures live in regular runs on every ascent: crabs on the beach, snakes in the Tropics and mountain goats in the Alps. Below ascent 17 all they do is half as strong (half the injury, poison and knockback, knocked down or paralysed half as long) and snakes chase a third slower. Off: none of them appears (a change mid-climb leaves those already out). Custom runs have a switch for each among the Custom Run screen's threats instead (on by default), and there they are half as strong unless the run picks ascent 17 or higher. The host's switch applies to everyone |
| Interface | Effect Icons | true | The icons above the stamina bar for the mod's new effects: the Climbing Chalk's chalked hand and a tree snake's paralysis drop (with its time left). Off: neither shows; the effects still work. Each player's own |
| Debug | Verbose Logging | false | Extra log lines |

## Compatibility

* **True Final Ascent 2.8.1** — supported (see above). TFA's Final Ascent map stacks all biomes; the mod
  uses TFA's own stage tracking there, moves its Caldera lava tide with TFA's Caldera offset,
  ends the nights where TFA ends its cold nights (TFA 2.9 on: at the Mesa's campfire), and leaves
  TFA's fog, weather and network keys alone. Its run records, daily board and Continue panel
  show ascents 9–25 by name.
  * **Apocalypse mode:** TFA decides how hard its own events are, so the ascent rules leave them alone.
    Whatever TFA's events do to a scout lands as TFA gives it: no ascent 14 boost on their statuses,
    their faints stay faints at ascent 24, TFA's own cures work in full. Falls are the exception: a
    fall the game hurts you for brings ascent 12's drowsiness and ascent 13's drop whatever made you
    fall, TFA's gusts, rain, antigravity, knock-downs and throws included. What they spawn (the blood moon's creatures, the
    cactus rain, the poison fog's mines, supply drops) keeps TFA's numbers, without ascents 10, 17 and
    21. During TFA's Blackout and Watch, ascent 11's Scoutmaster hunt steps aside; the Sleep Fog's
    Early Worms wake you fully. While an Apocalypse weather runs, the sun rules rest (ascent 23's
    heat, the roasting of ascents 18 and 23, ascent 15's leaky sunscreen), the way TFA rests More
    Ascents' sun, and so do ascent 25's storms, which run in the breaks between TFA's weather events. Once a status is on your bar it is yours, wherever it came from: how it heals and
    what it does (ascents 16 and 22) stay the ascent's.
* **Item Stats 0.6.1** and **Effect Preview 1.2.0** — supported: they show what items do on the ascent
  you climb (half food, weaker cures, partial cure-alls, ascent 14's boost and exact poison totals, the
  rules' crashes, weights and durations, suitcase pain, cannibalism, campfire petrification, Jetpack
  fuel, the Toxic Drink's poison, Scout's Generosity's price for the Warp Compass and the Toxic Drink)
  instead of the game's numbers; Item Stats also shows the Lightweight Parasol weighing nothing
  in custom runs. From ascent 17 Effect Preview shows no cooking stages, since cooking ruins the food
  at once.
* **PlayersInfo 0.2.5** — supported: its countdowns next to the statuses (how long until one wears
  off) follow the ascent's recovery (slow heat in the Caldera and the Kiln, slow drowsiness in the
  Gloom and the Citadel, no drowsiness recovery at night from ascent 22), for your teammates too;
  Effect Preview reads the same. Thorns that never fall out by themselves (ascent 17's thorns on the body)
  show **∞** (its clock showed -2147483648:-2147483648 for any endless time).
* **More Ascents** and **13 Downright Dastardly Ascents** (incl. Continued) — *incompatible* (they would
  fight over the same ascent slots); BepInEx refuses to load Ultimate Ascent next to them.
* **Russian:** the new items (and the swing prompt) have Russian names; the game's cut items it brings back
  use the game's own Russian names. Other languages show the English names.
* **Six-slot Backpack (single-player):** in solo play the Backpack holds **six items** instead of four, at
  every ascent. It has four orange pouches on its sides (two stacked on each) and no side handles, and
  its own icon; slots 1 and 2 hang in the bag, 3 to 6 in the pouches, and its wheel has a slice for each.
  In any online lobby, even one you host alone, every backpack stays the game's. The *Six-Slot Backpack*
  setting switches it off (the game's four-slot Backpack in single-player too).
* **Six-Slot Backpack in multiplayer:** with the host's *Six-Slot Backpack in Multiplayer* setting on (off by
  default), the Backpack by the crashed plane at the start of the beach is a **Six-Slot Backpack**: an item
  of its own with the six slots, the pouches and the icon above; every other backpack stays the game's. Its
  contents reach every player with all six slots, and Scout's Generosity clones it only for 100
  petrification, which is death.
* **Snow in the Alps:** a light snowfall drifts down around you wherever the ground below is the Alps',
  on every run. It is only for the look (no cold, no wind) and thin enough to keep the view clear; under a
  roof or an overhang it stops.
* **Shelter from snowstorms and wind storms** (on every ascent): a hill, rock or wall between you and the oncoming
  snowstorm or wind storm shelters you (from its cold and its push alike), out in the lowlands too (also on True Final Ascent's own terrain). The game only asks
  whether the sky above you is open. It only ever adds shelter: standing in the open, you are as exposed
  as before, and a gentle slope rising toward the storm does not count. Items lying on the ground (a backpack you
  put down, food, anything) are sheltered from a wind storm the same way, as you would be where they lie: the game
  blew them all away, under a roof or behind a rock as much as in the open.
* **Breaking free** from a spider's web or a fly trap (on every ascent): jumps while caught need no stamina
  and cost none, and a jump pressed too early counts as soon as the next one can. The game counted them
  only with stamina left, which ran out half way at the higher ascents, and dropped early presses.
* **Arrow traps** (on every ascent): a swinging spike ball crossing an arrow trap's line no longer sets it off
  (the game fired at it as at any loose body).
* **Campfire saves** (on every ascent): a save loaded after a crash keeps the petrification lighting the fire took
  off (20, from ascent 17 only 10). The game's save took it off the arrows stuck in you instead, so loading put it
  all back. The save is written once, as the fire is lit, so loading it again never takes off more.
* **Joining a climb under way** (from ascent 9): a scout who joins after the others have left the shore arrives
  alive at the current base camp (when it is safe: no fog or lava closing in), with the starting curse and nothing
  else. The game revives joiners only at a base camp whose end-of-location statue has revived scouts, so from
  ascent 19 (no such statues) they arrived dead. Their arrival no longer counts as a death for ascent 11's curse
  on the survivors (two joiners had added 16–18 curse to everyone).
* **Starting curse** (on every ascent): the game gives it only to scouts who wake up on the beach, so a scout
  on a plane seat that does not start passed out began without it. They get it as they appear.
* **The Scoutmaster** (on every ascent): he gives up on his target when another scout is within 35 m of them (the
  game: 15 m).
* **Scouts warm each other** (on every ascent): a scout gives off warmth once nothing has made them cold for 2 s
  (the game: 3 s).
* **Waterfalls** (on every ascent): the falling water knocks you down when it hits you and washes you out of it at
  a steady pace, sideways where there is room, else down the stream (the game knocked you down again and braked you every moment you were in it,
  and shoved each part of you on its own: you were held fast or flung). The splash at the foot throws you only where
  the water lands, within 3 m, half as hard (the game: anywhere in its 11 m).
* **Rescue Claw and webs** (on every ascent): a rescue grapple that catches a webbed scout pulls them out
  of the web.
* **The Lightweight Parasol under low gravity** (on every ascent): held open in the air while an Initiative
  gem's or a shroomberry's low gravity lasts, it lifts you up the way balloons do (as much as eleven balloons,
  the fewest that lift a scout); from ascent 15 half as high. The Parasol keeps the game's behaviour.
* **Blindness** (a ghost's burst, a shroomberry, a clown chest's trick) is cured by the **First Aid Kit** and
  the **Cure-All**, along with what they heal, on every ascent.
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
