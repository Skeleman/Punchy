#!/bin/sh
# Installs the Punchy hooks for every worktree of this clone (J-041). Run once per machine from any tree:
#   sh hooks/install.sh        (from the coordination worktree)
# Copies the hooks to D:/Unity/PunchyHooks (or $1) and points core.hooksPath there, so every branch keeps
# Git LFS working and gets commit telemetry, whether or not the branch carries this folder.
set -e
src=$(cd "$(dirname "$0")" && pwd)
dst=${1:-/d/Unity/PunchyHooks}
mkdir -p "$dst"
cp "$src"/post-checkout "$src"/post-commit "$src"/post-merge "$src"/pre-push "$src"/telemetry.py "$dst"/
chmod +x "$dst"/post-* "$dst"/pre-push
git config core.hooksPath "$dst"
echo "hooks installed in $dst; telemetry goes to ~/.punchy-telemetry/commits.csv"
