# PC rig proof (agent1): 0.5.1

**Verdict: PASS for the requested mandatory checks 1–4 and verified restore/release. Check 5 remains INCOMPLETE for an error-free smoke and autosave.** This is UI/defaults acceptance, not stock-game behavioral or performance validation.

PR #2, unchanged head `46585ff8ffa7606803bff512ed9e81ac3b70e344`, `codex/button-label`. Role: rig_test. Native Windows agent1 at `C:\BigAmbitions-AgentTests`, existing modified build 3682, locked session. No merge, production-code/test edit, release-branch change, Workshop upload, Steam change, WSL use, or normal-profile access/control. Input hashes verified before extraction/copy/use; ZIP CRCs and copied payload hashes verified. The installed Workshop directory was read-only and copied to staging.

| Check | Expected | BEFORE / installed observation | AFTER observation | Result / evidence |
| --- | --- | --- | --- | --- |
| 1. Slider labels | Red positional placeholders; green named values | `{0}%`, `{0} min` | `150%`, `5 min`; changed to `160%`, `7 min`; restored to 150/5 | **PASS** — S1 PNGs; before-live.json; after-live-defaults.json; after-changed-cleanup.json; after-live-restored.json |
| 2. Export caption | Red placeholder; green Export; title and foreign captions retained | `Your text here` (rendered `YOUR TEXT HERE`) | `Export` (rendered `EXPORT`); same descriptive title; 19/19 RLO captions identical | **PASS** — S2 PNGs; acceptance-summary.json |
| 3. Fresh defaults | Both installed DLL and candidate read live; 0.5.1 matches all 15 expectations | Workshop DLL: nine behavior switches ON, four logging switches OFF, 150, 5; optional BEFORE ZIP identical | Candidate: same 15 values | **PASS** — fresh-*.json; *-registered.json (stored=null before UI); *-live*.json; S3 PNGs |
| 4. Native QA | Correct version/build; no CP exceptions or inactive-Harmony-patch errors | Version 0.5.0, build 3682; zero CP/Harmony failure lines | Version 0.5.1, build 3682; zero CP/Harmony failure lines; expected unaudited-build refusal | **PASS, scoped** — native-qa.json; player-log-excerpts.txt. Other game exceptions retained and controlled below. |
| 5. Fixture smoke | About 2 minutes, autosave if possible, reload, no errors | No gameplay baseline requested; added no-CP control for exception attribution | 131 s active simulation, native manual save and verified full-restart reload succeeded. No periodic autosave observed. Four load-time game exceptions reproduce without CP; transition TimeHelper exception retained. | **INCOMPLETE** for error-free smoke / autosave; bounded simulation/save/reload **PASS** — S5 PNGs; native-qa.json |

## Issue #3: fresh-namespace defaults

| Setting | Installed Workshop 0.5.0 | 0.5.1 AFTER | Expected 0.5.1 |
| --- | --- | --- | --- |
| Performance summaries | OFF | OFF | OFF |
| General pedestrian refill smoothing | ON | ON | ON |
| Shared stationary-zone spawn budget | ON | ON | ON |
| Avoid repeated Bakery Help searches | ON | ON | ON |
| Faster Hyperreal lamp discovery | ON | ON | ON |
| Distance-based tree detail | ON | ON | ON |
| Camera-aware world coverage | ON | ON | ON |
| Listen from the second camera while active | ON | ON | ON |
| Batch midnight asset cleanups | ON | ON | ON |
| Background autosaves | ON | ON | ON |
| Record per-frame hitch trace | OFF | OFF | OFF |
| Automatically save trace | OFF | OFF | OFF |
| Extra diagnostics | OFF | OFF | OFF |
| Cinematic view distance | 150% | 150% | 150% |
| Maximum cleanup wait | 5 min | 5 min | 5 min |

The installed DLL actually enables Background autosaves. All three builds tested, including optional BEFORE, agree. This is measured in a fresh runtime namespace, not inferred from a README/branch or an existing preference. Every fresh folder `CityPerfPR2_<build>_20261031_043336` was absent and its registry query found **0 matching keys before launch**; actual registered runtime IDs were then checked. All 15 registered option keys were absent (`stored=null`) before opening Settings. Native rendered Toggle/Slider controls supplied the live values. Diagnostic state independently records BackgroundSaves.Enabled=true. Existing CityPerformance preference keys were untouched. New keys and cleanup are recorded in preference-delta.json; no preference key was cleared to obtain defaults.

## Native screenshots

All are original 960×540 Unity PNGs, visually inspected. The launcher supplied no graphics arguments and no graphics or Windows settings were manually changed. The game opened at the existing default resolution. Native startup wrote refresh-rate metadata and created particleQuality/textureQuality preference keys; the exact original preference export, including absence of those new keys, was restored. Total published PNG size: 4,253,861 bytes. SHA-256 per image: images.json.

| Purpose | Native PNG |
| --- | --- |
| Red: both sliders and export | [S1-before-distance.png](S1-before-distance.png) |
| Red: export beside unchanged RLO captions | [S2-before-export.png](S2-before-export.png) |
| Green: default sliders and export | [S1-after-defaults.png](S1-after-defaults.png) |
| Green: native value-change callbacks | [S1-after-changed.png](S1-after-changed.png) |
| Green: export and foreign captions | [S2-after-export.png](S2-after-export.png) |
| Installed Workshop autosaves ON | [S3-workshop-background-default.png](S3-workshop-background-default.png) |
| Installed Workshop numeric defaults | [S3-workshop-slider-defaults.png](S3-workshop-slider-defaults.png) |
| End of active smoke window | [S5-after-two-minutes.png](S5-after-two-minutes.png) |
| Verified saved fixture reload | [S5-after-verified-reload.png](S5-after-verified-reload.png) |
| No-CP control city | [S5-control-without-cityperformance.png](S5-control-without-cityperformance.png) |

## Native evidence and scope

```text
BEFORE: Cinematic view distance value=150, text={0}%
BEFORE: Maximum cleanup wait value=5, text={0} min
BEFORE: Export current trace (background CSV write) -> Your text here
AFTER:  Cinematic view distance value=150, text=150%
AFTER:  Maximum cleanup wait value=5, text=5 min
AFTER changed: value=160 -> 160%; value=7 -> 7 min
AFTER:  Export current trace (background CSV write) -> Export
```

The preceding excerpt is a compact transcription of the linked native control JSON, not fabricated Player.log output. Native Player.log excerpts:

```text
Version 0.5.1; BigAmbitions SHA256=055700fd26a7a6430bbb6a9d79379781909fbc518af760453396947887db5063; verified=False; game build not audited, so behavioral changes stay off and only timing/trace hooks and the presentation-only button-caption hook run
Loaded Big Ambitions (Build 3682)
AUTOSAVE inactive: option off, quitting, or unverified build.
AUTOSAVE requested=True; validated=False; faulted=False
Cleanup batching maximum_wait_seconds=420; pending_deadline_seconds=none
Cleanup batching maximum_wait_seconds=300; pending_deadline_seconds=none
```

Prefixes are omitted here for readability; the committed excerpt file retains sanitized native mod prefixes. No CP exception block, setup failure, caption-localizer warning or Harmony installation error was found. Intentional `Not active` hash-guard lines are recorded, not hidden or counted as patch errors.

Smoke used only copies of the preexisting disposable `FT Ownership Performance 20261021` fixture. Nine behaviors were requested ON but the behavioral hash guard stayed active: `verified=False`, background worker hooks not installed. Active simulation ran from native observer UTC 04:40:34.0997334 (unpause) through 04:42:45.3566501, **131.26 seconds**, with clock advancing from 08:03 at pause to 10:14. A separate observation bracket was 08:19 to 10:14. This was stationary city simulation, not a walking/driving route. A native manual-save confirmation overwrote only `FT CityPerf PR2 20261031 smoke`; full-restart reload then verified `loading=false`, the same fixture, day 1, $10,000 and native hour 10, minute 17.4572; the final PNG visibly shows 10:18. `preventAutoSave=false` was read, but no periodic autosave was observed in the bounded smoke. Optimized autosave acceptance needs the audited stock-game arrangement; no allowlist bypass occurred.

Four loading exceptions in `CullingGroupController.Update` were reproduced with **City Performance absent**, the same game/RLO hashes and another untouched copy of the same original fixture. Without Harmony's CP wrapper, two control stacks expose `BuildingOutsideMusicController.HasMusicOutside` and two expose `BuildingOutsideHangoutZoneController.HasPedestriansOutside`, followed by `OnLod0 -> DispatchLod -> DistributedWork.ProgressWork -> CullingGroupController.Update`. This is baseline rig/fixture behavior, not an exception introduced by this UI candidate. An in-session return/reload attempt also logged `TimeHelper.GetDayOfWeek` InvalidOperationException from pedestrian target selection and stayed at the menu; its cause was not isolated. No CityPerformance method appears in that stack. It is retained as an open smoke issue. The final accepted reload used a complete clean restart.

Test-tool limitations were corrected transparently: initial readiness based on UI controls could fire before the loading overlay cleared; those preliminary loading/menu captures are excluded from accepted proof. The helper was extended only to read the actual loading flag/native clock. An obsolete readiness poller was cancelled because its scripted load did not start a city; no game was killed and no command was stopped just because of elapsed time. Native UI callbacks and screenshots were used; no injected pointer events, forged screenshots, production patches or simulated gesture proofs.

## Hashes

| Input | SHA-256 |
| --- | --- |
| AFTER 0.5.1 ZIP | `3bcd705eb9a37b6e6d8c0b590099ea07fc11d08dab5edb680d85e9f0f2616f86` |
| BEFORE 0.5.0 ZIP | `8e0ece1c09cce1fd2ad9c061a5c134f6d9d846f2bb961a58cc50b7f12d54541b` |
| AFTER DLL | `cf51c486c2f294f536608d87d5f435054b23cdc8bebd9db65f9b52c935f557ee` |
| BEFORE DLL | `d0757a088641f6dcd1567b14c536d1657fabfe72bcf010696ee4950185cb9000` |
| Installed Workshop 0.5.0 DLL | `ae114913db19d8fa7db0a967245410908ff671c8f4e90d0d4dca85fa2dfb9002` |
| Loaded isolated game DLL | `055700fd26a7a6430bbb6a9d79379781909fbc518af760453396947887db5063` |
| RLO 0.45.0 control DLL | `2f360a365cf7915f012cd62f4fd06214ef5cdaa0498331e05a7c831a3c20ab32` |

Every staged payload hash is in payload-hashes.json. Native log hashes and full short exception blocks are in native-qa.json. Restore-file-hashes.json records all 4,828 original file hashes with opaque path IDs to avoid publishing personal save names/content.

## Restoration and lease

Lease owner `cityperf-pr2-0.5.1-rig`; acquired **2026-10-31T04:33:36.9943346Z UTC**, released **2026-10-31T04:51:58.9624767Z UTC**. No takeover or queue wait was needed. Every launch required the compiled lease/lifecycle permit, verified receipt hashes and absence of all Big Ambitions processes. Only owned agent1 processes were launched and all quit normally.

Fresh backup verified 4,828 files, full ModsLocal/preferences/receipt/root metadata, and 107 installation references before mutation. Final restore verified **4,828/4,828 original content hashes**, exact inventory, original ModsLocal, preferences, receipt, root files and **107/107 receipt references**. Preference export SHA-256 restored exactly: `66fccbcc5f63bc492e36ba7f342d9d2480adb2b87404a650a8607094d207381d`. Original receipt SHA-256: `9785d1de5c7360875b2a2a47f511815660dd98a38d6f64ab4ecff8c20627b949`. Restoration UTC: 2026-10-31T04:51:53.592561+00:00. Added test keys were individually removed during restoration; the original registry key was never deleted/recreated. The first preference-restore attempt used a read-only registry handle and stopped before writing a value; reopening only the isolated key with write access completed restoration and all hashes then passed. Existing CP keys changed: **0**. Final game processes: **0**; lease held=false, pending=0. See backup.json, restore-receipt.json, preference-delta.json and lease-release.json. Private originals, retained tested profile and full logs remain on the home Windows PC.

## SKIPPED-LOCKED and open issues

- **SKIPPED-LOCKED:** physical hover and free-pointer slider dragging/clicks. Locked Windows does not provide the established reliable pointer/focus route. Native slider callbacks were exercised and verified instead; physical gesture success is not claimed.
- **INCOMPLETE:** error-free smoke and periodic/optimized autosave. Baseline outdoor-music/outdoor-hangout loading errors reproduce without CP. TimeHelper transition error remains unisolated. Manual save and final reload passed.
- This rig's game hash is unaudited. Stock-build behavioral activation, all-on visual parity, optimized autosave and performance gains are unmeasured and unclaimed.
- Issue #3 documentation conflict is now resolved at the installed-DLL evidence level; no issue comment was posted and no text/code was changed. A separate 3–6-line draft is prepared for the harness.
