// filepath: src/FileHarvester.Tests/ImageTextify/UtteranceAudioConditionerTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 live-transcription accuracy pass
// purpose:  Proves UtteranceAudioConditioner removes DC offset, boosts
//           quiet speech toward the target RMS without exceeding the
//           gain cap, and leaves silence untouched.
// RELATIONSHIPS
//   Implements : n/a (test class)
//   Depends on : ImageTextify.Platform.Audio.UtteranceAudioConditioner
//   Used by    : dotnet test --filter UtteranceAudioConditionerTests
//   See also   : ImageTextify.Platform.Audio.LiveTranscriptionSession

using System;
using FluentAssertions;
using ImageTextify.Platform.Audio;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class UtteranceAudioConditionerTests
{
    private static float Rms(float[] samples)
    {
        double sumSq = 0;
        foreach (var s in samples) { sumSq += s * s; }
        return (float)Math.Sqrt(sumSq / samples.Length);
    }

    [Fact]
    public void SilenceStaysSilent()
    {
        var samples = new float[16000];
        UtteranceAudioConditioner.Condition(samples);
        Rms(samples).Should().BeLessThan((float)1e-6);
    }

    [Fact]
    public void DcOffsetIsRemoved()
    {
        var samples = new float[16000];
        Array.Fill(samples, 0.3f);
        UtteranceAudioConditioner.Condition(samples);
        // After the high-pass, the steady DC component is gone; the tail
        // of the signal should be near zero.
        var tail = samples.AsSpan(samples.Length - 1000);
        double mean = 0;
        foreach (var v in tail) { mean += v; }
        mean /= tail.Length;
        Math.Abs(mean).Should().BeLessThan(0.01);
    }

    [Fact]
    public void QuietSpeechIsBoostedTowardTarget()
    {
        var samples = new float[16000];
        var rng = new Random(42);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(rng.NextDouble() * 2 - 1) * 0.01f; // RMS ~0.006
        }
        UtteranceAudioConditioner.Condition(samples);
        var rms = Rms(samples);
        rms.Should().BeGreaterThan(0.02f); // boosted well above the input
        rms.Should().BeLessThanOrEqualTo(0.12f); // bounded by gain cap / target
    }

    [Fact]
    public void LoudSpeechIsNotAttenuated()
    {
        var samples = new float[16000];
        var rng = new Random(7);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(rng.NextDouble() * 2 - 1) * 0.2f; // RMS ~0.115
        }
        var before = Rms(samples);
        UtteranceAudioConditioner.Condition(samples);
        var after = Rms(samples);
        after.Should().BeGreaterThanOrEqualTo(before * 0.9f); // not turned down
    }
}
