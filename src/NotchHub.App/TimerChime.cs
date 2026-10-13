using System.IO;
using System.Media;
using System.Windows.Threading;
using NotchHub.Core;

namespace NotchHub.App;

/// <summary>
/// "Timer finished" sound: a soft ~4.6 s celesta-like arpeggio (sines with a gentle attack, exponential decay, quiet
/// overtones, a light echo, low volume) synthesized once at runtime and cached as a WAV in the data folder.
/// Plays up to <c>repeats</c> times; <see cref="Stop"/> silences it (HUD dismissed).
/// </summary>
internal sealed class TimerChime
{
    private const int Rate = 44100;
    private const string FileName = "timer-chime-v1.wav";
    private readonly Dispatcher _dispatcher;
    private SoundPlayer? _player;
    private DispatcherTimer? _timer;
    private int _remaining;
    private bool _active;
    private double _seconds;

    public TimerChime(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public bool IsPlaying => _active;

    /// <summary>UI thread.</summary>
    public void Play(int repeats)
    {
        Stop();
        try
        {
            if (_player is null)
            {
                var path = EnsureFile();
                _player = new SoundPlayer(path);
                _player.Load();
            }
            _remaining = Math.Max(1, repeats) - 1;
            _active = true;
            _player.Play();                                               // async (winmm), no thread of ours
            Arm();
        }
        catch (Exception ex)
        {
            Log.Warn("Timer chime failed", ex);
            _active = false;
            SystemSounds.Asterisk.Play();
        }
    }

    public void Stop()
    {
        _timer?.Stop();
        if (!_active) return;
        _active = false;
        try { _player?.Stop(); } catch (Exception ex) { Log.Debug("chime stop: " + ex.Message); }
    }

    private void Arm()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher);
            _timer.Tick += OnTick;
        }
        _timer.Interval = TimeSpan.FromSeconds(_seconds + (_remaining > 0 ? 0.45 : 0.1));
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer!.Stop();
        if (!_active) return;
        if (_remaining <= 0) { _active = false; return; }
        _remaining--;
        _player?.Play();
        Arm();
    }

    private string EnsureFile()
    {
        var path = AppPaths.File(FileName);
        var samples = Synthesize();
        _seconds = samples.Length / (double)Rate;
        if (!File.Exists(path) || new FileInfo(path).Length != 44 + samples.Length * 2)
        {
            var tmp = path + ".tmp";
            using (var fs = File.Create(tmp)) WriteWav(fs, samples);
            File.Move(tmp, path, overwrite: true);
        }
        return path;
    }

    /// <summary>Synthesizes the melody (mono, 16-bit PCM samples).</summary>
    internal static short[] Synthesize()
    {
        // (start s, frequency Hz, gain, decay s): a rising C-major arpeggio, a gentle answer, then a soft resolving chord
        (double T, double F, double G, double Tau)[] notes =
        [
            (0.00, 659.25, 0.85, 0.9),  // E5
            (0.28, 783.99, 0.80, 0.9),  // G5
            (0.56, 1046.50, 0.75, 1.0), // C6
            (0.84, 783.99, 0.60, 0.8),  // G5
            (1.20, 880.00, 0.70, 0.9),  // A5
            (1.48, 1046.50, 0.65, 0.9), // C6
            (1.76, 1174.66, 0.60, 1.0), // D6
            (2.04, 987.77, 0.55, 0.9),  // B5
            (2.50, 523.25, 0.60, 1.5),  // C5  ┐
            (2.54, 659.25, 0.55, 1.5),  // E5  │ rolled chord
            (2.58, 783.99, 0.50, 1.5),  // G5  │
            (2.62, 1046.50, 0.55, 1.6), // C6  ┘
        ];
        const double length = 4.6;
        int n = (int)(length * Rate);
        var dry = new double[n];
        foreach (var (t0, f, g, tau) in notes)
        {
            int start = (int)(t0 * Rate);
            double gain = g * Math.Pow(600 / f, 0.35);                     // higher notes softer
            for (int i = start; i < n; i++)
            {
                double t = (i - start) / (double)Rate;
                double env = Math.Exp(-t / tau);
                if (env < 1e-4) break;
                double attack = t < 0.012 ? 0.5 - 0.5 * Math.Cos(Math.PI * t / 0.012) : 1;
                double w = 2 * Math.PI * f * t;
                double v = Math.Sin(w)
                           + 0.28 * Math.Sin(2 * w) * Math.Exp(-t / 0.22)           // soft bell overtone, fades fast
                           + 0.08 * Math.Sin(3.01 * w) * Math.Exp(-t / 0.12);
                dry[i] += gain * attack * env * v;
            }
        }
        // light echo (two taps), one-pole low-pass for a mellow top end, fade-out at the very end
        int d1 = (int)(0.23 * Rate), d2 = (int)(0.46 * Rate);
        var wet = new double[n];
        double lp = 0, peak = 0;
        const double alpha = 0.42;                                         // ≈ 5 kHz at 44.1 kHz
        for (int i = 0; i < n; i++)
        {
            double x = dry[i] + (i >= d1 ? 0.26 * dry[i - d1] : 0) + (i >= d2 ? 0.12 * dry[i - d2] : 0);
            lp += alpha * (x - lp);
            double fade = i > n - 0.35 * Rate ? (n - i) / (0.35 * Rate) : 1;
            wet[i] = lp * fade;
            peak = Math.Max(peak, Math.Abs(wet[i]));
        }
        var pcm = new short[n];
        double scale = peak > 0 ? 0.30 / peak : 0;                        // quiet: peak at 30 % of full scale
        for (int i = 0; i < n; i++) pcm[i] = (short)Math.Round(wet[i] * scale * short.MaxValue);
        return pcm;
    }

    private static void WriteWav(Stream s, short[] pcm)
    {
        using var w = new BinaryWriter(s);
        int bytes = pcm.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(bytes);
        foreach (var v in pcm) w.Write(v);
    }
}
