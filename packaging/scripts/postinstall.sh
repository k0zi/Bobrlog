#!/bin/sh
# Runs after install/upgrade of the bobrlog package (deb: "configure", rpm: 1 or 2).
# The optional background service is a copy in /opt/bobrlog/service; refresh it so it matches
# the upgraded application, keeping the allowed UIDs.
set -e

if [ -f /etc/systemd/system/bobrlog.service ] && [ -x /usr/lib/bobrlog/bobrlog ]; then
    /usr/lib/bobrlog/bobrlog --install-service --uid 0 --source /usr/lib/bobrlog/service \
        || echo "bobrlog: could not update the background service" >&2
fi

exit 0
