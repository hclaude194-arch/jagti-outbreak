# Sound licences

## Everything in Assets/Resources/Audio except voice/

Every sound effect, ambience bed, bird call and music track is original. Each one is synthesised
from code in this folder (`dsp.py`, `weapons.py`, `foley.py`, `vehicles.py`, `ambience.py`, `ui.py`,
`music.py`): no recordings, samples or sound packs from anywhere else are used. The project owner
may license them as they like; as written they are dedicated to the public domain under CC0 1.0
(https://creativecommons.org/publicdomain/zero/1.0/). Rebuild them with
`python3 tools/audio/build_sounds.py` (numpy, scipy and ffmpeg with libvorbis).

The music is written in Indian modes (Yaman, Bhairavi) with synthesised tanpura, santoor, bansuri,
tabla, dhol and pads; the melodies are new, not taken from existing songs.

## Assets/Resources/Audio/voice/ (announcer lines)

Made with text-to-speech through Speko (routed to Azure's MAI-Voice-2.1 voice en-IN-Dhruv) on
2026-11-21; the text is original (see `voice_lines.json`). Synthetic speech from that service is
for use in the account holder's own products under the provider's terms; check the current Speko
and Azure terms before a commercial release, or re-record the ten lines with a voice actor (the
file names are the keys in `voice_lines.json`, and `Announcer.cs` loads whatever is there).

## Fonts

Barlow Condensed (Assets/Resources/Fonts) is under the SIL Open Font Licence, see OFL.txt there.
