# Switch Playtime (Exophase) — Playnite extension

Pulls the playtime of your **Nintendo Switch** and **Switch 2** games from your
[Exophase](https://www.exophase.com/) profile and writes it into Playnite:

- **updates** the playtime of Switch games already in your library (matched by name);
- **imports** missing Switch games as new entries (platform *Nintendo Switch* / *Nintendo Switch 2*,
  source *Exophase (Nintendo Switch)*), with playtime, last‑played date and a link to Exophase;
- optionally **downloads metadata from IGDB** (cover, description, genres, release date, …)
  for the imported games.

Syncing is **idempotent**: imported games are tagged with a stable `GameId`
(`exophase:<id>`), so running it again updates instead of duplicating.

## How it works

Exophase has no documented public API, but the site exposes an internal JSON API on a
separate subdomain (which is why it does not show up in the Network tab of the profile page):

1. The page `https://www.exophase.com/user/<username>/` embeds the player id in its HTML
   (`window.playerProfileId = ...`). The extension loads it through Playnite's embedded
   browser (CefSharp) to get past Cloudflare, and extracts the id.
2. Games are then read, page by page, from
   `https://api.exophase.com/public/player/<id>/games?page=N` (JSON, no authentication).
3. Entries whose platform is *Switch* / *Switch 2* are kept, and `playtimeUnits.hours/minutes`
   gives the playtime.

## Settings

Playnite → Settings → Add-ons → **Switch Playtime (Exophase)**:

**Exophase account**
- **Exophase username** — your global Exophase account username (this is **not** your
  Nintendo account name). A profile URL also works.

> **Keep your Exophase data fresh first.** Exophase does not refresh on its own:
> open your Exophase user page → **Nintendo** tab → **Options** → **Run profile sync**,
> wait a moment, then run the sync from this add-on.

**Platforms**
- **Import Switch games** / **Import Switch 2 games** — which platforms to include.

**Library**
- **Add games missing from Playnite** — create new entries for Switch games not present.
- **Update games already in Playnite** — refresh games matched by name.
- **Only match games on a Switch platform** *(recommended)* — avoids overwriting another
  version (PC, etc.) of the same game. Disable if your existing Switch games aren't tagged
  with a Switch platform.

**Playtime**
- **Import playtime** — write the Exophase playtime into Playnite.
- **Overwrite existing playtime** — replace the current value; when off, keep the larger one.

**Other data**
- **Import last played date**.

**Metadata**
- **Download metadata from IGDB for imported games** — covers, descriptions, genres, etc.
- **Overwrite existing metadata** — refresh all fields; when off, only fill empty fields.

**Filters**
- **Skip demos** — ignore titles that look like demos.
- **Ignore games played less than N minutes** — `0` imports everything.

## Usage

Main menu → **Add-ons** → **Switch Playtime (Exophase)** → **Sync Switch playtime from Exophase**.

## Build (development)

Requirements: a .NET SDK (≥ 5) with `dotnet`, Playnite installed (it provides
`Playnite.SDK.dll` at runtime), and the `nuget.org` package source configured.

```powershell
dotnet build .\SwitchPlaytimeExophase.csproj -c Release
```

Output lands in `bin\Release\` (DLL + `extension.yaml` + `icon.png`). The Playnite SDK is
deliberately **not** copied to the output (Playnite ships its own copy).

Helper script:

```powershell
.\build.ps1            # build only
.\build.ps1 -Install   # build + copy into %AppData%\Playnite\Extensions (restart Playnite)
.\build.ps1 -Pack      # build + produce dist\*.pext via Toolbox
```

## Package (.pext)

```powershell
& "$env:LOCALAPPDATA\Playnite\Toolbox.exe" pack ".\bin\Release" ".\dist"
```

Double‑click the resulting `dist\SwitchPlaytime_Exophase_<version>.pext` to install.

## Notes & limitations

- **Exophase only tracks the last ~20 games** the Switch shows in its activity log
  (a Nintendo limit). Once a game has been seen, Exophase keeps it, but the initial history
  may be incomplete. The Switch activity log must be public.
- **Metadata matching is by name**, so demos / unusual titles may not be found on IGDB
  (logged, not an error).
- Achievements / completion percent are not meaningful for Switch and are not imported.
- Imported games are catalog entries (not launchable) — you can't launch a Switch game from PC.
