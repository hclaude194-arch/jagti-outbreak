"""
The sound registry. Each sound module registers its events with @sound; build_sounds.py renders
every variant, writes the OGG files under Assets/Resources/Audio/ and the event table
Assets/Resources/Audio/sound_bank.json that Jagti.Audio.SoundBank reads at runtime.

Fields (all mirrored by SoundBank.EventDef in Assets/Scripts/Audio/SoundBank.cs):
  bus       mixer group: Weapons, World, Vehicles, Ambience, Music, Interface, Voice
  variants  how many takes to render (played at random, never the same twice running)
  level     loudness the takes are normalised to (dB RMS over the loud part)
  vol       runtime gain on top (the mix, tuned by ear against the other sounds)
  pitch     random pitch range at runtime
  min, max  distance (m) the sound is full volume within, and silent beyond
  curve     how fast it falls off between: 1 = like 1/distance, 2 = faster
  far       another event that takes over with distance (gunshots: the distant crack and echo),
            crossfading between farFrom and farFull metres
  delay     true: the sound arrives late with distance (speed of sound), for shots and blasts
  stereo    true: rendered in stereo (2D sounds, tails, beds, music)
  loop      true: the take loops without a seam
  priority  0 is most important; the voice pool steals from the highest number first
  reverb    how much of the room reverb it gets (0 for UI and music)
"""

EVENTS = {}


def sound(name, cat, bus="World", variants=1, level=-18.0, vol=1.0, pitch=(0.96, 1.04), min=2.0, max=60.0,
          curve=1.0, far="", farFrom=0.0, farFull=0.0, delay=False, stereo=False, loop=False, priority=128,
          reverb=1.0, spatial=True, quality=0.5):
    def deco(fn):
        EVENTS[name] = dict(fn=fn, name=name, cat=cat, bus=bus, variants=variants, level=level, vol=vol,
                            pitchMin=pitch[0], pitchMax=pitch[1], minDistance=min, maxDistance=max,
                            curve=curve, far=far, farFrom=farFrom, farFull=farFull, delay=delay,
                            stereo=stereo, loop=loop, priority=priority, reverb=reverb, spatial=spatial,
                            quality=quality)
        return fn
    return deco
