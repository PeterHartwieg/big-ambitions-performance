# perf-pc-pedestrian-owner

Status: done
Date: 2026-10-28
Branch: codex/pc-pedestrian-owner
Commit: 83556a3 (pushed)
Draft PR: https://github.com/PeterHartwieg/big-ambitions-performance/pull/1
Base: codex/button-label

## Changes
- Detect loaded PersistentCity assembly at setup and via AssemblyLoad notifications; no repeated assembly scans on refill calls.
- Stand down before refill requests, deferred ticks, flushes and target restoration. Cancel backlog without flushing; preserve original arguments/exceptions and saved preference. Log one ownership line per CP load.
- Detach watcher on CP unload. Existing audited hashes and foreign-patch refusal remain unchanged.
- Add pure detection tests and actual CP/Harmony offline entry-point checks; document behavior in README and Unreleased changelog.
- Verified identifiers from the read-only Persistent City checkout: tools/build.py emits PersistentCity.dll; Runtime/AmbientPedestrians.cs uses hart.persistentcity.ambientpedestrians.v1. No edits there.

## Commands and results
- Read AGENTS.md and README.md; no CLAUDE.md present. rg unavailable; used PowerShell Select-String/Get-ChildItem instead.
- Process BA_GAME_MANAGED and dotnet PATH were missing. Used the existing Windows user/machine BA_GAME_MANAGED setting and installed SDK executable C:/Users/Peter/AppData/Local/Microsoft/dotnet/dotnet.exe; no persistent environment changes.
- PowerShell: set process BA_GAME_MANAGED from Environment.GetEnvironmentVariable('BA_GAME_MANAGED','User'), falling back to 'Machine'; set DOTNET to the executable above and DOTNET_ROLL_FORWARD=Major. Ran & 'C:/Program Files/Git/bin/bash.exe' -lc 'bash tools/build.sh'. PASS: 347 core checks; build zero warnings/errors; local package created. Game references remain ignored under build/refs.
- dotnet run --project tools/DetailCheck -c Release -- build/refs build/CityPerformance/CityPerformance.dll --refill-ownership: PASS 14 checks (initial run 13, then added in-flight restoration coverage and reran).
- dotnet run --project tools/DetailCheck -c Release -- build/refs build/CityPerformance/CityPerformance.dll --vanilla: PASS 53 checks.
- Git Bash python3 tools/package.py: PASS; refreshed local package after documentation edits. Version unchanged at 0.5.0.
- git diff --check: PASS.
- git commit -m "Yield pedestrian refill to Persistent City regardless of load order": 83556a3.
- git push -u origin codex/pc-pedestrian-owner: PASS, only own branch pushed.
- gh pr create --draft --base codex/button-label --head codex/pc-pedestrian-owner --title "Yield pedestrian refill to Persistent City in either load order" --body-file .scratch/pr-body.md: PASS, URL above.

## Open questions / limits
- No implementation blocker. Native Unity/Mono behavior remains unvalidated; offline tests use a synthetic assembly with the verified simple name, not a running Persistent City instance.
- When Persistent City loads later, the already-installed CP hook remains inert until normal unpatching on unload. Presence remains authoritative even if Persistent City's own hooks are subsequently disabled. This intentionally follows the requested presence-based ownership rule.
- No mod installation, game launch/state/profile modification, version bump, release, merge or publication performed. No FPS claim.

## Suggested next steps
- Review draft PR against codex/button-label.
- In a separately authorized native playtest, test both load orders, density requests, option changes with pending work, city/mod unload and the single ownership status line. Also compare CP-alone behavior.
- Keep runtime evidence separate from the successful build/offline tests.

This handoff is intentionally uncommitted and copied to the requested handoff directory.

## Review fix round 1 — 2026-10-28

Status: done. Fetched origin; own branch was already synchronized (0/0); git merge --ff-only origin/codex/pc-pedestrian-owner reported already up to date.

Triage: 2 Medium FIX items, 8 SKIP items, no QUESTION items. Full reasons and ready-to-post comment are in handoffs/leftover-2026-10-28/TRIAGE-performance-1-r1.md and COMMENT-performance-1-r1.md. No comment posted; no questions file needed.

Pushed commits:
- 5a7ccef — Fix ownership retention after failed unload (review round 1)
- eb978f7 — Restore in-flight refill targets during handoff (review round 1)

Regression evidence: new early-unload failure injection failed before the first fix; the deferred handoff target-restore assertion failed before the second fix. Final checks: Git Bash tools/build.sh passed 347 core checks and built/packaged with zero mod warnings/errors; DetailCheck --refill-ownership 24 PASS; --vanilla 53 PASS; --button-captions 12 PASS; standalone DetailCheck build zero warnings/errors; git diff --check clean. A parallel harness run caused temporary executable-copy retry warnings; all runs succeeded and the later standalone build was clean.

PR #1 description updated via gh pr edit for material in-flight behavior correction and new validation. PR remains draft on codex/pc-pedestrian-owner targeting codex/button-label. No deployment, install, release, force push or other-branch push. Native playtesting remains the next separately authorized step.

## Review fix round 2 — 2026-10-28

Status: done. Fetched origin; branch was synchronized (0/0); fast-forward merge reported already up to date.

Read all three round-two reviews and current source. Both Codex reviewers APPROVE; Opus APPROVE WITH NITS. Triage contains zero High/Medium FIX items: three Low documentation/test FIX items and one Low speculative runtime SKIP. No product decision or questions file needed. Details: TRIAGE-performance-1-r2.md and ready-to-post COMMENT-performance-1-r2.md in the requested handoff folder. Neither comment nor any message was posted.

Pushed aa3245d — Clarify handoff acceptance and isolate ownership test (review round 2).
- Narrowed README failed-teardown guarantee to already-observed ownership, documenting post-detachment detection limits.
- Added explicit native target/count and repeated same-density request acceptance for in-flight handoff, evaluated against Persistent City's intended behavior.
- Removed the managed native-body fixture patch before the PC-first installation case.

Commands: DetailCheck --refill-ownership passed 24 checks; Git Bash tools/build.sh passed 347 core checks and build/package with zero warnings/errors; git diff --check passed. Used the same configured BA_GAME_MANAGED and installed DOTNET executable as prior rounds. No production source changes, no change to runtime behavior, no version bump or installation. PR description intentionally unchanged under the behavior-change-only rule. PR #1 remains draft, head codex/pc-pedestrian-owner, base codex/button-label. Native playtest remains pending separately authorized work.
