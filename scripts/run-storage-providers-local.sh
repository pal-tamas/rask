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

# Per run, names and ports both (#1098). Fixed ones meant a second worktree running this gate `docker rm -f`'d the
# first one's MinIO mid-test and bound its port. The suffix is this shell's pid; the ports are whatever the kernel
# hands out free right now, which also keeps clear of a MinIO or Azurite a developer already runs on the defaults.
run_id="$$"
MINIO="rask-storage-minio-$run_id"
AZURITE="rask-storage-azurite-$run_id"
free_port() {
  python3 -c 'import socket; s = socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()'
}
S3_PORT="$(free_port)"
AZURE_PORT="$(free_port)"
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

# wait_for <name> <url> <container> [curl flag]. With no flag any HTTP answer counts, which is right for Azurite: its
# root answers an error status to an unsigned request once it is up. MinIO passes -f, because its readiness probe
# answers 503 until it is ready and plain `curl -s` exits 0 on a 503.
wait_for() {
  local name=$1 url=$2 strict=${4:-}
  for _ in $(seq 1 90); do
    if curl -s $strict -o /dev/null "$url"; then
      return 0
    fi
    sleep 1
  done
  echo "error: $name did not start at $url" >&2
  docker logs "$3" >&2 || true
  exit 1
}

# /ready, not /live: live answers as soon as the process is up, before IAM accepts the root credentials, and the
# first signed request then fails 403. ProviderSmokeTests still retries that first request briefly on 403.
wait_for MinIO "http://127.0.0.1:${S3_PORT}/minio/health/ready" "$MINIO" -f
wait_for Azurite "http://127.0.0.1:${AZURE_PORT}/" "$AZURITE"

RASK_STORAGE_PROVIDERS=1 \
RASK_STORAGE_AZURE="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=${AZURITE_KEY};BlobEndpoint=http://127.0.0.1:${AZURE_PORT}/devstoreaccount1" \
RASK_STORAGE_S3_URL="http://127.0.0.1:${S3_PORT}" \
RASK_STORAGE_S3_KEY=raskminio \
RASK_STORAGE_S3_SECRET=raskminiosecret \
  dotnet test tests/Rask.Storage.Tests/Rask.Storage.Tests.csproj --nologo \
    --filter "FullyQualifiedName~ProviderSmokeTests" \
    --logger "console;verbosity=normal"
