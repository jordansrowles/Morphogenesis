using Microsoft.Extensions.Configuration;

namespace Rowles.Morphogenesis.Server.Configuration;

public sealed class LaboratoryServerOptions
{
    public string BindUrl { get; set; } = "http://0.0.0.0:5080";

    public string DataDirectory { get; set; } = "./data";

    public string LogDirectory { get; set; } = "./logs";

    public string DatabaseFileName { get; set; } = "laboratory.db";

    public string CanonicalExperimentsDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "experiments", "canonical");

    public LaboratoryResourceLimits ResourceLimits { get; set; } = new();

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
            throw new InvalidOperationException("BindUrl must be an absolute HTTP or HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(DatabaseFileName) ||
            !StringComparer.Ordinal.Equals(Path.GetFileName(DatabaseFileName), DatabaseFileName))
        {
            throw new InvalidOperationException("DatabaseFileName must be a file name without a directory component.");
        }

        ResourceLimits.Validate();
    }
}
