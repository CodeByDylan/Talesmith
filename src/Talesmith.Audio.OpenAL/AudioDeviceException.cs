namespace Talesmith.Audio.OpenAL;

/// <summary>No audio device could be opened, or OpenAL itself could not be loaded.</summary>
public sealed class AudioDeviceException(string message, Exception? innerException = null) : Exception(message, innerException);
