using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Alsa.Net.Internal
{
    class UnixSoundOutDevice : IDisposable
    {
        static readonly object PlaybackInitializationLock = new();

        public SoundDeviceSettings Settings { get; }

        bool _playbackMute;
        IntPtr _playbackPcm;
        IntPtr _mixelElement;
        bool _wasDisposed;

        private volatile PlaybackState playbackState;
        public PlaybackState PlaybackState => playbackState;
        
        public UnixSoundOutDevice(SoundDeviceSettings settings)
        {
            playbackState = PlaybackState.Stopped;

            Settings = settings;
        }

        public void Play(StreamBuffer wavStreamBuffer)
        {
            if (_wasDisposed)
                throw new ObjectDisposedException(nameof(UnixSoundInDevice));

            Play(wavStreamBuffer, CancellationToken.None);
        }

        public void Play(StreamBuffer wavStreamBuffer, CancellationToken cancellationToken)
        {
            if (_wasDisposed)
                throw new ObjectDisposedException(nameof(UnixSoundInDevice));

            if (this.playbackState == PlaybackState.Stopped)
            {
                var parameter = new IntPtr();
                var dir = 0;
                var header = WavHeader.FromStream(wavStreamBuffer);
            
                this.playbackState = PlaybackState.Playing;
                this.OpenPlaybackPcm();
                this.PcmInitialize(this._playbackPcm, header, ref parameter, ref dir);
                this.WriteStreamBuffer(wavStreamBuffer, header, ref parameter, ref dir, cancellationToken);
                this.ClosePlaybackPcm();
            }
            else
            {
                if (this.playbackState != PlaybackState.Paused)
                {
                    return;
                }

            }
        }
        unsafe void WriteStreamBuffer(StreamBuffer wavStreamBuffer, WavHeader header, ref IntPtr @params, ref int dir, CancellationToken cancellationToken)
        {
            ulong frames;

            fixed (int* dirP = &dir)
                ThrowErrorMessage(InteropAlsa.snd_pcm_hw_params_get_period_size(@params, &frames, dirP), ExceptionMessages.CanNotGetPeriodSize);

            var bufferSize = frames * header.BlockAlign;
            var readBuffer = new byte[(int)bufferSize];

            fixed (byte* buffer = readBuffer)
            {
                while (!_wasDisposed && !cancellationToken.IsCancellationRequested && wavStreamBuffer.Read(readBuffer) != 0)
                    ThrowErrorMessage(InteropAlsa.snd_pcm_writei(_playbackPcm, (IntPtr)buffer, frames), ExceptionMessages.CanNotWriteToDevice);
            }
        }

        unsafe void PcmInitialize(IntPtr pcm, WavHeader header, ref IntPtr @params, ref int dir)
        {
            ThrowErrorMessage(InteropAlsa.snd_pcm_hw_params_malloc(ref @params), ExceptionMessages.CanNotAllocateParameters);
            ThrowErrorMessage(InteropAlsa.snd_pcm_hw_params_any(pcm, @params), ExceptionMessages.CanNotFillParameters);
            ThrowErrorMessage(InteropAlsa.snd_pcm_hw_params_set_access(pcm, @params, snd_pcm_access_t.SND_PCM_ACCESS_RW_INTERLEAVED), ExceptionMessages.CanNotSetAccessMode);

            var formatResult = (header.BitsPerSample / 8) switch
            {
                1 => InteropAlsa.snd_pcm_hw_params_set_format(pcm, @params, snd_pcm_format_t.SND_PCM_FORMAT_U8),
                2 => InteropAlsa.snd_pcm_hw_params_set_format(pcm, @params, snd_pcm_format_t.SND_PCM_FORMAT_S16_LE),
                3 => InteropAlsa.snd_pcm_hw_params_set_format(pcm, @params, snd_pcm_format_t.SND_PCM_FORMAT_S24_LE),
                _ => throw new AlsaDeviceException(ExceptionMessages.BitsPerSampleError)
            };
            ThrowErrorMessage(formatResult, ExceptionMessages.CanNotSetFormat);

            ThrowErrorMessage(InteropAlsa.snd_pcm_hw_params_set_channels(pcm, @params, header.NumChannels), ExceptionMessages.CanNotSetChannel);

            var val = header.SampleRate;
            fixed (int* dirP = &dir)
                ThrowErrorMessage(InteropAlsa.snd_pcm_hw_params_set_rate_near(pcm, @params, &val, dirP), ExceptionMessages.CanNotSetRate);

            ThrowErrorMessage(InteropAlsa.snd_pcm_hw_params(pcm, @params), ExceptionMessages.CanNotSetHwParams);
        }
        
        void OpenPlaybackPcm()
        {
            if (_playbackPcm != default)
                return;

            lock (PlaybackInitializationLock)
                ThrowErrorMessage(InteropAlsa.snd_pcm_open(ref _playbackPcm, Settings.PlaybackDeviceName, snd_pcm_stream_t.SND_PCM_STREAM_PLAYBACK, 0), ExceptionMessages.CanNotOpenPlayback);
        }

        void ClosePlaybackPcm()
        {
            if (_playbackPcm == default)
                return;

            ThrowErrorMessage(InteropAlsa.snd_pcm_drain(_playbackPcm), ExceptionMessages.CanNotDropDevice);
            ThrowErrorMessage(InteropAlsa.snd_pcm_close(_playbackPcm), ExceptionMessages.CanNotCloseDevice);

            _playbackPcm = default;
        }


        public void Dispose()
        {
            _wasDisposed = true;

            ClosePlaybackPcm();
        }

        void ThrowErrorMessage(int errorNum, string message)
        {
            if (errorNum >= 0)
                return;

            var errorMsg = Marshal.PtrToStringAnsi(InteropAlsa.snd_strerror(errorNum));

            Dispose();
            throw new AlsaDeviceException($"{message}\nError {errorNum}. {errorMsg}.");
        }
    }
}

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
}