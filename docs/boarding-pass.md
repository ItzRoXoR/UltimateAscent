# Choosing an ascent

[Ultimate Ascent](../README.md) documentation.

Open the boarding pass at the airport and use the arrows, like vanilla.

* **Unlocking:** ascent 9 unlocks once you have beaten vanilla ascent 8; beating ascent *N* unlocks *N + 1*.
  The game's own Steam stat stops at 8; progress beyond is saved in the mod's config.
  Set **Unlock All Ascents** to skip that. Ascents 9–25 give no sash promotion; ascent 25's reward line shows the
  Pain Sash (see [challenges and cosmetics](challenges-and-cosmetics.md)) until you have unlocked it.
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
  ASCENT keeps its own map. With **Map Biomes** on ([configuration](configuration.md)), every map but today's shows its biomes by their
  first letters: **MAP 90 (STMC)** is Shore, Tropics, Mesa and Caldera (R: Roots, A: Alpine, G: the Gloom). The
  reward line sits under the ascent's text, clear of the map ("And all previous modifiers." a little closer to the
  text than in the game, to make room for it).
* **Builder maps** (an optional download, with True Final Ascent): 300 maps built with TFA's Builder, each with the
  original maps' layout (the Shore; the Tropics or the Roots; the Alpine or the Mesa; the Caldera and the Kiln or the
  Gloom and the Citadel; then the Peak, and the Nadir through the gem statue), the layouts and each biome's variants
  (Black Sand, Snake Beach, Sky Jungle, Geyser Hell…) spread evenly over them. With the pack installed the map picker
  lists them after the original maps, **Builder: MAP 1** on (not in custom runs). A picked one plays as TFA's FINAL
  ASCENT on that map, on the ascent the pass shows, with that ascent's rules; only the host needs the pack (TFA sends
  the map to the others, who need TFA). The pack is its own zip on the release page: import it in r2modman
  (Settings > Import local mod), or put its BuilderMaps folder anywhere under `BepInEx/plugins`.
* **Ticket:** **GATE** is the ascent's number (Tenderfoot and Peak 0), **ATTEMPTS** the runs you have started on it
  (on any map; a campfire save resumed is the same attempt) and **COMPLETIONS** the runs on it that someone won. Your
  own count, kept in the mod's save from this version on; custom runs count nothing and show dashes. TFA's FINAL
  ASCENT counts on its ascent's ticket, unless TFA keeps that chain of its Builder from earning achievements. A passenger's name too long for the room before them is made smaller, then cut short.
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
