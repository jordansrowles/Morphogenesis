using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Data.Sqlite;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Server.Configuration;

namespace Rowles.Morphogenesis.Server.Tests.Testing;

internal sealed class TemporaryLaboratoryRoot : IDisposable
{
    internal TemporaryLaboratoryRoot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "morphogenesis-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

internal sealed class LaboratoryFactory : WebApplicationFactory<Program>
{
    internal LaboratoryFactory(
        string root,
        bool longRunning = true,
        Action<string>? mutateCatalogue = null,
        LaboratoryResourceLimits? resourceLimits = null)
    {
        Root = root;
        ResourceLimits = resourceLimits ?? new LaboratoryResourceLimits();
        DataDirectory = System.IO.Path.Combine(root, "data");
        LogDirectory = System.IO.Path.Combine(root, "logs");
        CanonicalDirectory = System.IO.Path.Combine(root, "canonical");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(CanonicalDirectory);
        if (!Directory.EnumerateFiles(CanonicalDirectory, "*.json").Any())
        {
            string source = System.IO.Path.Combine(AppContext.BaseDirectory, "experiments", "canonical");
            foreach (string path in Directory.EnumerateFiles(source, "*.json"))
            {
                string destination = System.IO.Path.Combine(CanonicalDirectory, System.IO.Path.GetFileName(path));
                File.Copy(path, destination);
            }

            if (longRunning)
            {
                string sortingPath = System.IO.Path.Combine(CanonicalDirectory, "E02-sorting.json");
                ExperimentManifest sorting = ExperimentManifest.ReadJson(sortingPath);
                File.WriteAllText(sortingPath, (sorting with { McsCount = 100_000 }).ToJson());
            }
        }

        mutateCatalogue?.Invoke(CanonicalDirectory);
    }

    internal string Root { get; }
    internal string DataDirectory { get; }
    internal string LogDirectory { get; }
    internal string CanonicalDirectory { get; }
    internal LaboratoryResourceLimits ResourceLimits { get; }
    internal string DatabasePath => System.IO.Path.Combine(DataDirectory, "laboratory.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<LaboratoryServerOptions>();
            services.AddSingleton(new LaboratoryServerOptions
            {
                BindUrl = "http://127.0.0.1:5080",
                DataDirectory = DataDirectory,
                LogDirectory = LogDirectory,
                CanonicalExperimentsDirectory = CanonicalDirectory,
                DatabaseFileName = "laboratory.db",
                ResourceLimits = ResourceLimits
            });
        });
    }
}
