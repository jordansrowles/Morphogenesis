namespace Rowles.Morphogenesis.Server.Configuration;

public sealed record LaboratoryResourceLimits
{
    public const int HardMaxGridWidth = 512;
    public const int HardMaxGridHeight = 512;
    public const int HardMaxSites = 262_144;
    public const int HardMaxRunningSessions = 4;
    public const int HardMaxPausedSessions = 8;
    public const int HardMaxResidentSessions = 16;
    public const int HardMaxConcurrentRecordingReconstructions = 2;
    public const int HardMaxLiveSubscribersPerSession = 8;
    public const int HardMaxLivePublishFps = 20;
    public const int HardCommandQueueCapacity = 64;
    public const int HardFrameBufferCountPerSession = 10;
    public const int HardRecordingWriterQueueCapacity = 8;
    public const int HardSqliteWriterQueueCapacity = 128;
    public const int HardMaxManifestBytes = 1_048_576;
    public const int HardMaxRequestBodyBytes = 2_097_152;
    public const long HardMaxRecordingBytesPerRun = 2_147_483_648;
    public const long HardMaxStoredRecordingBytes = 21_474_836_480;
    public const int HardMaxPersistedRuns = 500;

    public int MaxGridWidth { get; init; } = HardMaxGridWidth;
    public int MaxGridHeight { get; init; } = HardMaxGridHeight;
    public int MaxSites { get; init; } = HardMaxSites;
    public int MaxRunningSessions { get; init; } = HardMaxRunningSessions;
    public int MaxPausedSessions { get; init; } = HardMaxPausedSessions;
    public int MaxResidentSessions { get; init; } = HardMaxResidentSessions;
    public int MaxConcurrentRecordingReconstructions { get; init; } = HardMaxConcurrentRecordingReconstructions;
    public int MaxLiveSubscribersPerSession { get; init; } = HardMaxLiveSubscribersPerSession;
    public int DefaultLivePublishFps { get; init; } = 10;
    public int MaxLivePublishFps { get; init; } = HardMaxLivePublishFps;
    public int CommandQueueCapacity { get; init; } = HardCommandQueueCapacity;
    public int FrameBufferCountPerSession { get; init; } = HardFrameBufferCountPerSession;
    public int RecordingWriterQueueCapacity { get; init; } = HardRecordingWriterQueueCapacity;
    public int SqliteWriterQueueCapacity { get; init; } = HardSqliteWriterQueueCapacity;
    public int MaxManifestBytes { get; init; } = HardMaxManifestBytes;
    public int MaxRequestBodyBytes { get; init; } = HardMaxRequestBodyBytes;
    public long MaxRecordingBytesPerRun { get; init; } = HardMaxRecordingBytesPerRun;
    public long MaxStoredRecordingBytes { get; init; } = HardMaxStoredRecordingBytes;
    public int MaxPersistedRuns { get; init; } = HardMaxPersistedRuns;
    public TimeSpan CreatedSessionIdleTimeout { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan PausedSessionIdleTimeout { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan TerminalRetention { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan RetentionSweepInterval { get; init; } = TimeSpan.FromHours(1);

    public int GetSiteCount(int width, int height)
    {
        if (width <= 0 || width > MaxGridWidth || height <= 0 || height > MaxGridHeight)
            throw new LaboratoryResourceLimitException("Grid dimensions exceed the configured interactive limits.");

        int siteCount;
        try
        {
            siteCount = checked(width * height);
        }
        catch (OverflowException exception)
        {
            throw new LaboratoryResourceLimitException("Grid site count overflowed the supported range.", exception);
        }

        if (siteCount > MaxSites)
            throw new LaboratoryResourceLimitException("Grid site count exceeds the configured interactive limit.");

        return siteCount;
    }

    public void Validate()
    {
        ValidateBounded(MaxGridWidth, HardMaxGridWidth, nameof(MaxGridWidth));
        ValidateBounded(MaxGridHeight, HardMaxGridHeight, nameof(MaxGridHeight));
        ValidateBounded(MaxSites, HardMaxSites, nameof(MaxSites));
        ValidateBounded(MaxRunningSessions, HardMaxRunningSessions, nameof(MaxRunningSessions));
        ValidateBounded(MaxPausedSessions, HardMaxPausedSessions, nameof(MaxPausedSessions));
        ValidateBounded(MaxResidentSessions, HardMaxResidentSessions, nameof(MaxResidentSessions));
        ValidateBounded(MaxConcurrentRecordingReconstructions, HardMaxConcurrentRecordingReconstructions, nameof(MaxConcurrentRecordingReconstructions));
        ValidateFixed(MaxLiveSubscribersPerSession, HardMaxLiveSubscribersPerSession, nameof(MaxLiveSubscribersPerSession));
        ValidateBounded(MaxLivePublishFps, HardMaxLivePublishFps, nameof(MaxLivePublishFps));
        ValidateFixed(CommandQueueCapacity, HardCommandQueueCapacity, nameof(CommandQueueCapacity));
        ValidateFixed(FrameBufferCountPerSession, HardFrameBufferCountPerSession, nameof(FrameBufferCountPerSession));
        ValidateFixed(RecordingWriterQueueCapacity, HardRecordingWriterQueueCapacity, nameof(RecordingWriterQueueCapacity));
        ValidateFixed(SqliteWriterQueueCapacity, HardSqliteWriterQueueCapacity, nameof(SqliteWriterQueueCapacity));
        ValidateBounded(MaxManifestBytes, HardMaxManifestBytes, nameof(MaxManifestBytes));
        ValidateBounded(MaxRequestBodyBytes, HardMaxRequestBodyBytes, nameof(MaxRequestBodyBytes));
        ValidateBounded(MaxRecordingBytesPerRun, HardMaxRecordingBytesPerRun, nameof(MaxRecordingBytesPerRun));
        ValidateBounded(MaxStoredRecordingBytes, HardMaxStoredRecordingBytes, nameof(MaxStoredRecordingBytes));
        ValidateBounded(MaxPersistedRuns, HardMaxPersistedRuns, nameof(MaxPersistedRuns));
        ValidateBounded(DefaultLivePublishFps, MaxLivePublishFps, nameof(DefaultLivePublishFps));

        if (CreatedSessionIdleTimeout <= TimeSpan.Zero || PausedSessionIdleTimeout <= TimeSpan.Zero ||
            TerminalRetention <= TimeSpan.Zero || RetentionSweepInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Laboratory retention intervals must be positive.");
        }
    }

    private static void ValidateBounded(long value, long hardMaximum, string name)
    {
        if (value <= 0 || value > hardMaximum)
            throw new InvalidOperationException($"{name} must be between 1 and its hard maximum ({hardMaximum}).");
    }

    private static void ValidateFixed(long value, long expected, string name)
    {
        if (value != expected)
            throw new InvalidOperationException($"{name} is fixed at {expected}.");
    }
}

public sealed class LaboratoryResourceLimitException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
