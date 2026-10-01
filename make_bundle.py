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

REPO_ROOT = Path(__file__).resolve().parent
STAGING_DIR = REPO_ROOT / "_bundle_staging"
ZIP_PATH = REPO_ROOT / "GameWatch_bundle.zip"

INCLUDE = [
    "GameWatch/App.xaml",
    "GameWatch/App.xaml.cs",
    "GameWatch/MainWindow.xaml",
    "GameWatch/MainWindow.xaml.cs",
    "GameWatch/ProcessRowViewModel.cs",
    "GameWatch/GameWatch.csproj",
    "GameWatch/app.manifest",
    "GameWatch/Services/EtwNetworkMonitor.cs",
    "GameWatch/Services/GameModeController.cs",
    "GameWatch/Services/GameModeStateStore.cs",
    "GameWatch/Services/NetworkStatsService.cs",
    "GameWatch/Services/RateFormatting.cs",
    "GameWatch/Services/AppSettings.cs",
    "GameWatch/Services/AdapterClassifier.cs",
    "GameWatch/Services/NativeConnectionReader.cs",
    "GameWatch/Services/ConnectionAggregator.cs",
    "GameWatch/Services/TrafficHistory.cs",
    "GameWatch/Services/NetworkMonitorEngine.cs",
    "GameWatch/Services/ProcessTrafficHistory.cs",
    "GameWatch/Services/ProcessIconProvider.cs",
    "GameWatch/Services/BitsTransferReader.cs",
    "GameWatch/Services/ServiceProcessReader.cs",
    "GameWatch/TransferDisplayRow.cs",
    "GameWatch/Services/TimeRangeMapper.cs",
    "GameWatch/Services/NativeOwnerModuleReader.cs",
    "GameWatch/Services/ServiceConnectionGrouper.cs",
    "README.md",
    "make_bundle.py",
    ".github/workflows/build.yml",
    ".gitignore",
]

AUTO_GLOB_FOLDERS = [
    "GameWatch",
    "GameWatch.Tests",
]

EXCLUDE_DIRS = {"bin", "obj", ".git", ".vs", ".vscode", "node_modules"}




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

    with zipfile.ZipFile(ZIP_PATH, "w", zipfile.ZIP_DEFLATED) as zf:
        for root, _, filenames in os.walk(STAGING_DIR):
            for fname in filenames:
                full = Path(root) / fname
                rel = full.relative_to(STAGING_DIR)
                zf.write(full, rel)

    shutil.rmtree(STAGING_DIR)

    size_kb = ZIP_PATH.stat().st_size / 1024
    print()
    print(f"Done. {len(files)} files bundled (including README).")
    print(f"Output: {ZIP_PATH}")
    print(f"Size:   {size_kb:.1f} KB")


if __name__ == "__main__":
    main()
