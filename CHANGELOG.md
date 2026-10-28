# City Performance changelog

## Unreleased

- Ownership handoff preserves the full target of a refill call already in flight, while newer requests prevent stale restoration. Failed mod teardown now cancels pending refill and retains the observed Persistent City ownership decision even if CP's hooks remain installed.
- General pedestrian refill smoothing now stands down when Persistent City's assembly is present, including when it loads after City Performance. Pending refill work is cancelled without flushing, and one status line identifies Persistent City as the owner. Without Persistent City, the existing audited-build and conflict guards and refill behavior remain unchanged. Offline checks cover both load orders; native gameplay validation is still pending.

## 0.5.0 (22 October 2026), first Workshop candidate

- Experimental first Workshop release. All behavior features and experiments now default on; performance summaries, trace recording, automatic trace export and extra diagnostics default off. Existing saved choices are preserved.
- If problems occur, turn on Performance summaries (logging), then disable features one by one and repeat the same route or action to find the best setup for your city.
- Audited-build and conflict guards remain active. Bakery, Hyperreal and HART Free Camera features require their supported dependencies.

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

Release defaults approved 23 October: all behavior features and experiments are on. Logging and trace tools are off. Existing stored choices and all audited-build/dependency/conflict guards remain in force.

## Earlier versions

0.1.0 to 0.4.4 were local test builds for the HART series pack. 0.4.2 is the one installed on the series PC. 0.4.3 added the Hyperreal lamp query and 0.4.4 took over Bakery Help from Hard Mode Patch; neither was installed. The README and `docs/reviews/` hold their notes.
