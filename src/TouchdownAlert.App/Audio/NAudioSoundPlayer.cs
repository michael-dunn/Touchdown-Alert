using System.Threading.Channels;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;

namespace TouchdownAlert.App.Audio;

/// <summary>
/// Plays sound files strictly one at a time via NAudio. A single background task consumes a
/// channel so playback is serialized FIFO; a failure on one file is logged and does not stop
/// the loop from processing the next.
/// </summary>
public sealed class NAudioSoundPlayer : ISoundPlayer, IDisposable
{
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
    private readonly ILogger<NAudioSoundPlayer> _logger;
    private readonly IOptionsMonitor<SoundOptions> _options;
    private readonly Task _worker;
    private int _queueLength;

    public NAudioSoundPlayer(ILogger<NAudioSoundPlayer> logger, IOptionsMonitor<SoundOptions> options)
    {
        _logger = logger;
        _options = options;
        _worker = Task.Factory.StartNew(RunAsync, TaskCreationOptions.LongRunning).Unwrap();
    }

    public int QueueLength => _queueLength;

    public void Enqueue(string soundFilePath)
    {
        Interlocked.Increment(ref _queueLength);
        if (!_queue.Writer.TryWrite(soundFilePath))
        {
            Interlocked.Decrement(ref _queueLength);
        }
    }

    private async Task RunAsync()
    {
        await foreach (var path in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await PlayOneAsync(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to play sound {SoundFilePath}", path);
            }
            finally
            {
                Interlocked.Decrement(ref _queueLength);
            }
        }
    }

    private Task PlayOneAsync(string path)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Intentionally not `using` here: disposal is deferred to AwaitAndCleanup, which runs
        // after playback finishes (PlaybackStopped fires asynchronously on NAudio's own thread).
        var reader = new AudioFileReader(path)
        {
            Volume = Math.Clamp(_options.CurrentValue.Volume, 0f, 1f),
        };
        // NAudio 3.x WaveOut is the event-driven player (formerly WaveOutEvent), safe in a console host.
        var output = new WaveOut();
        _logger.LogInformation("Playing {SoundFilePath}", path);

        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is not null)
            {
                tcs.TrySetException(e.Exception);
            }
            else
            {
                tcs.TrySetResult();
            }
        };

        output.Init(reader);
        output.Play();
        return AwaitAndCleanup(tcs.Task, output, reader);
    }

    private static async Task AwaitAndCleanup(Task playbackTask, WaveOut output, AudioFileReader reader)
    {
        try
        {
            await playbackTask;
        }
        finally
        {
            output.Dispose();
            reader.Dispose();
        }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
    }
}
