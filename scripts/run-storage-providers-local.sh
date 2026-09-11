#!/usr/bin/env bash
# By hand, never in a hook: proves Rask.Storage's S3 and Azure signing against real implementations.
#
# The unit tests pin the exact strings SigV4 and Shared Key sign, and AWS's published vectors; they cannot prove a
# service accepts them. This starts MinIO (S3) and Azurite (Azure Blob) in containers, runs ProviderSmokeTests —
# save, read, ranged read, a provider-signed URL fetched by a plain client, list, delete — and removes both.
#
#   scripts/run-storage-providers-local.sh
set -euo pipefail

cd "$(dirname "$0")/.."

MINIO=rask-storage-minio
AZURITE=rask-storage-azurite
# Off the defaults (9000, 10000) on purpose: a developer machine often already runs MinIO or Azurite for
# something else, and this must neither collide with it nor silently test against it.
S3_PORT=19000
AZURE_PORT=20000
AZURITE_KEY="Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=="

cleanup() {
  docker rm -f "$MINIO" "$AZURITE" >/dev/null 2>&1 || true
}
trap cleanup EXIT
cleanup

docker run -d --name "$MINIO" -p "127.0.0.1:${S3_PORT}:9000" \
  -e MINIO_ROOT_USER=raskminio -e MINIO_ROOT_PASSWORD=raskminiosecret \
  docker.io/minio/minio:latest server /data >/dev/null

# --skipApiVersionCheck: an Azurite image older than the pinned x-ms-version would refuse it outright, which
# tests the image, not the signature.
docker run -d --name "$AZURITE" -p "127.0.0.1:${AZURE_PORT}:10000" \
  mcr.microsoft.com/azure-storage/azurite:latest \
  azurite-blob --blobHost 0.0.0.0 --skipApiVersionCheck >/dev/null

wait_for() {
  local name=$1 url=$2
  for _ in $(seq 1 90); do
    if curl -s -o /dev/null "$url"; then
      return 0
    fi
    sleep 1
  done
  echo "error: $name did not start at $url" >&2
  docker logs "$3" >&2 || true
  exit 1
}

wait_for MinIO "http://127.0.0.1:${S3_PORT}/minio/health/live" "$MINIO"
wait_for Azurite "http://127.0.0.1:${AZURE_PORT}/" "$AZURITE"

RASK_STORAGE_PROVIDERS=1 \
RASK_STORAGE_AZURE="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=${AZURITE_KEY};BlobEndpoint=http://127.0.0.1:${AZURE_PORT}/devstoreaccount1" \
RASK_STORAGE_S3_URL="http://127.0.0.1:${S3_PORT}" \
RASK_STORAGE_S3_KEY=raskminio \
RASK_STORAGE_S3_SECRET=raskminiosecret \
  dotnet test tests/Rask.Storage.Tests/Rask.Storage.Tests.csproj --nologo \
    --filter "FullyQualifiedName~ProviderSmokeTests" \
    --logger "console;verbosity=normal"
