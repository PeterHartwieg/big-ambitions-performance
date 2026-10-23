# Workshop art

Sources for the Workshop thumbnail (`../preview.png`, 512x512) and the gallery cards (`../cards/*.png`, 16:9, each under 1 MB). Each image is an HTML page: `preview.html` and `cards/NN-name.html`, styled by the shared `hart.css` and this mod's `theme.css`.

Run `./render.sh` to render them with headless Chrome. It takes the Archivo and IBM Plex Mono fonts from the HART brand kit (`HART_BRAND`, default `~/Coding_Projects/youtube/hart-group/brand`) and Pillow from `HART_PY` (default: the hart-group venv). Temporary files go to `build/`, which git ignores.

The package uses only `workshop/preview.png` as its root thumbnail. Gallery cards are not packaged. See `DESIGN-HANDOFF-MODS.md` in Coding_Projects for pending Opus copy redraws before gallery publication.
