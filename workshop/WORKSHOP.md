# City Performance: Workshop upload

Paste-ready fields for the game's Mod Creator (Main menu > Mods > Mod Creator). This folder is not part of the package, except `preview.jpg` once it exists. Nothing here has been uploaded. Read `docs/release-readiness-0.5.0.md` first: the in-game checks listed there come before the upload.

## Fields

**Mod folder:** `CityPerformance`. Unzip `build/CityPerformance-0.5.0.zip` into `ModsLocal` on the PC that uploads; the Mod Creator lists the folders in `ModsLocal`.

**Title:** `City Performance` (open: Peter may want a HART. prefix like the other HART mods).

**Target build:** `3682`, the Windows build whose `BigAmbitions.dll` the behavioral hooks were audited against (SHA256 `9427561b…e911`).

**Version:** `0.5.0`. Package: `build/CityPerformance/`; archive: `build/CityPerformance-0.5.0.zip`; file hashes: `build/CityPerformance.sha256`.

**Thumbnail:** none yet. The repo has no preview image and no tooling that makes one. The Mod Creator takes the first image in the mod folder's root, so put a `preview.jpg` under 1 MB in `workshop/` and rerun `python3 tools/package.py`; it copies the file into the package root.

**Bug reports:** open. The other HART mods each have a Discord channel (`#self-driving-bugs`, `#free-camera-bugs`). There is no City Performance channel yet; the description below has a placeholder.

**Donations:** the PayPal hosted button `Q8KVURCRBFLQN` covers all of Peter's Big Ambitions projects, so the description reuses it.

**Licence:** open. The repo has no licence file. Driven Rides uses MIT for code. The package ships Harmony's own licence in `Dependencies/`.

**Description** (Steam formatting):

```
Experimental performance tools and diagnostics for Big Ambitions. Everything that changes how the game runs is optional, and most of it starts switched off. The mod never promises a frame-rate gain; it gives you the switches and the measurements to find out on your own city.

[h2]Works on its own[/h2]
You need no other mods. Parts made for other mods start only when those mods are loaded, in the exact versions this release was checked against:
[list]
[*][b]Bakery - V1.2[/b]: the Help menu finds its pages without searching the whole scene.
[*][b]POLYMODS HYPERREAL[/b]: its street-lamp search skips sorting the results.
[*][b]HART Free Camera[/b]: the city fills in around the recording camera, and you can listen from it.
[/list]
If one of those mods updates, that part steps aside and the mod keeps the original behavior until City Performance is updated too.

[h2]Game version[/h2]
The changes to the game were checked against the current Windows build (3682). On any other build, including the Mac version and the next game update, City Performance runs only its timing and trace tools. It switches the rest off by itself and says so in the log.

[h2]Options[/h2]
Under Settings > Mods > City Performance:
[list]
[*][b]Performance summaries[/b] (on): a frame-time summary in Player.log every 30 seconds.
[*][b]Smooth pedestrian refills[/b] (off): spreads a pedestrian refill over a few frames.
[*][b]Share stationary-zone spawn budget[/b] (off): limits how many street performers and standing characters appear in one frame.
[*][b]Distance-based tree detail[/b] (off): lets distant trees use their lighter mesh while keeping them visible.
[*][b]Batch midnight asset cleanups[/b] (off): runs the game's midnight memory cleanup less often, behind a loading screen where possible.
[*][b]Background autosaves[/b] (off): writes autosaves on a background thread after checking the first one byte for byte against the game's own save.
[*][b]Bakery Help and Hyperreal lamp search[/b] (on): only with those mods loaded.
[*][b]Camera-aware world coverage[/b] and [b]Listen from the second camera[/b] (on): only with HART Free Camera loaded.
[*][b]Trace recording and export[/b] (off): records recent frames and the worst hitches and saves them as CSV files.
[/list]

[h2]Good to know[/h2]
[list]
[*]These are experiments. Turn one on, play the same route as before and compare. If a switch does nothing for your city, turn it back off.
[*]Background autosaves are the riskiest option. Keep a manual save. The mod keeps the previous autosave next to the new one as a .cityperformance-previous file.
[*]If another mod hooks the same game code, that part of City Performance steps aside instead of fighting it.
[*]Traces go to the CityPerformanceTraces folder next to Player.log, in %USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions.
[*]The mod adds nothing to your save. Unsubscribe and your saves load without it.
[/list]

[h2]Bug reports[/h2]
Post bugs in [url=DISCORD-LINK]#city-performance-bugs on my Discord[/url], one post per problem. Include the mod version, the game build, your other mods and what happened. The City Performance lines in Player.log help most, especially the first one, which shows the version and whether your game build is the audited one.

[h2]Support[/h2]
The mod is free and stays free. If you'd like to say thanks, you can [url=https://www.paypal.com/donate/?hosted_button_id=Q8KVURCRBFLQN]support my Big Ambitions projects via PayPal[/url]. It helps me keep this mod working after game updates.

Made for the HART. Group series. The full README ships with the mod.
```

**Change log** (first upload):

```
0.5.0: first Workshop release

- Works with no other mods installed. Bakery, Hyperreal and HART Free Camera features start only when those mods are loaded.
- Camera-aware coverage now needs HART Free Camera; without it the game's own coverage is untouched.
- On a game build other than the audited one, only the timing and trace tools run.
- A feature that breaks after a game update now stops on its own and leaves the others running.

Restart the game after updating.
```
