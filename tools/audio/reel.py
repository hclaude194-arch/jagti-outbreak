"""
A listening reel: a tour of the sounds in one file (tools/audio/preview/sound_reel.mp3) with a cue
sheet of what plays when (sound_reel.txt), and spectrograms of the same takes (spectrograms.png),
so the sounds can be checked without opening Unity.
"""
import os
import subprocess
import tempfile

import numpy as np

import dsp

TOUR = [
    ("Guns, close (each: two shots with the outdoor tail)", [
        "pistol", "smg", "shotgun", "rifle", "dmr", "revolver", "carbine", "double", "lmg", "sniper"]),
    ("Guns, far away (300 m)", ["rifle_far", "sniper_far", "shotgun_far", "explosion_far"]),
    ("Indoors (rifle with the room tail), suppressed", ["rifle+tail_in_rifle", "suppressed_light", "suppressed_rifle"]),
    ("Reloads", ["mag_out_heavy", "mag_in_heavy", "charge_rifle", "pump", "cyl_open", "speedloader", "cyl_close", "bolt"]),
    ("Bullets", ["crack", "whiz", "ricochet", "impact_concrete", "impact_metal", "impact_dirt", "glass_break"]),
    ("Grenade", ["pin_pull", "clink", "explosion"]),
    ("Footsteps (run)", ["step_run_grass", "step_run_gravel", "step_run_concrete", "step_run_tile", "step_run_wood", "step_run_metal", "step_run_water"]),
    ("Vehicles", ["scooter_low", "auto_low", "jeep_low", "cart_high", "horn_bulb", "horn_high", "crash", "rotor"]),
    ("Sky bus and the drop", ["skybus_props", "burner", "wind", "canopy"]),
    ("Items and HUD", ["pickup_ammo", "pickup_armour", "chai", "hit_body", "hit_head", "kill_confirm", "armour_break", "ui_click", "ui_start"]),
    ("Haze", ["haze_warning", "haze_wall", "haze_tick"]),
    ("Campus ambience", ["bed_day", "bird_koel", "bird_crow", "bed_evening", "bed_night", "jackals"]),
    ("Music", ["music_menu", "music_explore", "music_combat", "music_final", "victory", "music_defeat"]),
]
CLIP_SECONDS = {"bed_day": 8, "bed_evening": 8, "bed_night": 8, "music_menu": 40, "music_explore": 20, "music_combat": 20,
                "music_final": 25, "victory": 12, "music_defeat": 9, "haze_wall": 4, "rotor": 4, "skybus_props": 4,
                "scooter_low": 3, "auto_low": 3, "jeep_low": 3, "cart_high": 3, "wind": 4}


def load(out_dir, rel):
    path = os.path.join(out_dir, rel + ".ogg")
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
        tmp = f.name
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", path, "-ar", str(dsp.SR), "-ac", "2", tmp], check=True)
    import wave
    with wave.open(tmp) as w:
        x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(float) / 32767
    os.remove(tmp)
    return x.reshape(-1, 2)


def first_file(out_dir, name):
    for cand in (name + "_1", name):
        for dp, dn, fn in os.walk(out_dir):
            if cand + ".ogg" in fn:
                return os.path.relpath(os.path.join(dp, cand), out_dir)
    return None


def spectrogram(x, w=420, h=120):
    from PIL import Image
    m = dsp.to_mono(x)
    hop = max(1, len(m) // w)
    win = 1024
    cols = []
    for i in range(w):
        seg = m[i * hop: i * hop + win]
        if len(seg) < win:
            seg = np.pad(seg, (0, win - len(seg)))
        s = np.abs(np.fft.rfft(seg * np.hanning(win)))[:512]
        cols.append(20 * np.log10(s + 1e-6))
    img = np.array(cols).T[::-1]
    img = np.clip((img + 30) / 70, 0, 1)
    rows = np.geomspace(1, 511, h).astype(int)[::-1]
    img = img[::-1][rows]
    rgb = np.stack([img ** 0.6, img ** 1.2, 0.3 + 0.7 * img ** 3], -1)
    return Image.fromarray((rgb * 255).astype(np.uint8))


def write(out_dir, preview_dir):
    from PIL import Image, ImageDraw
    os.makedirs(preview_dir, exist_ok=True)
    parts = []
    cues = []
    t = 0.0
    tiles = []
    for title, names in TOUR:
        cues.append("%5.1f s  %s" % (t, title))
        for nm in names:
            layers = nm.split("+")
            clips = []
            for ln in layers:
                rel = first_file(out_dir, ln)
                if rel:
                    clips.append(load(out_dir, rel))
            if not clips:
                continue
            x = dsp.mix([(c, 0, 0.8) for c in clips])
            if nm in ("pistol", "smg", "shotgun", "rifle", "dmr", "revolver", "carbine", "double", "lmg", "sniper"):
                grp = {"pistol": "light", "smg": "light", "revolver": "light", "shotgun": "shot", "double": "shot",
                       "dmr": "heavy", "sniper": "heavy"}.get(nm, "rifle")
                tail = load(out_dir, first_file(out_dir, "tail_out_" + grp))
                one = dsp.mix([(x, 0, 1), (tail, 0, 0.7)])
                x = dsp.mix([(one, 0, 1), (one, 0.45, 1)])
            lim = CLIP_SECONDS.get(nm, 3.0)
            x = dsp.fade(x[: dsp.n_of(lim)], 0, 0.3 if len(x) > dsp.n_of(lim) else 0.01)
            tiles.append((nm, spectrogram(x)))
            cues.append("%5.1f s    %s" % (t, nm))
            parts.append((x, t, 1.0))
            t += len(x) / dsp.SR + 0.6
        t += 1.0
    reel = dsp.limit(dsp.mix(parts), -1)
    wav = os.path.join(preview_dir, "sound_reel.wav")
    import wave
    with wave.open(wav, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(dsp.SR)
        w.writeframes((np.clip(reel, -1, 1) * 32767).astype("<i2").tobytes())
    mp3 = os.path.join(preview_dir, "sound_reel.mp3")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-c:a", "libmp3lame", "-q:a", "2", mp3], check=True)
    os.remove(wav)
    open(os.path.join(preview_dir, "sound_reel.txt"), "w").write("\n".join(cues) + "\n")
    cols = 5
    tw, th = 420, 140
    sheet = Image.new("RGB", (cols * tw, ((len(tiles) + cols - 1) // cols) * th), (12, 12, 16))
    d = ImageDraw.Draw(sheet)
    for i, (nm, im) in enumerate(tiles):
        x0, y0 = (i % cols) * tw, (i // cols) * th
        sheet.paste(im, (x0, y0 + 18))
        d.text((x0 + 4, y0 + 2), nm, fill=(230, 230, 230))
    sheet.save(os.path.join(preview_dir, "spectrograms.png"))
    print("[audio] reel: %.0f s -> %s" % (t, os.path.relpath(mp3)))
