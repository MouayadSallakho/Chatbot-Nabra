#!/bin/sh
# Run by the nginx image entrypoint before nginx starts: render the initial
# upstreams (every instance routable) and start the readiness watcher.
set -eu
/usr/local/bin/omniai-readiness-watcher --init
nohup /usr/local/bin/omniai-readiness-watcher >/proc/1/fd/1 2>/proc/1/fd/2 &
