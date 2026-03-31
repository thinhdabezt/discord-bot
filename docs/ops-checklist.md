# Ops Checklist

## Security
- Rotate Discord token on leak suspicion
- Rotate database credentials and update env vars
- Confirm no secrets are hardcoded in source files

## Database
- Verify latest EF migration has been applied
- Confirm dedupe table growth is monitored
- Set retention strategy for old `processed_tweets` rows

## Runtime
- Confirm polling interval and retry values are tuned
- Check worker logs for publish failures and retry spikes
- Validate Discord rate-limit incidents are below threshold

## Monitoring
- Track publish success/failure ratio daily
- Alert on sustained fetch failures per username
- Alert when bot disconnects from gateway repeatedly

## Recovery
- Document how to redeploy from known-good commit
- Keep a tested backup/restore procedure for Postgres
- Keep a manual command playbook for emergency disable of noisy feeds
