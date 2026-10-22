# Performance ownership handoff — 22 October 2026

Candidate pair: **Hard Mode Patch 0.4.2 / City Performance 0.4.4**. Source and offline assembly tests only; not installed or validated in a running game.

## Responsibility

- City Performance: performance changes, profiling and diagnostics. It now owns Bakery's registered Help lookup, pre-build author page insertion and scoped redundant-refresh suppression. It retains its reviewed Hyperreal full, inactive-inclusive, unsorted query.
- Hard Mode Patch: cross-mod compatibility and intended modpack mechanics. Bakery prices, Hyperreal map-return workaround, scenario, rent/happiness and factory diploma rules remain here.
- HMP's old lamp cache is retired. Its enable/destroy hooks, cache implementation, performance counters and two performance options are removed. The new fresh scan finds newly created inactive lights on the next scheduled scan, but still searches globally and allocates. Its cost is not the old cache-hit cost; a matched native comparison is required.

## Upgrade behavior

| Versions | Discovery ownership |
| --- | --- |
| HMP 0.4.2 + CP 0.4.4 | CP owns both, subject to independent exact binary/conflict guards. HMP prices/map behavior remain independent. |
| HMP 0.4.1 + CP 0.4.4 | Legacy HMP discovery hooks take precedence, in either load order, including old options off. CP withdraws. |
| HMP 0.4.2 + CP 0.4.3 | CP may supply its lamp query; Bakery uses the author's original Help behavior. |
| HMP 0.4.2 without active compatible CP features | Original author discovery. No fallback HMP cache. |

The old HMP `bakery_help_discovery` / `hyperreal_light_discovery` preferences are left untouched for downgrade but no longer registered or read. New CP `bakeryhelp` / `hyperrealdiscovery` settings default on if absent, in `m:<actual CP mod ID>:`. No guessed namespace is imported. During future paired installation, read the actual IDs and preserve the player's desired on/off choices explicitly. An old off choice is **not automatically carried over**. The Bakery price option keeps its existing restart requirement.

Bakery activation currently requires the audited Windows game/Core/HGExtensions identities; the current Mac binary set is refused. Hyperreal has its previously audited separate Mac/Windows Core+HDRP pairs. No allowlist was broadened. Initial activation waits for city-ready so legacy mod initialization precedes it. Unknown builds, foreign hooks and failed installation leave author/composed behavior available; incompatibilities latch until mod reload. Three Help hooks install and withdraw together. Partial cleanup never removes another owner's hook or retries a failed unpatch every frame.

## Checked offline

- HMP: 104 pure rule cases; 30 compatibility checks (price values/restoration, map camera/fade/one-shot/scene/reset/Init lifecycle and removal of discovery ownership); 38 scenario/rebalance checks; 57 factory checks against the locally retained RLO 0.45.0 binary.
- CP: 338 pure checks; 99 actual-assembly ownership checks; 3 checks that the unaudited Mac Bakery dependency set stays inactive; 69 existing Hyperreal checks on each audited Windows and Mac dependency set.
- Ownership checks use the actual legacy HMP DLL and current pair, in both load orders with legacy options on/off. They exercise actual authored page insertion under explicit native-scene stubs, success despite suppressed refresh, idempotent page count, missing/registered Help, reentrant scope, original fallback exceptions, all three partial-install failure points and failed cleanup.
- Nine existing CP feature installers and fixed hourly/daily diagnostic targets coexist with both discovery adapters in the 99-check harness. The stationary guard uses an explicit test-only frame counter; dynamic live subscriber registration remains pending.
- Builds and offline Harmony run under .NET/CoreCLR. They establish managed contracts and ownership, not Unity/Mono behavior, rendered Help/light output or frame-time improvements.

## Review disposition

Astra, Sol, Opus 5.5 and Grok 4.7 independently reviewed the split. First-round requests added actual old/new DLL composition, actual Bakery page-return/idempotence checks, every installation failure point, price restoration and Mac refusal checks. Round two: all four approve the source/offline candidate, with no production-code blocker. This is not native, installation or recording clearance. Opus reported `claude-opus-5-5`; Grok reported `grok-4.7-build`, both successful final turns. Astra and Sol independently reviewed local source and evidence; Sol also reran the 88-check pre-addendum ownership suite and 30 compatibility checks.

The conservative conflict scan is retained intentionally: throttling it could bypass a later hook before the next scan. Shared generic singleton code can affect Help even when patched through another generic instantiation. The broad boundaries and sticky refusal are documented behavior, not proof that every foreign patch is harmful. An offline guard-only sample with four registered methods measured approximately 0.20 ms and 18 KB per call (an earlier run was 0.45 ms); this is not representative Mono/full-pack profiling. Native guard cost remains part of acceptance, with no FPS promise. File hashing repeats on city acquisition and is not a per-frame operation.

Other review questions: HMP's existing TargetGate lowercases digest prefixes and rejects incomplete hook lists before Apply. Hyperreal has one exact reviewed target hash. Pricing toggle semantics are unchanged and already say restart required. The audited Help builder clears generated UI, not the input page structures, and Bakery returns success when it finds its target category even if the nested refresh returns false. Existing-singleton catch-up intentionally allows the author's normal refresh because the UI may already exist. No original game exception is suppressed by that optional catch-up. The old HMP performance options are absent from Rules.All and covered by a core regression test; price and map options remain. Unused Help fixture code was removed from HMP.

## Native validation still required

Use separately authorized disposable saves and the same modpack/settings. Compare prior pack, original discovery and the paired candidate, with diagnostic overhead controls. Check Help after the retry window, existing-menu activation, language change, on/off and city reload. Check lamp identity sets, inactive/new/destroyed lamps, foliage, off/on restoration and scene transitions. Repeat map return outdoors and indoors, including fixed/automatic exposure. Confirm both discovery activation logs with diagnostics off/on and check Mono dispatch through the pre-patch Bakery delegate. Existing-Help catch-up may perform one author refresh at city-ready. Measure query/Scan costs and whole-frame tails on Mono. Earlier Persistent City frame-time acceptance failures (including normal-speed and 6× overshoots), save/cleanup spikes and accelerated-soak/reload requirements remain open; this split does not re-evaluate them. The earlier normal-speed 20-minute observation completed; it did not establish sustained peak-load acceptance.

No Windows connection, live rig launch, installed mod, game binary, save or preference change was made for this handoff.

## Local evidence

Ignored working evidence: `build/ownership-split-20261022/`, including baseline source hashes, retained legacy package, test logs, review transcripts and final manifests. Proprietary target binaries and decompiles stay outside source/packages. Four-model round two is complete. The final 99-check suite adds nine actual sibling-feature hook installations and the fixed hourly/daily diagnostic targets, then proves both discovery guards remain active. Only the native frame counter is explicitly stubbed for the stationary guard. Dynamic live subscribers and optional save/cleanup execution are outside that fixture. Source inspection finds no direct save/cleanup target overlap; future subscriber additions must still pass the conservative conflict guards.

Final paired artifact metadata and complete source/package hashes: `build/CityPerformance-0.4.4-release.json`. Candidate archives: `build/CityPerformance-0.4.4.zip` and the sibling HMP `build/HardModePatch-0.4.2.zip`. The DLL identities reviewed are CP `80b0a0d8d22d3fa6ec0dbc3fd247fd6057c45f43ae2d9b43bb5c5459ded2637a` and HMP `72c89d0e145fa935c95f8080e1b6cfb4f5b005d44dd436bf0530c1c043a1d28b`.
