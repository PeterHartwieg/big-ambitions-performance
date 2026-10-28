# City Performance 0.5.0: release readiness (22 October 2026)

Status: Workshop candidate, built and checked offline on the Mac. Not uploaded, not installed anywhere, not run in the game. The series PC still runs 0.4.2. 0.4.3 (Hyperreal lamp query) and 0.4.4 (Bakery Help takeover) were never installed either, so this release carries three versions of unplayed code. The in-game checks at the end come before the upload.

Peter's goal for the release: it must work in a game with no other mods, and features for other mods start only when it detects them.

## Hook audit

Every feature, what it hooks, what it needs, and what a game without that dependency gets. "Audited build" means `BigAmbitions.dll` SHA256 `9427561b81aaab2ada09c22a3c98dd9a4017a65225b389513885bf336852e911`, Windows build 3682.

| Feature | Hooks | Needs | Without it |
| --- | --- | --- | --- |
| Pedestrian refill | `PedestrianSpawner.SetActivePedestrians` prefix and finalizer | Audited build | No hook. On the audited build the hook is a pass-through while the option is off. |
| Stationary-zone budget | `StationaryAiSpawner.SpawnGroupOverFrames` postfix, `ClearPedestrians` prefix | Audited build, no foreign hook on the spawner methods | No hook. Persistent City hooks `StationaryAiSpawner.Spawn`, so this feature withdraws when it is loaded. |
| Midnight cleanup batching | One call site in `GameManager.NewDay`, transpiler | Audited build, no foreign hook on `NewDay`, no Going Public (`BigAmbitionsMP`, `GoingPublic`) | Original call runs. The transpiler stays in place on the audited build but calls Unity's cleanup directly while the option is off. |
| Background autosaves | Save path, Odin and loader gates | Audited build and nine audited save dependencies, option on | Nothing installs while the option is off. |
| Tree detail | `TreeController` map handler postfix | Audited build | No hook. |
| Bakery Help | Three hooks in Bakery's `BakeryHelpNavigationPatch` and `HelpSystem.LoadCategories` | Assembly `Bakery` with SHA256 `6d68edab…ee3a`, plus the audited Windows game, Unity Core and HGExtensions | Logs `Bakery Help: Bakery absent; original Help discovery`. No hook. |
| Hyperreal lamp discovery | Transpiler on Hyperreal's `StreetLights.Scan` | Assembly `PolyModsHyperreal` with SHA256 `7d1bd589…ef10`, plus an audited Unity Core and HDRP pair (Windows or Mac) | Logs `Hyperreal discovery: Hyperreal absent; original behavior`. No hook. Independent of the game hash. |
| Camera-aware coverage, camera audio, extra pedestrians, traffic observers, decal distance | Culling dispatch, audio listener, audio zones, pedestrian targets and pool, Gley traffic, HDRP decal distance | Audited build for the hooks. Behavior needs the HART Free Camera bridge type `Hart.FreeCamera.CinematicCameraBridge`, protocol 1. Traffic also needs `ExternalPlugins.dll` `7d43e9d4…15d1`, decals the Windows HDRP `d0f86930…dfc6` | Since 0.5.0 the hooks pass through and the log says `CINEMA HART Free Camera bridge not loaded…` once. |
| Frame trace and the five timing scopes | Timing-only prefixes and finalizers | Nothing, they run on every build | A method that no longer exists logs `Not active: timing …`. |
| Export button caption | `ModOptionsButtonControl.Initialize(ModOption)` postfix under its own owner `cityperformance.ui.buttoncaptions`; presentation only | Nothing, it runs on every build. It captions only this mod's export button, restores a rebound control only while it still shows this caption, and clears this caption from untracked clones | Other mods' buttons keep their own captions. A changed localizer or TMP API logs `Button captions unavailable: …` once per distinct failure. |
| Extra diagnostics | Timing-only hooks on hourly and daily game methods, subscribers, plus Camera Tools (`CameraTools.CameraToolsRuntime`) and Food Trucks (`FoodTrucks.Catering`) when those are loaded | Option on, audited build for the game hooks | Absent mods are skipped. `AccessTools.TypeByName` never forces a load. |

The built DLL references only `netstandard`, `BigAmbitions.ModAPI`, `0Harmony`, `BigAmbitions`, `UnityEngine.CoreModule`, `UnityEngine.PhysicsModule`, `ExternalPlugins`, `Unity.Mathematics`, `OdinSerializer`, `HGPlugins`, `HGExtensions`, `UnityEngine.UI` and `Unity.TextMeshPro`. All are game files. No other mod is referenced at compile time; every mod is found by assembly or type name at runtime.

### Interactions with the series pack

These come from reading the sibling repos, not from a run.

- Driven Rides and Persistent City both hook `TrafficManager.FixedUpdate`. City Performance's traffic observers treat that as a conflict and withdraw, so with Driven Rides loaded the recording camera gets no extra traffic. Population, culling and audio coverage are unaffected. This was already true for 0.4.2 if Driven Rides was loaded.
- Hard Mode Patch 0.4.1 keeps its own Bakery and lamp hooks and City Performance withdraws from both. HMP 0.4.2 hands both to City Performance. The checks below cover both orders.
- Real Life Overhaul, Dynamic Rivals, Food Trucks and Roadster hook none of the guarded methods. Food Trucks is only timed when extra diagnostics are on.

## What changed for the release

1. Camera-aware coverage now needs the HART Free Camera bridge. Before, it defaulted on in every game and, without HART, still widened main-camera culling bands to 150%, raised the main camera's far clip to at least 300 m, widened decal distance and attached six traffic observers. In a vanilla game that is extra work and a visual change, not an optimization. Peter's setup with HART Free Camera behaves as before.
2. Setup steps are isolated. Before, one throwing installer skipped every installer after it and set the refill fault flag. Now each step logs its own failure.
3. Per-frame ticks and the city start, stop and unload steps are isolated against a changed game build. If one fails to load (a missing type, method or field, or a failed type initializer), that feature stops for the session and logs once instead of throwing every frame or aborting city start. Any other exception, including the game's and Unity's own, propagates exactly as before, as AGENTS.md requires.
4. Saved choices are read per feature, and city start runs in a guard that holds no game types itself.
5. A HART bridge whose getter throws is dropped for the session, and coverage returns to vanilla through the normal off path.
6. Smaller items: one log line when the bridge is absent, no camera lines in the 30-second summary without the bridge, the version line explains an unaudited build, the option label says coverage needs HART Free Camera, the version is 0.5.0, packaging reads it from the project file and ships the changelog.

The default changes approved on 23 October below also enable the vanilla behavior experiments. Change 1 explains the camera dependency. 2 to 4 matter after a game update. The vanilla harness needs the 0.5.0 entry points, so it could not run against 0.4.4; reading the 0.4.4 code, I found no path that throws in a vanilla game on the audited build.

## Defaults

Peter approved the 23 October release defaults: all nine behavior switches are on (refill, zones, trees, cleanup, background autosaves, Bakery Help, Hyperreal discovery, camera coverage and camera audio). Summaries, trace recording, automatic export and extra diagnostics are off. Existing stored preferences win. Cinematic distance stays 150% and cleanup wait stays five minutes.

These are experiments. Players who encounter problems should enable Performance summaries (logging) and disable features one by one, repeating the same action or route to find the best setup. Native binary and conflict guards remain mandatory; enabled does not mean a guard is bypassed. Background autosaves still validate the detached snapshot against native serialization before using the worker.

Preferences are keyed by the runtime mod ID. A Workshop install has a different ID from the `ModsLocal` copy, so a Workshop subscriber starts from these defaults, and Peter's PC choices (refill and zones on, trace on) do not follow him if he switches to the Workshop copy.

## Hash gate

- A Windows viewer on build 3682 gets the same `BigAmbitions.dll` from Steam, so `verified=True` and the same features as Peter's PC. Bakery Help and the lamp query additionally need the exact mod versions above; a viewer on a newer Bakery or Hyperreal gets the original behavior and a log line saying the binary is unreviewed.
- A Mac viewer: the local Mac game hashes to `a06699c9…951e`, so `verified=False`. Only timing and trace hooks run; camera coverage, refill, zones, cleanup, trees and Bakery Help stay off. The Hyperreal query can still run, because its audited Unity pair includes the Mac one.
- After a game update the hash changes and every behavioral feature stays off with a `Not active` line. Bakery Help also stops, since it checks the game hash. The lamp query keeps running only if Unity's Core and HDRP files are unchanged. Timing hooks attach by method name and log `Not active: timing …` for anything renamed. Re-enabling features for the new build needs a fresh audit and a new hash, per AGENTS.md.
- What the gate cannot cover: an update that removes or renames a game type City Performance names directly. Each setup step, lifecycle step and tick now contains that failure. The host's `Update` still reads `LoadScene.isLoading` itself, so an update that removes `LoadScene` would still throw there every frame. That would be a large engine-level change, and the offline check cannot simulate it.

## Checked offline (22 October, this Mac)

All against the 0.5.0 Release DLL built from this branch, with the Windows build 3682 reference set in `build/refs` unless noted. Logs are in `build/workshop-release-20261022/`.

- Release build: 0 warnings, 0 errors.
- Pure core checks: 340 pass (338 before plus 2 for bridge gating).
- New vanilla harness, `tools/DetailCheck --vanilla`: 25 pass on the Windows set and 7 on the Mac game's own Managed folder (the audited-build pass is skipped there because the hash differs). Every installer runs with no other mod loaded and logs no failure. Bakery, Hyperreal and the HART bridge stay inert. Refill is not faulted. An unaudited build installs only timing and trace hooks. A throwing setup step is logged once and contained. The lifecycle guard contains a type-initializer failure and lets a game exception through.
- Option captions (added 28 October, after this run): `tools/DetailCheck --button-captions` checks the caption hook, ownership filters, a foreign caption writer before and after ours, clone clearing, the packaged locale and unload cleanup; `tools/DetailCheck --slider-labels` runs the game localizer on both slider labels. Neither checks native visuals.
- Existing harnesses, unchanged and passing: Hyperreal 69 (Windows and Mac sets), ownership 99 with HMP 0.4.2 `72c89d0e…` and 0.4.1, unsupported-platform 3 (Mac), cinematic hooks 28, detail and tree hooks 88, save hooks 43 and gate 23.
- Baseline: before any change, the tree as found rebuilt to the reviewed 0.4.4 DLL `80b0a0d8…` and passed the same suites. It is committed unchanged as the branch's first commit.
- Independent review with Codex (`gpt-6.1-sol`), read-only. Round 1 found four issues, all fixed: the first tick guard swallowed Unity's cleanup exceptions; lifecycle calls were unguarded; a throwing bridge getter logged every frame; three ticks still retried permanent failures. Round 2 found two more: unload could skip `UnpatchAll` if diagnostics failed to load, and the city-ready status line read feature fields outside a guard. Both fixed. Round 3: approve. Prompts, logs and answers are in `build/workshop-release-20261022/`.

These run on .NET and CoreCLR without Unity. They check managed contracts and hook composition, not Mono loading, native behavior, visuals or frame time.

## Not verified

- Anything in the game: loading on Mono, the options menu, every log line above, Help pages with Bakery, lamp state with Hyperreal, coverage with the HART bridge.
- Any performance gain from any feature.
- Behavior after a real game update.
- The HART Free Camera bridge on the Workshop. HART Free Camera 1.1.1 with the bridge is prepared but not published (its `WORKSHOP.md`, 22 October). The 1.1.0 DLL from 18 October has no bridge type. Until 1.1.1 ships, Workshop viewers get no camera coverage, which is the safe state.
- A HART Free Camera unloaded while its assembly stays loaded: the bridge type is still found, so the main view keeps coverage as with HART present and the recording camera off. Assemblies cannot unload in Mono, so only HART itself could report this.
- Approved thumbnail is packaged as preview.png; title remains City Performance. Bug reports use Workshop comments/discussions. No project licence file has been added; Harmony retains its licence.

## In-game checks for Peter

Use a disposable save and a separate profile or a copy of the game folder, so `ModsLocal`, `steam_mod_manifest.txt` and the series saves stay as they are. Restart the game between runs.

**1. Vanilla, City Performance only.**
Install `build/CityPerformance-0.5.0.zip` as the only mod. Start the game, open Settings > Mods once, start a new game, walk and drive for five minutes, let one in-game hour pass, quit. In Player.log, check:
- `Version 0.5.0; BigAmbitions SHA256=9427561b…; verified=True`
- `Hyperreal discovery: Hyperreal absent; original behavior`
- `Bakery Help: Bakery absent; original Help discovery`
- `CINEMA HART Free Camera bridge not loaded; …`, once
- No recurring `PERF frames …` summaries with logging off; enable Performance summaries to collect them.
- no `setup failed`, no `Stopped … for this session`, no exception stack trace from City Performance

In the game, the City Performance options page opens and lists every option. Confirm all behavior toggles start on and logging toggles start off in a fresh profile. The trace button reads **Export** beside its descriptive title, also after closing and reopening the page and after changing language. The sliders read `150%` and `5 min` and update while dragged.

In a separate run with RLO loaded, its key-binding buttons must not read **Export**, and its key suffix must land on the row label, not the button.

**2. Series pack with the new pair.**
City Performance 0.5.0, Hard Mode Patch 0.4.2, Bakery, Hyperreal and HART Free Camera 1.1.1, on a copy of the series save. Check:
- `Bakery Help: active; registered Help lookup…` and `Hyperreal discovery: active; full inactive-inclusive discovery, unsorted…`
- `CINEMA connected HART recording-camera bridge 1.`
- The Help menu shows Bakery's pages, also after changing the language and after a city reload.
- Street lamps switch on at dusk as before, including near trees.
- With the recording camera on: the area around it fills in and you hear from it. With it off: audio returns to the player.
- With Driven Rides loaded, the `CINEMA pedestrians` summary shows `traffic=vanilla`, or the log has `CINEMA traffic disabled: foreign patch detected.` (expected, see above).

**3. Old HMP.** With Hard Mode Patch 0.4.1 instead, both `Bakery Help` and `Hyperreal discovery` lines say `inactive: conflicting hook`. Bakery Help and the lamps still work through HMP.

**4. Mac (optional).** Load the Mac game with City Performance only: the version line says `verified=False; game build not audited…`, and there are no errors.

The approved defaults above do not establish performance gains; matched off/on comparisons remain pending.

## Historical package, 22 October (superseded)

- Folder: `build/CityPerformance/` (DLL SHA256 `fac504be9258198ca9ed9cee2eef48777aacaa9b2151fd990b569d4c2ad79dbc`)
- Archive: `build/CityPerformance-0.5.0.zip` (SHA256 `36cc298d30f699ef01978181c9abb4ba02bb0579a479e563112f7adf76ab705e`)
- File hashes: `build/CityPerformance.sha256`
- Workshop fields and description: `workshop/WORKSHOP.md`
- Changelog: `CHANGELOG.md`

`build/` is ignored by git, so the archive is local to this Mac. Rebuild with `tools/package.py` after `dotnet build -c Release`.

The 23 October release-session-2 package and validation supersede those historical hashes. See `docs/release-session2-2026-10-23.md`. Releases now build from `main`, created from `workshop-release`.
