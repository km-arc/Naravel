using Microsoft.Extensions.Configuration;
using NaravelApp.Bootstrap;

namespace NaravelApp.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Config_files_are_read_from_the_directory()
    {
        var value = Read("""{ "Demo": { "Value": "from the file" } }""", environment: null);

        Assert.Equal("from the file", value);
    }

    [Fact]
    public void Environment_variables_override_config_files()
    {
        var value = Read("""{ "Demo": { "Value": "from the file" } }""", environment: "from the environment");

        Assert.Equal("from the environment", value);
    }

    // Writes demo.json to a new folder, loads that folder the way Program.cs does, and returns Demo:Value. The environment variable
    // Demo__Value is set only while the configuration is built, so one test cannot leak into another.
    private static string? Read(string json, string? environment)
    {
        var directory = Directory.CreateTempSubdirectory("naravel-config-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "demo.json"), json);
            Environment.SetEnvironmentVariable("Demo__Value", environment);

            var configuration = new ConfigurationBuilder().AddConfigDirectory(directory, development: false, args: []).Build();
            return configuration["Demo:Value"];
        }
        finally
        {
            Environment.SetEnvironmentVariable("Demo__Value", null);
            Directory.Delete(directory, recursive: true);
        }
    }
}
