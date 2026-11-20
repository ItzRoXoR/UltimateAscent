# Building and testing

[Ultimate Ascent](../README.md) documentation.

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
(and, at ascent 25, that the weather is left to TFA). `-WithTfa` adds True Final Ascent and its libraries
to the throwaway profile (copied from `-Profile`, which is only read), and `-Chain "S,A,K*200,N"` climbs
TFA's FINAL ASCENT as that chain of its Builder (`-Only tfabuilder` checks each block). `-BuilderMaps <folder>`
(with `-WithTfa`) installs those Builder maps in the throwaway profile; `-Only buildermap` climbs the first of them.
`tools/pack-buildermaps.ps1 -Maps <folder>` packs the Builder maps into their own zip for the release.
