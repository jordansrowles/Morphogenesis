using Rowles.Morphogenesis.Desktop.Configuration;
using Xunit;

namespace Rowles.Morphogenesis.Desktop.Tests.Configuration;

public sealed class DesktopPreferencesTests
{
    [Fact]
    public void PreferencesRoundTripTheServerBaseUri()
    {
        string path = Path.Combine(Path.GetTempPath(), "rowles-desktop-preferences", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            DesktopPreferences preferences = new("http://127.0.0.1:5090");
            preferences.Save(path);

            Assert.Equal(preferences, DesktopPreferences.Load(path));
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("http://127.0.0.1:5080", true)]
    [InlineData("https://morphogenesis.example", true)]
    [InlineData("ftp://127.0.0.1:5080", false)]
    [InlineData("http://user:secret@example", false)]
    public void OnlyCredentialFreeHttpUrisAreAccepted(string value, bool expected) =>
        Assert.Equal(expected, DesktopPreferences.IsHttpUri(value));
}
