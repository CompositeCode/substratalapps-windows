#!/usr/bin/env bash
# Regenerates the C# API client (src/Substratal.Hub.Core/Api/Generated) from api/openapi.yaml with Kiota.
#
#   scripts/gen-api.sh            # from the committed snapshot
#   scripts/gen-api.sh --fetch    # first refresh api/openapi.yaml from the published API docs
#
# The client targets the host root (https://api.substratalapps.com, no /v1). The Substratal-Version header is added
# once, by ApiVersionHandler, not per call.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "${1:-}" == "--fetch" ]]; then
  curl -fsS https://compositecode.github.io/substratalapps.com/openapi.yaml -o api/openapi.yaml
fi

dotnet tool run kiota generate \
  --language CSharp \
  --openapi api/openapi.yaml \
  --class-name SubstratalApiClient \
  --namespace-name Substratal.Hub.Core.Api.Generated \
  --output src/Substratal.Hub.Core/Api/Generated \
  --backing-store \
  --exclude-backward-compatible \
  --clean-output \
  --log-level Error

version=$(grep -m1 -E '^\s+version:' api/openapi.yaml | awk '{print $2}')
echo "Generated the client from openapi.yaml $version"
