"""Static source gate for the supplied Unity project. This is not a Unity compile or headset test."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path


EXPECTED_PACKAGES = {
    "com.meta.xr.sdk.all": "205.0.0",
    "com.unity.xr.openxr": "1.17.0",
}


def script_guid(path: Path) -> str:
    match = re.search(r"^guid: ([a-f0-9]{32})$", path.read_text(encoding="utf-8"), re.MULTILINE)
    if not match:
        raise AssertionError(f"Missing Unity GUID: {path}")
    return match.group(1)


def verify(root: Path) -> dict:
    project = root / "unity-project"
    sdk = root / "unity-meta" / "Assets" / "ProtocolRunVRMeta"
    merged = project / "Assets" / "ProtocolRunVRMeta"
    version = (project / "ProjectSettings" / "ProjectVersion.txt").read_text(encoding="utf-8")
    assert "m_EditorVersion: 6000.3.16f1" in version
    manifest = json.loads((project / "Packages" / "manifest.json").read_text(encoding="utf-8"))
    for name, expected in EXPECTED_PACKAGES.items():
        assert manifest["dependencies"].get(name) == expected, f"Unexpected {name} version"

    scripts = sorted(sdk.rglob("*.cs"))
    assert len(scripts) == 9, "Expected nine Meta SDK C# files"
    for source in scripts:
        relative = source.relative_to(sdk)
        target = merged / relative
        assert target.read_bytes() == source.read_bytes(), f"Merged SDK differs: {relative}"
        assert script_guid(source.with_suffix(source.suffix + ".meta")) == script_guid(target.with_suffix(target.suffix + ".meta"))

    scene = (project / "Assets" / "Scenes" / "ProtocolRunVR_DEMO.unity").read_text(encoding="utf-8")
    roles = {
        "CUBE_A": ("expectedGrabbable: 1", "allowBaselineRestore: 0", "allowDemoControls: 0"),
        "CUBE_B": ("expectedGrabbable: 1", "allowBaselineRestore: 1", "allowDemoControls: 1"),
        "CUBE_C": ("expectedGrabbable: 0", "allowBaselineRestore: 0", "allowDemoControls: 0"),
    }
    for object_id, fields in roles.items():
        marker = f"objectId: {object_id}"
        assert scene.count(marker) == 1, f"Expected exactly one {marker}"
        start = scene.index(marker)
        block = scene[start : start + 220]
        assert all(field in block for field in fields), f"Wrong ProtocolRun role for {object_id}"
    assert scene.count("m_Name: ISDK_HandGrabInteraction") == 2, "Only A and B may have direct hand-grab children"
    assert scene.count("Oculus.Interaction::Oculus.Interaction.Grabbable") == 2, "Only A and B may have Grabbable"

    session = (sdk / "Runtime" / "ProtocolRunMetaSession.cs").read_text(encoding="utf-8")
    assert "GetJointPose(HandJointId.HandIndexTip" in session
    assert "restore_hand_grab_baseline" in session
    assert not list(project.rglob("connection.json")), "Private connection file must not be packaged"
    assert not list(project.rglob("*.jsonl")), "Runtime journals must not be packaged"
    return {
        "static_unity_source": "passed",
        "unity": "6000.3.16f1",
        "meta_xr_all_in_one": EXPECTED_PACKAGES["com.meta.xr.sdk.all"],
        "openxr": EXPECTED_PACKAGES["com.unity.xr.openxr"],
        "meta_csharp_files": len(scripts),
        "scene_roles": roles,
        "runtime_compile_and_quest": "NOT VERIFIED",
    }


if __name__ == "__main__":
    repository = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]).resolve()
    print(json.dumps(verify(repository), ensure_ascii=False, indent=2))
