using TouchdownAlert.Core.Abstractions;

namespace TouchdownAlert.App.Audio;

/// <summary>
/// Logs instead of playing. Selected when Sounds:Enabled is false or TOUCHDOWNALERT_SILENT=1,
/// so integration tests and headless runs don't need an audio device.
/// </summary>
public sealed class NullSoundPlayer : ISoundPlayer
{
    private readonly ILogger<NullSoundPlayer> _logger;

    public NullSoundPlayer(ILogger<NullSoundPlayer> logger)
    {
        _logger = logger;
    }

    public int QueueLength => 0;

    public void Enqueue(string soundFilePath)
    {
        _logger.LogInformation("[silent] would play {SoundFilePath}", soundFilePath);
    }
}
