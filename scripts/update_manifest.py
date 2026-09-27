#!/usr/bin/env python3
"""Add a released plugin version to a Jellyfin plugin repository manifest.

The manifest is the JSON file Jellyfin reads when a repository is added under
Dashboard > Plugins > Repositories. Each release adds (or replaces) one entry
in the plugin's "versions" list; older versions are kept so users can roll back.
"""

import argparse
import hashlib
import json
import sys
from pathlib import Path


def normalise_version(version: str) -> str:
    """Pad a version to four parts, as Jellyfin compares versions with System.Version."""
    parts = str(version).strip().split(".")
    if not 1 <= len(parts) <= 4 or not all(p.isdigit() for p in parts):
        raise ValueError(f"Invalid plugin version: {version!r}")
    return ".".join(parts + ["0"] * (4 - len(parts)))


def md5sum(path: Path) -> str:
    digest = hashlib.md5()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            digest.update(chunk)
    return digest.hexdigest()


def update_manifest(manifest: list, config: dict, entry: dict) -> list:
    guid = config["guid"]
    plugin = next((p for p in manifest if p.get("guid") == guid), None)
    if plugin is None:
        plugin = {"guid": guid, "versions": []}
        manifest.append(plugin)

    # Plugin details always follow the latest build.yaml.
    for key in ("name", "description", "overview", "owner", "category", "imageUrl"):
        if config.get(key):
            plugin[key] = str(config[key]).strip()

    versions = [v for v in plugin.get("versions", []) if v.get("version") != entry["version"]]
    versions.append(entry)
    versions.sort(key=lambda v: tuple(int(p) for p in v["version"].split(".")), reverse=True)
    del plugin["versions"]
    plugin["versions"] = versions  # keep versions after the plugin details
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, type=Path, help="build.yaml converted to JSON")
    parser.add_argument("--zip", required=True, type=Path, help="Plugin zip attached to the release")
    parser.add_argument("--source-url", required=True, help="Download URL of the plugin zip")
    parser.add_argument("--timestamp", required=True, help="Release publish time (ISO 8601)")
    parser.add_argument("--changelog", type=Path, help="File holding the release notes")
    parser.add_argument("--manifest", required=True, type=Path, help="Manifest to create or update")
    args = parser.parse_args()

    config = json.loads(args.config.read_text(encoding="utf-8"))
    changelog = args.changelog.read_text(encoding="utf-8").strip() if args.changelog else ""
    if not changelog:
        changelog = str(config.get("changelog") or "").strip()

    entry = {
        "version": normalise_version(config["version"]),
        "changelog": changelog,
        "targetAbi": config["targetAbi"],
        "sourceUrl": args.source_url,
        "checksum": md5sum(args.zip),
        "timestamp": args.timestamp,
    }

    manifest = []
    if args.manifest.exists():
        manifest = json.loads(args.manifest.read_text(encoding="utf-8"))

    manifest = update_manifest(manifest, config, entry)
    args.manifest.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Added version {entry['version']} (targetAbi {entry['targetAbi']}) to {args.manifest}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
