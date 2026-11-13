using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ashana.Infrastructure.Persistence;

/// <summary>
/// Keeps the state on disk as one JSON file (the <c>File</c> storage provider).
/// Writes are debounced (one write per burst of changes) and atomic: the file is written
/// to a temporary name and then moved over the old one, so a crash never leaves half a file.
/// Because the state is immutable, saving needs no lock: the snapshot cannot change while it is written.
/// </summary>
public sealed class FileSnapshotStore : IHostedService, IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

    private readonly InMemoryStateStore _store;
    private readonly string _path;
    private readonly ILogger<FileSnapshotStore> _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private Timer? _timer;
    private long _savedRevision = -1;

    public FileSnapshotStore(InMemoryStateStore store, string path, ILogger<FileSnapshotStore> logger)
    {
        _store = store;
        _path = Path.GetFullPath(path);
        _logger = logger;
    }

    public string FilePath => _path;

    /// <summary>Loads the file if it exists. Called before the app starts serving requests.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            _logger.LogInformation("No data file at {Path}; starting empty", _path);
            return;
        }

        await using var stream = File.OpenRead(_path);
        _store.Replace(await StateSerializer.ReadAsync(stream, cancellationToken).ConfigureAwait(false));
        _savedRevision = _store.Revision;
        _logger.LogInformation("Loaded data file {Path}", _path);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(_ => _ = SaveAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _store.Committed += OnCommitted;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _store.Committed -= OnCommitted;
        await SaveAsync().ConfigureAwait(false); // flush on shutdown
    }

    /// <summary>Writes the current state now if it changed since the last write.</summary>
    public async Task SaveAsync()
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var revision = _store.Revision;
            if (revision == _savedRevision && File.Exists(_path))
            {
                return;
            }

            var snapshot = _store.Current;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await using (var stream = File.Create(temp))
            {
                await StateSerializer.WriteAsync(snapshot, stream).ConfigureAwait(false);
            }

            File.Move(temp, _path, overwrite: true);
            _savedRevision = revision;
        }
#pragma warning disable CA1031 // A failed write (disk full, file locked) must not crash the app; the next change retries.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Could not save data file {Path}", _path);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _writeGate.Dispose();
    }

    private void OnCommitted() => _timer?.Change(Debounce, Timeout.InfiniteTimeSpan);
}
