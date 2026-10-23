# Release session 2, 23 October 2026

Created `main` directly from `workshop-release` (5eba1dc), retaining the release history and approved art. Future release work uses main. All nine behavior toggles default on in startup preferences, option registration and initial state. All four diagnostics/trace toggles default off. Stored choices remain authoritative. Exact-build, dependency and conflict guards remain unchanged. Startup/failure status messages remain available; recurring diagnostic logging is off.

Full offline checks passed: 340 Core; 53 Windows vanilla/default and 35 Mac vanilla/default; 28 cinematic; 88 detail/tree; 69 Hyperreal checks on each Windows and Mac set; 99 ownership checks with current HMP and old HMP 0.4.1; 3 unsupported-platform checks; 43 save-hook and 23 serialization-gate checks; both existing save fixtures passed exact native byte comparison, native reload and detached-state checks, with 80 worker checks total and injected warmup-warning refusal.

The new default contract checks the actual registered values of all 13 toggles and their compiled startup fallbacks. Enabling background saves in the vanilla harness exposed a missing Newtonsoft.Json dependency in the .NET harness; added the test-only package reference. No runtime dependency is newly shipped. Assembly informational version is pinned to 0.5.0 rather than embedding the pre-commit source revision; the full suites were rerun on that final DLL. SaveHookCheck now explicitly tests the off path instead of assuming it is the release default. Logs: `build/release-session2-*.log`; the exact full command sequence is retained in ignored `build/release-session2-checks.sh`.

Pending: native Windows all-on behavior, UI, Help/lamp parity, background autosave acceptance, performance A/B comparisons and game-update behavior. No FPS gain is claimed. Opus must redraw cards 01, 02 and 03; only the HTML copy changed.

## Package verification

- Archive: `build/CityPerformance-0.5.0.zip`.
- SHA-256: `a4c3566c582ee1e671b7260f623cd1d3828ae766bf9861c72e61c54d8b19744f`; matching `.zip.sha256` sidecar.
- DLL SHA-256: `ae114913db19d8fa7db0a967245410908ff671c8f4e90d0d4dca85fa2dfb9002`.
- 7 files: ZIP CRC, exact file inventory, every decompressed hash, package manifest and approved thumbnail equality passed. Exactly one root DLL and only `preview.png` as the root image; no preview.jpg.
- Full description: `workshop/DESCRIPTION.bbcode.txt` (3689 characters, below 8,000), ends with the PayPal Support section.
- Change notes: `workshop/CHANGENOTE-0.5.0.txt`, `.bbcode.txt` and `.md`.

## Native Mac integration and limits

Both rigs were initially occupied and were left alone during offline work. At 14:01 CEST the Mac process had exited and its lock was released. Acquired a new reservation (`mods-release-session2-20261023`), verified/backed up all 3,144 profile files, preferences and installation manifest, and installed the four release candidates plus unchanged Campaign Objectives 1.2.1 and RLO 0.49.0. The existing Food Trucks 0.10.2 bridge snapshot was untouched. Private client shims used the existing compiled lock controller, so no Food Trucks repository/tool files were accessed or changed.

PASS: actual series button changed a deliberately seeded rent value from 1 to 0, enabled equipment utilities/office IT, and stored all 74 expected options. All persisted after restart. City Performance's 13 toggles matched the fresh defaults (nine behavior switches on, four diagnostics/trace switches off). The real Custom menu prefilled all 24 world fields. New Day 1 had $551.37 and one $75,454.35 loan. The world reached day 2 at 00:05, with $41.37 and $75,140.35 principal; save/quit/restart/reload preserved day, cash, debt, all world settings and the complete-v1 scenario marker. Main session `dc5f0d43`; reload session `a2ed72bc`.

PASS: 12 bounded running-cost fixtures executed inside native Mono using actual catalogue items and the patched RLO billing methods: 12h and 24h fryer/fridge use, closed-day refrigeration, sink allowance, unchanged home bills, factory equipment, office plan-only versus three-computer IT charges, outage credit and law-firm internet requirement. Fixtures used temporary registrations while paused, were removed synchronously without saving, and verified unchanged world money/day/registrations. Subsequent reads also matched cash, debt, settings and marker. This is native billing-method coverage, not actual player leases, nightly/monthly business settlement, office closure presentation or Utilities/BizMan UI acceptance.

Restored the complete original profile, all 3,144 hashes, preferences and install manifest; stopped the game and released ownership. Disposable test profile and structured evidence remain under Mod Pack's ignored `build/release-session2-20261023/`. The two game logs were archived and SHA-256 verified at `C:\Users\Peter\HART-Diagnostics\Performance\release-session2-20261023`; temporary Mac log copies were removed. Normal profiles were untouched. Windows agent1 remained reserved by the Persistent City session throughout; no Windows gameplay claim is made.

The Mac's unaudited game hash disables most City Performance behavior even when its toggles are on. Known Windows-only Bakery/Hyperreal bundle errors remain. RLO reports FAIL in minimap alignment, transit network and transit-pole diagnostics; these are outside this scoped release check and are not cleared by it. Native Windows all-on behavior, rendered UI/performance, actual business bills and the Steam install path remain manual checks. Campaign Objectives 1.2.1 was already pinned after the earlier passing integration; this fresh pass confirms the updated combination without changing Campaign Objectives or adding an untested pin.

No Workshop upload, force-push or history rewrite. Food Trucks repository/worktrees/files and Campaign Objectives files were untouched. Artwork images were not redesigned or re-rendered. The required gallery redraws are in the shared `DESIGN-HANDOFF-MODS.md`.
