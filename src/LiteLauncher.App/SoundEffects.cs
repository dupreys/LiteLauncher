using System.IO;
using System.Media;

namespace LiteLauncher.App;

public enum SoundKind { Move, Select, Back, Launch }

/// <summary>
/// Tiny interface sounds synthesised in memory (no audio files shipped).
/// Volume is applied when the tone is generated, so the setting is instant.
/// </summary>
public sealed class SoundEffects
{
    private const int SampleRate = 22050;

    private DateTime _lastMove = DateTime.MinValue;
    private DateTime _silentUntil = DateTime.MinValue;

    public bool Enabled { get; set; } = true;

    /// <summary>0 to 1.</summary>
    public double Volume { get; set; } = 0.5;

    /// <summary>Suppresses sounds for a short time (e.g. focus moves caused by switching screens).</summary>
    public void Silence(TimeSpan duration) => _silentUntil = DateTime.UtcNow + duration;

    public void Play(SoundKind kind)
    {
        if (!Enabled || Volume < 0.01) return;

        var now = DateTime.UtcNow;
        if (now < _silentUntil) return;

        if (kind == SoundKind.Move)
        {
            if ((now - _lastMove).TotalMilliseconds < 45) return; // fast scrolling shouldn't buzz
            _lastMove = now;
        }

        var wav = kind switch
        {
            SoundKind.Move => Tone(new[] { (1200.0, 22) }, 0.35),
            SoundKind.Select => Tone(new[] { (660.0, 45), (990.0, 70) }, 0.5),
            SoundKind.Back => Tone(new[] { (520.0, 45), (390.0, 70) }, 0.5),
            _ => Tone(new[] { (523.0, 60), (659.0, 60), (784.0, 120) }, 0.55),
        };

        // PlaySync on a pool thread: SoundPlayer.Play() loads from the stream asynchronously,
        // which is awkward with a stream that goes out of scope.
        _ = Task.Run(() =>
        {
            try
            {
                using var stream = new MemoryStream(wav);
                using var player = new SoundPlayer(stream);
                player.PlaySync();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
            {
                // No audio device: UI sounds are optional.
            }
        });
    }

    private byte[] Tone((double Frequency, int Milliseconds)[] notes, double gain)
    {
        // Squared so the slider feels linear to the ear.
        var amplitude = Math.Pow(Math.Clamp(Volume, 0, 1), 2) * gain * short.MaxValue * 0.6;

        var samples = new List<short>();
        foreach (var (frequency, milliseconds) in notes)
        {
            var count = SampleRate * milliseconds / 1000;
            for (var i = 0; i < count; i++)
            {
                var attack = Math.Min(1.0, i / (SampleRate * 0.004));
                var decay = Math.Exp(-3.0 * i / count);
                var value = Math.Sin(2 * Math.PI * frequency * i / SampleRate) * attack * decay * amplitude;
                samples.Add((short)value);
            }
        }

        var dataLength = samples.Count * 2;
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);          // PCM
        writer.Write((short)1);          // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);    // byte rate
        writer.Write((short)2);          // block align
        writer.Write((short)16);         // bits per sample
        writer.Write("data"u8);
        writer.Write(dataLength);
        foreach (var sample in samples) writer.Write(sample);
        writer.Flush();
        return buffer.ToArray();
    }
}
