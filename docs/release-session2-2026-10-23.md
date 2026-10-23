# Release session 2, 23 October 2026

Created `main` directly from `workshop-release` (5eba1dc), retaining the release history and approved art. Future release work uses main. All nine behavior toggles default on in startup preferences, option registration and initial state. All four diagnostics/trace toggles default off. Stored choices remain authoritative. Exact-build, dependency and conflict guards remain unchanged. Startup/failure status messages remain available; recurring diagnostic logging is off.

Full offline checks passed: 340 Core; 53 Windows vanilla/default and 35 Mac vanilla/default; 28 cinematic; 88 detail/tree; 69 Hyperreal checks on each Windows and Mac set; 99 ownership checks with current HMP and old HMP 0.4.1; 3 unsupported-platform checks; 43 save-hook and 23 serialization-gate checks; both existing save fixtures passed exact native byte comparison, native reload and detached-state checks, with 80 worker checks total and injected warmup-warning refusal.

The new default contract checks the actual registered values of all 13 toggles and their compiled startup fallbacks. Enabling background saves in the vanilla harness exposed a missing Newtonsoft.Json dependency in the .NET harness; added the test-only package reference. No runtime dependency is newly shipped. Assembly informational version is pinned to 0.5.0 rather than embedding the pre-commit source revision; the full suites were rerun on that final DLL. SaveHookCheck now explicitly tests the off path instead of assuming it is the release default. Logs: `build/release-session2-*.log`; the exact full command sequence is retained in ignored `build/release-session2-checks.sh`.

Pending: native Windows all-on behavior, UI, Help/lamp parity, background autosave acceptance, performance A/B comparisons and game-update behavior. No FPS gain is claimed. Opus must redraw cards 01, 02 and 03; only the HTML copy changed.

## Package verification

- Archive: `build/CityPerformance-0.5.0.zip`.
- SHA-256: `d56f39ee7673a40af9fe9a74bd249f74f37131557933060d7bbadda5a7219811`; matching `.zip.sha256` sidecar.
- DLL SHA-256: `ae114913db19d8fa7db0a967245410908ff671c8f4e90d0d4dca85fa2dfb9002`.
- 7 files: ZIP CRC, exact file inventory, every decompressed hash, package manifest and approved thumbnail equality passed. Exactly one root DLL and only `preview.png` as the root image; no preview.jpg.
- Full description: `workshop/DESCRIPTION.bbcode.txt` (3689 characters, below 8,000), ends with the PayPal Support section.
- Change notes: `workshop/CHANGENOTE-0.5.0.txt`, `.bbcode.txt` and `.md`.

## Rig availability and limits

Read-only availability checks and retries during this session found both rigs occupied. At 13:55 CEST, Windows agent1 PID 34936 was reserved by `persistent-city-neighborhood-finance-20261023`; Mac agentmac PID 18917 was reserved by `street-food-cooler-release-20261023`. No reservation was taken over; no game, mod, profile, save, preference or manifest was changed. New native integration and running-cost acceptance remain pending. These are verified build candidates, not a claim that the missing in-game checks passed.

No Workshop upload, force-push or history rewrite. Food Trucks repository/worktrees/files and Campaign Objectives files were untouched. Artwork images were not redesigned or re-rendered. The required gallery redraws are in the shared `DESIGN-HANDOFF-MODS.md`.
