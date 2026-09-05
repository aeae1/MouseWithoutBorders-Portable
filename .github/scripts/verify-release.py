"""Fail closed on release metadata mismatch. Uses only the Python standard library."""
import re
import sys
import xml.etree.ElementTree as ET


def validate(tag, prerelease, version):
    if not re.fullmatch(r"mwb-v[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.]+)?", tag):
        raise ValueError("Invalid release tag")
    if tag.removeprefix("mwb-v") != version:
        raise ValueError("Release tag does not match the version of the built source")
    expected = "true" if "-" in version else "false"
    if prerelease != expected:
        raise ValueError("Release prerelease flag does not match the version")


if __name__ == "__main__":
    version = ET.parse("Directory.Build.props").findtext(".//Version")
    validate(sys.argv[1], sys.argv[2], version)
