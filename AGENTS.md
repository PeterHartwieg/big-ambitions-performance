# City Performance

This repository is independent from HART Free Camera and other game mods. Never modify game assemblies, saves, running game state or sibling mod repositories as part of a build. Live installation/restart needs explicit session authorization.

Mod ownership (Peter, 22 October 2026): City Performance owns performance changes and diagnostics. Hard Mode Patch owns cross-mod compatibility and the modpack's intended gameplay, economy and balance. Bakery Help discovery/build scheduling and Hyperreal lamp discovery belong only to City Performance; Bakery prices and Hyperreal map-return compatibility belong to Hard Mode Patch. The HMP 0.4.2 / CP 0.4.4 candidate pair implements this split. Preserve legacy conflict refusal so older HMP discovery hooks cannot run alongside CP's replacement. Incidental caches needed for gameplay rules stay with those rules.

Performance contribution routing (Peter, 18 October 2026): performance fixes requiring Harmony ship through City Performance in the HART modpack. Improvements to another project's own code that do not require Harmony should go upstream as PRs to its owning repository. A source change or candidate build does not establish installation or runtime validation.

Keep changes narrow, configurable and fail-open. Do not suppress vanilla exceptions, change save/economy timing, disable physics globally or promise unmeasured FPS gains. Behavioral patches require exact audited assembly hashes and conflict detection. Do not simply expand the hash allowlist for a new build without re-auditing the patched code.

Never commit game DLLs, decompiled game source, Harmony binaries, generated packages or save data. `tools/build.sh` builds locally and packages an explicit allowlist. `tests/Tests.csproj` tests pure scheduling/metrics without game dependencies. Document runtime validation separately from compile/core-test evidence.

Diagnostic storage (Peter, 21 October 2026): keep historical game logs, trace exports and playtest transcript archives on the home Windows PC under `C:\Users\Peter\HART-Diagnostics\Performance`, not on the MacBook. Mac copies are temporary working data; verify the PC archive before removing them. Keep source, test fixtures, active files and concise working reports local.

Release branch (Peter, 23 October 2026): `main` was created from `workshop-release`. Build and prepare future releases from `main`; retain the old branch and do not rewrite published history. Release defaults enable all behavior experiments and disable recurring diagnostics/trace logging, while preserving existing saved preferences and compatibility guards.
