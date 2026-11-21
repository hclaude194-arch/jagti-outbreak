"""
Writes the Audio Mixer asset (Assets/Resources/Audio/JagtiMixer.mixer) as Unity YAML, since Unity has
no public API to make one from code. Groups:

    Master
      Music
      Ambience
      Effects
        Weapons
        World
        Vehicles
      Interface
      Voice

Each group's volume is exposed as <Group>Volume (dB), which Jagti.Audio.AudioMixerLink sets from the
volume sliders. Ids are fixed (hashed from the names) so the file only changes when the groups do.
If Unity ever fails to read it, the game still plays: AudioMixerLink falls back to per-bus gains.
"""
import hashlib
import os

GROUPS = [
    ("Master", None),
    ("Music", "Master"),
    ("Ambience", "Master"),
    ("Effects", "Master"),
    ("Weapons", "Effects"),
    ("World", "Effects"),
    ("Vehicles", "Effects"),
    ("Interface", "Master"),
    ("Voice", "Master"),
]

MIXER_GUID = "4a1e7c2d9b3f45e8a6c0d1e2f3a4b5c6"


def guid(text):
    return hashlib.md5(("jagti-mixer-" + text).encode()).hexdigest()


def file_id(kind, i):
    base = {"group": 243000000, "effect": 244000000, "snapshot": 245000000}[kind]
    return base + i * 2 + 2


def write(out_dir):
    lines = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:"]
    gid = {name: file_id("group", i) for i, (name, _) in enumerate(GROUPS)}
    eid = {name: file_id("effect", i) for i, (name, _) in enumerate(GROUPS)}
    snap = file_id("snapshot", 0)
    lines += [
        "--- !u!241 &24100000",
        "AudioMixerController:",
        "  m_ObjectHideFlags: 0",
        "  m_CorrespondingSourceObject: {fileID: 0}",
        "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}",
        "  m_Name: JagtiMixer",
        "  m_OutputGroup: {fileID: 0}",
        "  m_MasterGroup: {fileID: %d}" % gid["Master"],
        "  m_Snapshots:",
        "  - {fileID: %d}" % snap,
        "  m_StartSnapshot: {fileID: %d}" % snap,
        "  m_SuspendThreshold: -80",
        "  m_EnableSuspend: 1",
        "  m_UpdateMode: 1",
        "  m_ExposedParameters:",
    ]
    for name, _ in GROUPS:
        lines += ["  - guid: %s" % guid(name + "/volume"), "    name: %sVolume" % name]
    lines += ["  m_AudioMixerGroupViews:", "  - guids:"]
    for name, _ in GROUPS:
        lines.append("    - %s" % guid(name + "/group"))
    lines += ["    name: View", "  m_CurrentViewIndex: 0", "  m_TargetSnapshot: {fileID: %d}" % snap]
    for i, (name, parent) in enumerate(GROUPS):
        children = [c for c, p in GROUPS if p == name]
        lines += [
            "--- !u!243 &%d" % gid[name],
            "AudioMixerGroupController:",
            "  m_ObjectHideFlags: 0",
            "  m_CorrespondingSourceObject: {fileID: 0}",
            "  m_PrefabInstance: {fileID: 0}",
            "  m_PrefabAsset: {fileID: 0}",
            "  m_Name: %s" % name,
            "  m_AudioMixer: {fileID: 24100000}",
            "  m_GroupID: %s" % guid(name + "/group"),
            "  m_Children:" + ("" if children else " []"),
        ]
        for c in children:
            lines.append("  - {fileID: %d}" % gid[c])
        lines += [
            "  m_Volume: %s" % guid(name + "/volume"),
            "  m_Pitch: %s" % guid(name + "/pitch"),
            "  m_Send: 00000000000000000000000000000000",
            "  m_Effects:",
            "  - {fileID: %d}" % eid[name],
            "  m_UserColorIndex: %d" % (i % 8),
            "  m_Mute: 0",
            "  m_Solo: 0",
            "  m_BypassEffects: 0",
        ]
    for name, _ in GROUPS:
        lines += [
            "--- !u!244 &%d" % eid[name],
            "AudioMixerEffectController:",
            "  m_ObjectHideFlags: 3",
            "  m_CorrespondingSourceObject: {fileID: 0}",
            "  m_PrefabInstance: {fileID: 0}",
            "  m_PrefabAsset: {fileID: 0}",
            "  m_Name: ",
            "  m_EffectID: %s" % guid(name + "/attenuation"),
            "  m_EffectName: Attenuation",
            "  m_MixLevel: %s" % guid(name + "/mix"),
            "  m_Parameters: []",
            "  m_SendTarget: {fileID: 0}",
            "  m_EnableWetMix: 0",
            "  m_Bypass: 0",
        ]
    lines += [
        "--- !u!245 &%d" % snap,
        "AudioMixerSnapshotController:",
        "  m_ObjectHideFlags: 0",
        "  m_CorrespondingSourceObject: {fileID: 0}",
        "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}",
        "  m_Name: Snapshot",
        "  m_AudioMixer: {fileID: 24100000}",
        "  m_SnapshotID: %s" % guid("snapshot"),
        "  m_FloatValues: {}",
        "  m_TransitionOverrides: {}",
        "",
    ]
    with open(os.path.join(out_dir, "JagtiMixer.mixer"), "w") as f:
        f.write("\n".join(lines))
    meta = os.path.join(out_dir, "JagtiMixer.mixer.meta")
    with open(meta, "w") as f:
        f.write("fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 24100000\n"
                "  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % MIXER_GUID)
