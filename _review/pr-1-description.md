General pedestrian refill now yields to Persistent City whenever its assembly is loaded. Previously CP checked only existing Harmony owners, so initialization order decided which mod controlled refill. An initial assembly scan plus load notification covers both orders without per-frame scans; CP cancels pending acquisitions without flushing, preserves incoming requests/exceptions, and logs the ownership decision once per load.

A call CP already clamped still restores its full target during handoff, without acquiring pedestrians; a newer request supersedes that restoration. Failed teardown detaches the assembly callback and cancels pending work while retaining the observed ownership decision if CP's hooks remain installed.

The assembly identity was verified read-only from Persistent City's build script (`PersistentCity.dll`); its pedestrian Harmony ID is `hart.persistentcity.ambientpedestrians.v1`. Existing build/conflict guards and saved preferences remain unchanged. README and Unreleased changelog describe the behavior.

Validation:
- Git Bash `bash tools/build.sh` with configured BA_GAME_MANAGED: 347 core checks pass; build passes with zero warnings/errors; local package generated.
- DetailCheck `--refill-ownership`: 24 offline checks pass, including both load orders, no-PC deferred behavior, pending cancellation, in-flight target restoration, superseding requests, exception identity, once-only logging and injected early unload failure.
- DetailCheck `--vanilla`: 53 checks pass, including audited and unaudited builds.
- DetailCheck `--button-captions`: 12 checks pass, including unload cleanup after a prior exception.
- DetailCheck build: zero warnings/errors. `git diff --check`: clean.

Refs Persistent City docs/issues.md #5. Native Unity/Mono acceptance remains open: verify both mod load orders, density requests, option changes and unload with pending work. The offline harness uses synthetic assembly identity and managed replacements for native calls. The late-load hook stays installed but no longer schedules refill; it only finishes target restoration owed by calls already in flight. Assembly presence remains authoritative even if Persistent City's hooks are subsequently disabled. No installation, version bump, publication or runtime performance claim.

