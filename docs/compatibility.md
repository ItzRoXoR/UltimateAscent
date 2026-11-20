# Compatibility

[Ultimate Ascent](../README.md) documentation.

* **True Final Ascent 3.0.4** — supported (its FINAL ASCENT on the boarding pass: see [choosing an ascent](boarding-pass.md)); the Builder maps are there too. TFA's Final Ascent map stacks all biomes; the mod
  uses TFA's own stage tracking there, keeps each Caldera's lava tide in its own basin,
  ends the nights where TFA ends its cold nights (TFA 2.9 on: at the Mesa's campfire; not where another
  mod's biome has mild nights), and leaves TFA's fog, weather and network keys alone. Its run records,
  daily board and Continue panel show ascents 9–25 by name.
  * **Builder:** in a chain of TFA's Builder every block plays by its own biome's rules wherever it
    stands in the chain, repeats included: a Nadir block is the Nadir (ascents 9, 17, 18 and 25), a
    Kiln anywhere has its ash and eruptions, every Caldera its own tide and last stretch ablaze, every
    Shore, Tropics and Alpine block its own crabs, snakes or goats (and stones and sticks), and the peak after the
    chain's last block its zombies and bees. One storm of
    each kind blows at ascent 25 however often a biome repeats. TFA's rest stops spare the resting scouts
    from this mod's storms, suns and extra tornadoes too, its rest island stands clear of ascent 18's
    higher tide, and its invisible walls between blocks do not shade this mod's suns. A climb counts on
    the ticket, and unlocks the mod's cosmetics, only where TFA lets the chain earn achievements.
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
  * **Other mods' weather:** a TFA weather another mod starts through TFA (TFA 3.0 on) is handled the same
    way, in any climb, with or without Apocalypse: what it does lands as TFA gives it, the sun rules rest,
    ascent 11's Scoutmaster hunt steps aside for its Blackout and Watch, and ascent 25's storms keep calm
    while it runs.
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
  its own icon; slots 1 and 2 hang in the bag, 3 to 6 in the pouches, and its wheel has a slice for each, laid out
  as the backpack seen from behind (on each side the bag's slot at the top, the upper pouch's, the lower pouch's).
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
