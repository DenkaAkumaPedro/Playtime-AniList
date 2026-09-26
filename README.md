# Playtime AniList

> A [Playnite](https://playnite.link) 10.x generic extension that turns your
> [AniList](https://anilist.co) progress into real play time inside Playnite.

Anime and manga are not "games", but they have a natural place in a game library: they
have a title, a cover, a status and a progress. What they don't have is a place in the
**Playtime** field, which is exactly what Playnite uses for *Total Playtime*,
*Average Playtime* and *Most Played* in its statistics screen.

**Playtime AniList** fills that field from your AniList progress, so your whole anime
**and manga** collection shows up in the statistics next to your games.

- AniList id: `PlaytimeAniList_C034A45E-3C56-48DA-ABE0-1C46B4C4A57D`
- Menu: **Extensions ▸ Playtime AniList**

> **Testing note:** 1.1 is a beta. The stable release is
> [1.0](https://github.com/DenkaAkumaPedro/Playtime-AniList/releases/tag/v1.0)
> (anime only). See the [changelog](CHANGELOG.md) for what changed.

## Features

### Anime and manga play time
- Play time is written to Playnite's `Playtime` field, so it shows up in
  **Library ▸ Statistics** (`Total Playtime`, `Average Playtime`, `Most Played`).
- Anime: `AniList progress × episode duration`, so a half-watched series gets a
  proportional value instead of the full runtime.
- Manga: estimated from the AniList progress and the volume/chapter counts. AniList has
  no page counts, so reading speed is estimated - see
  [How the manga time is calculated](#how-the-manga-time-is-calculated) and
  [docs/COMO_CALCULA_TEMPO.md](docs/COMO_CALCULA_TEMPO.md) (in Portuguese).
- Optional **Add to existing playtime** mode, which accumulates instead of replacing.

### Library activity
- `LastActivity` is set from the AniList `updatedAt` value, so an anime or manga you
  updated on AniList shows up in the library's *Recent activity* grouping and filters.
- `PlayCount` is flagged the first time an entry has progress, which is what the
  activity-based filters look at.

### Automation
- Optional **automatic sync**, at most once every 24 hours, triggered by a library
  update (`OnLibraryUpdated`) instead of running on every launch.
- Result is reported through a Playnite notification (can be turned off).
- The 24 h lock is stored in `state.json` in the extension's user data folder, outside
  the settings dialog, so it survives restarts and never fights with the settings UI.

### Sync scopes and menus
- `Extensions ▸ Playtime AniList` (main menu, with the 3 original items plus
  *Sync anime and manga*).
- `Playtime AniList ▸ Update play time of these items` from the game context menu.
- Sync everything, or only what changed on AniList in the last 7 or 30 days.

## Requirements

- Playnite 10.x with API version 6.17.0 or newer.
- `Importer for AniList` installed and authenticated (its access token is reused).

## Installation

1. Download the `.pext` from the
   [releases page](https://github.com/DenkaAkumaPedro/Playtime-AniList/releases).
2. Open Playnite and drag the file into the window, or double-click it.

> Coming from the pre-release build that was called **AniList Com Horas**? Uninstall it
> first (the extension id changed to `PlaytimeAniList_…`, so Playnite would otherwise load
> both copies). Your settings start fresh.

Once the add-on is listed in the Playnite add-on database, you can also install it from
Playnite's add-on browser, or with the URI:

```
playnite://playnite/installaddon/PlaytimeAniList_C034A45E-3C56-48DA-ABE0-1C46B4C4A57D
```

## Usage

**Extensions ▸ Playtime AniList**

| Item | What it does |
| --- | --- |
| Sync everything | Updates every imported game, using the scope selected in the settings. |
| Sync last week updates | Only entries updated on AniList in the last 7 days. |
| Sync last month updates | Only entries updated on AniList in the last 30 days. |
| Sync anime and manga | Always syncs both media types, ignoring the scope setting. |

For selected games, right-click and choose **Playtime AniList ▸ Update play time of these
items**.

A summary dialog reports how many games were updated, how many had no AniList link, how
many were skipped, how many manga matched, how much time was added for manga and how many
activity dates were updated.

## How the anime time is calculated

```
seconds = AniList progress (episodes watched) × episode duration (minutes) × 60
```

Example: *Chainsaw Man*, 4 episodes of 24 min watched → `4 × 24 × 60 = 5 760 s` = 1 h 36 min.

## How the manga time is calculated

AniList does not store page counts, so manga needs an estimate. With the default settings
(200 pages per volume, 18 seconds per page):

```
pages per chapter = pages per volume × volumes ÷ chapters      (clamped to 6…120)
seconds           = chapters read × pages per chapter × seconds per page
```

Which lands very close to reality: a volume is a physical book, and reading one takes
roughly an hour.

| Situation | Result with defaults |
| --- | --- |
| Chainsaw Man, 232 chapters / 24 volumes, all read | 24 h (≈ 1 h per volume) |
| Attack on Titan, 141 chapters / 34 volumes | 34 h (monthly release, 14.5 min/chapter) |
| One Piece, 500 chapters, no volume data yet | 41.7 h (5 min/chapter fallback) |
| Solo Leveling, 143 chapters / 11 volumes, `countryOfOrigin: KR` | 23.8 h (webtoon, 10 min/chapter) |
| Mushoku Tensei, 334 chapters, format `NOVEL` | 66.8 h (novel, 12 min/chapter) |
| Look Back, format `ONE_SHOT` | 1 h (one volume = 200 pages) |

Special cases, all configurable: `NOVEL` uses minutes per chapter, webtoons
(`countryOfOrigin` KR or CN) use their own minutes per chapter, and `ONE_SHOT` is a single
volume. Series that AniList has no chapter or volume count for fall back to
minutes per chapter. Progress `0` is always `0`.

## Settings

*Playnite ▸ Settings ▸ Extensions ▸ Playtime AniList*

| Option | Default | Description |
| --- | --- | --- |
| Sync anime | on | Include anime in the sync. |
| Sync manga | on | Include manga in the sync. |
| Add to existing playtime | off | Accumulate the AniList value instead of replacing. |
| Pages per volume | 200 | Typical tankobon page count, used to derive pages per chapter. |
| Seconds per page | 18 | Average reading speed. |
| Fallback minutes per chapter | 5 | Used when AniList has no chapter/volume data. |
| Novel minutes per chapter | 12 | For `format: NOVEL`. |
| Webtoon minutes per chapter | 10 | For `countryOfOrigin: KR` or `CN`. |
| Automatic sync on library update | off | Sync at most once every 24 h, after a library update. |
| Show notification after automatic sync | on | Report the result as a Playnite notification. |
| Access token override | empty | Leave empty to reuse the `Importer for AniList` token. |

Out-of-range values are clamped, so a typo cannot produce an absurd play time.

## How the AniList id is found

`Importer for AniList` can be configured with `addLinksAndImages: false`, and when it is,
`Game.Links` is empty. The extension therefore reads the AniList media id from
`Game.GameId` first (that is where the importer stores it) and only falls back to
parsing `Game.Links` for an `anilist.co` URL. Version 1.0 only did the latter, which
silently skipped every game imported without links.

## Known limitations

- Manga play time is an estimate, not a measurement. AniList has no page counts and the
  extension cannot know your real reading speed. Tune the settings to match how you read.
- There is no per-day or per-month play time history. Playnite's public extension API
  exposes `Playtime` and `LastActivity`, but not a session table, so Playnite's charts
  aggregate totals rather than showing a time series.
- Sync is not real time. AniList has no push notifications for list updates, so the
  extension syncs when you ask it to, or at most once a day after a library update.
- The extension does not launch anything; it only writes the `Playtime` field.

## Building from source

```powershell
# auto-detects the Playnite install dir (or pass -PlayniteDir)
.\build.ps1
```

Requirements: .NET SDK, and a Playnite install with `Toolbox.exe` and
`Playnite.SDK.dll`. The script builds `src/AniListWatchTime.csproj`, stages
`extension.yaml` + `icon.png` + the built DLL, and calls `Toolbox.exe pack` to produce
the `.pext` in `dist/`.

## Links

- Source code: <https://github.com/DenkaAkumaPedro/Playtime-AniList>
- Issues: <https://github.com/DenkaAkumaPedro/Playtime-AniList/issues>
- Portuguese version of this page: [README.pt-BR.md](README.pt-BR.md)
- Changelog: [CHANGELOG.md](CHANGELOG.md)

## Credits

Data from the [AniList](https://anilist.co) GraphQL API. Playnite is by
[Josef Nemec](https://github.com/JosefNemec/Playnite). The default reading speed was
sanity-checked against real reading-time reports for tankobon and webtoon.

## License

[MIT](LICENSE)
