#!/bin/sh
set -eu
if command -v dotnet >/dev/null 2>&1; then
    echo 'This smoke image must not include a .NET runtime.' >&2
    exit 1
fi
app='/opt/Trag With Spaces/Trag'
"$app" --ui-smoke
"$app" --ui-auth-smoke
"$app" --auth-probe
"$app" --account-probe
"$app" --process-probe
# All keyring material is synthetic and confined to the disposable container. --unlock starts the daemon and unlocks
# the login keyring, as a login does; --start, as the session that follows, puts its secret service on the bus. Without
# it the bus starts a second daemon for Trag, whose keyring nothing unlocked.
eval "$(printf '%s\n' 'trag-disposable-keyring' | gnome-keyring-daemon --unlock --components=secrets)"
export GNOME_KEYRING_CONTROL
gnome-keyring-daemon --start --components=secrets >/dev/null
"$app" --vault-probe
echo 'Runtime-free Linux smoke passed'
