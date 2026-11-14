// filepath: src/ImageTextify.Platform/Audio/UtteranceAudioConditioner.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 10 live-transcription accuracy pass
// purpose:  Conservative DSP conditioning applied to a finalized
//           utterance before it is written to WAV and sent to Whisper.
//           Two stages: a first-order high-pass filter (~80 Hz) that
//           removes DC offset and low-frequency rumble, and RMS
//           normalization that brings quiet or distant speech to a
//           consistent level without amplifying silence into noise.
// RELATIONSHIPS
//   Implements : n/a (static helper)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.Audio.LiveTranscriptionSession
//   See also   : ImageTextify.Core.Abstractions.IAudioEnhancer (contract;
//                no implementation exists yet)
// NOTES
//   - Internal. Pure function over float samples; no I/O, no state.
//   - The high-pass is a first-order DC-blocker variant:
//     y[n] = x[n] - x[n-1] + R*y[n-1], R chosen for ~80 Hz at 16 kHz.
//   - Normalization targets RMS 0.08 (~-22 dBFS) with a gain cap of 4x
//     and a peak ceiling of 0.95. Speech already louder than the target
//     is left near unity gain (only peak-limited), so clean loud audio
//     is not degraded.
//   - This is not voice separation and does not restore speech that was
//     never captured; it only makes the captured signal easier for the
//     recognizer.

using System;

namespace ImageTextify.Platform.Audio;

internal static class UtteranceAudioConditioner
{
    private const int SampleRate = 16000;
    private const double HighPassR = 0.9686; // ~80 Hz first-order high-pass at 16 kHz
    private const double TargetRms = 0.08;
    private const double MaxGain = 4.0;
    private const double PeakCeiling = 0.95;

    /// <summary>
    /// Conditions the utterance samples in place: high-pass, then RMS
    /// normalization with peak limiting.
    /// </summary>
    /// <param name="samples">The utterance samples. Modified in place.</param>
    public static void Condition(Span<float> samples)
    {
        if (samples.Length == 0) { return; }

        // Stage 1: first-order high-pass (DC blocker).
        float xPrev = 0f;
        float yPrev = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float x = samples[i];
            float y = x - xPrev + (float)HighPassR * yPrev;
            xPrev = x;
            yPrev = y;
            samples[i] = y;
        }

        // Stage 2: RMS normalization with gain cap and peak ceiling.
        double sumSq = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double v = samples[i];
            sumSq += v * v;
        }
        double rms = Math.Sqrt(sumSq / samples.Length);
        if (rms < 1e-6) { return; } // silence; leave as-is

        double gain = TargetRms / rms;
        if (gain > MaxGain) { gain = MaxGain; }
        if (gain < 1.0) { gain = 1.0; } // never attenuate; Whisper handles hot signals

        double peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double v = samples[i] * gain;
            double a = Math.Abs(v);
            if (a > peak) { peak = a; }
        }
        if (peak > PeakCeiling)
        {
            gain *= PeakCeiling / peak;
        }

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(samples[i] * gain);
        }
    }
}
