# NexusVM: Cross-Platform Android Emulator Orchestrator

NexusVM is a lightweight, open-source alternative to traditional heavy Android emulators like BlueStacks. Instead of bundled background VirtualBox bloat, NexusVM acts as a high-performance desktop shell wrapper that orchestrates micro-virtualized instances of Android using **native operating system hypervisors** and **QEMU**.

## 🚀 Multi-Architecture Support Matrix

NexusVM dynamically senses your host hardware profile at runtime to spin up the optimized, near-native execution path:

| Host Operating System | Host CPU | Guest Android Image | Native Hypervisor Layer | QEMU Architecture Binary |
| :--- | :--- | :--- | :--- | :--- |
| **Windows 11 / 10** | `x86_64` | `Android-x86_64.iso` | Windows Hypervisor Platform (WHPX) | `qemu-system-x86_64` |
| **Linux (Ubuntu/Arch)** | `x86_64` | `Android-x86_64.iso` | Kernel-based Virtual Machine (KVM) | `qemu-system-x86_64` |
| **macOS (M1/M2/M3/M4)** | `ARM64` | `Android-arm64-v8a.img` | Apple Hypervisor.framework (HVF) | `qemu-system-aarch64` |

> ⚠️ **Critical Note for Apple Silicon Users:** To maintain maximum speed and avoid slow CPU instruction translation, NexusVM forces the use of a native ARM64 Android GSI system image instead of an x86 image on Apple Silicon devices.

---

## 🛠️ System Prerequisites

Before running the application, your host computer must have virtualization layers activated:

### Windows
*   Enable **Windows Hypervisor Platform** and **Virtual Machine Platform** via *Turn Windows Features on or off*.
*   Run `bcdedit /set hypervisorlaunchtype auto` in an Administrator terminal and restart your PC.

### Linux
*   Ensure your user profile is added to the KVM hardware access group:
    ```bash
    sudo usermod -aG kvm $USER
    sudo chmod 666 /dev/kvm
    ```

### macOS
*   No configurations needed; Apple's native `Hypervisor.framework` is active by default on all Apple Silicon devices.

---

## 📂 Project Architecture Layout

```text
nexus-vm/
├── src/
│   ├── NexusVM.Core/               # Shared cross-platform orchestration layer
│   │   ├── Detectors/              # OS, architecture, and accelerator probes
│   │   ├── Engine/                 # QEMU command builder and process host
│   │   ├── Input/                  # Keymap profile and loopback adb input
│   │   ├── Protocol/               # Loopback QMP client and framebuffer capture
│   │   └── Models/
│   └── NexusVM.UI/                 # Avalonia desktop shell
│       ├── Views/                  # Main window, diagnostic window, guest surface
│       ├── ViewModels/
│       ├── Embedding/              # Win32 HWND and X11 reparent, macOS framebuffer
│       └── Session/
├── assets/
│   └── keymaps/
│       └── default.json
├── build/
│   ├── publish-hosts.sh
│   └── publish-hosts.ps1
└── scripts/
    └── setup-dependencies.sh       # Checks that QEMU is on PATH
```

---

## 🏗️ Building and Compiling

### Core Dependency Management
NexusVM assumes standard QEMU system binaries are either accessible in your local system path (`PATH`) or placed systematically within the executing application target framework root under an `assets/bin/` subfolder.

### Compilation
Build with the .NET 10 SDK. The projects target `net8.0`.

```bash
dotnet test NexusVM.sln
./build/publish-hosts.sh
```

`./build/publish-hosts.sh` publishes self-contained `win-x64`, `linux-x64`, and `osx-arm64` binaries. On Windows, use `build/publish-hosts.ps1`.

---

## 🎹 Key-Mapping Schema (`assets/keymaps/default.json`)

When a guest starts, NexusVM runs `adb connect 127.0.0.1:<port>` and then `adb devices` until that serial is `device`, `offline`, or `unauthorized`. QEMU’s log is shown under the guest surface. Matching keys, and a left click on the surface, then become `adb shell input tap` or `swipe`. Edit writes the profile back to `assets/keymaps/default.json`:

```json
{
  "profileName": "Standard FPS Profile",
  "mappings": [
    { "key": "W", "action": "swipe", "startX": 200, "startY": 800, "endX": 200, "endY": 700, "label": "Move Forward" },
    { "key": "LeftClick", "action": "tap", "targetX": 960, "targetY": 540, "label": "Fire weapon" }
  ]
}
```

---

## 📄 License
This project is licensed under the MIT License - see the `LICENSE` file for full structural details.
