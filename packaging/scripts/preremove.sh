#!/bin/sh
# Runs before removal (deb: "remove", rpm: 0) and before upgrades (deb: "upgrade", rpm: 1).
# On real removal, also remove the optional background service installed from the app.
set -e

case "$1" in
    remove|0)
        if [ -f /etc/systemd/system/bobrlog.service ] && [ -x /usr/lib/bobrlog/bobrlog ]; then
            /usr/lib/bobrlog/bobrlog --uninstall-service \
                || echo "bobrlog: could not remove the background service" >&2
        fi
        ;;
esac

exit 0
