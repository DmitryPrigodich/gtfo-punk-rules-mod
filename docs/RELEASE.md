# Publishing to Thunderstore (r2modman)

r2modman installs mods from [Thunderstore](https://thunderstore.io/c/gtfo/); publishing there makes the package
show up in r2modman's online list for GTFO.

## The package (`./build.sh` → `dist/<Name>-<version>.zip`)
Files at the zip root:
- `manifest.json` — `name` (letters, digits, `_` only; shown with `_` as spaces), `version_number` (`X.Y.Z`; a version
  can never be uploaded twice), `website_url` (the GitHub repo), `description` (≤ 250 characters),
  `dependencies` (`["BepInEx-BepInExPack_GTFO-<version>"]`).
- `icon.png` — 256×256 PNG.
- `README.md` — the package page. `CHANGELOG.md` — the Changelog tab.
- The plugin DLL (and for Rules the `icons/*.png`; r2modman flattens folders, the loader looks next to the DLL too).

## Before a release
1. Bump `version_number` in `packaging/<Pkg>/manifest.json` and add the CHANGELOG entry.
2. `./build.sh`; check the zip's layout (`unzip -l dist/*.zip`).
3. Import the zip into a clean r2modman profile (Settings → Import local mod), start the game, check the log.
4. Commit, tag `v<version>`, push.

## Uploading
1. Sign in at thunderstore.io (Discord, GitHub or Overwolf account).
2. Create a team (Settings → Teams). The team name becomes the package's namespace and **can't be changed later**;
   it goes into dependency strings (`<Team>-<Name>-<version>`).
3. Upload: thunderstore.io → *Upload*, pick the zip, the team, community **GTFO**, categories (e.g. Mods, Client-side /
   Server-side as fitting), *Submit*.
4. Updates: a new zip with a higher `version_number`; an uploaded version can't be edited.

Optional: the Thunderstore CLI (`tcli`, a `thunderstore.toml` + an API token from the team settings) uploads from the
command line / CI. Keep the token out of the repo.
