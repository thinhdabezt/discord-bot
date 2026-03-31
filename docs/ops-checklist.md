# Ops Checklist

## Security
- Rotate Discord token on leak suspicion
- Rotate database credentials and update env vars
- Confirm no secrets are hardcoded in source files
- Keep runtime secrets only in environment or secret manager, not in appsettings tracked by git

## Database
- Verify latest EF migration has been applied
- Confirm dedupe table growth is monitored
- Set retention strategy for old `processed_tweets` rows

## Runtime
- Confirm polling interval and retry values are tuned
- Check worker logs for publish failures and retry spikes
- Validate Discord rate-limit incidents are below threshold
- Run preflight script before each release
- Run smoke test script after each release

## Deployment Artifacts
- Confirm [docs/ENV-MATRIX.md](docs/ENV-MATRIX.md) is up to date with current config shape
- Confirm [.env.prod.example](.env.prod.example) and [.env.supabase.example](.env.supabase.example) reflect current required variables
- Confirm [scripts/preflight.ps1](scripts/preflight.ps1) and [scripts/smoke-test.ps1](scripts/smoke-test.ps1) execute successfully

## Monitoring
- Track publish success/failure ratio daily
- Alert on sustained fetch failures per username
- Alert when bot disconnects from gateway repeatedly

## Recovery
- Document how to redeploy from known-good commit
- Keep a tested backup/restore procedure for Postgres
- Keep a manual command playbook for emergency disable of noisy feeds
