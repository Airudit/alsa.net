﻿using System;

namespace Alsa.Net.Internal
{
    public class AlsaDeviceException : Exception
    {
        public AlsaDeviceException(string message) : base(message) { }
    }

    static class ExceptionMessages
    {
        public const string CanNotGetPeriodSize = "Alsa Error: Can not get period size";
        public const string CanNotWriteToDevice = "Alsa Error: Can not write data to the device";
        public const string CanNotReadFromDevice = "Alsa Error: Can not read data from the device";
        public const string CanNotAllocateParameters = "Alsa Error: Can not allocate parameters object";
        public const string CanNotFillParameters = "Alsa Error: Can not fill parameters object";
        public const string CanNotSetAccessMode = "Alsa Error: Can not set access mode";
        public const string BitsPerSampleError = "Alsa Error: Bits per sample error. Please reset the value of RecordingBitsPerSample";
        public const string CanNotSetFormat = "Alsa Error: Can not set format";
        public const string CanNotSetChannel = "Alsa Error: Can not set channel";
        public const string CanNotSetRate = "Alsa Error: Can not set rate";
        public const string CanNotSetHwParams = "Alsa Error: Can not set hardware parameters";
        public const string CanNotSetVolume = "Alsa Error: Volume error";
        public const string CanNotSetMute = "Alsa Error: Mute error";
        public const string CanNotOpenPlayback = "Alsa Error: Can not open playback device";
        public const string CanNotDropDevice = "Alsa Error: Drop playback device error";
        public const string CanNotCloseDevice = "Alsa Error: Close playback device error";
        public const string CanNotOpenRecording = "Alsa Error: Can not open recording device";
        public const string CanNotOpenMixer = "Alsa Error: Can not open sound device mixer";
        public const string CanNotAttachMixer = "Alsa Error: Can not attach sound device mixer";
        public const string CanNotRegisterMixer = "Alsa Error: Can not register sound device mixer";
        public const string CanNotLoadMixer = "Alsa Error: Can not load sound device mixer";
        public const string CanNotCloseMixer = "Alsa Error: Close sound device mixer error";
    }
}
