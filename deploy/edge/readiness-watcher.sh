#!/bin/sh
# OmniAI edge readiness watcher: readiness-aware routing for open-source nginx.
#
# Open-source nginx has no active health checks; without this, an instance that
# still accepts connections but has lost its database or Redis keeps receiving
# new requests until they fail (Phase 35: 56 visible 503s in 45 s). This loop
# probes each API's /health/ready on the private network and renders the
# upstream file nginx includes:
#
#   FALL consecutive failed probes  -> the instance is marked `down`
#                                      (no NEW requests; in-flight ones finish)
#   RISE consecutive good probes    -> it is routed to again
#
# An operator (or deploy/scripts/rolling-deploy.sh) can also drain an instance
# on purpose before stopping it: `touch /tmp/omniai/drain/<address>` removes it
# from routing at the next pass regardless of its readiness; deleting the file
# returns it to readiness-based routing.
#
# Each change is validated (`nginx -t`) and applied with a graceful reload.
# If EVERY instance is unready the file keeps them all: that is a dependency
# outage, and the APIs' own truthful 503s are better than a blanket 502.
# /health/live is never used here -- liveness is not readiness.
set -u

UPSTREAMS=${OMNIAI_API_UPSTREAMS:-"172.30.35.21:8080 172.30.35.22:8080"}
INTERVAL=${OMNIAI_READINESS_INTERVAL_SECONDS:-1}
TIMEOUT=${OMNIAI_READINESS_TIMEOUT_SECONDS:-4}
FALL=${OMNIAI_READINESS_FALL:-2}
RISE=${OMNIAI_READINESS_RISE:-2}
DIR=/tmp/omniai
OUT=$DIR/upstreams.conf
DRAIN=$DIR/drain

log() { echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) omniai-readiness: $*"; }

# $1 = space-separated list of instances to mark down
render() {
    down=" $1 "
    {
        echo "# Rendered by readiness-watcher.sh; do not edit."
        echo "upstream omniai_api {"
        for s in $UPSTREAMS; do
            case "$down" in *" $s "*) flag=" down" ;; *) flag="" ;; esac
            echo "    server $s max_fails=1 fail_timeout=10s$flag;"
        done
        echo "}"
        echo "upstream omniai_hubs {"
        echo "    hash \$remote_addr consistent;"
        for s in $UPSTREAMS; do
            case "$down" in *" $s "*) flag=" down" ;; *) flag="" ;; esac
            echo "    server $s max_fails=1 fail_timeout=10s$flag;"
        done
        echo "}"
    } > "$OUT.next"
}

mkdir -p "$DIR" "$DRAIN"

if [ "${1:-}" = "--init" ]; then
    # Before nginx starts: every instance routable.
    render ""
    mv "$OUT.next" "$OUT"
    exit 0
fi

applied=""
for s in $UPSTREAMS; do eval "ok_$(echo "$s" | tr '.:' '__')=0; bad_$(echo "$s" | tr '.:' '__')=0; state_$(echo "$s" | tr '.:' '__')=up"; done

while :; do
    for s in $UPSTREAMS; do
        k=$(echo "$s" | tr '.:' '__')
        if wget -q -T "$TIMEOUT" -O /dev/null "http://$s/health/ready" 2>/dev/null; then
            eval "ok_$k=\$((ok_$k + 1)); bad_$k=0"
        else
            eval "bad_$k=\$((bad_$k + 1)); ok_$k=0"
        fi
        eval "ok=\$ok_$k; bad=\$bad_$k; state=\$state_$k"
        if [ "$state" = up ] && [ "$bad" -ge "$FALL" ]; then
            eval "state_$k=down"; log "$s NOT READY ($bad consecutive failed probes)"
        elif [ "$state" = down ] && [ "$ok" -ge "$RISE" ]; then
            eval "state_$k=up"; log "$s READY again ($ok consecutive good probes)"
        fi
    done

    wanted=""; up_count=0
    for s in $UPSTREAMS; do
        k=$(echo "$s" | tr '.:' '__')
        eval "state=\$state_$k"
        if [ "$state" = down ] || [ -e "$DRAIN/$s" ]; then wanted="$wanted $s"; else up_count=$((up_count + 1)); fi
    done
    if [ "$up_count" -eq 0 ]; then
        wanted=""   # never take the whole site down for a dependency outage
    fi
    wanted=$(echo "$wanted" | xargs)

    if [ "$wanted" != "$applied" ]; then
        render "$wanted"
        cp "$OUT" "$OUT.last" 2>/dev/null
        mv "$OUT.next" "$OUT"
        if nginx -t -q 2>/dev/null && nginx -s reload 2>/dev/null; then
            log "routing to: $(for s in $UPSTREAMS; do case " $wanted " in *" $s "*) ;; *) printf '%s ' "$s" ;; esac; done)(drained: ${wanted:-none})"
            applied="$wanted"
        else
            mv "$OUT.last" "$OUT"; log "render rejected by nginx -t; kept the previous routing"
        fi
    fi
    sleep "$INTERVAL"
done
