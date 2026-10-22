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
│   │   ├── Detectors/              # OS & System Architecture hardware probing
│   │   ├── Engine/                 # Dynamic QEMU CLI Command Builder
│   │   └── Network/                # Background ADB local socket listener
│   ├── NexusVM.UI/                 # Cross-Platform Desktop User Interface (Avalonia UI / Qt)
│   │   ├── Views/                  # Main Display, Sidebar Control Layout, Key-mapping Dialogs
│   │   └── ViewModels/             # Reactive UI Binding & Hypervisor Lifecycle Controllers
├── assets/
│   ├── keymaps/                    # Custom JSON input translation tables
│   └── skins/                      # UI Window Theme Definitions
└── scripts/
    └── setup-dependencies.sh       # Automation scripts to fetch headless QEMU binaries & ISOs
```

---

## 🏗️ Building and Compiling

### Core Dependency Management
NexusVM assumes standard QEMU system binaries are either accessible in your local system path (`PATH`) or placed systematically within the executing application target framework root under an `assets/bin/` subfolder.

### Compilation
Ensure you have the required desktop SDK modules installed, then trigger a native framework compilation matching your environment layout:

```bash
# If built using .NET Core / Avalonia C#
dotnet build --configuration Release

# If built using C++ / Qt-CMake
mkdir build && cd build
cmake ..
cmake --build . --config Release
```

---

## 🎹 Key-Mapping Schema (`assets/keymaps/default.json`)

Keyboard inputs and click coordinates are bound directly using high-performance JSON translation matrices mapped dynamically onto guest touch points over ADB channels:

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
