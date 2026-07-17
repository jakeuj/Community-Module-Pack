---
name: localize-build-events-module
description: Localize, audit, build, test, install, release, and document the Community-Module-Pack Events and Metas Observer for Taiwan Traditional Chinese. Use when translating runtime names or categories from the official Guild Wars 2 Wiki Event timer Widget or bundled events.json, diagnosing English fallback, schedule or waypoint mismatches, MediaWiki 403/timeouts/cache fallback, parser or icon regressions, missing packaged resources, a stale locally installed BHM, producing or safely installing a standalone Chinese DLL/BHM, maintaining the events-module-zh-tw workflow, publishing a fork release, or updating its GitHub Pages landing page.
---

# Localize, test, and release Events Module

Treat the official English Guild Wars 2 Wiki timer data as the schedule authority and the bundled `events.json` as enrichment plus the final offline fallback.

## Data contract

- Load `Widget:Event_timer/data.json` through `https://wiki.guildwars2.com/api.php` for event times, Wiki links, and waypoint chat links.
- Keep source priority: validated official live data → last-known-good official cache → bundled `events.json`.
- Use bundled data for Taiwan Traditional Chinese terminology, icons, reminders, and template matching; never let it override newer official times or waypoints.
- Do not substitute the ArenaNet API v2 for the timer Widget. API v2 does not provide the complete Wiki timer schedule and waypoints.

## Workflow

1. Inspect `git status` and preserve unrelated changes.
2. Read [references/events-module.md](references/events-module.md) for the relevant task area before changing source wiring, runtime keys, parser/cache behavior, icons, CI/CD, releases, or GitHub Pages.
3. Run localization coverage before editing:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\validate-events-localization.ps1
   ```

4. When official data, runtime key matching, cache behavior, or API code is in scope, run the parser tests. Add `-Live` to audit the current Widget revision and known waypoints:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\test-official-event-timer.ps1 -Live
   ```

5. Establish terminology from the current Wishing Star timer data, then cross-check GW2 API `lang=zh` proper nouns and player usage. Convert Simplified Chinese manually to Taiwan Traditional Chinese.
6. Keep runtime lookup keys byte-for-byte exact. Add a new official-only display key to both `Resources.resx` and `Resources.zh.resx`; keep the English value in the neutral file and translate only the zh value. For an existing key, normally edit only `Resources.zh.resx`.
7. Re-run coverage. Resolve missing or duplicate keys, placeholder mismatches, unused keys, and English event values.
8. If event icons or stable-ID mappings changed, run:

   ```powershell
   & "Events Module\Tests\ValidateEventIcons.ps1"
   & .agents\skills\localize-build-events-module\scripts\test-official-event-timer.ps1
   ```

9. Build and verify the standalone Chinese DLL and complete BHM:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\build-events-zh-tw.ps1
   ```

10. Compare the built BHM with the copy installed under the redirected Windows Documents folder. This is read-only and reports both hashes, packaged icon counts, and whether Blish HUD is running:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\install-events-zh-tw.ps1 -CheckOnly
   ```

11. Only when the user explicitly asks to install locally, ensure Blish HUD has fully exited and run:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\install-events-zh-tw.ps1
   ```

   The script refuses to overwrite a loaded module, creates a timestamped backup, replaces through a verified temporary copy, and checks the final SHA-256 and packaged icons.
12. Report test results, official Widget version/event count when live-audited, DLL/BHM paths, embedded resource and event-icon counts, built/installed SHA-256 comparison, and whether a Blish HUD restart is still required. Tag, publish, install, or update the live website only when the user explicitly asks.

## Guardrails

- Use Taiwan Traditional Chinese and established GW2 player terminology; treat machine translation only as a draft.
- Preserve `{0}` placeholders, filenames, URLs, chat links, and stable IDs.
- Never invent a waypoint when the official segment has no valid `chatlink`; omit the waypoint action.
- Preserve stable IDs in the form `wiki:{group}:{segment}`. If an ID must change, migrate watched-event settings.
- Keep a descriptive User-Agent, the 15-second request timeout, cancellation, schema/sanity validation, and atomic last-known-good cache writes.
- Do not edit `Resources.Designer.cs` for dynamically looked-up event keys.
- Build with `/p:ChineseBuild=true` when the main DLL must work without a `zh/` satellite assembly.
- Treat the artifact BHM and installed BHM as different files. Do not claim an in-game fix is active until their SHA-256 hashes match and Blish HUD has restarted.
- Never close Blish HUD automatically. Refuse installation while it is running and ask the user to exit it first.
- Treat `Events Module.bhm` as the installable deliverable; use the safe installation script instead of copying over a loaded module by hand.

## Detailed reference

Use [references/events-module.md](references/events-module.md) for file paths, official MediaWiki requests, runtime localization rules, source fallback, testing, build and local-install verification, troubleshooting, release tags, and GitHub Pages deployment.
