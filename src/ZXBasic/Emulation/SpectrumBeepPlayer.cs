// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using OpenTK.Audio.OpenAL;

namespace ZXBasic.Emulation;

public sealed class SpectrumBeepPlayer : IDisposable
{
    internal const int SampleRate = 44100;
    internal const double MiddleCFrequency = 261.6255653005986;

    private readonly SemaphoreSlim m_playbackLock = new(1, 1);
    private readonly CancellationTokenSource m_disposalCancellation = new();
    private ALDevice m_device;
    private ALContext m_context;
    private bool m_isInitialized;
    private bool m_isDisposed;

    public async Task PlayAsync(double duration, double pitch, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(m_isDisposed, this);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            m_disposalCancellation.Token);
        var playbackCancellation = linkedCancellation.Token;
        await m_playbackLock.WaitAsync(playbackCancellation);
        try
        {
            try
            {
                await Task.Run(() => Play(duration, pitch, playbackCancellation), playbackCancellation);
            }
            catch (Exception) when (!playbackCancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(duration), playbackCancellation);
            }
        }
        finally
        {
            m_playbackLock.Release();
        }
    }

    internal static double GetFrequency(double pitch)
    {
        return MiddleCFrequency * Math.Pow(2, pitch / 12);
    }

    internal static byte[] CreateSamples(double duration, double pitch)
    {
        var samples = new byte[checked((int)Math.Round(duration * SampleRate))];
        var frequency = GetFrequency(pitch);
        var phase = 0.0;
        var phaseStep = frequency / SampleRate;
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = phase < 0.5 ? (byte)32 : (byte)224;
            phase += phaseStep;
            phase -= Math.Floor(phase);
        }
        return samples;
    }

    private void Play(double duration, double pitch, CancellationToken cancellationToken)
    {
        EnsureInitialized();
        ALC.MakeContextCurrent(m_context);

        var buffer = AL.GenBuffer();
        var source = AL.GenSource();
        try
        {
            var samples = CreateSamples(duration, pitch);
            AL.BufferData(buffer, ALFormat.Mono8, samples, SampleRate);
            AL.Source(source, ALSourcei.Buffer, buffer);
            AL.Source(source, ALSourcef.Gain, 0.2f);
            AL.SourcePlay(source);
            ThrowOnAudioError();

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(5);
                AL.GetSource(source, ALGetSourcei.SourceState, out var state);
                if ((ALSourceState)state != ALSourceState.Playing)
                {
                    break;
                }
            } while (true);
        }
        finally
        {
            try
            {
                AL.SourceStop(source);
                AL.DeleteSource(source);
                AL.DeleteBuffer(buffer);
            }
            finally
            {
                ALC.MakeContextCurrent(ALContext.Null);
            }
        }
    }

    private void EnsureInitialized()
    {
        if (m_isInitialized)
        {
            return;
        }

        m_device = ALC.OpenDevice(null);
        if (m_device == ALDevice.Null)
        {
            throw new InvalidOperationException("Could not open the default audio device.");
        }

        m_context = ALC.CreateContext(m_device, (int[]?)null);
        if (m_context == ALContext.Null)
        {
            ALC.CloseDevice(m_device);
            m_device = ALDevice.Null;
            throw new InvalidOperationException("Could not create an OpenAL audio context.");
        }
        m_isInitialized = true;
    }

    private static void ThrowOnAudioError()
    {
        var error = AL.GetError();
        if (error != ALError.NoError)
        {
            throw new InvalidOperationException($"OpenAL playback failed: {error}.");
        }
    }

    public void Dispose()
    {
        if (m_isDisposed)
        {
            return;
        }

        m_disposalCancellation.Cancel();
        m_playbackLock.Wait();
        try
        {
            if (m_isInitialized)
            {
                ALC.MakeContextCurrent(ALContext.Null);
                ALC.DestroyContext(m_context);
                ALC.CloseDevice(m_device);
                m_isInitialized = false;
            }
            m_isDisposed = true;
        }
        finally
        {
            m_playbackLock.Release();
            m_playbackLock.Dispose();
            m_disposalCancellation.Dispose();
        }
    }
}
