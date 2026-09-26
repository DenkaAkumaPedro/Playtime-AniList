# Playtime AniList

> A [Playnite](https://playnite.link) 10.x generic extension that turns your
> [AniList](https://anilist.co) progress into real play time inside Playnite.

Anime and manga are not "games", but they have a natural place in a game library: they
have a title, a cover, a status and a progress. What they don't have is a place in the
**Playtime** field, which is exactly what Playnite uses for *Total Playtime*,
*Average Playtime* and *Most Played* in its statistics screen.

**Playtime AniList** fills that field from your AniList progress, so your whole anime
(and, since 1.1, manga) collection shows up in the statistics next to your games.

- AniList id: `PlaytimeAniList_C034A45E-3C56-48DA-ABE0-1C46B4C4A57D`
- Menu: **Extensions ▸ Playtime AniList**

## Features (1.0)

- Syncs the play time of every game imported by
  [`Importer for AniList`](https://github.com/darklinkpower/PlayniteExtensionsCollection/wiki/Importer-for-Anilist).
- Play time is `AniList progress × episode duration`, so a half-watched series gets a
  proportional value instead of the full runtime.
- Three sync scopes: whole library, updates from the last 7 days, updates from the last
  30 days.
- Per-game sync from the game context menu.
- Optional **Add to existing playtime** mode, which accumulates the AniList value
  instead of replacing it.
- Reuses the access token from `Importer for AniList`; you can also paste your own.

## Requirements

- Playnite 10.x with API version 6.17.0 or newer.
- `Importer for AniList` installed and authenticated (its token is reused).

## Installation

1. Download the `.pext` from the
   [releases page](https://github.com/DenkaAkumaPedro/Playtime-AniList/releases).
2. Open Playnite and drag the file into the window, or double-click it.

Once the add-on is listed in the Playnite add-on database, you can also install it
straight from Playnite's add-on browser, or with the URI:

```
playnite://playnite/installaddon/PlaytimeAniList_C034A45E-3C56-48DA-ABE0-1C46B4C4A57D
```

## Usage

**Extensions ▸ Playtime AniList**

| Item | What it does |
| --- | --- |
| Sync everything | Updates play time for every imported game. |
| Sync last week updates | Only entries updated on AniList in the last 7 days. |
| Sync last month updates | Only entries updated on AniList in the last 30 days. |
| Sync anime and manga | *(since 1.1)* Same as "Sync everything", for both media types. |

For a single game, right-click it and choose
**Playtime AniList ▸ Update playtime of this anime**.

A summary dialog reports how many games were updated, how many had no AniList link and
how many were skipped.

## How the play time is calculated

For anime:

```
seconds = AniList progress (episodes watched) × episode duration (minutes) × 60
```

Example: *Chainsaw Man*, 4 episodes of 24 min watched → `4 × 24 × 60 = 5 760 s` = 1 h 36 min.

Games already have play time in Playnite, so the default is to **replace** the value.
Turn on *Add to existing playtime* if you want the AniList value to be added to whatever
is already there (useful for games that also have a PC build with real play sessions).

## Settings

*Playnite ▸ Settings ▸ Extensions ▸ Playtime AniList*

| Option | Description |
| --- | --- |
| Add to existing playtime | Accumulate instead of replacing. |
| Access token override | Leave empty to reuse the `Importer for AniList` token. |

## Known limitations in 1.0

- **Games imported without links are skipped.** `Importer for AniList` can be
  configured with `addLinksAndImages: false`; in that case `Game.Links` is empty and
  1.0 cannot find the AniList id. This is fixed in 1.1, which reads `Game.GameId`.
- Manga is not supported yet.

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

Data from [AniList](https://anilist.co) GraphQL API. Playnite is by
[Josef Nemec](https://github.com/JosefNemec/Playnite).

## License

[MIT](LICENSE)
