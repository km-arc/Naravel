namespace AppRootNamespace.Bootstrap;

public static class ConfigurationSetup
{
    /// <summary>
    /// Adds every <c>*.json</c> file of <paramref name="directory"/> in name order, then user secrets (Development only), then
    /// environment variables and the command line. A later source wins, so a value in the environment or in user secrets replaces
    /// the same key in a file, and no secret has to be written into a file.
    /// </summary>
    public static IConfigurationBuilder AddConfigDirectory(
        this IConfigurationBuilder configuration, string directory, bool development, string[] args)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"The configuration directory '{directory}' does not exist. Start the application from its own folder, the one that holds config/.");
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            configuration.AddJsonFile(file, optional: false, reloadOnChange: false);
        }

        if (development)
        {
            // Needs a UserSecretsId in the project file; `dotnet user-secrets init` adds one. Without it this adds nothing.
            configuration.AddUserSecrets(typeof(ConfigurationSetup).Assembly, optional: true);
        }

        return configuration.AddEnvironmentVariables().AddCommandLine(args);
    }
}
