---
name: localize-build-events-module
description: Localize and compile the Community-Module-Pack Events and Metas Observer for Taiwan Traditional Chinese. Use when working on `Events Module/Properties/Resources.zh.resx`, translating event names or categories from `Events Module/ref/events.json`, diagnosing English fallback text, producing a standalone Chinese `Events Module.dll`, building or validating `Events Module.bhm`, or maintaining the `events-module-zh-tw` GitHub Actions release workflow.
---

# Localize and build Events Module

Use the repository scripts as the source of truth for coverage and build verification.

## Workflow

1. Inspect `git status` and preserve unrelated changes.
2. Run the coverage check before editing:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\validate-events-localization.ps1
   ```

3. Establish terminology before translating:
   - Use `https://gw2.wishingstarmoye.com/gw2timer` as the primary source for current event, phase, map, boss, and player shorthand names. Inspect the event-data script loaded by the page because the visible table is generated dynamically.
   - Use `https://gw2.wishingstarmoye.com/gw2timerbox` to cross-check core world-boss names.
   - For proper nouns not covered by either timer, query the Guild Wars 2 API with `lang=zh`, then check the Wishing Star database or guide pages for established player terminology.
   - Convert confirmed Simplified Chinese terminology to Taiwan Traditional Chinese manually. Do not replace an established in-game/player label with a literal translation of the English key.
4. Add or revise translations only in `Events Module/Properties/Resources.zh.resx` unless the user explicitly requests event schedule changes.
5. Keep each `<data name="...">` key byte-for-byte aligned with the English resource key, event `name`, or event `category`. Translate only `<value>`.
6. Run the coverage check again. Resolve missing keys, duplicate keys, placeholder mismatches, and English event values.
7. Build and verify the standalone Chinese DLL and complete BHM:

   ```powershell
   & .agents\skills\localize-build-events-module\scripts\build-events-zh-tw.ps1
   ```

8. Report the DLL/BHM paths, embedded resource count, and SHA-256. Publish or tag only when the user explicitly asks.

## Guardrails

- Use Taiwan Traditional Chinese. Treat machine translation only as a draft; game terminology must be checked against the sources above.
- Prefer the term players identify in a timer (for example a boss or phase name) when a literal meta-event title would be unfamiliar, while keeping enough map context to avoid ambiguity.
- Preserve placeholders such as `{0}`, filenames, URLs, and waypoint tokens.
- Do not edit `Resources.Designer.cs` for translation-only changes.
- Expect English fallback whenever `ResourceManager.GetString(meta.Name)` or `meta.Category` cannot find an exact key.
- Build with `/p:ChineseBuild=true` when the requested DLL must contain Chinese as its neutral resource and work without a `zh/` satellite assembly.
- Treat `Events Module.bhm` as the installable deliverable; a DLL alone does not include `events.json` and textures.
- Fully close Blish HUD before testing a replacement BHM or DLL.

## Detailed reference

Read [references/events-module.md](references/events-module.md) when changing project resource wiring, diagnosing MSBuild/.NET Framework failures, updating CI/CD, or preparing a release tag.
