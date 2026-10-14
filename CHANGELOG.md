# Changelog

## 1.1.1

For PEAK 2.6.b.

* Config: every ascent 9-25 has its own switch (section "Ascents"): one switched off leaves out only
  that ascent's rule, so ascent 25 can be played without ascent 20's Bing Bong. The host's switches
  apply to everyone. The "Big Lobbies" section is gone (Nerfed Revives and Curse On Teammate Death are
  simply part of ascents 14 and 11 again), and the config file drops entries the mod no longer has,
  such as "Double Apocalypse Weather".
* Ascent 12: fall damage makes you 60 drowsy (was 80; below ascent 15 46.2, was 61.5). At ascent 24
  a fall no longer kills by itself: with the starting 20 curse it takes a fall that hurts for 20 or
  more, or other statuses on the bar.
* Ascent 12: pulling someone up makes you 10 drowsy a second (7.7 below ascent 15). It was 5 at
  every ascent: the bar's 2.5-point steps swallowed ascent 15's boost.
* Ascents 12 and 13 with True Final Ascent's Apocalypse: every fall the game hurts you for brings
  the drowsiness and the drop, also when TFA's events made you fall. Before, a landing within 5 s of
  a TFA gust, knock-down or throw, or within 15 s of its antigravity, was left to TFA, so during its
  storms no fall brought them (balloons or not).
* Ascent 14: a lit Lantern takes 1.5 cold a second off the scouts near it (game: 2.5), a lit Faerie
  Lantern 2.5 a second of each of cold, injury, poison, drowsiness and heat (game: 5).
* Ascent 14: the Lightweight Parasol weighs 5% of the bar, as much as the Parasol (it stays
  weightless from 9 to 13 and in custom runs).
* Ascent 16: heat heals at 0.6 a second in the Caldera and the Kiln (was 0.33; game: 1.3), and
  drowsiness at 1 a second in the Gloom and the Citadel (was 0.33; game: 2.5).
* Campfires are extremely hot from ascent 17 instead of 16: cooking on a campfire, over lava or by
  an explosion chars to ruin on the first cycle. Lighting a campfire still takes only 10
  petrification off from ascent 16.
* Ascent 17: a Mandrake's stronger scream makes only players drowsy who can hear it (the game's
  rule): with your sound-effects or master volume at 0 it gives nothing.
* Climbing Chalk: as rare as the Big Lollipop (was as common as the Energy Drink), still in the
  Energy Drink's luggage. Three uses, two from ascent 14 (which takes two uses per use), each with
  its cloud of dust; every further use starts the effect's time over. Cooking now darkens it like any
  other item (it kept its colour) and gives it none of the food changes (Item Stats and Effect
  Preview showed poison on an incinerated chalk); a burned chalk can no longer be used.
* The Lightweight Parasol looks its own: a dark blue, turquoise and pale mint canopy instead of the
  Parasol's rainbow, and its own inventory icon (from ascent 9 and in custom runs).
* Shorter boarding-pass descriptions for ascents 9, 11, 13, 16, 18 and 23.
* Fixed, checkpoint flags:
  - a flag that saved you put the cold, drowsiness, heat, poison and spores it had saved back bigger
    than they were (ascent 15's boost hit them a second time); it now puts them back exactly;
  - its 10 curse (ascent 14) never landed, because the game locks statuses while it moves you to the
    flag; it now lands;
  - at ascent 24 a scout the flag saved collapsed again at the flag a moment later and dropped what
    they held (the pass-out's fade to black finished after the save); the flag now saves you before
    you pass out.
* Fixed: ascent 17's Citadel mimics (every second ordinary Citadel chest) never came on normal maps,
  because the game files list the Citadel under the Gloom's biome. The mod also tells the Citadel
  from the Gloom again.
* Fixed: with True Final Ascent, the boarding pass's left arrow on Tenderfoot skipped TFA's FINAL
  ASCENT and went straight to the highest ascent; it now goes to FINAL ASCENT first, like the right
  arrow the other way.
* PlayersInfo: its countdowns of how long a status takes to wear off follow the ascent's recovery
  (it read the game's rates); cactus thorns that never fall out show no broken countdown.
* Item Stats: the Lightweight Parasol shows no weight in custom runs either.

## 1.1.0

For PEAK 2.6.b.

* Ascent 17: poison mines burst for up to 50 poison and spore mines for up to 45 spores (were 60 and
  55); their clouds stay at up to 5 every 0.5 s.
* Ascent 17: ticks bite for 2.5 poison; bees sting within 2 m, stay angry 50 s and chase up to
  120 m from their hive or ambush spot; beetles deal 10 injury; the Roots' bouncy mushrooms give
  back 5 stamina per bounce (purple ones 2.5).
* Ascent 14: eating a scorpion costs 15 poison; the scout statue gems cost more petrification and
  do less; the Shroomberries' good effects are shorter and their bad ones longer, and their spore
  burst reaches 10 m and knocks down for 4 s (it no longer counts as an explosion at 17).
* Ascent 15: poisonous food lands exact totals (Button Shroom 150 poison, Cluster Shroom 75, Bugle
  Shroom 120, Green Crispberry 40, Yellow Kingberry 150, Pink Berrynana 100, Green Kingberry 35 at
  once, Shroomberries 15 spores), every meal in full; this replaces ascent 14's old poisonous-food
  rule. Ascent 16: lava and explosions char what they cook to ruin, like campfires.
* Ascent 14: eating a raw egg (the Caldera's and the Gloom's) costs 5 poison. Ascent 17: Lookers show
  up on 25% of their spots and curse 2.5 a second.
* Ascent 14 items: Heat Pack 20 s; sunscreen keeps off only 75% of the sun's heat (also under
  ascent 23's sun); Energy Drink walks only +30%; rope spools hold half the rope; letting go of a
  Rescue Claw no longer drops your items; thrown Shelf, Cloud and Warp Fungus fly as if twice as
  heavy; a Bounce Fungus pad gives back 2.5 stamina; a Magic Bean's vine grows at most 15 m and
  standing on it sticks 2.5 thorns a second into you, which fall out again after 30 s;
  placed pitons crack like the Alps' pitons, 30% sturdier; eating a raw tick costs 5 poison; the
  Scoutmaster's Bugle hunt lasts 180 s; a checkpoint flag that saves you curses for 10; the Cursed
  Skull is the only cure for crab status and keeps 10% of every status ("Nerfed Revives"); the
  Ritual Dagger zombifies instead of killing; Pandora's Lunchbox can give crab status. The Roots'
  parasol becomes the **Lightweight Parasol**: it weighs nothing and is the one parasol that still
  slows your fall.
* Ascent 15: a fed Napberry gives 200 drowsiness, a Scorchberry 80 heat, the Energy Drink's crash 50
  drowsiness. Ascent 17: a frog's tongue pull poisons for 10.
* Ascent 20: Bing Bong's crab status can now be cured, by the Cursed Skull only (ascent 14 rule).
* Ascent 20: instead of the world shaking, a Bing Bong nobody holds starts talking after 10 s, line
  after line, in the voice he has once burned, for every scout to hear. He falls silent as soon as
  someone holds him, and when the crab status starts at 15 s.
* Ascent 14 items: a snowball that bonks a scout gives 30 cold and knocks them down for 2 s, and
  holding one chills 2.5 a second; a Remedy Fungus cloud lasts 8 s; the Anti-Zooka's bubble is 8 m
  across and lasts 40 s; a Portable Stove burns 30 s; the Gloom's Early Worm removes only 50
  drowsiness; the Book of Bones costs 35 curse net when it makes you a skeleton and 20 for a
  teammate it revives; the Ancient Idol weighs half the bar; a raw Mandrake gives 30 spores, a cooked
  one removes 15 curse; a carried Balloon takes 2% of the bar off your weight, a Balloon Bunch 8%.
  Cooking now keeps only poison and spores (13DDA's rule also kept a cooked Mandrake's numbness, a
  Prickleberry's thorns and a lollipop's raw rush). Ascent 17: a Mandrake's scream gives up to 75
  drowsiness.
* No effect is stronger at a lower ascent than at a higher one:
  - Ascent 15's boost now shows below 15 too: the mod's exact ailment numbers land as given from 15
    on and without the boost below 15 (the Big Lollipop 65 instead of 85 at 14, a raw egg 2.5 instead of 5, ascent 12's fall 61.5 instead of 80, cannibalism 28.6
    instead of 50 poison). The Shroomberry's spore effect keeps the game's 25 at 14 (30 from 15).
  - Ascent 14: what items cost you (a Napberry's sleep, a Scorchberry's heat, the Book of Bones'
    curse) is no longer cut to 60% like their cures; speed boosts other than the Energy Drink no
    longer walk faster than the game's +50%; the Lightweight Parasol is weightless from ascent 9.
  - Ascent 17: entering a bee swarm stings for 7.5 like at 15–16 (was 5); geysers' warm spots remove
    the game's 5 cold a second (were 15); tumbleweeds no longer roll slower than in the game, but
    15% faster.
  - Ascent 25: no more relief in the Gloom (its sleep fog was weaker and drowsiness healed twice as
    fast as at 24); the Alpine's snowstorms keep their game pauses (20–90 s, not 30–90 s).
* Sun shields roast from ascent 18 in the Mesa's midday sun (ascent 23: wherever the sun is
  scorching). A roasting item stays clean for 5 s, then smokes heavily for 15 s and burns (was 30 s
  of light smoke); parasols take 1.5× as long, the Mesa's sun roasts twice as fast. The smoke is its
  own, three times as dense, bigger and darker, so cooking smoke stays as in the game.
* Ascent 14: whatever you feed the Jetpack gives 35% less fuel.
* The game's unused **Climbing Chalk** is a real item from ascent 9: a rare find wherever the Energy
  Drink turns up (same luggage, same odds). Using it takes 2 s (with a hand-rub sound) and gives, for
  16 s, climbing that costs 75% of its stamina (walls, ropes and vines; ascent 14: 85% for 12 s); a
  cloud of dust bursts from the scout's hands for everyone and settles over 9 s, the scout's hands
  turn chalk white for everyone (gloves included), and a chalked hand shows above the stamina bar
  while it lasts. It gets a chalk-bag model, an icon, its name and the prompt "use", and loses the
  game's "mystical" tag (picking up a mystical item earns a Steam achievement).
* The Custom Run screen has switches for the Climbing Chalk and the Lightweight Parasol. In a custom
  run each follows its own switch (the Parasol's no longer covers the Roots' parasol), and the Roots'
  parasol is the Lightweight Parasol.
* True Final Ascent shows ascents 9–25 by name ("Ascent 24") in its run records, daily board and
  Continue panel; it showed the mod's internal text key.
* True Final Ascent's Apocalypse mode: TFA decides how hard its own events are, and the ascent rules
  no longer change them. Its events' statuses land without ascent 15's boost, their faints stay faints
  at ascent 24, the falls they cause bring no ascent 12 drowsiness or ascent 13 drops, TFA's own cures
  and the Sleep Fog's Early Worms work in full, and skeletons and invincibility follow TFA's rules
  there. What its events spawn (blood moon creatures, cactus rain, the poison fog's mines, supply drops)
  keeps TFA's numbers (no ascent 10, 17 or 21 rules). Ascent 11's Scoutmaster hunt steps aside during
  TFA's Blackout and Watch. While an Apocalypse weather runs, the sun rules rest (ascent 23's heat,
  the roasting of ascents 18 and 23, ascent 14's leaky sunscreen), as TFA rests More Ascents' sun.
* Ascent 25 no longer adds a second Apocalypse weather to TFA's: TFA's weather runs as TFA runs it
  (the extra tornadoes stay). The second weather inherited the other weather's length, so a second
  Blackout's torch emptied 2–18 times too fast, and a second avalanche or tsunami was cut short. The
  "Double Apocalypse Weather" option is gone.
* True Final Ascent's Ice Fog, which borrows the game's fog sphere for its own chilling fog, keeps its
  own ice-blue colour; from ascent 18 it turned purple like the poisonous rising fog.
* Ascents 9–25 give no reward: the boarding pass shows no reward line and the end screen no promotion.
* Item Stats and Effect Preview show item numbers as they work on the ascent being climbed (food,
  cures, cure-alls, ascent 15's boost, exact poison totals, crashes, durations, weights, gems), and
  Effect Preview also suitcase pain (21), cannibalism (11), campfire petrification (16), the Remedy
  Fungus cloud and Jetpack fuel (14); it no longer previews cooking stages from ascent 16.
* Fixes (ascents 9–25): a Portable Stove protected the scouts present when it was placed from the
  Gloom only after it had burned out, and then for good; it now protects while it burns. A sugar rush
  without a crash (the Looker's, a Shroomberry's) during a lollipop's no longer throws an error.

## 1.0.0

First release, for PEAK 2.6.b.

* Ascents 9–25 on the game's own boarding pass, combining 13 Downright Dastardly Ascents (Continued)
  and More Ascents, all in English.
* Boarding pass: wrap-around arrows, configurable default ascent, unlock progression past ascent 8,
  "Unlock All Ascents" option, refuses to start 9–25 unless everyone has the same version.
* True Final Ascent: FINAL ASCENT keeps its place in the arrow list and offers 9–25; stage-aware
  rules in the stacked map; the Caldera's lava tide follows TFA's Caldera; ascent 25 runs two
  Apocalypse weathers at once.
* New rules: eaten ticks restore no hunger (14), sun protection burns (23, parasols 1.5× slower,
  twice as fast in the Mesa), no sun in the Alpine (23), freshly burned items burn your hands for
  10 s (17), a burned Bing Bong does not count, holding Bing Bong keeps the timer at zero, and
  nothing cures its crab status (20), bee ambushes in the Tropics (17), ticks only in the Tropics
  with up to five at once, 35% tick chance, latching on where they touch you (spread out), visible
  at once and growing fast (17), spores in ascent 15, 1.5× colder nights and dark first and last
  quarters of the day in the Alpine (18), clown chests play a random trick (21).
* Exact hazard numbers at ascent 17 (as they land on the bar, ascent 15's boost included, which now
  applies to everything): jellyfish 20, urchins 40, thorny vines 60 poison; bees 10/15 poison a
  second (calm/angry; ambushes at the middle of each day and night); ticks 10 per bite until
  pulled off, with 10 s of no poison healing; poison/spore mines
  60/55 and a 10/s cloud; spiders 12 every 0.8 s and a 40 web (a rescue grapple frees you); fly
  traps 10/s; lava 100 injury + 200 heat, lava streams 50 + 100; petrifying stones 4 (8/s while
  climbing); magma rocks burn while climbed like ice rocks chill; the Gloom ghost (every 50 s, for
  anyone, 60 cold + 80 s blind); Citadel traps (endless arrows every 0.5 s, saws, rollers, spiked
  balls, fences) and mimic gas (5 drowsiness, poison and spores per 0.5 s; every second Citadel
  chest is a mimic); zombies, geysers, cacti, scorpions, dynamite, napberries and flash plants;
  70 s Scoutmaster hunts; banana peels darken the screen; Lookers curse whoever watches them and
  can send the Scoutmaster after them.
* Fall damage makes you 80 drowsy (12); cannibalism costs 50 poison and 25 curse (11); campfires
  heal 10 petrification (16); no revive statues, and every other revive costs 15 curse and keeps
  petrification, with 20 starting curse (19); no night from the Caldera and the Gloom on (22).
* Ascent 9 only speeds up the fog until the Caldera or the Gloom (no more fog chasing you through
  the Caldera or the Gloom); the Kiln's lava, the Citadel's gloom and the Nadir's ghosts still rise
  sooner and faster. Ascent 10 also covers Explorer's luggage; ascent 23's sun shields smoke, then
  burn in one go and stay in your hands. Shorter boarding-pass texts.
* Status effects keep the game's feel: a status plays its screen shake, overlay and sound at most
  once per effect length, so fast stings no longer pile shakes into violent shaking.
* Ascent 25 guarantees rain, wind and snowstorms on every map (13DDA only ever had the storms of the
  map's own biome variants).
* Fixes over the originals: More Ascents' null reference in "helping is bad", its crash on Turkish
  (15th language); 13DDA's outdated copies of
  game methods (ticks, dynamite, explosions) were rebuilt on the current game code.
