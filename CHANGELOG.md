# Changelog

All notable changes to **Playtime AniList**.
This project follows [Semantic Versioning](https://semver.org/) loosely: `1.0` is the
first public release, `1.1` adds manga support.

## [1.1] - beta (unreleased)

### Added
- Manga time tracking: reading time is estimated from AniList progress and written to
  Playnite's `Playtime` field, so manga shows up in Playnite's statistics.
- Optional automatic sync: runs once every 24 h after a library update
  (`OnLibraryUpdated`), with an optional notification.
- `LastActivity` is now set from the AniList `updatedAt` value and `PlayCount` is
  flagged, so entries show up in the library's activity filters.
- Main menu moved to **Extensions ▸ Playtime AniList**, with a new
  "Sync anime and manga" item.
- Settings for pages per volume, seconds per page, fallback minutes per chapter,
  novel/webtoon minutes per chapter.

### Fixed
- **Games imported without AniList links are now matched.** `Importer for AniList` can
  be configured with `addLinksAndImages: false`, in which case `Game.Links` is empty
  and version 1.0 silently skipped those games. The AniList media id is now read from
  `Game.GameId` first, with the link regex kept only as a fallback.

## [1.0] - 2026-09-26

### Added
- First public release.
- Syncs AniList watch time into Playnite's `Playtime` field for every game imported by
  `Importer for AniList` (anime only).
- Time is calculated as `AniList progress × episode duration`, so partially watched
  series get a proportional value.
- Main menu: sync the whole library, only last week's updates, or only last month's
  updates.
- Game context menu: "Update playtime of this anime" for the selected games.
- Optional "Add to existing playtime" mode, which accumulates instead of replacing.
- Access token can be pasted in the settings, otherwise the token from
  `Importer for AniList` is reused.

### Known issues in 1.0
- Games imported with `addLinksAndImages: false` are not matched, because 1.0 reads the
  AniList id from `Game.Links` only. Fixed in 1.1.
- Manga is not supported yet; only anime is synchronized.
