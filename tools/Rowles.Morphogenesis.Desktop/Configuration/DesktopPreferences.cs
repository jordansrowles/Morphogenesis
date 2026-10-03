using System.Text.Json;

namespace Rowles.Morphogenesis.Desktop.Configuration;

public sealed record DesktopPreferences(string ServerBaseUri)
{
    public const string DefaultServerBaseUri = "http://127.0.0.1:5080";

    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static DesktopPreferences LoadDefault() => Load(GetDefaultPath());

    public static DesktopPreferences Load(string path)
    {
        if (!File.Exists(path))
            return new DesktopPreferences(DefaultServerBaseUri);

        try
        {
            DesktopPreferences? preferences = JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllBytes(path), _serializerOptions);
            return preferences is not null && IsHttpUri(preferences.ServerBaseUri)
                ? preferences
                : new DesktopPreferences(DefaultServerBaseUri);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new DesktopPreferences(DefaultServerBaseUri);
        }
    }

    public void SaveDefault() => Save(GetDefaultPath());

    public void Save(string path)
    {
        if (!IsHttpUri(ServerBaseUri))
            throw new ArgumentException("The server base URI must be an absolute HTTP or HTTPS URI.", nameof(ServerBaseUri));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(this, _serializerOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static bool IsHttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https" &&
        string.IsNullOrEmpty(uri.UserInfo);

    public static string GetDefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rowles.Morphogenesis",
        "desktop-preferences.json");
}
