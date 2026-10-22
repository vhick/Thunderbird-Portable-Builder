"""Locate the app payload, not the installer/uninstaller helpers."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def find_app_dir(extracted):
    matches = []
    for candidate in extracted.rglob("thunderbird.exe"):
        if not candidate.is_file():
            continue
        folder = candidate.parent
        if (folder / "application.ini").is_file() and (folder / "omni.ja").is_file():
            matches.append(folder)
    if not matches:
        raise FileNotFoundError("Missing thunderbird.exe + application.ini + omni.ja in extracted installer")
    # core is Mozilla's normal install payload; prevent arbitrary helper selection.
    matches.sort(key=lambda p: (p.name.lower() != "core", len(p.parts)))
    return matches[0]


def main():
    folder = find_app_dir(ROOT / "work" / "extracted")
    (ROOT / "work" / "source_path.txt").write_text(str(folder / "thunderbird.exe"), encoding="utf-8")
    print("Official Thunderbird app directory:", folder)


if __name__ == "__main__":
    main()
