---
name: localize-build-events-module
description: Localize, audit, build, test, package, safely install, self-update, release, and document the Community-Module-Pack Events and Metas Observer for Taiwan Traditional Chinese. Use when translating official Guild Wars 2 Wiki Event timer or bundled events.json names; preserving bilingual search/display; auditing Wiki-sourced world-boss rewards; diagnosing localization, schedule, waypoint, cache, parser, icon, package, or installed-BHM regressions; implementing or troubleshooting stable GitHub Release auto-update, autoUpdate settings, SHA-256 replacement, restart, or debug-module protection; injecting fork package versions without source-manifest churn; maintaining the events-module-zh-tw workflow and release digest checks; publishing a fork release; or updating its GitHub Pages landing page.
---

# Localize, update, test, and release Events Module

Treat the official English Guild Wars 2 Wiki timer data as the schedule authority and the bundled `events.json` as enrichment plus the final offline fallback.

## Data contract

- Load `Widget:Event_timer/data.json` through `https://wiki.guildwars2.com/api.php` for event times, Wiki links, and waypoint chat links.
- Keep source priority: validated official live data → last-known-good official cache → bundled `events.json`.
- Use bundled data for Taiwan Traditional Chinese terminology, icons, reminders, and template matching; never let it override newer official times or waypoints.
- Do not substitute the ArenaNet API v2 for the timer Widget. API v2 does not provide the complete Wiki timer schedule and waypoints.
- Treat `ref/event-rewards.json` as a separately verified enrichment source. The timer Widget has no reward fields; use current Guild Wars 2 Wiki reward/chest pages and never scrape that_shaman or another third-party timer at runtime.
- Preserve the official Widget segment name in `Meta.EnglishName`. Show it as a second line only when it differs from the localized name, and keep search and sorting based on `Meta` fields rather than the combined button text.

## Self-update contract

- Enable runtime self-update only in a stable release BHM built with a valid `X.Y.Z-fork.N` package version. Keep ordinary local, Debug, PR, push, and `-test` builds unable to replace themselves.
- Check only the repository's latest stable GitHub Release. Require the exact `events-zh-tw-vX.Y.Z-fork.N` tag, `Events.Module.bhm` asset, trusted GitHub HTTPS download URL, and `sha256:<64 hex>` digest.
- Use Blish HUD 1.0.0 `ModulePkgRepoHandler.InstallPackage`; do not use `ReplacePackage`, which disables the current module before download verification.
- Keep `autoUpdate` defaulted on. When off, report an available update without installing; turning it back on after a completed check must not trigger an immediate restart.
- Replace only a normally installed packed BHM. Never self-replace an unpacked module or a BHM loaded through `--module`／`-M`.
- Restart Blish HUD only after install and SHA-256 verification succeed, the `bh.general.events` enabled state is restored, and settings are saved. Any check or install failure must keep the current version running and must not request restart.

## Workflow

1. Inspect `git status` and preserve unrelated changes.
2. Read [references/events-module.md](references/events-module.md) for the relevant task area before changing source wiring, runtime keys, parser/cache behavior, icons, CI/CD, releases, or GitHub Pages.
3. Run localization coverage before editing:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\validate-events-localization.ps1
   ```

4. When official data, runtime key matching, reward matching, cache behavior, updater behavior, or API code is in scope, run the combined offline tests. Add `-Live` to audit the current Widget revision, known waypoints, all catalogued reward matches, and the latest stable GitHub Release contract:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\test-official-event-timer.ps1 -Live
   ```

5. Establish terminology from the current Wishing Star timer data, then cross-check GW2 API `lang=zh` proper nouns and player usage. Convert Simplified Chinese manually to Taiwan Traditional Chinese.
6. For reward changes, verify each amount and rule against current Guild Wars 2 Wiki pages, retain its HTTPS source URL and `verifiedOn` date, and add or update parser tests. Match by waypoint first and normalized English aliases second; never infer rewards for events absent from the catalog.
7. Keep runtime lookup keys byte-for-byte exact. Add a new official-only display key to both `Resources.resx` and `Resources.zh.resx`; keep the English value in the neutral file and translate only the zh value. For an existing key, normally edit only `Resources.zh.resx`.
8. Re-run coverage. Resolve missing or duplicate keys, placeholder mismatches, unused keys, and English event values.
9. If event icons or stable-ID mappings changed, run:

   ```powershell
   & "Events Module\Tests\ValidateEventIcons.ps1"
   & .agents\skills\localize-build-events-module\scripts\test-official-event-timer.ps1
   ```

10. Build and verify the standalone Chinese DLL and complete BHM. The package gate must include `ref/events.json`, `ref/event-rewards.json`, and all event icons:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\build-events-zh-tw.ps1
   ```

11. When self-update, release packaging, or CI is in scope, build a release-gated BHM into an isolated artifact directory with a nonpublished package version whose `X.Y.Z` matches the source manifest:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\build-events-zh-tw.ps1 `
       -OutDir artifacts\events-release-validation `
       -PackageVersion X.Y.Z-fork.N
   ```

   Confirm the packaged manifest uses that version, the source manifest version is unchanged, stable builds enable `RELEASE_BUILD`, ordinary and `-test` builds do not, and the BHM excludes `Blish HUD.exe` and `SemVer.dll`. This validates packaging only; do not install or publish the synthetic BHM.
12. Compare the built BHM with the copy installed under the redirected Windows Documents folder. This is read-only and reports both hashes, packaged icon counts, and whether Blish HUD is running:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\install-events-zh-tw.ps1 -CheckOnly
   ```

13. Only when the user explicitly asks to install locally, ensure Blish HUD has fully exited and run:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\install-events-zh-tw.ps1
   ```

   The script refuses to overwrite a loaded module, creates a timestamped backup, replaces through a verified temporary copy, and checks the final SHA-256 and packaged icons.
14. Report test results, official Widget version/event count and reward-match count, live GitHub tag/digest when audited, DLL/BHM paths, packaged version, self-update gate state, embedded resource and event-icon counts, built/installed SHA-256 comparison, and whether a Blish HUD restart is still required. Tag, publish, install, or update the live website only when the user explicitly asks.

## Guardrails

- Use Taiwan Traditional Chinese and established GW2 player terminology; treat machine translation only as a draft.
- Preserve `{0}` placeholders, filenames, URLs, chat links, and stable IDs.
- Preserve official English names independently from localized resource keys. Do not gate bilingual display on `CultureInfo`: the Chinese build embeds zh resources as neutral resources.
- Keep reward claims conservative and source-backed. Show rewards only for catalogued events, distinguish same-name bosses by waypoint, and keep account-daily versus character-daily limits explicit.
- Validate reward source URLs as `https://wiki.guildwars2.com/`, reject unsupported schemas and materially future verification dates, and let schedule functionality continue without rewards if the catalog fails safely.
- Never invent a waypoint when the official segment has no valid `chatlink`; omit the waypoint action.
- Preserve stable IDs in the form `wiki:{group}:{segment}`. If an ID must change, migrate watched-event settings.
- For the official Wiki source, keep a descriptive User-Agent, the 15-second timeout, cancellation, schema/sanity validation, and atomic last-known-good cache writes.
- For GitHub update checks, keep the 10-second timeout, unload cancellation, exact stable-tag/asset/digest validation, tuple version ordering, and nonblocking task completion from the module update loop.
- Do not edit `Resources.Designer.cs` for dynamically looked-up event keys.
- Build with `/p:ChineseBuild=true` when the main DLL must work without a `zh/` satellite assembly.
- Keep .NET Framework 4.7.2, compile against Blish HUD 1.0.0 or later as declared by the manifest, and never package `Blish HUD.exe` or `SemVer.dll` into the BHM.
- Treat the artifact BHM and installed BHM as different files. Do not claim an in-game fix is active until their SHA-256 hashes match and Blish HUD has restarted.
- During Codex-driven local installation, never close Blish HUD automatically. Refuse installation while it is running and ask the user to exit it first. This does not prohibit the module's user-enabled updater from restarting Blish HUD after a verified successful update.
- Treat `Events.Module.bhm` as the release/installable deliverable; use the safe installation script instead of copying over a loaded module by hand.
- Keep the source manifest at the upstream base `X.Y.Z`; inject `X.Y.Z-fork.N` only into the packaged manifest. Do not create a tag or Release merely to test this gate.

## Detailed reference

Use [references/events-module.md](references/events-module.md) for file paths, official MediaWiki requests, reward data and bilingual-name rules, runtime localization, source fallback, self-update implementation and failure rules, testing, build and local-install verification, troubleshooting, release tags/digests, and GitHub Pages deployment.
