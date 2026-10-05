# AGENTS.en.md — AniList PlayTime

Generic extension for Playnite 10.x (C#, `net462`, WPF). It writes AniList anime/manga time
into Playnite's `Playtime` field, and optionally one session per item into GameActivity.
No solution file, no test project, no CI.

> Main instruction file: `AGENTS.md` (pt-BR). Keep the two in sync.
> The local folder is called "Aniilist Com Horas" (old add-on name). The project is
> **AniList PlayTime** (called *Playtime AniList* up to 1.2), id
> `PlaytimeAniList_C034A45E-3C56-48DA-ABE0-1C46B4C4A57D`.

## Commands

```powershell
dotnet build src\AniListWatchTime.csproj -c Release   # builds on its own: Playnite SDK comes from NuGet
.\build.ps1 -NoPack                                   # build without packaging
.\build.ps1                                           # build + pack the .pext into dist\
.\build.ps1 -PlayniteDir "C:\Program Files\Playnite"  # when autodetect fails
```

- `build.ps1` needs a Playnite install with `Toolbox.exe` and `Playnite.SDK.dll`. Autodetect
  includes a personal path (`D:\Progamas\Biblioteca\Playnite`).
- There are **no** lint, format or test commands. Do not invent any. `net462` + WPF means
  Windows-only builds.
- How to verify logic: there is no automated test; the repo's practice is to call the private
  static methods by reflection over the compiled DLL, resolving dependencies with an
  `AssemblyResolve` pointing at the Playnite directory (see "Verificação" in
  `CONTINUAR.md`). For GameActivity, also load
  `Extensions\playnite-gameactivity-plugin` and the WPF assemblies
  (`PresentationFramework`, `PresentationCore`, `WindowsBase`, `System.Xaml`).
- PowerShell detail that bites: `Add-Type -AssemblyName A,B,C` fails for WPF; call one
  assembly at a time. And `PropertyInfo.SetValue` resolves ambiguously when the value is a
  `[Guid]`: when in doubt, write the test in C# (`Add-Type -TypeDefinition`) and pass in the
  already-loaded `Type` objects.

## Current state: 1.3 compiled and reflection-tested, nothing has run in Playnite

- Dirty working tree, with 1.2 and 1.3 side by side. 1.3 is compiled and packaged, but
  **nothing has run inside Playnite**.
- `ComputeTarget` and `PruneBaseline` have a reflection suite (11 + 1 cases) and
  `GameActivityWriter.UpsertSession` has a suite against the real GameActivity types
  installed. Everything passes. The end-to-end path is still missing.
- **Do not commit** and **do not** add the 1.3 entry to
  `Manifests/PackageInstaller/…yaml` until the user validates it in-game: the `PackageUrl`
  points at a release that has to exist.
- `CONTINUAR.md` (untracked, pt-BR) is the live handoff note — locked decisions, validation
  checklist and pending items. Read it before changing scope.
- `PENDENCIAS.md` was deleted on purpose and is in `.gitignore`. Do not recreate it.

## Where to edit

| File | Role |
| --- | --- |
| `src/AniListWatchTime.cs` | plugin, menus, 24 h autosync, `state.json` |
| `src/Services/AniListClient.cs` | GraphQL, time calculation, database writes, pending sessions |
| `src/Services/GameActivityWriter.cs` | session write by reflection (no compile-time reference) |
| `src/AniListWatchTimeSettings.cs` | settings, validation and clamps |
| `src/AniListWatchTimeSettingsView.xaml` | settings screen (fixed text, no resource binding) |

Rules that break silently if "simplified":

- `ComputeTarget` (`AniListClient.cs:807`) is the heart of "accumulate". Without a baseline
  (`lastApplied`) it never deletes time of unknown origin; on progress 0 it removes only its
  own contribution; with accumulate on it **replaces** the previous contribution instead of
  adding on top — the unbounded inflation was the bug 1.2 fixed.
- `PruneBaseline` (`AniListClient.cs:844`) may only receive in `seenGameIds` what was really
  **evaluated** in the round, never what was merely queried. A weekly window sync does not
  evaluate the manga; if they entered the prune, the next sync would add the whole time
  again. That is why the `seenGameIds.Add` sits at the top of the loop, before the filters.
- `SyncSelectedGames` can no longer `continue` on `seconds <= 0`: it has to go through
  `ComputeTarget` to clear the contribution and through the session queue to remove the old
  session. It used to be a `continue`, which left the selected sync without the progress-0
  behaviour.
- `returnedIds` and `evaluableIds` (`AniListClient.cs:177-206`) must stay separate:
  "AniList did not return this item" must never become "progress 0", otherwise a partial API
  response wipes playtime.
- `IndividualMediaQueryLimit = 30` (`AniListClient.cs:81`) because the AniList by-ids query
  blows up at 50. A larger selection goes through `CollectBatch`; do not build the query with
  all of them.
- Database writes stay inside `api.Database.BufferedUpdate()` and only call
  `Games.Update(game)` when something actually changed.
- AniList id: `Game.GameId` first, the `Game.Links` regex is only a fallback
  (`GetMediaIdFromGame`). Games are filtered by `PluginId == ImporterPluginId`
  (`2366fb38-bf25-45ea-9a78-dcc797ee83c3`).
- Token: read from `ExtensionsData/2366fb38-…/config.json` (`AccountAccessCode`), with an
  optional settings override. Never log it.
- `HttpClient.SendAsync(...).GetAwaiter().GetResult()` is blocking on purpose: it runs inside
  `ActivateGlobalProgress`. Autosync enters through `Task.Run` and returns to the UI via
  `RunOnUiThread`.
- Every sync path goes through `lock (syncLock)` (`AniListWatchTime.cs:207`, `:266`, `:279`,
  `:340`). The selected sync was left out once and could run together with the full sync.
- `SyncResult { Message, Failed }` replaced success detection by string prefix. Do not go
  back to checking text: a failure can start with the same word.
- `LoadState(out bool damaged)` + atomic `SaveState` (`.tmp` + `File.Replace`,
  `AniListWatchTime.cs:401`). A corrupt file **must not** be read as empty: that would turn
  "accumulate" into "replace" without the user asking.
- `MediaListCollection` is paginated in practice (≈1547 anime across 12 groups, measured
  09/2026). Never assume a single group in `lists`.

## GameActivity by reflection — the three traps

There is no public Playnite API to write sessions, so `GameActivityWriter.cs` does everything
by reflection and **does not** reference GameActivity at compile time. The three things that
have already broken are written as comments in the file:

1. `api.Addons.Plugins` contains **instances of `Plugin`**, not the `IAddon`. Find GameActivity
   by `Type.FullName == "GameActivity.GameActivity"`; trying to read `plugin.Addon` finds
   nothing.
2. The interface arrives **closed** (`IPluginDatabase<GameActivities>`), so `Get` and
   `AddOrUpdate` are already materialised: `IsGenericMethodDefinition` is `false` and
   `MakeGenericMethod` does not exist in that case. Filtering on it finds zero methods.
3. `GameActivities.SessionPlaytime` is a **computed property** with no setter: the getter sums
   `Items`. Assigning it by reflection throws `ArgumentException`, and the `try/catch` in
   `WriteSessions` swallowed that, so no session ever persisted without a visible error.

Chain confirmed against the installed GameActivity 3.5:
`GameActivity.GameActivity` → `GameActivityMonitoring` (property `GameActivityMonitoring`) →
`PluginDatabase` → `IPluginDatabase<GameActivities>`.

- `SessionSourceId` (`GameActivityWriter.cs:23`) is a fixed `Guid` and is what identifies
  "our" session. A session with another `SourceID` is never touched, and `SessionPlaytime`
  keeps counting it. Changing that Guid makes the extension duplicate the session once.
- `UpsertSession` removes the previous session **before** deciding what to insert, so progress
  0 wipes the dead session and `SessionPlaytime` drops along with it.
- A GameActivity failure must never fail the sync: `Playtime` is already in the database.

## The version lives in 4 places, and they have already been out of sync

1. `extension.yaml` → `Version:` and `Name:`
2. `src/Services/AniListClient.cs:137` → User-Agent `"AniListPlayTime/1.3 (Playnite extension)"`
3. `CHANGELOG.md` → the version section
4. `Manifests/PackageInstaller/PlaytimeAniList_….yaml` → `Packages:` (still 1.0 only)

The package name does **not** come from `Id:`: `Toolbox.exe pack` does not accept a custom
name, so `build.ps1` packs and then renames to `<Name>_<version>.pext`
(`AniList_PlayTime_1-3.pext`). `Id:` stays `PlaytimeAniList_…` on purpose, so Playnite treats
this as an update of the old extension and keeps the settings.

`build.ps1` hardcodes the path `src\bin\Release\net462\AniListWatchTime.dll`: changing
`TargetFramework`, `AssemblyName` or `Configuration` breaks packaging without a compile
error.

## Conventions

- Every user-facing string (menus, XAML, sync summary, logs) is **fixed Portuguese**.
  `src/Localization/en_US.xaml` is empty and no code reads `Localization/` — the glob only
  ships in the package. i18n is out of scope; do not introduce it.
- The visible name is "AniList PlayTime", with **one** `i` in `Ani`, even though the folder
  and some identifiers still say `Aniilist`/`AniList`. Do not "fix" the spelling of the name.
- Comment only when the "why" is not obvious (baseline math, returned vs evaluable, reflection
  traps). No line-by-line narration.
- No `.editorconfig` and no analyzers: follow the existing style (brace of `if`/`else` on its
  own line, `var` nearly everywhere).
- When touching a feature or setting, update `README.md` **and** `README.pt-BR.md` together.
- `dist/` and `*.pext` are gitignored: artifacts never enter a commit, the release upload is
  manual.

## Backup and release

- Before a beta: snapshot in `backup\<version>_<timestamp>\` with the `.pext` and a
  `src_<version>.zip`, plus the tag `<version>-snapshot-<timestamp>`. Delete it when the beta
  leaves testing. The folder only exists while there is no stable release.
- Commit: single-line subject in English, `AniList PlayTime <version>: <summary>`. Only
  commit when the user asks.

## Known bugs — do not "fix" them without the user asking

- If Playnite crashes between the database commit and `SaveState`, the baseline desyncs for
  one sync.
- **Deleting `state.json` is not neutral.** With "Add to existing playtime" on, the first sync
  after deleting it adds the AniList time on top of the value the extension had already
  written, because that value now looks like time of unknown origin. The safe procedure is in
  the corrupt-state message and in `docs/COMO_CALCULA_TEMPO.md`: delete, turn accumulate off,
  sync, turn it back on.