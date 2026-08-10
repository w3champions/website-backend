#!/usr/bin/env bash
# Runs the .NET 8 SDK in a container against this worktree.
#
# There is no dotnet on this host, so `dotnet build` / `dotnet test` are run
# inside mcr.microsoft.com/dotnet/sdk:8.0 (the same image the Dockerfile uses).
# NuGet packages and build output are cached on the host so repeat runs are fast.
#
# Usage:
#   ./dotnet-docker.sh build
#   ./dotnet-docker.sh test
#   ./dotnet-docker.sh test --filter "FullyQualifiedName~GameModes"
#   ./dotnet-docker.sh format --verify-no-changes
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NUGET_CACHE="${REPO_DIR}/.nuget-docker"
mkdir -p "${NUGET_CACHE}"

exec docker run --rm \
  -v "${REPO_DIR}":/src \
  -v "${NUGET_CACHE}":/nuget \
  -w /src \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -e DOTNET_NOLOGO=1 \
  -e NUGET_PACKAGES=/nuget \
  -e HOME=/tmp \
  -e DOTNET_CLI_HOME=/tmp \
  -u "$(id -u):$(id -g)" \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet "$@"
