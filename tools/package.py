"""Build an immutable release bundle and matching Mod API update feed."""
import argparse
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent
REPOSITORY = "https://github.com/fankserver/vanguard-galaxy-blueprint-pin"


def package(configuration="Release", output=ROOT / "dist", tag=None):
    version = ET.parse(ROOT / "VGBlueprintPin/VGBlueprintPin.csproj").findtext("PropertyGroup/Version")
    assert re.fullmatch(r"\d+\.\d+\.\d+", version), "Invalid release version"
    plugin = (ROOT / "VGBlueprintPin/Plugin.cs").read_text()
    assert f'PluginVersion = "{version}"' in plugin, "Plugin and project versions differ"
    assert tag is None or tag == "v" + version, "Release tag differs from package version"
    metadata = ROOT / "vgblueprintpin.vgmod.json"
    data = json.loads(metadata.read_text(encoding="utf-8"))
    assert data["schemaVersion"] == 1 and data["pluginId"] == "vgblueprintpin"
    assert data["channel"] == "stable"
    assert data["updateUrl"] == REPOSITORY + "/releases/latest/download/update.json"
    assert all(data.get(key) for key in ("author", "description", "projectUrl"))
    dll = ROOT / "VGBlueprintPin/bin" / configuration / "netstandard2.1/VGBlueprintPin.dll"
    assert dll.is_file(), "Build the plugin before packaging"
    output.mkdir(parents=True, exist_ok=True)
    archive = output / f"VGBlueprintPin-v{version}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for file in (dll, metadata, ROOT / "README.md", ROOT / "LICENSE"):
            if file.is_file():
                bundle.write(file, "VGBlueprintPin/" + file.name)
    feed = {"schemaVersion": 1, "pluginId": "vgblueprintpin", "channel": "stable",
            "version": version, "releaseUrl": REPOSITORY + "/releases/tag/v" + version}
    (output / "update.json").write_text(json.dumps(feed, indent=2) + "\n", encoding="utf-8")
    with zipfile.ZipFile(archive) as bundle:
        assert "VGBlueprintPin/vgblueprintpin.vgmod.json" in bundle.namelist()
        assert [n for n in bundle.namelist() if n.endswith(".dll")] == ["VGBlueprintPin/VGBlueprintPin.dll"]
    print(archive)
    return archive


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--tag")
    options = parser.parse_args()
    package(options.configuration, tag=options.tag)
