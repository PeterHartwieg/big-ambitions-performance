# City Performance changelog

## 0.5.0 (22 October 2026), first Workshop candidate

- Camera-aware world coverage now starts only when the HART Free Camera bridge is loaded. Without it the game keeps its own coverage, camera far clip, decal distance, traffic observers and pedestrians. Before this, a game without HART Free Camera still got wider main-camera coverage at 150%.
- Each setup step runs on its own. If a game update breaks one feature, the log names it and the other features still load. Before this, one failing step skipped every step after it and switched off pedestrian refill.
- If a feature's per-frame tick, city start or city stop fails to load after a game update, that feature stops for the session and logs once instead of throwing every frame. Game exceptions still propagate as before.
- A HART Free Camera bridge that throws is dropped for the session, and coverage returns to vanilla.
- Saved choices are read per feature, so one broken feature cannot block the settings of the others.
- When the game build is not the audited one, the version line in the log says that only timing and trace hooks run.
- The 30-second summaries no longer print camera lines in a game without HART Free Camera.
- The option label says coverage needs HART Free Camera.
- Packaging reads the version from the project file, refuses a mismatch with the source and ships this changelog.
- New offline check: `tools/DetailCheck --vanilla` runs every installer with no other mod loaded, on the audited build and on another build.

Option defaults are unchanged. Pedestrian refill, the stationary-zone budget, tree detail, midnight cleanup batching, background autosaves and all trace options stay off. Bakery Help and Hyperreal lamp discovery stay on and run only when those mods, in their audited versions, are loaded.

## Earlier versions

0.1.0 to 0.4.4 were local test builds for the HART series pack. 0.4.2 is the one installed on the series PC. 0.4.3 added the Hyperreal lamp query and 0.4.4 took over Bakery Help from Hard Mode Patch; neither was installed. The README and `docs/reviews/` hold their notes.
