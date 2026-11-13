# City Performance and Real Life Overhaul 1.0.5

Date: 13 November 2026. Offline source and binary audit. Nothing was installed and the game was not started. In-game verification is still pending and belongs to a later batched release rig run.

## What was checked

| Item | Value |
|---|---|
| RLO source | tag `v1.0.5`, commit `a9f51ac697df7c44759421b438bb30f7113bea34` ("Merge version 1.0.5") |
| RLO release asset | `RealLifeOverhaul-1.0.5.zip`, SHA-256 `765f2501e268c800ffc375557e9cc0bdd0434e619c45e8b676877c10dd03c1b1` (matches the published hash) |
| RLO DLL inside it | `RealLifeOverhaul/RealLifeOverhaul.dll`, SHA-256 `0c4b4aea5c339f9131f46d3a72b54657dd79bb0a8f086069243c34e7f6286ae7` |
| Game | build 3682, `BigAmbitions.dll` SHA-256 `9427561b81aaab2ada09c22a3c98dd9a4017a65225b389513885bf336852e911` (unchanged by RLO) |
| CP base | `main` at `850984e` |

I searched the RLO 1.0.5 scripts for every CP Harmony target and for the game objects those hooks change, then read each hit.

## Harmony overlap

RLO 1.0.5 patches no game method. Its only Harmony use is `Core/GoingPublicLink.cs`, which finds Going Public's own Harmony by reflection and prefixes Going Public's network receivers. The DLL has no reference to `0Harmony`. RLO's decision record `docs/standards/decisions/0001-no-harmony.md` still applies. The "about 33 files" in the brief were name hits such as `FamilyState.Harmony` (marriage harmony) and `GroundDirector.Patch` (a navmesh patch).

So no CP hook shares a method with an RLO patch, and Harmony order and composition questions do not arise. Every remaining interaction is shared game state, which the table below covers.

## Per-hook verdict

| CP target | What RLO 1.0.5 does there | Verdict |
|---|---|---|
| `SaveGameManager.Save`, `SerializeSaveGame`, `CompressSaveGame`, serialization gate, background saves | `DistrictSave` takes records naming RLO's own places out of the game's lists in `GlobalEvents.onSaveGame`. It puts them back when the main thread logs `Game <temp> saved in N ms` from the native `SerializeSaveGame`, or on the next frame if that line never comes. A CP background capture skipped the native call, so the line never came and the rest of the save frame (the midnight save runs inside the game hour) ran on shortened lists. | **Conflict, fixed.** CP now sends that save to native serialization while `RealLifeOverhaul.DistrictSave.DistrictSaveDirector.Waiting` is true. If the RLO assembly is loaded but that member is missing or unreadable, CP also stays native. The snapshot itself was already correct, since `onSaveGame` runs before the capture. |
| `SaveGameSerializationHelper.DeserializeBinaryDataAsync` gate | RLO reads saves through it in `NewBuildings/MadeGame.Save.cs` and a test path. | Compatible. The gate only makes an overlapping CP save native. |
| Odin formatter guard | RLO registers no Odin formatters. `DistrictSaveCodec` uses its own context. | Compatible. |
| `StationaryAiSpawner.SpawnGroupOverFrames`, `ClearPedestrians`, `Spawn*` (shared spawn budget) | RLO clones game zones for new ground, landmarks, schools and Uptown art. The clones are real `StationaryAiSpawner` components and use the native coroutine. Removal calls `ClearPedestrians`, or destroys the zone when the pools are gone. | Compatible. RLO zones share CP's budget like game zones. CP's iterator stops on a destroyed owner. Crowds may fill over a few more frames, which is the intended smoothing. |
| `PedestrianSpawner.SetActivePedestrians`, refill smoothing | RLO never calls it and does not change the target. It reads `activePedestrians` for counts, horn reactions, car strikes and solid streets. | Compatible. Counts can be briefly lower during a refill. RLO's street scenes use their own people. |
| `PedestrianPool.ActionOnGet/ActionOnRelease`, `Pedestrian.*` (cinematic population) | RLO only reads `PedestrianPool.animatorOverrides` for seasonal walking paces. | Compatible. CP's hooks only act on pedestrians CP created for the recording camera. |
| `TrafficManager.FixedUpdate/UpdateCamera`, `DensityManager.*`, `GridManager.*` | `TrafficMend` can replace the `DensityManager` component and swap the `densityManager` field after the density pass keeps failing. `RoadsGame` adds intersections to the grid at city load. | Compatible. CP reads the density manager through the field on every call, so it follows the replacement. CP does not suppress the exceptions RLO counts. |
| `CullingGroupController.DispatchLod/UnregisterCullable/Dispose` | `SceneryCulling` compacts `_cullables`, `_spheres` and `_pendingBands` directly for objects RLO destroys, without `UnregisterCullable`. `PerfGame` reads the list for street lamps. | Compatible. CP walks the live list and checks pending bands. It can keep a few stale entries for destroyed RLO objects until the controller is disposed. That costs a little memory, not behaviour. |
| `TreeController.Start/ToggleMapLod`, `TreeLods.MapChanged` (distance tree LOD) | RLO removes destroyed trees from `TreeController.AllTrees`, puts a map handler first in the map event, tints leaf materials for seasons and scales `QualitySettings.lodBias` in `VisualEnhancer`. It never sets tree LOD definitions. | Compatible. Seasons change materials, not LOD entries. A list removal can make CP skip a tree, which then keeps native LOD. A copied tree already carrying CP's thresholds fails the layout check and is skipped. |
| `GameManager.RunMainGameTick`, `GameManager.NewDay`, midnight cleanup, `Resources.UnloadUnusedAssets` | RLO subscribes to hour and day events and never calls `UnloadUnusedAssets`. | Compatible. CP's tick hooks are timing only. The delayed unload holds memory a little longer. |
| `AudioZone.*`, `AudioListenerPositioner` | No RLO use. | Compatible. |
| `HelpSystem.LoadCategories` (Bakery Help) | `StaffHelpGame` adds two categories to `_categories` and calls `LoadCategories`. | Compatible. CP's prefix always lets the native build run, so RLO's categories build. |
| `ModOptionsButtonControl.Initialize` caption fix, slider labels | RLO's own options page captions only rows it made. Its key-binding rows clone a game button row and call `Initialize`. With the RLO interface on, `OptionsScreen.Mods` draws other mods' options with its own controls, presses the game's button underneath, and shows the game slider's figure text. | Compatible. CP clears its caption from an untracked clone and never touches foreign rows. With the RLO interface on, CP's export button shows RLO's generic "Press" caption (see open product questions). Slider values come through. |
| Settings IDs | RLO uses `reallife_*` keys. | No collision with `cityperformance:*`. |
| Hyperreal lamp discovery, timing hooks, trace hooks | No RLO use. | Compatible. |

## Change and offline evidence

- `src/RloDistrictSave.cs` reads RLO's `Waiting` flag. `BackgroundSaves.Capture` checks it after the save event and logs once per install when it hands a save to the native path.
- `tests/RloDistrictSaveChecks.cs` builds stand-in RLO assemblies. With the helper forced to the old "never yield" behaviour, `tests/Tests.csproj` fails at "RLO holding district records sends that save to native serialization". With the fix all 364 checks pass.
- The real 1.0.5 DLL contains `RealLifeOverhaul.DistrictSave`, `DistrictSaveDirector` and `get_Waiting`. The source declares `public static bool Waiting`.
- `tools/build.sh` and `DetailCheck --vanilla` (53 checks) pass.

Without RLO nothing changes. With RLO and no district places, background saves still run.

## Not verified

The game's Mono runtime, RLO's in-game put-back line, and the save timings with both mods loaded. Native acceptance: with RLO 1.0.5 and a district place, take a midnight save and check that RLO logs "put back after the write (the game's line that the file is written)" and that CP logs its yield line once.

## Open product questions

With the RLO interface on, CP's trace export button reads "Press" in RLO's options window instead of "Export". CP cannot change that without reaching into RLO's UI. Options are to leave it (the default here), or to ask RLO upstream to use the native button caption when one is set.

Under RLO with district places, most midnight and recovery saves will now be native, so CP's background save gives little benefit there. The default keeps RLO's save correct. Making CP restore RLO's records itself would mean driving RLO internals and is not proposed.
