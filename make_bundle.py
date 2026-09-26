"""
make_bundle.py

Bundles the GameWatch source files into a single zip, ready to send to
an AI or share with someone else. Excludes build output (bin/, obj/),
.git, and IDE config.

Usage:
    python make_bundle.py

Output:
    E:\\Programming\\GameWatchCS\\GameWatch_bundle.zip
"""

import os
import shutil
import zipfile
from pathlib import Path
from datetime import datetime

REPO_ROOT = Path(__file__).resolve().parent
STAGING_DIR = REPO_ROOT / "_bundle_staging"
ZIP_PATH = REPO_ROOT / "GameWatch_bundle.zip"

INCLUDE = [
    "GameWatch/App.xaml",
    "GameWatch/App.xaml.cs",
    "GameWatch/MainWindow.xaml",
    "GameWatch/MainWindow.xaml.cs",
    "GameWatch/GameWatch.csproj",
    "GameWatch/app.manifest",
    "GameWatch/Services/ConnectionsService.cs",
    "GameWatch/Services/EtwNetworkMonitor.cs",
    "GameWatch/Services/GameModeController.cs",
    "GameWatch/Services/NetstatParser.cs",
    "GameWatch/Services/NetworkStatsService.cs",
    "GameWatch/Services/RateFormatting.cs",
    ".gitignore",
]

AUTO_GLOB_FOLDERS = [
    "GameWatch.Tests",
]

EXCLUDE_DIRS = {"bin", "obj", ".git", ".vs", ".vscode", "node_modules"}


README_CONTENT = """\
# GameWatch

A Windows desktop app (WPF, .NET 8) that shows live network activity,
breaks it down by process, and offers a "Game Mode" that suspends
bandwidth-hogging processes and services.

## What it does

- Overall up/down rates, summed across all active network adapters,
  refreshed every 2 seconds.
- Per-process breakdown via ETW (Event Tracing for Windows) using the
  `Microsoft.Diagnostics.Tracing.TraceEvent` library. Same low-level
  mechanism Task Manager uses for its per-process network columns.
- Connection list showing process, PID, TCP connection count, per-process
  rates (if ETW is running), and the full path to the .exe.
- Game Mode: check a process row, click "Enable Game Mode" to suspend
  those processes (via NtSuspendProcess) and stop a configurable list of
  Windows services (BITS, DoSvc, wuauserv by default). Reversible.

## Requirements

- Windows 10/11
- .NET 8 SDK to build (or .NET 8 Desktop Runtime to run a pre-built binary)
- Administrator privileges for ETW per-process tracking and service control.
  The app runs without admin, but per-app rates are unavailable.

## Project layout

    GameWatch/
    |-- App.xaml, App.xaml.cs               WPF application entry point
    |-- MainWindow.xaml, MainWindow.xaml.cs UI + 2-second update loop
    |-- GameWatch.csproj                    Project file
    |-- app.manifest                        Requests admin elevation
    +-- Services/
        |-- ConnectionsService.cs           Runs netstat, maps PIDs to processes
        |-- EtwNetworkMonitor.cs            ETW kernel session, per-PID byte counters
        |-- GameModeController.cs           Suspend/resume processes, stop/start services
        |-- NetstatParser.cs                Pure parsing of netstat output (testable)
        |-- NetworkStatsService.cs          Total adapter byte counts
        +-- RateFormatting.cs               Byte/s to human-readable, level thresholds

    GameWatch.Tests/
    +-- Unit tests for NetstatParser and RateFormatting

## Build and run

From the GameWatch/ folder:

    dotnet build
    bin/Debug/net8.0-windows/GameWatch.exe

Or in one step:

    dotnet run

Run as Administrator for the ETW per-process feature to work.

## Run tests

From the repo root:

    dotnet test

## Dependencies

- Microsoft.Diagnostics.Tracing.TraceEvent - ETW kernel session + event decoding
- System.ServiceProcess.ServiceController - Start/stop Windows services for Game Mode

Both are official Microsoft packages, restored automatically by NuGet
on first build.

## Known limitations

- IPv6 traffic is not counted in the per-process breakdown (IPv4 only).
- No live sort by bandwidth; the table is ordered by connection count.
- Elevation required for full functionality (see Requirements).

## Bundle info

- Contents: source files only (no bin/, obj/, .git/, or build output).
- Everything here is sufficient to rebuild the project from scratch
  given the .NET 8 SDK.
"""


def is_excluded(path: Path) -> bool:
    return any(part in EXCLUDE_DIRS for part in path.parts)


def gather_files():
    files = []
    for rel in INCLUDE:
        src = REPO_ROOT / rel
        if src.exists():
            files.append((src, Path(rel)))
        else:
            print(f"  ! MISSING: {rel}")

    for folder in AUTO_GLOB_FOLDERS:
        folder_path = REPO_ROOT / folder
        if not folder_path.exists():
            print(f"  ! SKIPPED (not found): {folder}")
            continue
        for pattern in ("*.cs", "*.csproj"):
            for src in folder_path.rglob(pattern):
                rel = src.relative_to(REPO_ROOT)
                if is_excluded(rel):
                    continue
                if not any(r == rel for _, r in files):
                    files.append((src, rel))
    return files


def main():
    if STAGING_DIR.exists():
        shutil.rmtree(STAGING_DIR)
    if ZIP_PATH.exists():
        ZIP_PATH.unlink()

    STAGING_DIR.mkdir()

    files = gather_files()

    print("Bundling:")
    for src, rel in files:
        dst = STAGING_DIR / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
        print(f"  + {rel}")

    readme = README_CONTENT + f"\n- Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n"
    (STAGING_DIR / "README.md").write_text(readme, encoding="utf-8")
    print("  + README.md (generated)")

    with zipfile.ZipFile(ZIP_PATH, "w", zipfile.ZIP_DEFLATED) as zf:
        for root, _, filenames in os.walk(STAGING_DIR):
            for fname in filenames:
                full = Path(root) / fname
                rel = full.relative_to(STAGING_DIR)
                zf.write(full, rel)

    shutil.rmtree(STAGING_DIR)

    size_kb = ZIP_PATH.stat().st_size / 1024
    print()
    print(f"Done. {len(files) + 1} files bundled (including README).")
    print(f"Output: {ZIP_PATH}")
    print(f"Size:   {size_kb:.1f} KB")


if __name__ == "__main__":
    main()