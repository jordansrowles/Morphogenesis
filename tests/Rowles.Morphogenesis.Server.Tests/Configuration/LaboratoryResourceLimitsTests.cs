using Rowles.Morphogenesis.Server.Configuration;
using Xunit;

namespace Rowles.Morphogenesis.Server.Tests.Configuration;

public sealed class LaboratoryResourceLimitsTests
{
    [Fact]
    public void DefaultsMatchTheLaboratoryHardeningContract()
    {
        LaboratoryResourceLimits limits = new();
        limits.Validate();

        Assert.Equal(512, limits.MaxGridWidth);
        Assert.Equal(512, limits.MaxGridHeight);
        Assert.Equal(262_144, limits.MaxSites);
        Assert.Equal(4, limits.MaxRunningSessions);
        Assert.Equal(8, limits.MaxPausedSessions);
        Assert.Equal(8, limits.MaxLiveSubscribersPerSession);
        Assert.Equal(10, limits.DefaultLivePublishFps);
        Assert.Equal(20, limits.MaxLivePublishFps);
        Assert.Equal(64, limits.CommandQueueCapacity);
        Assert.Equal(10, limits.FrameBufferCountPerSession);
        Assert.Equal(8, limits.RecordingWriterQueueCapacity);
        Assert.Equal(128, limits.SqliteWriterQueueCapacity);
        Assert.Equal(1_048_576, limits.MaxManifestBytes);
        Assert.Equal(2_097_152, limits.MaxRequestBodyBytes);
        Assert.Equal(2_147_483_648, limits.MaxRecordingBytesPerRun);
        Assert.Equal(21_474_836_480, limits.MaxStoredRecordingBytes);
        Assert.Equal(500, limits.MaxPersistedRuns);
        Assert.Equal(TimeSpan.FromHours(1), limits.CreatedSessionIdleTimeout);
        Assert.Equal(TimeSpan.FromHours(24), limits.PausedSessionIdleTimeout);
        Assert.Equal(TimeSpan.FromDays(30), limits.TerminalRetention);
        Assert.Equal(TimeSpan.FromHours(1), limits.RetentionSweepInterval);
        Assert.Equal(262_144, limits.GetSiteCount(512, 512));
    }

    [Fact]
    public void CheckedSiteSizingRejectsOverflowBeforeAllocation()
    {
        LaboratoryResourceLimits limits = new()
        {
            MaxGridWidth = int.MaxValue,
            MaxGridHeight = int.MaxValue,
            MaxSites = int.MaxValue
        };

        LaboratoryResourceLimitException exception = Assert.Throws<LaboratoryResourceLimitException>(
            () => limits.GetSiteCount(int.MaxValue, 2));

        Assert.Contains("overflowed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfiguredLimitsCannotExceedTheFixedHardMaximum()
    {
        LaboratoryResourceLimits limits = new() { MaxRunningSessions = 5 };
        Assert.Throws<InvalidOperationException>(limits.Validate);
    }
}
