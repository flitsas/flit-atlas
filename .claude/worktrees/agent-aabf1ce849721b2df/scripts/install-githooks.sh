#!/usr/bin/env bash
# Activa los hooks versionados en .githooks/ para este clon.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
git config core.hooksPath .githooks
chmod +x .githooks/* 2>/dev/null || true
echo "Hooks activados: core.hooksPath=.githooks"
