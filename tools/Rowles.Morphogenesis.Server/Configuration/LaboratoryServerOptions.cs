using Microsoft.Extensions.Configuration;

namespace Rowles.Morphogenesis.Server.Configuration;

public sealed class LaboratoryServerOptions
{
    public const int DefaultMaxManifestBytes = 1024 * 1024;
    public const int DefaultMaxRequestBodyBytes = 2 * 1024 * 1024;

    public string BindUrl { get; set; } = "http://127.0.0.1:5080";

    public string DataDirectory { get; set; } = "./data";

    public string LogDirectory { get; set; } = "./logs";

    public string DatabaseFileName { get; set; } = "laboratory.db";

    public string CanonicalExperimentsDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "experiments", "canonical");

    public int MaxManifestBytes { get; set; } = DefaultMaxManifestBytes;

    public int MaxRequestBodyBytes { get; set; } = DefaultMaxRequestBodyBytes;

    public string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);

    public static LaboratoryServerOptions Load(IConfiguration configuration)
    {
        LaboratoryServerOptions options = new();
        configuration.GetSection("Laboratory").Bind(options);
        options.ResolvePaths();
        options.Validate();
        return options;
    }

    private void ResolvePaths()
    {
        DataDirectory = Path.GetFullPath(DataDirectory);
        LogDirectory = Path.GetFullPath(LogDirectory);
        CanonicalExperimentsDirectory = Path.GetFullPath(CanonicalExperimentsDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    private void Validate()
    {
        if (!Uri.TryCreate(BindUrl, UriKind.Absolute, out Uri? bindUri) ||
            bindUri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(bindUri.Host))
        {
            throw new InvalidOperationException("BindUrl must be an absolute HTTP or HTTPS URL. The default bind address is loopback.");
        }

        if (string.IsNullOrWhiteSpace(DatabaseFileName) ||
            !StringComparer.Ordinal.Equals(Path.GetFileName(DatabaseFileName), DatabaseFileName))
        {
            throw new InvalidOperationException("DatabaseFileName must be a file name without a directory component.");
        }

        if (MaxManifestBytes <= 0 || MaxRequestBodyBytes <= 0)
            throw new InvalidOperationException("Manifest and request body limits must be positive.");
    }
}
