# Ops Checklist

## Security
- Rotate Discord token on leak suspicion
- Rotate database credentials and update env vars
- Confirm no secrets are hardcoded in source files
- Keep runtime secrets only in environment or secret manager, not in appsettings tracked by git

## Database
- Verify latest EF migration has been applied
- Create backup before applying new migration (`scripts/apply-migrations.ps1` does this by default)
- Verify `tracked_feeds` schema contains `Platform`, `SourceType`, `Provider`, and `SourceKey`
- Confirm dedupe table growth is monitored
- Set retention strategy for old `processed_tweets` rows

## Runtime
- Confirm polling interval and retry values are tuned
- Check worker logs for publish failures and retry spikes
- Validate Discord rate-limit incidents are below threshold
- Run preflight script before each release
- Run smoke test script after each release
- Confirm smoke test output contains slash command registration summary for `add-x`, `add-fb`, and `add-link` command families
- Run `scripts/precheck-fanpages.ps1` for batch Facebook onboarding, and only use `/add-fb` for sources marked `use-add-fb`
- Run `scripts/integration-evidence.ps1` after real `/add-fb` and `/add-link` setup to verify DB + publish evidence
- For profile sources, verify `/add-fb` uses numeric ID with `sourceType=profile` and evidence script uses `-FacebookSourceType profile`
- If profile alerts are enabled, verify `FEEDPROVIDERS__FACEBOOKPROFILEALERTCHANNELID` points to an admin-only channel
- If RSS-Bridge priority fallback is enabled, verify `RSSBRIDGEFALLBACK__ENABLEFORPROFILE=false` unless profile support has been explicitly implemented for RSS-Bridge routes
- If Apify fallback is enabled, verify `APIFYFALLBACK__APITOKEN` is set and monitor fallback call frequency against budget

## Deployment Artifacts
- Confirm [docs/ENV-MATRIX.md](docs/ENV-MATRIX.md) is up to date with current config shape
- Confirm [.env.prod.example](.env.prod.example) and [.env.supabase.example](.env.supabase.example) reflect current required variables
- Confirm [scripts/preflight.ps1](scripts/preflight.ps1) and [scripts/smoke-test.ps1](scripts/smoke-test.ps1) execute successfully

## Monitoring
- Track publish success/failure ratio daily
- Alert on sustained fetch failures per username
- Alert on repeated Facebook profile fetch issues (403/error-only/empty after prior success) and rotate FB cookie when triggered
- Alert on unusual spikes of Apify fallback attempts per source (may indicate upstream RSS breakage)
- Alert when bot disconnects from gateway repeatedly

## Recovery
- Document how to redeploy from known-good commit
- Keep a tested backup/restore procedure for Postgres
- Keep a manual command playbook for emergency disable of noisy feeds
