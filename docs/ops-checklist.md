# Ops Checklist

## Security
- Rotate Discord token on leak suspicion
- Rotate database credentials and update env vars
- Rotate Apify token on leak suspicion
- Confirm no secrets are hardcoded in source files
- Keep runtime secrets only in environment or secret manager, not in appsettings tracked by git

## Database
- Verify latest EF migration has been applied
- Create backup before applying new migration (`scripts/apply-migrations.ps1` does this by default)
- Verify `tracked_feeds` schema contains `Platform`, `SourceType`, `Provider`, and `SourceKey`
- Confirm Facebook rows created by `/add-fb` use `Provider=Apify`; direct RSS Facebook rows use `Provider=DirectRss`
- Confirm Instagram rows created by `/add-ig` use `Provider=RssBridge`; direct RSS Instagram rows use `Provider=DirectRss`
- Confirm dedupe table growth is monitored
- Set retention strategy for old `processed_tweets` rows

## Runtime
- Confirm polling interval and retry values are tuned
- Check worker logs for publish failures and retry spikes
- Validate Discord rate-limit incidents are below threshold
- Run preflight script before each release
- Run smoke test script after each release
- Confirm smoke test output contains slash command registration summary for `add-x`, `add-fb`, `add-ig`, and `add-link` command families
- Run `scripts/precheck-fanpages.ps1` for batch Facebook onboarding
- Treat `use-add-fb` as the Apify-primary path
- Treat `use-add-link` as the direct RSS path and run the concrete `/add-link` command printed by the script
- Treat `configure-apify` as missing or disabled `APIFY__*` config
- Treat `fix-direct-rss` as a broken operator-provided direct RSS URL
- Run `scripts/integration-evidence.ps1` after real `/add-fb` and `/add-link` setup to verify DB + publish evidence
- For profile sources, verify `/add-fb` uses numeric ID with `sourceType=profile` and evidence script uses `-FacebookSourceType profile`
- For Instagram sources, verify `/add-ig` uses a username/profile URL, not a post/reel/story URL

## Deployment Artifacts
- Confirm [docs/ENV-MATRIX.md](ENV-MATRIX.md) is up to date with current config shape
- Confirm [.env.prod.example](../.env.prod.example) and [.env.supabase.example](../.env.supabase.example) reflect current required variables
- Confirm env examples do not contain Facebook cookie variables for RSS-Bridge
- Confirm [scripts/preflight.ps1](../scripts/preflight.ps1) and [scripts/smoke-test.ps1](../scripts/smoke-test.ps1) execute successfully

## Monitoring
- Track publish success/failure ratio daily
- Alert on sustained fetch failures per source
- Alert on unusual spikes of Apify runs per source
- Alert on repeated Instagram RSS-Bridge access errors or empty feeds; use `/add-link platform:IG` for tested direct RSS fallback
- Alert when bot disconnects from gateway repeatedly

## Recovery
- Document how to redeploy from known-good commit
- Keep a tested backup/restore procedure for Postgres
- Keep a manual command playbook for emergency disable of noisy feeds

