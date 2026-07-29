"""Vendor a releasable unit's assemblies into vendor/<unit>/<label>/ for game-free builds and tests.

NOT imported by MSBuild; a developer tool like compare-eval.py. Cross-platform (Windows and Linux installs).

    python build/tools/vendor.py --unit game --label V2.5-b8
    python build/tools/vendor.py --unit dedicated-server --label V2.5-b8

Copies every DLL in the unit's Managed directory plus Mods/0_TFP_Harmony/0Harmony.dll into a tree that mirrors
the source install exactly (the dedicated server keeps its 7DaysToDieServer_Data name; build/GamePaths.props
detects either layout), so the build consumes it with nothing more than -p:SdtdDir=vendor/<unit>/<label>. Always
redirect -p:ModsDir when building against a vendored tree, or Debug deploys into it.

The label is the human coordinate (the in-game "V 2.5 b8" as V2.5-b8); machine provenance — Steam buildid,
betakey (informational), source paths, per-file SHA-256 — lands in manifest.json beside the tree. A nuspec stub
is written for the future private-feed packaging step.

LEGAL: the output contains the game's licensed assemblies. It must never be committed (the repo is public) or
published anywhere public. vendor/ is gitignored; keep it that way. See .ai/f5b-game-assembly-packages.md.

Install discovery: --install-dir, else SDTD_HOME (game; "<SDTD_HOME> Dedicated Server" for the server, matching
build/GamePaths.props), else the default Steam locations for the current platform.
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path

UNITS = {
    "game": {
        "app_id": "251570",
        "install_names": ["7 Days To Die"],
        "data_dir": "7DaysToDie_Data",
        "package_id": "7DtD.Assemblies.Game",
    },
    "dedicated-server": {
        "app_id": "294420",
        "install_names": ["7 Days to Die Dedicated Server"],
        "data_dir": "7DaysToDieServer_Data",
        "package_id": "7DtD.Assemblies.DedicatedServer",
    },
}
LABEL_RE = re.compile(r"^V(\d+)\.(\d+)(?:\.(\d+))?-b(\d+)$")
HARMONY_REL = Path("Mods") / "0_TFP_Harmony" / "0Harmony.dll"
REPO_ROOT = Path(__file__).resolve().parents[2]

STEAM_COMMON = [
    Path(r"C:\Program Files (x86)\Steam\steamapps\common"),
    Path.home() / ".steam" / "steam" / "steamapps" / "common",
    Path.home() / ".local" / "share" / "Steam" / "steamapps" / "common",
]


def find_install(unit, explicit):
    if explicit:
        p = Path(explicit)
        if not p.is_dir():
            sys.exit(f"error: --install-dir does not exist: {p}")
        return p
    candidates = []
    env = os.environ.get("SDTD_HOME")
    if env:
        base = Path(env)
        candidates.append(base if unit == "game" else base.with_name(base.name + " Dedicated Server"))
    for common in STEAM_COMMON:
        for name in UNITS[unit]["install_names"]:
            candidates.append(common / name)
    for c in candidates:
        if c.is_dir():
            return c
    sys.exit(f"error: no {unit} install found. Tried:\n  " + "\n  ".join(str(c) for c in candidates)
             + "\nPass --install-dir or set SDTD_HOME.")


def find_managed(unit, install):
    """The data-dir name is how a unit is *verified*, not guessed: a game-layout install offered as the
    dedicated server (or vice versa) must fail here, not silently vendor a mislabeled tree."""
    managed = install / UNITS[unit]["data_dir"] / "Managed"
    if not managed.is_dir():
        sys.exit(f"error: {install} does not look like a {unit} install "
                 f"(expected {UNITS[unit]['data_dir']}/Managed)")
    return managed


def steam_provenance(unit, install):
    """buildid/betakey from the appmanifest, when the install sits in a Steam library. Absent -> nulls."""
    acf = install.parent.parent / f"appmanifest_{UNITS[unit]['app_id']}.acf"
    if not acf.is_file():
        return {"appmanifest": None, "buildid": None, "betakey": None}
    text = acf.read_text(encoding="utf-8", errors="replace")
    def field(name):
        m = re.search(r'"%s"\s+"([^"]*)"' % name, text)
        return m.group(1) if m else None
    return {"appmanifest": str(acf), "buildid": field("buildid"), "betakey": field("betakey")}


def four_part_version(label):
    m = LABEL_RE.match(label)
    major, minor, patch, build = m.group(1), m.group(2), m.group(3) or "0", m.group(4)
    return f"{major}.{minor}.{patch}.{build}"


def write_nuspec(dest, unit, label):
    package_id = UNITS[unit]["package_id"]
    (dest / f"{package_id}.nuspec").write_text(f"""<?xml version="1.0" encoding="utf-8"?>
<!-- Stub for the CI packaging step (private feed ONLY — contents are licensed game files, never publish
     publicly). Version derived from label {label}; buildid in manifest.json is the exact-depot arbiter. -->
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{package_id}</id>
    <version>{four_part_version(label)}</version>
    <authors>The Fun Pimps (assemblies); packaged by str0ngh34rt for private use</authors>
    <description>7 Days to Die {unit} assemblies, {label}, for compiling and testing StrongMods without a game
    install. Private use only; not redistributable.</description>
  </metadata>
</package>
""", encoding="utf-8")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--unit", choices=sorted(UNITS), required=True)
    ap.add_argument("--label", required=True, help="human version label, e.g. V2.5-b8 (in-game 'V 2.5 b8')")
    ap.add_argument("--install-dir", help="explicit install location (else SDTD_HOME, else Steam defaults)")
    ap.add_argument("--output-root", default=str(REPO_ROOT / "vendor"))
    ap.add_argument("--force", action="store_true", help="replace an existing tree for this unit+label")
    args = ap.parse_args()

    if not LABEL_RE.match(args.label):
        sys.exit(f"error: label {args.label!r} does not match V<major>.<minor>[.<patch>]-b<build>, e.g. V2.5-b8")

    install = find_install(args.unit, args.install_dir)
    managed = find_managed(args.unit, install)
    harmony = install / HARMONY_REL
    if not harmony.is_file():
        sys.exit(f"error: {harmony} not found")

    dest = Path(args.output_root) / args.unit / args.label
    if dest.exists():
        if not args.force:
            sys.exit(f"error: {dest} already exists (use --force to regenerate)")
        shutil.rmtree(dest)

    files = {}
    def vendor_file(src, rel):
        out = dest / rel
        out.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, out)
        files[rel.as_posix()] = {
            "size": src.stat().st_size,
            "sha256": hashlib.sha256(src.read_bytes()).hexdigest(),
        }

    data_dir_name = managed.parent.name
    for dll in sorted(managed.glob("*.dll")):
        vendor_file(dll, Path(data_dir_name) / "Managed" / dll.name)
    vendor_file(harmony, HARMONY_REL)

    manifest = {
        "unit": args.unit,
        "label": args.label,
        "package_id": UNITS[args.unit]["package_id"],
        "generated_utc": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "source_install": str(install),
        "source_managed": str(managed),
        "steam": steam_provenance(args.unit, install),
        "data_dir": data_dir_name,
        "files": files,
    }
    (dest / "manifest.json").write_text(json.dumps(manifest, indent=1) + "\n", encoding="utf-8")
    write_nuspec(dest, args.unit, args.label)

    total_mb = sum(f["size"] for f in files.values()) / 1048576
    steam = manifest["steam"]
    print(f"{args.unit} {args.label}: {len(files)} files, {total_mb:.1f} MB -> {dest}")
    print(f"  buildid={steam['buildid']}  betakey={steam['betakey'] or '(default branch)'}")


if __name__ == "__main__":
    main()
