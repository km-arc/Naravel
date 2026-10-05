using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Serialization;

namespace Naravel.Queue.Tests;

public partial class JobTypeRegistryTests
{
    [Fact]
    public void Explicit_alias_resolves_and_assembly_qualified_names_are_not_accepted()
    {
        var services = new ServiceCollection();
        services.AddJob<AliasedTestJob>("stable-test-job");
        services.AddQueue(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IJobSerializer>();

        serializer.GetTypeName(typeof(AliasedTestJob)).Should().Be("stable-test-job");
        serializer.ResolveType("stable-test-job").Should().Be(typeof(AliasedTestJob));
        var act = () => serializer.ResolveType(typeof(AliasedTestJob).AssemblyQualifiedName!);
        act.Should().Throw<UnknownJobTypeException>().Which.Name.Should().Be(typeof(AliasedTestJob).AssemblyQualifiedName);
    }

    [Fact]
    public void Assembly_registration_uses_attribute_and_full_name_aliases()
    {
        var services = new ServiceCollection();
        services.AddJobsFromAssembly(typeof(AliasedTestJob).Assembly);
        services.AddQueue(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IJobSerializer>();

        serializer.GetTypeName(typeof(AliasedTestJob)).Should().Be("stable-attribute-job");
        serializer.GetTypeName(typeof(SourceGeneratedTestJob)).Should().Be(typeof(SourceGeneratedTestJob).FullName);
    }

    [Fact]
    public void Source_generated_metadata_round_trips_a_registered_job()
    {
        var services = new ServiceCollection();
        services.AddJob(SourceGeneratedTestJobJsonContext.Default.SourceGeneratedTestJob);
        services.AddQueue(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IJobSerializer>();
        var job = new SourceGeneratedTestJob { Value = "source-generated" };

        var alias = serializer.GetTypeName(typeof(SourceGeneratedTestJob));
        var payload = serializer.Serialize(job);
        var restored = (SourceGeneratedTestJob)serializer.Deserialize(payload, serializer.ResolveType(alias));

        restored.Value.Should().Be("source-generated");
    }

    [Fact]
    public void Registry_rejects_types_that_do_not_implement_IJob()
    {
        var services = new ServiceCollection();
        services.AddJob<AliasedTestJob>();
        services.AddQueue(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IJobTypeRegistry>().Register(typeof(string));

        act.Should().Throw<ArgumentException>();
    }

    [Naravel.Queue.Serialization.Job("stable-attribute-job")]
    public class AliasedTestJob : Job
    {
        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class SourceGeneratedTestJob : Job
    {
        public string Value { get; set; } = "";

        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [JsonSerializable(typeof(SourceGeneratedTestJob))]
    public partial class SourceGeneratedTestJobJsonContext : JsonSerializerContext;
}