#!/usr/bin/env bash
# Confirm the QEMU system binaries NexusVM launches are available.
# This script does not download images or change device permissions.
set -euo pipefail

missing=0

check() {
  if command -v "$1" >/dev/null 2>&1; then
    echo "found $1: $(command -v "$1")"
  else
    echo "missing $1"
    missing=1
  fi
}

check qemu-system-x86_64
check qemu-system-aarch64

if [[ "$(uname -s)" == "Linux" ]]; then
  if [[ -e /dev/kvm ]]; then
    if [[ -r /dev/kvm && -w /dev/kvm ]]; then
      echo "/dev/kvm is readable and writable"
    else
      echo "/dev/kvm exists, but this user cannot open it. Add the user to the kvm group and start a new login:"
      echo "  sudo usermod -aG kvm \$USER"
    fi
  else
    echo "/dev/kvm is missing. Load kvm_intel or kvm_amd and confirm firmware virtualization is enabled."
  fi
fi

if [[ "$missing" -ne 0 ]]; then
  echo "Install QEMU so the binary for this host is on PATH."
  echo "  Windows and Linux: qemu-system-x86_64"
  echo "  Apple Silicon: qemu-system-aarch64"
  exit 1
fi
