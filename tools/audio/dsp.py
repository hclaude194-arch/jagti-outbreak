"""
Signal building blocks for the game's sounds (numpy + scipy only).

Every sound in the game is made from these: noise, oscillators, damped modes, plucked strings,
filters, envelopes, synthetic rooms and echoes, saturation and loudness. Nothing here is a
recording, so every clip the tool writes is original work (see tools/audio/LICENSES.md).

Arrays are float64 at SR; mono is shape (n,), stereo is shape (n, 2).
"""
import numpy as np
from scipy import signal

SR = 44100


# ---------------------------------------------------------------------------- basics

def rng(seed):
    return np.random.default_rng(seed)


def n_of(seconds):
    return max(1, int(round(seconds * SR)))


def times(seconds):
    return np.arange(n_of(seconds)) / SR


def silence(seconds, stereo=False):
    n = n_of(seconds)
    return np.zeros((n, 2)) if stereo else np.zeros(n)


def white(seconds, r):
    return r.standard_normal(n_of(seconds))


def coloured(seconds, r, slope):
    """Noise whose power falls by `slope` dB per octave (3 = pink, 6 = brown)."""
    n = n_of(seconds)
    spec = np.fft.rfft(r.standard_normal(n))
    f = np.fft.rfftfreq(n, 1.0 / SR)
    f[0] = f[1]
    spec *= (f / 1000.0) ** (-slope / 6.02)
    x = np.fft.irfft(spec, n)
    return x / (np.std(x) + 1e-12)


def pink(seconds, r):
    return coloured(seconds, r, 3.0)


def brown(seconds, r):
    return coloured(seconds, r, 6.0)


def pad_to(x, n):
    if len(x) >= n:
        return x[:n]
    shape = (n - len(x),) + x.shape[1:]
    return np.concatenate([x, np.zeros(shape)])


def mix(parts, seconds=None, stereo=None):
    """parts: list of (array, start_seconds, gain). Mono parts go to both sides of a stereo mix."""
    if stereo is None:
        stereo = any(p[0].ndim == 2 for p in parts)
    end = 0
    for x, at, g in parts:
        end = max(end, int(round(at * SR)) + len(x))
    n = n_of(seconds) if seconds else end
    out = np.zeros((n, 2)) if stereo else np.zeros(n)
    for x, at, g in parts:
        i = int(round(at * SR))
        if i >= n:
            continue
        seg = x[: n - i] * g
        if stereo and seg.ndim == 1:
            seg = np.stack([seg, seg], axis=1)
        out[i: i + len(seg)] += seg
    return out


def to_stereo(x, pan=0.0):
    if x.ndim == 2:
        return x
    a = (pan + 1.0) * np.pi / 4.0
    return np.stack([x * np.cos(a), x * np.sin(a)], axis=1) * np.sqrt(2.0)


def to_mono(x):
    return x.mean(axis=1) if x.ndim == 2 else x


# ---------------------------------------------------------------------------- envelopes

def env_exp(seconds, tau, attack=0.0005):
    t = times(seconds)
    e = np.exp(-t / max(tau, 1e-5))
    if attack > 0:
        e *= np.clip(t / attack, 0, 1)
    return e


def env_ad(seconds, attack, decay_tau):
    return env_exp(seconds, decay_tau, attack)


def env_lin(seconds, points):
    """points: list of (time, value), linearly joined."""
    t = times(seconds)
    ts = [p[0] for p in points]
    vs = [p[1] for p in points]
    return np.interp(t, ts, vs)


def env_hann(seconds):
    n = n_of(seconds)
    return np.hanning(n)


def fade(x, fade_in=0.0, fade_out=0.0):
    x = x.copy()
    n = len(x)
    if fade_in > 0:
        k = min(n, n_of(fade_in))
        ramp = np.sin(np.linspace(0, np.pi / 2, k)) ** 2
        x[:k] = (x[:k].T * ramp).T
    if fade_out > 0:
        k = min(n, n_of(fade_out))
        ramp = np.cos(np.linspace(0, np.pi / 2, k)) ** 2
        x[n - k:] = (x[n - k:].T * ramp).T
    return x


def apply(x, e):
    e = pad_to(e, len(x))
    return (x.T * e).T


# ---------------------------------------------------------------------------- oscillators

def phase_of(freq, seconds):
    n = n_of(seconds)
    f = np.broadcast_to(np.asarray(freq, dtype=float), (n,)) if np.ndim(freq) == 0 else pad_to(np.asarray(freq, float), n)
    return np.cumsum(f) / SR


def sine(freq, seconds, phase0=0.0):
    return np.sin(2 * np.pi * (phase_of(freq, seconds) + phase0))


def saw(freq, seconds, phase0=0.0):
    p = phase_of(freq, seconds) + phase0
    return 2.0 * (p - np.floor(p + 0.5))


def square(freq, seconds, duty=0.5):
    p = phase_of(freq, seconds)
    return np.where((p % 1.0) < duty, 1.0, -1.0)


def tri(freq, seconds):
    p = phase_of(freq, seconds)
    return 2.0 * np.abs(2.0 * (p - np.floor(p + 0.5))) - 1.0


def sweep(f0, f1, seconds, curve=1.0):
    t = np.linspace(0, 1, n_of(seconds)) ** curve
    return f0 + (f1 - f0) * t


def glide_exp(f0, f1, seconds, tau):
    t = times(seconds)
    return f1 + (f0 - f1) * np.exp(-t / tau)


# ---------------------------------------------------------------------------- filters

def _sos(kind, f, order):
    nyq = SR / 2.0
    if kind in ("bandpass", "bandstop"):
        lo, hi = f
        lo = max(5.0, min(lo, nyq * 0.95))
        hi = max(lo * 1.05, min(hi, nyq * 0.98))
        return signal.butter(order, [lo / nyq, hi / nyq], btype=kind, output="sos")
    f = max(5.0, min(f, nyq * 0.98))
    return signal.butter(order, f / nyq, btype=kind, output="sos")


def lp(x, f, order=2):
    return signal.sosfilt(_sos("lowpass", f, order), x, axis=0)


def hp(x, f, order=2):
    return signal.sosfilt(_sos("highpass", f, order), x, axis=0)


def bp(x, lo, hi, order=2):
    return signal.sosfilt(_sos("bandpass", (lo, hi), order), x, axis=0)


def biquad_peak(f, q, gain_db):
    a = 10 ** (gain_db / 40.0)
    w = 2 * np.pi * f / SR
    alpha = np.sin(w) / (2 * q)
    b = [1 + alpha * a, -2 * np.cos(w), 1 - alpha * a]
    den = [1 + alpha / a, -2 * np.cos(w), 1 - alpha / a]
    return np.array(b) / den[0], np.array(den) / den[0]


def peak(x, f, q, gain_db):
    b, a = biquad_peak(f, q, gain_db)
    return signal.lfilter(b, a, x, axis=0)


def resonator(x, f, q):
    """A narrow band-pass ring at f (the body of a drum, a pipe, a panel)."""
    w = 2 * np.pi * f / SR
    alpha = np.sin(w) / (2 * q)
    b = np.array([alpha, 0, -alpha])
    a = np.array([1 + alpha, -2 * np.cos(w), 1 - alpha])
    return signal.lfilter(b / a[0], a / a[0], x, axis=0)


def tv_lp(x, cutoff, block=256):
    """Low-pass whose cutoff follows the array `cutoff` (Hz per sample), block by block."""
    out = np.zeros_like(x)
    cutoff = pad_to(np.asarray(cutoff, float), len(x))
    z = None
    for i in range(0, len(x), block):
        f = float(np.clip(cutoff[i], 20, SR * 0.45))
        b, a = signal.butter(2, f / (SR / 2))
        if z is None:
            z = np.zeros((2,) + x.shape[1:]) if x.ndim == 1 else np.zeros((2, x.shape[1]))
        seg = x[i: i + block]
        if x.ndim == 1:
            y, z = signal.lfilter(b, a, seg, zi=z)
        else:
            y, z = signal.lfilter(b, a, seg, axis=0, zi=z)
        out[i: i + block] = y
    return out


def tv_bp(x, centre, q, block=256):
    """Band-pass sweeping with `centre` (Hz per sample)."""
    out = np.zeros_like(x)
    centre = pad_to(np.asarray(centre, float), len(x))
    z = np.zeros(2)
    for i in range(0, len(x), block):
        f = float(np.clip(centre[i], 30, SR * 0.45))
        w = 2 * np.pi * f / SR
        alpha = np.sin(w) / (2 * q)
        b = np.array([alpha, 0, -alpha]) / (1 + alpha)
        a = np.array([1, -2 * np.cos(w) / (1 + alpha), (1 - alpha) / (1 + alpha)])
        y, z = signal.lfilter(b, a, x[i: i + block], zi=z)
        out[i: i + block] = y
    return out


def one_pole(x, f):
    a = np.exp(-2 * np.pi * f / SR)
    return signal.lfilter([1 - a], [1, -a], x, axis=0)


def dc_block(x):
    return signal.lfilter([1, -1], [1, -0.995], x, axis=0)


# ---------------------------------------------------------------------------- sources

def modal(freqs, decays, amps, seconds, r=None, jitter=0.0):
    """A struck object: a sum of damped sine modes (metal, glass, wood, plastic)."""
    t = times(seconds)
    out = np.zeros_like(t)
    for f, d, a in zip(freqs, decays, amps):
        if r is not None and jitter > 0:
            f *= 1 + r.uniform(-jitter, jitter)
        ph = r.uniform(0, 2 * np.pi) if r is not None else 0.0
        out += a * np.sin(2 * np.pi * f * t + ph) * np.exp(-t / d)
    return out


def metal_modes(base, count, r, spread=2.7, decay=0.08, inharm=0.35):
    """Inharmonic, bright modes of a small steel or brass part."""
    fs, ds, amps = [], [], []
    for k in range(count):
        f = base * (1 + k * spread * (1 + r.uniform(-inharm, inharm) * 0.3)) ** 1.05
        fs.append(f)
        ds.append(decay * (0.4 + r.uniform(0, 0.9)) / (1 + k * 0.25))
        amps.append((0.5 + r.uniform(0, 0.5)) / (1 + k * 0.4))
    return fs, ds, amps


def click(seconds, r, lo, hi, tau, ring=None):
    """A short mechanical click: band-limited noise transient, optionally with ringing modes."""
    x = bp(white(seconds, r), lo, hi, 2) * env_exp(seconds, tau, 0.0002)
    if ring is not None:
        fs, ds, a = ring
        x = x + modal(fs, ds, a, seconds, r) * 0.6
    return x


def impulse_train(rate, seconds, jitter, r, width=0.0008):
    """Pulses at `rate` per second (array or constant) with timing jitter; returns (signal, pulse times)."""
    n = n_of(seconds)
    ph = phase_of(rate, seconds)
    idx = np.nonzero(np.diff(np.floor(ph)) > 0)[0]
    x = np.zeros(n)
    pulses = []
    for i in idx:
        j = int(i + r.normal(0, jitter * SR))
        if 0 <= j < n:
            x[j] += 1.0
            pulses.append(j)
    return x, pulses


def ks_string(freq, seconds, r, decay=0.996, bright=0.5, pluck=0.3):
    """Karplus-Strong plucked or hammered string."""
    n = n_of(seconds)
    period = SR / freq
    p = int(period)
    frac = period - p
    burst = r.uniform(-1, 1, p + 2)
    burst = lp(burst, 1500 + bright * 9000, 1)
    pos = max(1, int(p * pluck))
    burst[pos:] -= burst[:-pos] * 0.5
    x = np.zeros(n)
    x[: len(burst)] = burst
    # y[n] = x[n] + g*((1-frac)*y[n-p] + frac*y[n-p-1]) with a 2-point average for damping
    g = decay
    a = np.zeros(p + 3)
    a[0] = 1.0
    a[p] -= g * 0.5 * (1 - frac)
    a[p + 1] -= g * 0.5
    a[p + 2] -= g * 0.5 * frac
    y = signal.lfilter([1.0], a, x)
    return y


def grains(seconds, r, rate, lo, hi, tau=0.002, amp_var=0.8, rate_env=None):
    """Many tiny clicks (gravel, crackling leaves, sizzling, rain on a roof)."""
    n = n_of(seconds)
    x = np.zeros(n)
    lam = rate / SR
    if rate_env is None:
        rate_env = np.ones(n)
    else:
        rate_env = pad_to(rate_env, n)
    hits = np.nonzero(r.random(n) < lam * rate_env)[0]
    x[hits] = r.uniform(1 - amp_var, 1, len(hits)) * r.choice([-1, 1], len(hits))
    k = n_of(tau * 6)
    kern = np.exp(-np.arange(k) / (tau * SR))
    x = signal.fftconvolve(x, kern)[:n]
    return bp(x, lo, hi, 2)


# ---------------------------------------------------------------------------- shaping

def saturate(x, drive=2.0):
    return np.tanh(x * drive) / np.tanh(drive)


def soft_clip(x, thresh=0.8):
    return np.where(np.abs(x) < thresh, x, np.sign(x) * (thresh + (1 - thresh) * np.tanh((np.abs(x) - thresh) / (1 - thresh))))


def compress(x, thresh_db=-18, ratio=4, attack=0.003, release=0.12, makeup_db=0):
    mono = np.abs(to_mono(x)) if x.ndim == 2 else np.abs(x)
    a_a = np.exp(-1 / (attack * SR))
    a_r = np.exp(-1 / (release * SR))
    # peak follower, done coarse (block) for speed
    block = 64
    nb = (len(mono) + block - 1) // block
    pk = np.array([mono[i * block:(i + 1) * block].max() for i in range(nb)])
    env = np.zeros(nb)
    e = 0.0
    ca = a_a ** block
    cr = a_r ** block
    for i in range(nb):
        v = pk[i]
        e = ca * e + (1 - ca) * v if v > e else cr * e + (1 - cr) * v
        env[i] = e
    env = np.repeat(env, block)[: len(mono)]
    lvl = 20 * np.log10(env + 1e-9)
    over = np.maximum(0, lvl - thresh_db)
    gain_db = -over * (1 - 1 / ratio) + makeup_db
    g = 10 ** (gain_db / 20)
    return apply(x, g)


def limit(x, ceiling_db=-1.0):
    c = 10 ** (ceiling_db / 20)
    pkv = np.max(np.abs(x)) + 1e-12
    if pkv > c:
        x = soft_clip(x / pkv * 1.15, 0.85) * c / 1.0
        pkv = np.max(np.abs(x)) + 1e-12
        if pkv > c:
            x *= c / pkv
    return x


def peak_norm(x, db=-1.0):
    return x * (10 ** (db / 20) / (np.max(np.abs(x)) + 1e-12))


def rms_db(x):
    return 20 * np.log10(np.sqrt(np.mean(to_mono(x) ** 2)) + 1e-12)


def loud_norm(x, target_db, ceiling_db=-1.0):
    """Scale to an RMS level (over the loud part of the clip), then keep the peaks under the ceiling."""
    m = np.abs(to_mono(x))
    k = n_of(0.05)
    if len(m) > k:
        win = np.convolve(m ** 2, np.ones(k) / k, mode="same")
        loud = win > np.max(win) * 0.05
        level = 10 * np.log10(np.mean(win[loud]) + 1e-12)
    else:
        level = rms_db(x)
    x = x * 10 ** ((target_db - level) / 20)
    return limit(x, ceiling_db)


def resample(x, ratio):
    """Plays x at `ratio` times the speed (2 = an octave up, half as long)."""
    n = len(x)
    m = max(1, int(n / ratio))
    src = np.arange(m) * ratio
    if x.ndim == 1:
        return np.interp(src, np.arange(n), x)
    return np.stack([np.interp(src, np.arange(n), x[:, c]) for c in range(x.shape[1])], axis=1)


def loopify(x, xfade=0.5):
    """Makes x loop without a seam: the tail is cross-faded into the head (the result is shorter by xfade)."""
    k = n_of(xfade)
    head, body, tail = x[:k], x[k:-k] if len(x) > 2 * k else x[k:], x[-k:]
    w = np.sin(np.linspace(0, np.pi / 2, k)) ** 2
    if x.ndim == 2:
        w = w[:, None]
    joined = tail * (1 - w) + head * w
    return np.concatenate([body, joined]) if len(x) > 2 * k else x


def delay(x, seconds, feedback=0.0, gain=1.0, lp_hz=None, repeats=6):
    """Echo taps every `seconds`, each `feedback` times quieter (and darker with lp_hz)."""
    parts = [(x, 0, 1)]
    cur = x
    g = gain
    for k in range(repeats if feedback > 0 else 1):
        if lp_hz:
            cur = lp(cur, lp_hz, 1)
        parts.append((cur, seconds * (k + 1), g))
        g *= feedback
        if g < 0.01:
            break
    return mix(parts)


def chorus(x, r, depth=0.004, rate=0.4, voices=2):
    """Stereo chorus: a few slowly wandering delayed copies, spread left and right."""
    x = to_mono(x)
    n = len(x)
    idx = np.arange(n)
    left = x.copy()
    right = x.copy()
    for v in range(voices):
        ph = r.uniform(0, 2 * np.pi)
        d = (depth * 0.5 * (1 + np.sin(2 * np.pi * rate * (1 + v * 0.37) * idx / SR + ph)) + 0.006) * SR
        y = np.interp(idx - d, idx, x, left=0)
        if v % 2 == 0:
            left += y * 0.7
            right += y * 0.35
        else:
            right += y * 0.7
            left += y * 0.35
    return np.stack([left, right], axis=1) / (1 + voices * 0.5)


# ---------------------------------------------------------------------------- rooms and echoes

def room_ir(seconds, rt60, r, bright=6000, dark=1200, early=(), density=1.0, stereo=True, predelay=0.0):
    """
    A synthetic impulse response: early reflections (list of (time, gain)) and an exponentially
    decaying noise tail whose high end dies faster than its low end. Left and right tails are
    different noise, so the room sounds wide.
    """
    n = n_of(seconds)
    t = np.arange(n) / SR
    chans = []
    for c in range(2 if stereo else 1):
        rr = np.random.default_rng(r.integers(1 << 30))
        tail = rr.standard_normal(n)
        if density < 1.0:
            tail *= (rr.random(n) < density * 0.6 + 0.02) * 2
        decay = np.exp(-6.9 * t / rt60)
        hi = lp(tail, bright, 2) * decay
        lo = lp(tail, dark, 2) * np.exp(-6.9 * t / (rt60 * 1.35))
        x = hi * 0.6 + lo * 0.8
        # tail builds up over the first few ms
        x *= np.clip((t - predelay) / 0.012, 0, 1)
        for (et, eg) in early:
            i = int((et + predelay) * SR * (1 + rr.uniform(-0.04, 0.04)))
            if 0 <= i < n:
                x[i] += eg * (1 if rr.random() < 0.7 else -1) * 3.0
        chans.append(x)
    ir = np.stack(chans, axis=1) if stereo else chans[0]
    return ir / (np.sqrt(np.sum(to_mono(ir) ** 2)) + 1e-12)


def outdoor_ir(seconds, r, slaps=((0.06, 0.5), (0.11, 0.35), (0.19, 0.3), (0.31, 0.22)), roll=2.6, roll_gain=0.5, lp_hz=2200, stereo=True):
    """
    Open ground among buildings and hills: a few discrete slapback echoes off walls, then a long,
    dark rolling rumble off the hills (sound returning from many distances at once).
    """
    n = n_of(seconds)
    t = np.arange(n) / SR
    chans = []
    for c in range(2 if stereo else 1):
        rr = np.random.default_rng(r.integers(1 << 30))
        x = np.zeros(n)
        x[0] = 1.0
        for (st, sg) in slaps:
            i = int(st * SR * (1 + rr.uniform(-0.15, 0.15)))
            if i < n:
                x[i] += sg * rr.uniform(0.7, 1.1)
        # rolling hill echo: sparse, then dense, slowly swelling and dying
        swell = np.clip(t / 0.25, 0, 1) * np.exp(-t / (roll / 3.2))
        sparse = (rr.random(n) < 0.004) * rr.standard_normal(n)
        dense = rr.standard_normal(n) * 0.06
        rumble = lp(sparse + dense, lp_hz, 2) * swell * roll_gain * 4
        # very slow amplitude wander, like echo groups arriving
        wob = 0.6 + 0.4 * np.sin(2 * np.pi * (0.7 + rr.uniform(0, 0.6)) * t + rr.uniform(0, 6))
        x = x + rumble * wob
        chans.append(x)
    ir = np.stack(chans, axis=1) if stereo else chans[0]
    return ir


def convolve(x, ir):
    if x.ndim == 1 and ir.ndim == 2:
        return np.stack([signal.fftconvolve(x, ir[:, c]) for c in range(2)], axis=1)
    if x.ndim == 2 and ir.ndim == 2:
        return np.stack([signal.fftconvolve(x[:, c], ir[:, c]) for c in range(2)], axis=1)
    if x.ndim == 2:
        return np.stack([signal.fftconvolve(x[:, c], ir) for c in range(2)], axis=1)
    return signal.fftconvolve(x, ir)


def reverb(x, ir, wet=0.3, dry=1.0):
    y = convolve(x, ir)
    xs = x if (x.ndim == y.ndim) else to_stereo(x)
    return mix([(xs, 0, dry), (y, 0, wet)])


def hall(r, rt60=2.2, seconds=3.0):
    return room_ir(seconds, rt60, r, bright=7000, dark=1500,
                   early=[(0.013, 0.5), (0.021, 0.4), (0.034, 0.35), (0.047, 0.3), (0.061, 0.22)])


def trim(x, thresh_db=-70, keep=0.02):
    """Cuts silence off the end."""
    m = np.abs(to_mono(x))
    th = 10 ** (thresh_db / 20) * (np.max(m) + 1e-12)
    idx = np.nonzero(m > th)[0]
    if len(idx) == 0:
        return x
    end = min(len(x), idx[-1] + n_of(keep))
    return fade(x[:end], 0, min(0.02, end / SR / 4))
