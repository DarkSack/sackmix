namespace SackMix.Core;

/// <summary>
/// Normalises one program: brings what is loud (a shout, a loud friend on Discord) down to one
/// level and gives the volume back slowly when it calms down, so everything sounds alike.
/// Windows only lets a program be turned down, never up past 100 %: quiet sound passes
/// untouched and the rest comes down to it (the user turns Windows' master volume up once).
/// </summary>
public sealed class AutoLevel
{
    /// <summary>The level everything is brought to (-18 dBFS peak); quieter sound passes untouched.</summary>
    public const float Target = 0.125f;
    /// <summary>Never lower than this (-24 dB): it evens out, it does not silence.</summary>
    public const float Floor = 0.063f;
    /// <summary>Below this (-50 dBFS) is silence, and silence changes nothing.</summary>
    public const float Silence = 0.003f;

    // How long the loudness lingers after a peak: one quiet syllable is not "calm again".
    private const float HoldSeconds = 1.5f;
    // Down quickly so a shout barely gets through; up slowly so the level does not pump.
    private const float DownSeconds = 0.15f, UpSeconds = 2.5f;

    private float _envelope;
    private float _gain = 1f;

    /// <summary>The gain last handed out to Windows, <see cref="Floor"/>..1.</summary>
    public float Gain { get; private set; } = 1f;

    /// <param name="peak">The program's own loudest level since the last call, 0..1, before any volume.</param>
    /// <param name="seconds">Time since the last call.</param>
    /// <returns>True when <see cref="Gain"/> moved enough to be worth sending to Windows.</returns>
    public bool Update(float peak, float seconds)
    {
        // Silence (the pause between a loud friend's sentences) freezes everything: nothing to judge by.
        if (!float.IsFinite(peak) || seconds <= 0 || peak <= Silence) return false;
        _envelope = peak >= _envelope ? peak : _envelope * MathF.Exp(-seconds / HoldSeconds);
        var want = Math.Clamp(Target / _envelope, Floor, 1f);
        _gain += (want - _gain) * (1 - MathF.Exp(-seconds / (want < _gain ? DownSeconds : UpSeconds)));

        // Small steps are kept back, but the way home to exactly 1 is always finished.
        var next = _gain > 0.995f ? 1f : _gain;
        if (MathF.Abs(next - Gain) < 0.01f && !(next == 1f && Gain != 1f)) return false;
        Gain = next;
        return true;
    }

    /// <summary>Back to untouched, as when the channel stops normalising.</summary>
    public void Reset()
    {
        _envelope = 0;
        _gain = Gain = 1f;
    }
}
