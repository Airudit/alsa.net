using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Alsa.Net.Internal
{
    public class UnixSoundInDevice : IDisposable
    {
        static readonly object RecordingInitializationLock = new();

        private SoundDeviceSettings Settings { get; }

        bool _recordingMute;
        IntPtr _recordingPcm;
        IntPtr _mixelElement;
        bool _wasDisposed;
        public event EventHandler<AlsaStoppedEventArgs> RecordingStopped;


        public UnixSoundInDevice(SoundDeviceSettings settings)
        {
            Settings = settings;
        }

        public void StartRecording(Stream saveStream, CancellationToken token)
        {
            if (_wasDisposed)
                throw new ObjectDisposedException(nameof(UnixSoundInDevice));

            var parameters = new IntPtr();
            var dir = 0;
            var header = WavHeader.Build(Settings.RecordingSampleRate, Settings.RecordingChannels, Settings.RecordingBitsPerSample);

            OpenRecordingPcm();
            PcmInitialize(_recordingPcm, header, ref parameters, ref dir);
            ReadStream(saveStream, header, ref parameters, ref dir, token);
            CloseRecordingPcm();
        }

        public void StartRecording(Action<byte[]> onDataAvailable, CancellationToken token)
        {
            if (_wasDisposed)
                throw new ObjectDisposedException(nameof(UnixSoundInDevice));

            var parameters = new IntPtr();
            var dir = 0;

            var header = WavHeader.Build(Settings.RecordingSampleRate, Settings.RecordingChannels, Settings.RecordingBitsPerSample);
            using (var memoryStream = new MemoryStream())
            {
                header.WriteToStream(memoryStream);
                onDataAvailable?.Invoke(memoryStream.ToArray());
            }

            OpenRecordingPcm();
            PcmInitialize(_recordingPcm, header, ref parameters, ref dir);
            ReadStream(onDataAvailable, header, ref parameters, ref dir, token);
            CloseRecordingPcm();
        }
        
        unsafe void ReadStream(Stream saveStream, WavHeader header, ref IntPtr @params, ref int dir, CancellationToken cancellationToken)
        {
            ulong frames;

            fixed (int* dirP = &dir)
                ThrowErrorMessage(InteropAlsa.snd_pcm_hw_params_get_period_size(@params, &frames, dirP), ExceptionMessages.CanNotGetPeriodSize);

            var bufferSize = frames * header.BlockAlign;
            var readBuffer = new byte[(int)bufferSize];

            fixed (byte* buffer = readBuffer)
            {
                while (!_wasDisposed && !cancellationToken.IsCancellationRequested)
                {
                    ThrowErrorMessage(InteropAlsa.snd_pcm_readi(_recordingPcm, (IntPtr)buffer, frames), ExceptionMessages.CanNotReadFromDevice);
                    saveStream.Write(readBuffer, 0, readBuffer.Length);
                }
            }

            saveStream.Flush();
        }

        unsafe void ReadStream(Action<byte[]> onDataAvailable, WavHeader header, ref IntPtr @params, ref int dir, CancellationToken cancellationToken)
        {
            ulong frames;

            fixed (int* dirP = &dir)
                ThrowErrorMessage(InteropAlsa.snd_pcm_hw_params_get_period_size(@params, &frames, dirP), ExceptionMessages.CanNotGetPeriodSize);

            var bufferSize = frames * header.BlockAlign;
            var readBuffer = new byte[(int)bufferSize];

            fixed (byte* buffer = readBuffer)
            {
                while (!_wasDisposed && !cancellationToken.IsCancellationRequested)
                {
                    ThrowErrorMessage(InteropAlsa.snd_pcm_readi(_recordingPcm, (IntPtr)buffer, frames), ExceptionMessages.CanNotReadFromDevice);
                    onDataAvailable?.Invoke(readBuffer);
                }
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
        
        void OpenRecordingPcm()
        {
            if (_recordingPcm != default)
                return;

            lock (RecordingInitializationLock)
                ThrowErrorMessage(InteropAlsa.snd_pcm_open(ref _recordingPcm, Settings.RecordingDeviceName, snd_pcm_stream_t.SND_PCM_STREAM_CAPTURE, 0), ExceptionMessages.CanNotOpenRecording);
        }

        void CloseRecordingPcm()
        {
            if (_recordingPcm == default)
                return;

            ThrowErrorMessage(InteropAlsa.snd_pcm_drain(_recordingPcm), ExceptionMessages.CanNotDropDevice);
            ThrowErrorMessage(InteropAlsa.snd_pcm_close(_recordingPcm), ExceptionMessages.CanNotCloseDevice);
            this.RecordingStopped?.Invoke(this, new AlsaStoppedEventArgs());
            _recordingPcm = default;
        }

        public void Dispose()
        {
            _wasDisposed = true;

            CloseRecordingPcm();
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

public class AlsaStoppedEventArgs : EventArgs
{
    private readonly Exception exception;

    public AlsaStoppedEventArgs(Exception exception = null) => this.exception = exception;

    public Exception Exception => this.exception;
}

