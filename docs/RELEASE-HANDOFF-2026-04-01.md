# Release Handoff - 2026-04-01

## Scope
This release finalizes Facebook fanpage rollout, direct RSS integration, runtime migration safety, and command smoke/integration evidence for local docker-prod.

## Phase Summary

| Phase | Status | Commit | Notes |
|---|---|---|---|
| Phase 1 - Runtime migration workflow | Done | `49b20a8` | Added migration apply script and runbook updates. |
| Phase 2 - Command smoke hardening | Done | `45fa99b` | Smoke checks include slash-command registration and DB schema summary. |
| Phase 3 - Integration evidence harness | Done | `8b39ac4` | Added integration evidence script and docs/checklist updates. |
| Phase 4 - Facebook parser improvements | Done | `3593e18` | Platform-aware parser, caption cleanup, album dedupe tests. |
| Phase 5 - Fanpage-first refactor | Done | `2c7abd6` | Kept add-fb contract, updated semantics and wording. |
| Stabilization fixes | Done | `3d2ca35`, `44648a5`, `54448de`, `201d001`, `aa01860` | RSS-Bridge fallback, add-fb validation hardening, clearer UX, non-X media policy for publish. |

## Verification Evidence

### Fixed test inputs
- Fanpage source: `10150123547145211`
- Direct RSS URL: `https://www.nasa.gov/rss/dyn/breaking_news.rss`

### Verified outcomes
- `tracked_feeds` contains fanpage mapping (`Platform=Facebook`, `Provider=RssBridge`).
- `tracked_feeds` contains direct RSS mapping (`Provider=DirectRss`).
- Fresh polling cycle processed both feeds.
- `processed_tweets` has publish evidence for the target inputs (count > 0).
- Integration evidence script passed end-to-end.

## Production Checklist

### 1. Pre-release
1. Run preflight:
   - `./scripts/preflight.ps1 -EnvFile .env`
2. Apply migrations:
   - `./scripts/apply-migrations.ps1 -Mode docker`
3. Confirm provider toggles:
   - `FEEDPROVIDERS__ENABLEDIRECTRSS=true`
   - `FEEDPROVIDERS__DEFAULTFACEBOOKPROVIDER` set to your target provider (`RssBridge` recommended fallback, `RssHub` if route is confirmed).

### 2. Deploy
1. Deploy containers:
   - `docker compose --profile prod up -d --build`
2. Confirm healthy services:
   - `docker compose --profile prod ps`

### 3. Post-deploy checks
1. Run smoke checks:
   - `./scripts/smoke-test.ps1 -ComposeMode prod -BotLogSinceMinutes 240`
2. Run integration evidence for real feeds:
   - `./scripts/integration-evidence.ps1 -ComposeMode prod -FanpageSource 10150123547145211 -DirectRssUrl "https://www.nasa.gov/rss/dyn/breaking_news.rss" -LookbackMinutes 360`
3. Confirm bot log signals:
   - command registration summary appears.
   - publish events appear for expected feeds.

### 4. Rollback
1. Roll back app image/commit:
   - `docker compose --profile prod down`
   - `git checkout <last_known_good_commit_or_tag>`
   - `docker compose --profile prod up -d --build`
2. If schema rollback is needed:
   - `dotnet ef database update InitialCreate --project src/DiscordXBot/DiscordXBot.csproj --startup-project src/DiscordXBot/DiscordXBot.csproj`

## Residual Risks
- RSSHub Facebook route availability may vary by upstream image/version. Keep RSS-Bridge fallback available for add-fb fanpage path.
- Non-X feeds now allow caption/mixed media by policy; monitor noise/format quality for high-volume RSS sources.
- Existing X path remains strict image-only and unchanged by policy for stability.

## Next Iteration
1. Add explicit platform/provider controls in command options to reduce operator ambiguity.
2. Add source-type column for Facebook (`Fanpage` default, `Profile` reserved) when schema evolution is approved.
3. Add targeted integration tests for command-to-DB persistence where feasible.
