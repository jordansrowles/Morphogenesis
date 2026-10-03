using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Rowles.Morphogenesis.Server.Configuration;
using Rowles.Morphogenesis.Server.Diagnostics;

namespace Rowles.Morphogenesis.Server.Persistence;

public sealed class SqliteWriteQueue : IHostedService, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly Channel<IWriteOperation> _operations;
    private readonly LaboratoryDiagnostics _diagnostics;
    private SqliteConnection? _connection;
    private Task? _worker;
    private int _started;
    private long _queueDepth;

    public SqliteWriteQueue(LaboratoryServerOptions options, LaboratoryDiagnostics diagnostics)
    {
        _diagnostics = diagnostics;
        _operations = Channel.CreateBounded<IWriteOperation>(
            new BoundedChannelOptions(options.ResourceLimits.SqliteWriterQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };
        _connectionString = builder.ToString();
    }

    public long QueueDepth => Interlocked.Read(ref _queueDepth);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(_connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(_connection, "PRAGMA busy_timeout = 5000;", cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(_connection, "PRAGMA synchronous = NORMAL;", cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(_connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);
        _worker = ProcessQueueAsync(_connection);
    }

    public async ValueTask<T> ExecuteAsync<T>(
        Func<SqliteConnection, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Volatile.Read(ref _started) == 0)
            throw new InvalidOperationException("The SQLite writer queue has not started.");
        ObjectDisposedException.ThrowIf(_operations.Reader.Completion.IsCompleted, this);

        WriteOperation<T> queued = new(operation);
        Interlocked.Increment(ref _queueDepth);
        try
        {
            await _operations.Writer.WriteAsync(queued, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _queueDepth);
            throw;
        }

        return await queued.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _operations.Writer.TryComplete();
        if (_worker is not null)
            await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _operations.Writer.TryComplete();
        if (_worker is not null)
            await _worker.ConfigureAwait(false);
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    private static async Task ExecutePragmaAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessQueueAsync(SqliteConnection connection)
    {
        await foreach (IWriteOperation operation in _operations.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                await operation.ExecuteAsync(connection).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _queueDepth);
                _diagnostics.RecordSqliteWrite(Stopwatch.GetElapsedTime(started));
            }
        }
    }

    private interface IWriteOperation
    {
        ValueTask ExecuteAsync(SqliteConnection connection);
    }

    private sealed class WriteOperation<T>(
        Func<SqliteConnection, CancellationToken, ValueTask<T>> operation) : IWriteOperation
    {
        internal TaskCompletionSource<T> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ExecuteAsync(SqliteConnection connection)
        {
            try
            {
                T result = await operation(connection, CancellationToken.None).ConfigureAwait(false);
                Completion.TrySetResult(result);
            }
            catch (Exception exception)
            {
                Completion.TrySetException(exception);
            }
        }
    }
}
