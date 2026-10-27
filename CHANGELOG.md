# Changelog

## 1.6.3

* Ascent 20 in a custom run's **Mini Run**: one that starts past the beach (in the Tropics, the Alpine or the Caldera,
  far from the game's Bing Bong at the crash site) hands the host a Bing Bong to hold, 3 s after they are up on the
  ground (as the game hands out Grapple Mode's claw). A Mini Run on the beach keeps the game's Bing Bong at the crash
  site; a resumed campfire save keeps the one it saved.

## 1.6.2

* Snakes: 30 ground snakes (was 21) and 10 tree snakes (a third as many, was 7). A ground snake lives only where a
  scout can stand (room to stand there, no steeper than before), on the map's ground or one of its rocks (halfway up
  the Tropics' climb the ledges are rocks; never a tree, its crown or a vine), with plants near (a bush, weed,
  monstera, ivy, palm or small tree within 6 m) or among the game's grass blades; every surface down each look
  counts, so ledges under the rock above them are found too. Ground snakes crawl up and down cliffs and rocks (not
  vines or trees): going somewhere higher (they wander within 10 m above or below their spot) or after a scout up on
  the rock, one gets onto the face and winds up it at most 1.5 m/s, and going somewhere lower it crawls over the edge
  and down, holding on; it strikes only once off the rock, and a hit knocks it off. Before, a cliff or a high edge
  stopped them.
* On the climb itself the new mobs are spread all the way up it, by height (from its foot through halfway up to its
  top), not along the way, which bunched them near the top by the campfire. A ledge or rock on a cliff counts as the
  climb (the cliff's face is beside it, the ground far below), so the ledges halfway up are found too. A new mob found
  under the map is put back at its spot only when it is under the map's own ground or walls, not in the open under a
  rock or a tree's crown.
* Mountain goats: up to 15 (was 12).
* Ascent 17's bee ambushes: the check comes as the day begins and as the night begins (was the middle of each); every
  scout's 50% ambush comes at a moment of their own picked at random within that day or night, at least 30 s from
  anyone else's, so two scouts are never set upon at once. A scout not in the Tropics when their moment comes is left
  alone.
* Config: **Hot Campfires** (first in the [Ascents] list, the host's): off, campfires, lava and explosions no longer
  char what they cook, whatever the ascent; the rest of ascent 17 stays.
* During a climb a line under the ascent at the top right says whether all modifiers this ascent takes in are on in
  the config (**ALL MODIFIERS ON**) or which are off (**MODIFIERS OFF: 14, COLD NIGHTS**).
* Map picker: a die after the arrows picks a map at random.
* Custom runs: an **Ascent** setting, last of the Custom Run screen's gameplay settings (off by default; a click goes
  to the next ascent, a right click back, up to the highest unlocked). With an ascent picked, the gameplay settings
  above it are ignored and the run plays by that ascent's modifiers, the game's and this mod's, with the host's
  config switches; the screen's threats and items still decide what is there (a threat switched off stays away,
  ascent 17's and 25's own bees, zombies, crows, mimics and tornadoes included). It still counts for nothing. The
  boarding pass and the ascent at the top right during the climb name the picked ascent.
* Custom Run screen: an **Eruptions** threat switch (after The Lava Rises, on by default). Off, the volcano never
  erupts in that custom run: neither the game's eruptions in the Caldera nor ascent 17's on its last stretch and in
  the Kiln. The Kiln's hot ash (18) keeps following The Lava Rises.

## 1.6.1

* Snakes: 21 ground snakes and 7 tree snakes (a third as many) in the Tropics, whatever the map. Before, their number
  came from the map's scorpion spawners, which hold none (the Mesa's scorpions are laid out in the map), so every map
  had 9 (3 in trees). Ground snakes live on the ground only: never on a giant tree, its crown or anything else more
  than 2 m up off the ground; they wander within 40 m of their spot (was 20 m).
* The new mobs live mostly on the climb itself: seven in ten of the crabs, the ground snakes and the goats are on the
  cliffs up to the campfire, the rest on the way to it (the beach's sand, the jungle floor, the Alps' snowfield). The
  tree snakes still live in the giant trees.
* Mountain goats roam up to 150 m from their spot (was 25 m) and come down cliffs as well as climb them: where to go
  next is picked on every ledge along the way down, not only the highest, and so are the footholds they leap to. They
  leap up to 8 m forward (was 4.5 m), across gaps between ledges. Part way down a face too steep to rest on with no
  way on, a goat goes back up to firm ground instead of losing its footing and falling.
* Ascent 15: the Energy Drink's boost lasts 6 s, cooked 8 s, counted from the moment you drink it (the game: 8 s,
  cooked 12 s, its clock held for the first 3 s unless you climb).
* The new mobs never live under the map: a spot under its surface (a ray looking for ground can fall through a gap
  between the map's pieces) is passed over when they spawn, goats never roam or leap to one, and a crab, snake or goat
  that gets under the map anyway is put back at its spot.
* Shelter from wind storms covers items too: an item lying on the ground (a backpack, food, anything) is pushed only as
  hard as a scout would be where it lies; the game blew every one away at full strength, sheltered or not.

## 1.6.0

* Ascents 14 and 15 swapped: ascent 14 is now **Ailments apply more** (cold and drowsiness ×1.3, heat ×1.5, spores
  ×1.65, poison ×1.75) and ascent 15 **Items are worse**. From ascent 15 every number is as before (both rules run
  there). Amounts that were lower below ascent 15 (without its boost) are now lower below 14, so ascent 14 itself gets
  the full amounts (e.g. ascent 12's fall drowsiness: 60 from 14, 46.2 below). The "Ascent 14" and "Ascent 15" config
  switches follow the numbers.
* Six-Slot Backpack: after its wheel had been opened, every other wheel (Backpack, Jetpack, Auto-Parachute...)
  showed two extra empty slices.
* Map picker: on a custom run it covered the boarding pass's Custom Options button; there it now sits at the card's
  bottom left.
* Joining a climb under way (from ascent 9): you arrive alive at the current base camp (when it is safe) with the
  starting curse and nothing else; from ascent 19 joiners arrived dead. Your arrival no longer curses everyone through
  ascent 11's rule (two joiners had added 16–18 curse).
* Starting curse (every ascent, a game bug): a scout on a plane seat that does not start passed out began without
  it; now they get it.
* Revive statues revive as in the game: every status cleared, then the game's revive curse and hunger. Keeping 25%
  of your statuses is for the other revives (and the flag puts back its own).
* Ascent 15: a revive statue no longer takes petrification off (the game: 75), and a checkpoint flag's save leaves
  you the petrification you have (the game sets it back to what you had when you planted the flag).
* The Scoutmaster: on every ascent he gives up when another scout is within 35 m of his target (the game: 15 m);
  ascent 11's hunt starts only on a scout with nobody within 35 m and stops when someone comes that near.
* Bee ambushes (17): every scout in the Tropics is ambushed with a 50% chance at the middle of each day and night
  (was 30%); the swarm appears 2–3 m from the scout and chases them for at least 20 s however far they run, then up
  to 120 m from where it appeared.
* Thorns (17): thorns in the face pop out at the game's times (30 s alone, 120 s with others); thorns on the body
  never pop out by themselves, solo too (before: 120 s alone, never with others, wherever they were).
* Tumbleweeds (17): they steer for scouts within a cone of 10–240° (the game: 50–180°) and hit 1.5 times as hard
  (was twice).
* Scouts warm each other once nothing has made them cold for 2 s (the game: 3 s), on every ascent.
* Waterfalls (every ascent): the falling water knocks you down when it hits you (0.5 s) and washes you out of it
  at a steady pace, sideways where there is room, else down the stream (still in it when you get up, it knocks you
  down again); the game knocked you
  down again and braked you every moment you were in it, and shoved each part of you on its own, so you were held
  fast or flung. The splash at the foot throws you only where the water lands (within 3 m) and half as hard; the
  game threw anyone stepping anywhere into its 11 m.
* Sun roasting (18, 23): a burned item held up to the sun still shields you, but smokes and crumbles away after 5 s
  (7.5 s for a parasol, 2.5 s in the Mesa's sun): it goes up with the sound of burning and turns to ash, the Kiln's
  flakes and smoke; before, it shielded for good. An item that explodes when
  overcooked (a Lantern, a Faerie Lantern) explodes when the sun burns it, as before, but now only once (the sun's
  burning goes through every cooking step at once and could set it off at each).
* New creatures on every ascent: the crabs, snakes and mountain goats below live in regular runs on every ascent, with
  the config's new *New Mobs* switch (on by default; off, none of them appears); custom runs have a switch for each among
  the Custom Run screen's threats, in its biome's group (on by default; half as strong there). Below ascent 17
  all they do is half as strong (half the injury, poison and knockback, knocked down or paralysed half as long), and
  snakes chase a third slower (2.4 m/s on the ground, 2.1 m/s from the trees). The numbers below are ascent 17's.
* Config: *Effect Icons* (on by default); off, the icons above the stamina bar for the Climbing Chalk's effect and a
  tree snake's paralysis do not show (the effects still work). Each player's own.
* Pitons (15): a piton you placed turned into the Alps' old piton; it now keeps the yellow look of the piton in your
  hand (it still cracks and breaks as the Alps' do).
* Snakes in the Tropics, as many as the map has scorpions. A third are tree snakes (about 3.6 m long and 28 cm
  thick): they sleep curled up in a ball on a branch of the giant trees (on a map without them they live on the
  ground); when a scout comes within 5 m they uncoil, raising the head towards them, strike, and chase until the scout
  is 20 m away, then crawl back to the foot of their tree and climb it as snakes do, winding round the trunk and up
  the branches; a bite deals 30 injury, then for 2 minutes the scout collapses for 4 s every 15–30 s (another bite
  starts the 2 minutes over, it does not stack; a poison drop above the stamina bar drains as that time runs out; a
  First Aid Kit, Cure-All, Blowgun dart, Napberry, Medicinal Root or Remedy Fungus cloud ends it). The rest are
  ground snakes (about 1.4 m long, 11 cm thick): they never sleep, slither slowly around (0.5 m/s) and chase a scout
  they see, as scorpions do (3.6 m/s, tree snakes 3.2 m/s; a sprinting scout makes 5.2 m/s); a bite deals 10 injury,
  then 40 poison over 10 s. Before a strike a snake rears up into an S, then jumps at the scout with its mouth wide
  open and its fangs out; for 3 s after a bite it slithers round the scout 3.5 m out, then goes for another scout
  nearby if there is one (as near as it would notice them), or the same scout again. Once it goes for a scout it
  keeps after them for at least 10 s, however far they run in the Tropics: snakes never leave the Tropics, stopping
  where their ground ends and giving up a scout who goes beyond it. Like the game's creatures they
  leave scouts who are out cold alone. They cannot be picked up; a Stick (4 s), a Torch (2 s), a thrown Stone (2 s) or
  a kick (1 s) knocks one back 1.5 m, and it curls up for that long, then goes for the scout who stunned it (the first,
  if more did) when they are that near. They slither through the grass and rattle while they hunt (recordings styled
  after PEAK's own creature sounds), and fall silent while stunned; a bite lands with the game's hit sound, and so
  does a hit on a snake.
* Mountain goats in the Alps: up to 12, alone or in pairs, along the whole way up the Alps (from where they begin
  to the top of their climb). Shaped like PEAK's own creatures and fluffy (locks of off-white hair over almost all of
  them, longer grey hair hanging below), with long thick grey banded horns curving back, a beard and dark hooves, and
  solid: scouts bump into them. They graze, look about and roam the mountainside within 25 m of their spot, never
  leaving the Alps (they stop where the Alps' ground ends, and do not charge a scout beyond it), surer on it than the
  Roots' beetles: they walk slopes up to 80° (climbing, a goat's whole body lies along the cliff, its hooves on
  the rock) and leap from foothold to foothold (up to 3.5 m up, 6 m down, 4.5 m across). Once a scout is within 20 m and in
  sight, a goat decides whether to charge them, and again every 20 s while one is: three times in four it goes on
  grazing; the fourth it bleats, lowers its head, paws the ground for a second and charges (7 m/s; a sprinting scout
  makes 5.2 m/s), turning after the scout but running straight on in its last 5 m, so stepping aside then makes it
  miss (it skids on and shakes its head); it never runs off an edge. Its ram knocks the scout down for 2 s and throws
  them 6–8 m, with 20 injury. Goats bleat now and then (every 25–70 s), their hooves are heard while they move, and a
  ram lands with the Roots' beetles' hit, heavier and louder.
* Crabs on the beach: 25 small red crabs, about a scorpion's size, spread along the beach from where the scouts wake
  up to its campfire (they are not replaced once taken). They crawl around the beach at 1 m/s from spot to spot,
  resting a moment at each, scuttling sideways, and never leave it. A scout who comes within 2 m is chased (one at
  random, if more are that near) at the same 1 m/s (a walking scout makes 2.5 m/s), for at least 6 s, then for as long
  as they stay within 2 m. Near the scout a crab turns to face them and pinches: its big claw rises and opens, then
  snaps shut, with the scorpion's glint and sparks, and a moment after the snap a pinch that lands deals 10 injury and
  knocks whatever the scout holds out of their hands. Crabs can be picked up like scorpions, into the hands only (put
  away, a crab is dropped), and a held crab pinches its holder and falls out of their hands. Cooked, a crab turns into
  a cooked crab leg (its own model, darker than the live crab, and its own icon), food like any other that goes in a
  pocket or a backpack: 10 hunger and 10 extra stamina (5 and 5 from ascent 15). From ascent 17 the campfires burn it,
  and burnt it can still be eaten, as burnt food can: no hunger or stamina, only 35 poison (the game's 20 for food
  burnt that far, with ascent 14's boost). A crab's pinch (one snap) and its claw clicks (now and then, while someone
  is about) are made from the game's own scorpion clicks; its scuttle from a claw-click recording, made natural (every
  click varied, its ring tamed).
* Verbose Logging now also writes, each time you take out or put away an item, your stamina, the most stamina the
  bar allows, the statuses and what your weight is made of (to track down a stamina drop seen while holding items).
* A Stick's swing knocks down for 4 s (was 3 s), zombies too: the game stood every knocked-down zombie up again
  after 3 s.
* Config: Cold Nights and Ascent 8 now come first in the [Ascents] list.
* PlayersInfo shows ∞ for thorns that never pop out (it showed -2147483648:-2147483648).
* New config entries: Ascents > **Ascent 8** (on; off, the flares at the peak call the helicopter again from ascent
  8) and Items > **More Wild Food in Multiplayer** (off; on, bushes, palms, vines and mushroom patches grow half as
  much food again in multiplayer). Your config is otherwise untouched.

## 1.5.6

For PEAK 3.0.0.

* Checked against PEAK 3.0.0 (cross-platform play, 208 maps loaded from map bundles): every rule works as before,
  and the map picker lists all 208 maps and loads a picked one from its bundle.
* Map picker: hold an arrow down to run through the maps (PEAK now has 208), faster and faster; a click is still
  one map.
* Snowballs from ascent 14: holding one chills you 2.5 a second (was from ascent 17), and it melts after
  20 s in your hands; its use bar shows what is left, and stashed or on the ground it keeps.
* Campfire saves (PEAK 3.0.0, every ascent): a save loaded after a crash keeps the petrification lighting the fire
  took off (20; from ascent 17 only 10). The game's save took it off the arrows stuck in you instead, so loading put
  it all back. Reloading never takes off more: the save is written once, as the fire is lit.

## 1.5.5

For PEAK 2.6.b.

* Toxic Drink: it cools you by 60 heat (was 30, the Energy Drink's); from ascent 14, as every cure, 60% of it (36).
  From ascent 14 it wakes you by at most 80 drowsiness (its boost drained it all, as the Energy Drink's does).
* Energy Drink at ascent 14: it keeps the game's boosts, walking +50% and climbing +150% (was +30% and +70%). Its
  crash from ascent 15 (50 drowsiness) stays; the Cherry Lollipop's boost keeps walking +30%.

## 1.5.4

For PEAK 2.6.b.

* A new setting, Boarding Pass > Boarding Pass Extras (on by default), turns off the mod's additions to the
  boarding pass: the Blind Mode and Hot Mode boxes and the map picker. Off, none of them shows, both modes stay
  off (your ticks are kept for when it is on again) and every climb is on today's map. A change shows at once on
  an open boarding pass.
* Blind Mode and Hot Mode boxes: each takes the click over its box and name only. Their rows reached across the
  header to where True Final Ascent puts its light/dark switch, so a click there hit a box instead.

## 1.5.3

For PEAK 2.6.b.

* Map picker: its arrows go by number, today's map keeping its name among them (from TODAY'S MAP (9), left is
  MAP 8 and right MAP 10), and they have the game's button hover and click sounds.

## 1.5.2

For PEAK 2.6.b.

* Shelter from wind storms: a hill, rock or wall on the wind's side now shelters you from a wind storm the way it
  already did from a snowstorm (its push and its hold on a climber's stamina), on open low ground too. In the open
  you are as exposed as before.
* Shelter from snowstorms and wind storms works on every ascent (it was from ascent 9).
* Map picker on the boarding pass: at the bottom right of the ascent card, the map's name in a blue rounded
  outline with "<" and ">" beside it picks any map of the game's current rotation (the 21 maps it goes through,
  one a day). It opens on today's map, "TODAY'S MAP (9)"; the others follow as "MAP 0", "MAP 1"... without
  today's again. The pick stays for the rest of the day (in the mod's save, between climbs and restarts) and goes
  back to today's map when the day changes. The host's pick is the map everyone climbs; guests do not see the
  picker. True Final Ascent's FINAL ASCENT keeps its own map.

## 1.5.1

For PEAK 2.6.b.

* Six-Slot Backpack in multiplayer: it no longer turns into a regular Backpack once it has been worn. The game
  brings any worn backpack back as "Backpack" (taken off into the hands, dropped, swapped, carried, its scout
  dead); now the six-slot one comes back as itself, with all six slots.
* Ascent 15: its boost no longer applies to the night's own cold or the scorching sun's own heat (ascent 23's sun,
  Hot Mode's, and the game's Mesa sun where ascent 18 does not set it); they land as they come. At night from
  ascent 18 that is 0.75 cold a second (was about 1).

## 1.5.0

For PEAK 2.6.b.

* Ascent 17: the Caldera's last stretch is ablaze: from the Caldera's statue up to the campfire that opens the
  Kiln, an eruption breaks out every 2-6 s while anyone is on it, spread all over the stretch. No eruption breaks
  out within 20 m of that campfire or the statue (the Caldera's other eruptions keep off them too).
* Ascent 17: the Caldera's own eruptions break out on the ground or the lava's surface where they land. The game
  puts them all at the lava's lowest level, where their heat never reached the ground around them (nor anyone at
  high tide), so they did no harm.
* Ascent 17: an eruption at its height (about 21-30 s into it) burns 20 heat a second, however many of its heat
  zones reach you (was up to 192 a second); before that its mild heat burns 6 a second per zone as before.
* Ascent 17: awake zombies (the Peak's grassy ones too) share out the scouts instead of all going for the nearest:
  every scout gets one first, and the rest go for the scout with the fewest after them, the nearest of those. A
  zombie chases the scout it was given for at least 10 s, and only scouts within 100 m.
* Ascent 14: the Rope Cannon and the Anti-Rope Cannon reach 25 m (the game: 30); the Rescue Claw reaches 20 m aimed
  level or up and 30 m aimed down (the game: 30 and 40).
* Ascent 17: Lookers show up on 90% of their hiding spots (was 25%).
* Ascent 17: the Caldera's condors fly 1.35 times as fast: circling, diving at a scout, carrying them off and
  flying back.
* Ascent 18: the Caldera's lava river rises 14 m (779 to 793; was 14.5 m).
* Ascent 25: in the Caldera and the Gloom storms no longer bring cold or heat, and the wind and the snowstorm
  never blow at the same time there (the one that would start later waits for the other to end).
* Ascent 22 reworked: poison and spores no longer make you stumble, and heat no longer drains stamina while
  sprinting or climbing. Instead, episodes come more often the more of that ailment you have (about every 30 s
  with a full bar, none below 10): spores numb you for 20 s and then you burst in a Shroomberry's spore cloud,
  weaker than its own (scouts within 5 m are knocked down for up to 1 s, a little spores); from 70 cold you are
  now and then knocked down for 5 s, shivering where you land (cold still slows you as before); heat makes you
  dizzy for 6 s (the view closes in dark from the edges and sways, fading in and out smoothly, and stamina comes
  back half as fast); eating while poisoned may bring nausea (25 poison: one time in
  two; 50: always): that food and anything eaten in the next 3 s restores no hunger, and you stand bent over,
  hands on your knees, for 3 s while your stomach growls. Weight, injury and drowsiness are as before.
* New items on every ascent: with the "New Items" setting on, the mod's new items (the Climbing Chalk, the
  Lightweight Parasol, the Auto-Parachute, the Warp Compass, the Toxic Drink, the Vanilla Wafers, the Cherry
  Lollipop, the Torch in suitcases, the wild foods, the Stones and Sticks) turn up on every ascent, not only
  from ascent 9. The Torch swings on every ascent too.
* On every ascent, not only from ascent 9 (or 17): the Ritual Dagger on mushroom zombies, breaking free from
  webs and fly traps, and a Rescue Claw that catches a webbed scout pulling them out of the web.
* Ascent 14: the Parasol no longer pulls you down. It slows your fall as in the game but wears out: after 20 s of
  gliding in all it is used up, its use bar showing what is left. The Lightweight Parasol never wears out.
* Lightweight Parasol: clown chests hold it too, 0.5% of what they pick.
* Blindness (a ghost's burst, a shroomberry, a clown chest's trick) is cured by the First Aid Kit and the
  Cure-All: the blindness and the darkened screen both go.
* Ascent 17: an arrow trap's green warning line shows only its first half metre out of the opening.
* Ascent 17: the peak's grassy hill (after the Kiln or the Citadel, below the summit with the flares) has ten
  zombies dressed as scouts, in two looks: yellow in the fairy outfit and flower hat with sparkling eyes and a
  toothy grin, or lime in the green tee with cargo shorts and the sprout hat with solid angry eyes and a small
  frown; both freckled, without sash, medal or zombie mushrooms. A player with the game's zombie phobia setting
  sees the game's mushroom men.
* Ascent 17: twenty bee swarms without hives hover over the peak's hill. A swarm a scout comes near turns angry
  and chases them like a tropical ambush swarm (50 s, up to 120 m from its place), then disperses once it has lost
  them. The game's beehives and the Tropics' ambushes are as before.
* Lightweight Parasol under low gravity (an Initiative gem's, a shroomberry's): held open in the air, it lifts
  you up the way balloons do (as much as eleven balloons), on every ascent; from ascent 14 half as high.
* Gloom ravens: the eating is heard (it played at the volume of a scout eating, which a raven a few metres
  away left barely above the Gloom's own sound).
* Climbing Chalk: its effect lasts 20 s (was 16), from ascent 14 15 s (was 12). A use takes 1 s (was 2 s, the game's). Its rub sound plays once and fades out (it was
  played twice in a row to fill the 2 s use).
* Toxic Drink: 4 times as fast for 10 s (was 5 times); from ascent 14 3 times for 7 s (was 4 times
  for 8 s). Walking and climbing alike, as before.
* Stone: its rock smash is heard. It played at the coconut break's volume, which left it barely above the
  beach's own sound; now at full volume (about as loud as the Coconut's bonk). Hitting a scout or a zombie
  makes only that smash; it no longer also plays the Coconut's bonk.
* Wonderberry (14): 5 s of infinite stamina (was 6; 10 below 14).
* Cherry Lollipop: its drowsiness is the Big Lollipop's and 20 more on every ascent, with the ascents' changes on
  the 20 too (from ascent 14 that ascent's Big Lollipop crash and 20 more: 85 + 26 from ascent 15, 65 + 20 at 14).
* Six-Slot Backpack in multiplayer: a new setting, Items > Six-Slot Backpack in Multiplayer (the host's,
  off by default). With it on, the Backpack by the crashed plane at the start of the beach is a Six-Slot
  Backpack, an item of its own with six slots, the pouches and the icon; every other backpack stays the
  game's. Its contents reach every player with all six slots; Scout's Generosity clones it only for 100
  petrification, which is death.
* Ritual Dagger on zombies: every stab that counts is heard by everyone (the game's sound of a scout taking a
  hit; the scout who stabs hears it at their own ears, as loud as their own hits, everyone else at the zombie);
  before, a stab made no sound you could tell apart, and none at all on the Roots' mushroom men.
* Crab: the Ritual Dagger's stab that kills a zombie takes 15 crab off the scout who made it (exactly 15,
  ascent 14 included). Pandora's Lunchbox (14) rolls its crab at half the share, as the game rolls its curse.
* Ghosts (17): the Gloom's ghost blinds for 50 s (was 80). A burning Torch in a scout's hands keeps the
  ghost away like a lit bell tower (from ascent 9; below 17 the push burns it, as the bell towers do).
* Citadel (17): saw blades wobble 1.5 times as fast too, alongside running along their walls faster (as
  the spike rollers sweep round and turn on their axles). Ladders: 1.2 times rock's climbing
  speed (game: 3) for 90% of its stamina (game: 25%). The game's mimic chests look like ordinary chests,
  and 35% of the other Citadel chests are mimics (was every second one), picked anew each climb.
* Explosions (17): only a cooked Lantern's or Faerie Lantern's burst and the Gloom ghost's burst still knock you
  out (drowsiness up to passing out); every other explosion (dynamite, geysers, exploding spore mushrooms, the
  Citadel's floor spikes, fireworks...) keeps its stronger status, push and knock-down but no longer knocks you out.
* Arrow traps (on every ascent): a swinging spike ball crossing an arrow trap's line no longer sets it off.
* The Nadir: from ascent 17 drowsiness, poison, heat and spores heal half as fast there and blindness and
  numbness wear off half as fast; from ascent 18 gravity pulls twice as hard there.
* Warp Compass: when it does not warp, a line in the middle of the screen says why (no one to warp to,
  everyone too close, no one on solid ground, no room to land), in the game's lettering, without sound.
* The Gloom's ravens (17): three times in four, a raven comes for an Early Worm a scout sees lying close
  by, from the nearest perch (the raven sitting there takes off) or out of the sky; it lands beside it, eats
  it (with the sound a scout makes finishing food, heard by everyone around) and flies off. A worm picked up first is not eaten.

## 1.4.0

For PEAK 2.6.b.

* Snow in the Alps: a light snowfall drifts down around you wherever the ground below is the Alps', on every
  run (from the Alpine's start, not yet at the campfire below it). Only for the look (no cold, no wind),
  thin enough to keep the view clear; it stops under a roof or an overhang.
* Shelter from snowstorms (from ascent 9): a hill, rock or wall on the side the snowstorm blows from (rising
  at least 12-25 degrees as seen from your head, within 60 m) shelters you, in the lowlands too and on True
  Final Ascent's terrain; the game only looked at the open sky above you. In the open you stay as exposed
  as before. Only snowstorms; True Final Ascent's own weather is left as it is.
* Challenges: Blind Mode and Hot Mode, two boxes in the boarding pass's header, each player's own, on every
  ascent. Blind Mode keeps the screen as dark as the Scoutmaster at his closest all climb (no sound; he
  makes it no darker). Hot Mode brings ascent 23's sun to ascents below 23, in the same places: held items
  incinerate the same, scouts are heated at 65% of its strength (on 23 and higher the box is fixed on).
* Cosmetics: the game's unused VR Headset, Orange Spacesuit and Orange Helmet are in the passport. VR
  Headset: reach the peak with Blind Mode on for the whole climb. Orange Spacesuit and Helmet: reach the peak
  in the Astronaut suit and helmet with Hot Mode on (or on ascent 23+). A locked one tells how to unlock it
  when clicked; the run's statistics show new ones as small icons; no Scout badges. Unlocks and choices live
  in the mod's own save, not in Steam.

## 1.3.2

For PEAK 2.6.b.

* Six-slot Backpack (single-player): in solo play the Backpack holds six items instead of four, at every
  ascent. Four orange pouches sit on its sides (two stacked on each) in place of its side handles; slots 1
  and 2 hang in the bag, 3 to 6 in the pouches (the upper pouches' items stand high enough to leave the
  lower ones in view), and its wheel has seven slices (wearing it and six slots). Its contents save and
  load with all six, and it has its own icon (the game's unused six-slot backpack picture, toned to its
  Backpack icon's colours). In any online lobby every backpack stays the game's. The new setting Items >
  Six-Slot Backpack (on by default) switches it off, single-player included.

## 1.3.1

For PEAK 2.6.b.

* The Kiln's hot ash (ascent 18) does not burn at the top of the Kiln (as high as its lava rises, or
  higher), and within 3 m of a petrifying stone it neither shows nor burns: the haze clears within half a
  second and the smoke and flakes around fade.
* Ascent 17: lighting a campfire takes only 10 petrification off (the game: 20) from ascent 17, with the
  extremely hot campfires; it started at 16.
* Ascent 17: the Gloom's big ghost comes every 30 s (was 50 s; the game: 60-180 s), counted while none is out.
* Ascent 18: the Caldera's lava river rises 14.5 m (779 to 793.5; was 15 m).

## 1.3.0

For PEAK 2.6.b.

* The Kiln, new hazards. From ascent 18 hot ash fills it once a minute once its lava rises: 5 s of
  rumbling as the air thickens, then dark smoke and drifting ash flakes all around and 3 heat a second
  for 15 s, 45 in all, the same moment for everyone. From ascent 17 it erupts like the Caldera, taking
  turns with the ash (never with it, at most 10 s apart): once a minute, 2-9.5 s after the ash clears, a
  column of fire breaks out 18-32 m from one of the scouts in it and burns 30 s.
* Cherry Lollipop (was the Pink Lollipop): gives the Big Lollipop's infinite stamina and the Energy
  Drink's speed boost at once, for 6 s (4 s from ascent 14), from the first lick; then the Big Lollipop's
  drowsiness, not the Energy Drink's crash.
* Vanilla Wafers (the vanilla Scout Cookies, now with a name of their own in game): 5 hunger and 20 extra
  stamina a bite (cooked: 10 and 30). Both renamed items' Custom Run switches keep their settings.
* Toxic Drink: the poison after the boost is 4 a second for 5 s (8 a second from ascent 14), not 4 in
  all; Item Stats and Effect Preview show it now. The can wears a cooked Energy Drink's tint, so the label
  no longer glares, and its inventory icon is rendered again from that can.
* Scout's Generosity clones the Warp Compass and the Toxic Drink at a mystical item's cost (the game: 45
  petrification instead of 25; ascent 14: 60 instead of 35); Effect Preview shows that price.
* Weird Shroom: 5 hunger (was the game's 10).
* Auto-Parachute: after the landing the canopy folds away 3 times as fast. Its backpack wheel on the
  ground said "loc: name_5" instead of "Wear Auto-Parachute".
* Breaking free from a spider's web or a fly trap (from ascent 9): jumps while caught need no stamina and
  cost none, and a jump pressed too early counts as soon as the next one can. The game counted them only
  with stamina left, which ran out half way at the higher ascents, and dropped early presses.
* Ascent 9: the Citadel's gloom reaches the top in 700 s, as the Kiln's lava (was 2.5 times as fast, 480 s).
* Ascent 13: the items a fall drops pass through the scout who dropped them until the scout is clear of
  them. The game makes them right beside the landing scout, whose tumbling body knocked them about, and
  lets the item from the hand collide again after 0.5 s even if it is inside the scout by then, which
  shoots it out at 10 m/s.
* Ascent 14: Fortified Milk makes you invincible for 12 s, cooked 20 s (the game: 15 s, 25 s); Scout's
  Tenacity for 8 s (was 5 s; the game: 10 s).
* Ascent 16: heat heals at 0.5 a second in the Caldera and the Kiln (was 0.6; the game: 1.3).
* Ascent 17: mushroom zombies run wherever they go, as when they lunge (the game: they walk until they
  have chased you for 3 s). Slipping on a banana peel darkens the screen 3 s later (was at once), and
  every sound is muffled while it lasts, as in True Final Ascent's Ice Fog. The Citadel's spike rollers,
  swinging spike balls and wall saws move 1.5 times as fast, and its pressure plates fire 1.5 times as
  soon (0.53 s after the step; the game: 0.8 s). A cactus ball held in the hand counts its doubled thorns
  too (40 on the bar; it stayed at the game's 20).
* Ascent 18: the Caldera's lava river rises 15 m (779 to 794; was 14 m).
* Ascent 23: the scorching sun (its heat and its roasting of sun shields) stops at the Caldera and the Gloom.
* Russian names for the new items (and the Torch's and the Stick's swing prompt).
* The config file is no longer rewritten when the mod loads: an update only adds its new entries (with their
  defaults), and entries an older version had stay as they are (they used to be removed).

## 1.2.1

For PEAK 2.6.b.

* New: the Torch, the game's (three hang in the Mesa's Tomb), turns up in suitcases from ascent 9
  wherever the Lantern does, half as often; its own switch on the Custom Run screen, after the
  Lantern's. Lit, it is also a weapon: the right mouse button swings it like the Stick (33% stamina),
  knocking every scout and zombie it reaches down for 2 s; the scouts take 10 heat (20 from ascent
  14), zombies none. A swing burns 4 s of its 40 s of fuel (a full Torch: 10 swings); unlit it cannot
  swing. Lit, it warms like the Lantern: from ascent 14 2 cold a second (it kept the game's 2.5).
* Toxic Drink: the can is purple (was toxic green), with a new inventory icon. Its label is drawn
  without the checker pattern the game's item shader prints over cans, which made it look grainy.

## 1.2.0

For PEAK 2.6.b.

* New item: the Auto-Parachute, the game's cut item, is worn in the backpack slot (slot 4) instead of
  a backpack. It turns up where the Jetpack does, as rarely (Mythic), from ascent 9, opens by itself
  after 1.5 s of falling and saves you from the fall. Three charges (from ascent 14 a save takes two);
  with the last one it is gone. Its own switch on the Custom Run screen.
* New item: the Warp Compass, the game's cut item, from ascent 9. It turns up only in Ancient
  Luggage and in the Ancient Statues, as rarely as the Ritual Dagger, and never in solo. One use: it
  warps you to a teammate who has stood on solid ground for a second, onto free ground beside them
  (the game's compass put you inside the teammate: in the wall they climbed, or in the air with
  them); with no such teammate nothing happens and the use is kept. Its own switch among the Custom
  Run screen's mystical items.
* New item: the Toxic Drink, an energy drink in a toxic-green can. It turns up only in Ancient Luggage
  and in the Ancient Statues, as rarely as the Faerie Lantern, from ascent 9. Drunk, it makes you walk
  and climb 5 times as fast for 10 s (from ascent 14: 4 times for 8 s), then poisons you for 4 over
  5 s (8). Cooked, it bursts when drunk, like a cooked Energy Drink (a knock-down and a push; ascent 17
  does not take the burst for an explosion), and no poison follows. It sounds like the Energy Drink
  sped up 1.5 times. Its own switch among the Custom Run screen's mystical items.
* New: the Pink Lollipop, the Big Lollipop in pink and white with its effects, takes half of the Big
  Lollipop's spawns from ascent 9. Its own switch on the Custom Run screen, after the Big Lollipop's.
* New: the game's unused Vanilla Scout Cookies take half of the Scout Cookies' spawns from ascent 9,
  with the same name and effects.
* New: three cut foods grow in the wild from ascent 9: the Wonderberry (20 hunger, 10 s of infinite
  stamina) on 0.2% of the Crispberry bushes, the Green Clusterberry (10 hunger, removes 30 poison) on
  thorny vines as often as each of the other Clusterberries, the Weird Shroom (10 hunger, glows for
  60 s) in mushroom patches as often as the Cluster Shroom. The Weird Shroom's glow, broken in the
  game, works. From ascent 14 the Wonderberry gives 6 s of infinite stamina and the Green
  Clusterberry removes 15 poison (both restore half the hunger, like all food there).
* New: Stones and Sticks lie on the ground from ascent 9, up to 20 of each on the beach and 20 of
  each in the Tropics or the Roots. A Stone thrown hard at a scout knocks them down for 2 s and
  injures them for 10 (20 from ascent 17); it shatters on any hard impact, with a rock smash and a
  small gray dust cloud. Swinging a Stick costs 33% stamina and knocks down for 3 s, without hurting
  them, every scout and zombie a kick would reach in front of you.
* New: from ascent 9 the Ritual Dagger works on mushroom zombies, the Roots' and scouts who turned
  into zombies. Stabbed in the back (as a teammate must be), a zombie dies after 5 stabs (8 from
  ascent 14) and leaves a skeleton. Stabbing a zombie gives nobody anything, and the dagger is kept.
* New: from ascent 9 the Lantern turns up in the suitcases of every part of the climb up to the
  Caldera and the Gloom (the game: only the Tropics' and the Alpine's).
* Config: "New Items" covers the new items too. The wild foods, Stones and Sticks have no switch on
  the Custom Run screen (they never spawn in suitcases), so in custom runs too they follow "New
  Items".
* Custom Run screen: the Vanilla Scout Cookies have their own switch, after the Scout Cookies' (they
  followed the Scout Cookies' switch). With both on they take half of the Scout Cookies' spawns, with
  the Scout Cookies off all of them.
* Climbing Chalk: climbing costs 60% of its stamina while chalked (was 75%), 75% from ascent 14 (was
  85%). It is held upright like the Granola Bar, the hands round its lower front corners (it was held
  by the middle of its sides, the placeholder cube's grip).
* Ascent 14: a Magic Bean's vine sticks 2.5 thorns into you every 3 s instead of every second, and
  now also while you climb it (it was only while you stood on it).
* Ascent 14: a lit Lantern takes 2 cold a second off the scouts near it (was 1.5; game: 2.5).
* Ascent 17: holding a snowball chills you (2.5 cold a second); this was part of ascent 14. A snowball
  that hits a scout still gives 30 cold and a 2 s knock-down from ascent 14.
* Ascent 17: the burst of a cooked Energy Drink no longer knocks the drinker out (it took it for an
  explosion: drowsiness up to passing out, death at ascent 24). It still pushes and knocks down as
  hard as an explosion.
* Ascent 18: the Caldera's lava river rises 14 m (779 to 793) instead of 16 m. The game's own rise
  and fall, which ascent 18 replaces, is about 6 m.
* Ascent 18: the Mesa's midday sun burns 6 heat a second (the game: about 1.6, 2.4 with ascent 15),
  and the Gloom's fog makes you 2.5 drowsy a second (the game: 0.9, 1.17 with ascent 15). A tornado
  that catches you spins, drags and lifts you 2.5 times as hard.
* Ascent 18: only sunscreen keeps off a snowstorm's heat by day outside the Alpine (nothing did; an
  open parasol or a glider still does not), from ascent 14 all but a quarter of it. While a
  snowstorm blows on you the sun does not burn through it: the Mesa's sun and ascent 23's neither
  heat you nor roast what shields you, so the storm's heat is the only heat then. Storms reach the
  sun at ascent 25.
* Ascent 25 with True Final Ascent's Apocalypse mode: the rain, wind and snowstorms run in the breaks
  between TFA's weather events (TFA kept the game's storms quiet the whole time); during one, the
  weather is TFA's alone. The tornadoes roam throughout, as before.
* Fixed: ascent 17's 4 s knock-down from a mushroom zombie's bite only worked for the host; everyone
  else was knocked down for the game's 3 s.
* Fixed: ascent 18's stronger tornadoes did nothing (they were meant to be 4 times as hard).
* Fixed: ascent 25's gentler snowstorms outside the Alpine only applied with ascent 18 switched on;
  with it off, snowstorms everywhere chilled at the Alpine's full strength.
* Fixed: on the Custom Run screen the mystical items ran past the bottom of their panel once the mod's
  items made the lists longer; the rows of both lists get just short enough for everything to fit.
* Fixed: on the Custom Run screen the Rope Spool's switch sat on the dotted line above the mystical
  items and could not be clicked there (the Climbing Chalk and the Lightweight Parasol make the item
  list one row longer). The line and the mystical items now move down to make room.
* Fixed, with True Final Ascent 2.9: in FINAL ASCENT the mod's nights ended at the Alpine (its map
  has ten progress points instead of five). They now end where TFA ends its cold nights, at the
  Mesa's campfire, so ascent 22's drowsiness that doesn't heal at night also applies on the Alpine's
  and the Mesa's nights there.

## 1.1.2

For PEAK 2.6.b.

* Config: "Cold Nights" (section "Ascents") switches off the night's own cold, the game's rule from
  ascent 5 (with ascent 18's harsher and longer Alpine nights), on every ascent; cold from anything
  else stays, and custom runs keep their own Cold Night setting. The host's switch applies to everyone.
* Config: "New Items" (section "Items") keeps the mod's new items out of regular runs: the Climbing
  Chalk does not spawn, and the regular Parasol spawns wherever the Lightweight Parasol (the Roots
  parasol from ascent 9) would. Custom runs keep their own switches. The host's switch applies to
  everyone.
* Ascent 14: thrown Shelf and Cloud Fungus fly as if 1.5 times as heavy, the Warp Fungus as if 1.35
  times (all three were twice as heavy).
* Ascent 17: a Mandrake's scream also makes players drowsy who can't hear it (sound-effects or master
  volume at 0), at half strength: up to 37.5 per scream (was none, as in the game).
* Ascent 18: rain pulls you down much less while you are in the air under open sky: an extra 40 m/s²
  (60 in the Tropics) instead of 200 (400), about twice (three times) the pull of a normal jump, so
  jumps in the rain get off the ground again.
* Climbing Chalk: the bag is drawn with the game's own item shader, like every other item, instead of
  the plain material of the placeholder cube it replaces. It keeps its shape in the shade (it turned
  into a flat cut-out there), keeps its colours, and lights up when you look at it.
* Fixed: ascent 17's Mandrake scream gave only about 55 drowsiness instead of 75. The game lands 8
  hits per scream, not the 11 the mod counted on.
* Fixed, with True Final Ascent: ascent 17's rules for the Mandrake did not apply. TFA gives every
  Mandrake its own voice lines, and the mod took that for one of TFA's event spawns, which keep TFA's
  numbers. TFA's voice lines stay as they are.

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
