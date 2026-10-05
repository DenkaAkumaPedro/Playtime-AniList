# Changelog

All notable changes to **AniList PlayTime** (called *Playtime AniList* up to 1.2).
This project follows [Semantic Versioning](https://semver.org/) loosely: `1.0` is the
first public release, `1.1` adds manga support.

## [1.4] - 2026-10-04

### Added
- **Incremental (delta) session mode for GameActivity** (new default). Each sync now appends a session with only the time added since last sync, creating a true time series. The total playtime in GameActivity is the sum of all sessions.
- Session mode setting: choose between **Incremental** (new sessions on progress) and **Replace Total** (single session with total time, v1.3 behavior).

### Changed
- Default session mode is now **Incremental** for better time-series visualization in GameActivity charts.

## [1.3] - beta (unreleased)

### Added
- **Session history through GameActivity** (off by default). With *Write a session in
  GameActivity* enabled, the extension also writes one session per entry through the
  GameActivity add-on, which has a session table, so anime and manga show up as a time
  series and not only as a total. The session is dated with the AniList `updatedAt`, and it
  is *replaced* on every sync, so syncing ten times still leaves one session. Without
  GameActivity the sync still works and still writes `Playtime`; only the sessions are
  skipped, with one line in the log.
- The summary now reports how many sessions were written.

### Fixed
- **A partial sync no longer deletes the baseline of the media type it did not touch.**
  `PruneBaseline` dropped every entry whose game was not seen in this run, but a week or
  month sync only *queries* the recently updated media; the other ones were never
  evaluated, and still had their `AppliedSeconds` erased. With *Add to existing playtime*
  on, the next full sync then added the whole time a second time.
- **Syncing a selection no longer wipes the time of a series whose progress is back to 0.**
  The 1.2 fix for progress 0 only covered the library sync; the selected sync treated zero
  as "no data" and skipped the entry, leaving the old time in place.
- **A selection sync also clears the GameActivity session** when the progress returns to 0,
  so the charts no longer keep a dead session alive.
- **A sync of more than 30 selected items is no longer truncated.** The `GetMediaListByIds`
  query was built for all selected games at once, while AniList caps a query at 50 ids. The
  selection is now processed in batches.
- **Two syncs can no longer run at the same time.** The context-menu sync now takes the same
  lock as the main-menu one; previously it could run while a full sync was still going.
- **A failed automatic sync no longer delays the next one by 24 h.** The 24 h stamp was
  written before knowing whether the sync had succeeded.
- **`state.json` is written atomically** (temp file plus `File.Replace`), so an interrupted
  write can no longer leave a half-written file that makes every later sync refuse to run.
- **A corrupted `state.json` is reported instead of being ignored.** The file was read as
  empty, which silently turned a syncing extension into a replacing one. The sync now stops
  and explains how to recover.
- **The recovery instructions for a deleted `state.json` were wrong.** The message claimed
  that a first sync after deleting the file would not inflate anything; with *Add to
  existing playtime* on it does add the AniList time on top of the value the extension
  itself had written. The message now gives a procedure that is actually safe: delete the
  file, turn *Add to existing playtime* off, sync once, turn it back on.
- **Session dates are no longer shifted by the local time zone.** The `updatedAt` timestamp
  was converted to local time before being stored, so a session could land on the previous
  day depending on where you are.
- **The GameActivity integration was verified against the real add-on** rather than against
  an assumption. The `IPluginDatabase<GameActivities>` interface arrives already closed, so
  the method lookup found nothing and every session was skipped. `GameActivities.
  SessionPlaytime` is also a computed property with no setter, which the first version tried
  to assign.

### Changed
- **Renamed to *AniList PlayTime*** and moved out of the main menu bar: the entry now lives
  under **Extensions**, and the `@`-prefixed menu label is gone. The extension id is
  unchanged, so this installs over previous versions and keeps your settings.
- The packaged file is named after the extension: `AniList_PlayTime_1-3.pext`. The build
  script renames the output of `Toolbox.exe pack`, which cannot set a name itself.

### Known issues in 1.3
- Nothing has run inside Playnite yet. The maths, the baseline handling and the GameActivity
  write are covered by reflection tests against the built DLL and against the installed
  GameActivity, but the end-to-end path still needs a manual pass.

## [1.2] - beta (never published)

### Fixed
- **"Add to existing playtime" no longer grows without bound.** The option added the
  AniList value on top of the current `Playtime` on *every* run, so with the 24 h
  automatic sync each entry doubled its time daily. The extension now records how much it
  wrote per game in `state.json` and replaces only its own contribution, leaving any
  pre-existing time alone.
- **The summary no longer reports manga time as `0h`.** The "Manga with calculated time"
  line counted every matched manga but only summed the *increase* of the current run, so
  an already-synced library reported `467 (0h)`. It now shows how many manga have time
  (`284 of 467`) and the actual total (`1569.5h`).
- **Progress `0` in AniList now clears the extension's own time.** The documented
  behaviour ("progress 0 is always 0") was not implemented. It is now: the extension
  removes only the time it wrote, and never touches time it cannot identify as its own.
  (1.3 fixes this for the selected sync too.)
- **Incomplete API responses can no longer wipe playtime.** The sync treated "AniList did
  not return this entry" the same as "AniList says progress 0". A partial response (rate
  limit, or a list large enough to hit the default `limit`) could therefore have cleared
  hundreds of entries. The two cases are now counted separately and only real progress 0
  clears anything.
- **Week/month syncs are now readable.** Syncing a 7-day window counted everything outside
  the window as "skipped", mixing it in with items that genuinely had no time. Those
  entries are now reported as "outside the sync window".

### Changed
- `LastActivity`/`PlayCount` are no longer advertised as a feature. `Importer for AniList`
  writes `LastActivity` itself when its "update last activity on library update" option is
  on, which is the default, so this extension is normally a no-op here. It is kept only as
  a fallback and the summary now explains a `0` result.

## [1.1] - beta (never published)

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
