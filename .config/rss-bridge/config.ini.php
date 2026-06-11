; RSS-Bridge custom runtime configuration.
; Mounted into rssbridge/rss-bridge at /config/config.ini.php.

[system]
enabled_bridges[] = *
timezone = "UTC"

[http]
timeout = 30
retries = 2

[cache]
type = "file"
