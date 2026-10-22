# Hyperreal discovery candidate — City Performance 0.4.3

Implemented a conditional, configurable call substitution in City Performance. When the exact audited Hyperreal and Unity/HDRP pair is loaded and the Harmony boundaries are clear, replace only StreetLights.Scan's sorted inactive-inclusive finder with the corresponding unsorted native query. Preserve all other Scan instructions and original scheduling, baseline storage, foliage classification, adjustment and restoration. No scene-object cache or global Unity patch was introduced. The candidate is not installed or natively validated.

## Evidence and scope

The preceding Mac investigation directly measured periodic Scan calls at approximately 129–410 ms. This establishes the slow method, not sorting's share of that duration. Native traversal, array allocation and initial/new-lamp foliage work remain. The optimization's actual benefit is unknown.

The audited Hyperreal dictionary uses each HDAdditionalLightData instance as its key. Repeated prefab names do not merge lamps. Scan has no first-wins selection or cap; Apply/Restore null-check destroyed Unity objects and compute values per light. Unsorted discovery changes insertion/iteration order. Arbitrary downstream failures can therefore change partial application order, even though the selected query's exceptions are propagated once without suppression or retry.

Exact Hyperreal SHA256: `7d1bd589947558e2f3cc476f06d4699618732835718ffd2335f2e5102cfcef10`.

| Pair | UnityEngine.CoreModule SHA256 | HDRP runtime SHA256 |
| --- | --- | --- |
| Windows | `5afc743d350e95dc30379a6d2bafa2293b63259acf196fe3f7370f03644ef6f4` | `d0f869303996388151aa45aefb90c33147f0ae7961a1d0e0b406015ad5a8dfc6` |
| Mac | `ce064ee6dcc296f6108771ab9fe817828a7f3e1aa3af6ae95aea2dd0a2f087d2` | `acedb057709b46696572e8a6874e97d8525c1b900db95be2f9d314257e512450` |

Sol independently compared exact named query overloads, conversion, object-name/equality helpers and eight consumed HDRP property accessors. Their signatures/managed IL match across these pairs. Native engine internals and transitive HDRP implementations are outside this evidence. The feature-specific allowlist does not change any vanilla game hash gate.

## Compatibility and lifecycle

Activation waits for city-ready. Assembly-load notifications only set an atomic pending flag; installation runs on the host thread. Absent optional mods are inert. Unknown/ambiguous assemblies, altered IL or conflicting Harmony hooks withdraw the feature and retain the original/composed path. Compatibility failures remain blocked until mod reinitialization.

Existing Hard Mode Patch lamp-discovery hooks take precedence, even when their option is off. This candidate does not migrate them. Late default-priority same-call rewrites compose first; City Performance then declines and removes its own transpiler. Actual HMP/Mono modpack operation still requires native validation. Foreign patches at StreetLights and query/conversion boundaries, including registered alternate generic instantiations, are detected. Raw/native detours, incompatible Harmony registries and unrelated downstream HDRP patches are not covered.

Option disable, city exit and mod unload remove only this feature's transpiler. Failed removal leaves the helper on the original query path. Failures are retried only at explicit lifecycle boundaries, never each frame; a silent unsuccessful unpatch is also diagnosed. Adapter shutdown runs before unrelated drains because it owns a separate Harmony id.

## Validation

- Release compilation: zero warnings/errors.
- Existing pure regression suite: 338 checks pass (.NET 10 with net8 roll-forward).
- New HyperrealCheck: 69 checks pass on each local Windows and Mac assembly set.
- Actual Hyperreal IL: one operand changed, original input untouched, labels/EH preserved; zero/duplicate/wrong type/argument/branch fixtures refused.
- Actual helper tested using Harmony's generated replacement with only native query operands substituted. Arguments, fresh-result identity, disabled/unloaded fallback and exactly-once exception propagation are checked.
- Actual Harmony patch composition tests cover optional late loading, toggles/city lifetimes, pre-existing and later consumer/query/shared-generic conflicts, alternate finder substitution and own-only removal.
- Fault injection covers thrown and silently unsuccessful unpatches, no frame retry, persistent diagnostics, unknown Hyperreal hash and ambiguous loaded identity.

These are CoreCLR managed tests. They do not execute Unity native queries, validate Mono generic sharing, compare restored lamp fields or measure rendering/frame cadence.

## Independent review rounds

Astra and Sol reviewed the design and two implementation rounds; Sol independently ran the 65-check intermediate harness on both assembly sets. Opus 5.5 identified the frame-by-frame unpatch retry risk, enum constant checks, shared generic hook coverage and diagnostic gaps; these were fixed with regression tests. Grok 4.7 requested explicit method-identity equality and a direct clean activation assertion; both were added. The prior helper fixture already exercised the active path on CoreCLR, so no observed self-conflict is claimed. The first Grok attempt ended cancelled without a review and is excluded from the panel result.

Final closure: Astra, Sol, Opus 5.5 and Grok 4.7 all approved the source/offline candidate with no unresolved actionable findings. Their reports are retained under the ignored `build/hyperreal-20261022` evidence directory. The exact tested DLL is `3edcb55533fec5539f77c957309c36bba0d0fac83aaf42ad11677cf23dbdd402`. Native acceptance remains separate from source approval.

## Pending native acceptance

Use disposable saves and matched original/candidate configurations. Compare object identity sets for inactive/disabled, dynamic, destroyed, additive-scene, DDOL and DontSave lights. Check enhancement off/on, external intensity edits, city reload/new runtime instance and new lamps near foliage. Compare captured/restored values and visual output. Measure both query and complete Scan, distinguish steady versus new-lamp scans, include observer-free controls, and inspect whole-frame tails. Keep the existing unrelated save/cleanup spikes and Persistent City overshoot failures open.

No PC connection, live rig operation, normal-game installation, save modification or sibling-mod source change was performed for this candidate.
