---
name: localize-build-events-module
description: Localize, audit, build, test, package, safely install, self-update, release, document, and present the Community-Module-Pack Events and Metas Observer for Taiwan Traditional Chinese. Use when translating the official Guild Wars 2 Wiki Event timer or bundled events.json names; preserving bilingual search/display; implementing or troubleshooting Wiki links/buttons, waypoint/chat clipboard formats, compact verified reward summaries, previews, and settings; auditing Wiki-sourced world-boss or fixed-coin rewards; diagnosing localization, schedule, waypoint, cache, parser, icon, package, or installed-BHM regressions; implementing or troubleshooting GitHub Release self-update, autoUpdate settings, SHA-256 installs, restarts, or debug-module guards; injecting fork package versions without source-manifest churn; maintaining events-module-zh-tw CI and release digest checks; publishing a fork release; or building, validating, optimizing, and deploying the jakeuj GW2 Tools GitHub Pages landing page and tool portfolio.
---

# Localize, update, test, and release Events Module

Treat the official English Guild Wars 2 Wiki timer data as the schedule authority and the bundled `events.json` as enrichment plus the final offline fallback.

## Data contract

- Load `Widget:Event_timer/data.json` through `https://wiki.guildwars2.com/api.php` for event times, Wiki links, and waypoint chat links.
- Keep source priority: validated official live data → last-known-good official cache → bundled `events.json`.
- Treat a Widget `link` containing `://` as URL-shaped and accept it only when it is HTTPS on `wiki.guildwars2.com`; treat every other nonempty value as a Wiki title, including namespace titles such as `Convergence: Mount Balrior`. Do not use absolute `Uri.TryCreate` alone to distinguish titles because .NET treats the text before `:` as a URI scheme.
- Use bundled data for Taiwan Traditional Chinese terminology, icons, reminders, and template matching; never let it override newer official times or waypoints.
- Do not substitute the ArenaNet API v2 for the timer Widget. API v2 does not provide the complete Wiki timer schedule and waypoints.
- Treat `ref/event-rewards.json` as a separately verified enrichment source. The timer Widget has no reward fields; use current Guild Wars 2 Wiki reward/chest pages and never scrape that_shaman or another third-party timer at runtime.
- Preserve the official Widget segment name in `Meta.EnglishName`. Show it as a second line only when it differs from the localized name, and keep search and sorting based on `Meta` fields rather than the combined button text.

## Chat copy format contract

- Keep `Managed Settings/useCustomCopyFormat` defaulted to `false`. When disabled, both manual copy entry points must copy only the original valid waypoint chatlink.
- Store the editable template in `Managed Settings/customCopyFormat`. Support exactly `{point}`, `{event}`, `{event_zh}`, `{event_en}`, `{category}`, `{category_zh}`, `{category_en}`, `{time}`, and `{reward}`; use `{{` and `}}` for literal braces.
- Require a nonempty template containing a real `{point}` field, balanced braces, and no unknown fields. On failure, copy the original waypoint and show the localized fallback notice.
- Build `{event}` and `{category}` as localized/English smart bilingual values, omitting the English duplicate when both values match. Use `Meta.EnglishName` and `Meta.Category` for English values, localized resources for zh values, and `Meta.NextTime.ToShortTimeString()` for card-consistent local time.
- Build `{reward}` only from a matched, verified reward catalog entry as a localized compact guarantee summary. Compose optional rare/exotic, `CompactDragoniteAmount`, and fixed-coin components in that order; fixed coin must include its compact account-daily qualifier. Return an empty value for unlisted events and never include tooltip notes, character-daily limits, sources, or verification dates in chat.
- Trim only the completed message's outer whitespace. Preserve internal text, whitespace, and punctuation. When changing the localized default, migrate only stored values that exactly match a prior neutral or Chinese default; preserve every user-edited format.
- Route the event-card waypoint button and notification left-click through the same formatter/clipboard method. Never copy on notification display, synthesize a missing waypoint, paste, emulate keys, or send chat automatically.

## Self-update contract

- Enable runtime self-update only in a stable release BHM built with a valid `X.Y.Z-fork.N` package version. Keep ordinary local, Debug, PR, push, and `-test` builds unable to replace themselves.
- Check only the repository's latest stable GitHub Release. Require the exact `events-zh-tw-vX.Y.Z-fork.N` tag, `Events.Module.bhm` asset, trusted GitHub HTTPS download URL, and `sha256:<64 hex>` digest.
- Use Blish HUD 1.0.0 `ModulePkgRepoHandler.InstallPackage`; do not use `ReplacePackage`, which disables the current module before download verification.
- Keep `autoUpdate` defaulted on. When off, report an available update without installing; turning it back on after a completed check must not trigger an immediate restart.
- Replace only a normally installed packed BHM. Never self-replace an unpacked module or a BHM loaded through `--module`／`-M`.
- Restart Blish HUD only after install and SHA-256 verification succeed, the `bh.general.events` enabled state is restored, and settings are saved. Any check or install failure must keep the current version running and must not request restart.

## Workflow

1. Inspect `git status` and preserve unrelated changes.
2. Read [references/events-module.md](references/events-module.md) for the relevant task area before changing source wiring, runtime keys, chat-copy formatting or settings, parser/cache behavior, icons, CI/CD, releases, GitHub Pages, or the site's release metadata and assets.
3. Run localization coverage before editing:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\validate-events-localization.ps1
   ```

4. When official data, runtime key matching, chat formatting, reward matching, cache behavior, updater behavior, or API code is in scope, run the combined offline tests. Add `-Live` to audit the current Widget revision, known waypoints, all catalogued reward matches, and the latest stable GitHub Release contract:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\test-official-event-timer.ps1 -Live
   ```

   For Wiki-link parser changes, cover ordinary titles, namespace titles containing `:`, anchors, valid official absolute URLs, and malformed, non-HTTPS, or off-domain URL-shaped values offline; keep live assertions for known namespace links such as both Convergence events.

5. Establish terminology from the current Wishing Star timer data, then cross-check GW2 API `lang=zh` proper nouns and player usage. Convert Simplified Chinese manually to Taiwan Traditional Chinese.
6. For reward changes, verify each amount and rule against current Guild Wars 2 Wiki pages, retain every supporting HTTPS source URL and `verifiedOn` date, and add or update parser tests. Match by official stable ID first, then a waypoint proven unique across the complete selected schedule with a compatible normalized alias, and finally a unique normalized English alias; never infer rewards from a shared waypoint or for events absent from the catalog.
7. Keep runtime lookup keys byte-for-byte exact. Add new official-only display keys and new chat-format UI/status strings to both `Resources.resx` and `Resources.zh.resx`; keep English values in the neutral file and translate zh values for Taiwan. For an existing event key, normally edit only `Resources.zh.resx`. Do not modify `Resources.Designer.cs` for dynamically looked-up strings.
8. Re-run coverage. Resolve missing or duplicate keys, placeholder mismatches, unused keys, and English event values.
9. If event icons or stable-ID mappings changed, run:

   ```powershell
   & "Events Module\Tests\ValidateEventIcons.ps1"
   & .agents\skills\localize-build-events-module\scripts\test-official-event-timer.ps1
   ```

10. Build and verify the standalone Chinese DLL and complete BHM. The package gate must include `ref/events.json`, `ref/event-rewards.json`, and all event icons, and must reject any nested `.bhm` entry. Repeated builds to the same output directory must remain clean:

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
14. When the GitHub Pages site is in scope, keep it progressively enhanced and validate it locally:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\test-events-landing-page.ps1
   ```

   Keep the stable release fallback usable without JavaScript, accept only the exact stable release contract before replacing its version text, and preserve keyboard, touch, reduced-motion, and no-JavaScript behavior. Re-run coverage and the live audit before publishing changed statistics or release claims.
15. Only when the user explicitly asks to publish the website, push the intended commit to the fork's `master`, then wait for `master:/docs` to build that exact commit and verify the public site:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\test-events-landing-page.ps1 `
       -Live -WaitForCommit (git rev-parse HEAD)
   ```

   Do not create a module tag or Release for a website-only deployment. Do not push website work to the upstream `origin`.
16. Report test results, including chat-formatter coverage when changed; official Widget version/event count and reward-match count; live GitHub tag/digest when audited; DLL/BHM paths; packaged version; self-update gate state; embedded resource and event-icon counts; built/installed SHA-256 comparison; Pages commit/build state when deployed; and whether a Blish HUD restart is still required. Tag, publish, install, or update the live website only when the user explicitly asks.

## Guardrails

- Use Taiwan Traditional Chinese and established GW2 player terminology; treat machine translation only as a draft.
- Preserve `{0}` placeholders, filenames, URLs, chat links, and stable IDs.
- Preserve chat-template field names exactly and keep parsing case-sensitive. Do not treat escaped `{{point}}` as the required waypoint field.
- Keep custom chat formatting opt-in and manual. Invalid formats must fail safely to the original waypoint; alerts must never alter the clipboard merely by appearing.
- Preserve official English names independently from localized resource keys. Do not gate bilingual display on `CultureInfo`: the Chinese build embeds zh resources as neutral resources.
- Keep reward claims conservative and source-backed. Store rare/exotic gear, Dragonite, and fixed coin as independent components with explicit limits; show only daily-scope guarantees for public instances. Prove waypoint uniqueness across the complete schedule and require a compatible alias before waypoint fallback; otherwise use stable ID or a known alias. Keep account-daily versus character-daily limits explicit in the detailed card tooltip; include only the compact `（帳號每日）` qualifier for fixed coin in chat `{reward}`.
- Validate catalog v3 structurally: require unique event IDs, stable IDs, normalized aliases, optional waypoints, and nonempty unique `https://wiki.guildwars2.com/` sources. Pair every reward component with a supported `account-daily` or `character-daily` limit; reject limits without components, nonpositive item/coin amounts, empty reward entries, unsupported schemas, and materially future verification dates. Let schedule functionality continue without rewards if the catalog fails safely.
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
- Remove stale `Events Module.bhm` and `Events.Module.bhm` files from the selected build output before packaging, then reject nested BHM entries so repeated builds cannot recursively grow the artifact.
- Keep the source manifest at the upstream base `X.Y.Z`; inject `X.Y.Z-fork.N` only into the packaged manifest. Do not create a tag or Release merely to test this gate.
- Keep the site as static `docs/index.html`, `docs/styles.css`, `docs/script.js`, and local `docs/assets/`; do not make core content or downloads depend on JavaScript or another jakeuj site being available.
- Use only original site artwork, project-owned screenshots, and module-owned icons. Borrow GW2 visual language without copying ArenaNet logos, characters, promotional art, or implying official endorsement.
- Keep the site's GitHub Release request at a 3-second timeout. On API, schema, tag, asset, trusted-URL, or digest failure, retain the HTML fallback version and working unversioned download link.
- Disable continuous motion for `prefers-reduced-motion`, limit pointer effects to precise pointers, lazy-load noncritical images, and provide AVIF/WebP sources with a conventional fallback where practical.
- Keep technical counts and claims traceable to coverage, live audit, workflow, source, or Release metadata. Do not invent performance, download, or official-certification claims.

## Detailed reference

Use [references/events-module.md](references/events-module.md) for file paths, official MediaWiki requests, reward data and bilingual-name rules, chat-copy settings and format fields, runtime localization, source fallback, self-update implementation and failure rules, testing, build and local-install verification, troubleshooting, release tags/digests, and the GitHub Pages content, performance, accessibility, validation, and deployment contracts.
