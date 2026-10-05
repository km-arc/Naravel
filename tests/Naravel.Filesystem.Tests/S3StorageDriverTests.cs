using System.Reflection;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Naravel.Filesystem.Drivers;

namespace Naravel.Filesystem.Tests;

public sealed class S3StorageDriverTests
{
    [Fact]
    public void Configured_region_is_applied_to_the_s3_client_configuration()
    {
        var config = S3StorageDriver.CreateConfig("https://s3.example.test", "us-east-1");

        config.ServiceURL.Should().Be("https://s3.example.test/");
        config.ForcePathStyle.Should().BeTrue();
        config.AuthenticationRegion.Should().Be("us-east-1");
    }

    [Fact]
    public void Region_selects_the_standard_aws_endpoint_when_no_custom_endpoint_is_configured()
    {
        var config = S3StorageDriver.CreateConfig(endpoint: null, "us-west-2");

        config.RegionEndpoint.SystemName.Should().Be("us-west-2");
        config.ServiceURL.Should().BeNull();
    }

    [Fact]
    public void Driver_disposes_its_owned_s3_client()
    {
        var client = DispatchProxy.Create<IAmazonS3, DisposeTrackingS3Client>();
        var trackingClient = (DisposeTrackingS3Client)(object)client;
        var driver = new S3StorageDriver(client, "test-bucket");

        driver.Dispose();

        trackingClient.WasDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Non_expiring_url_uses_the_configured_service_url()
    {
        var client = DispatchProxy.Create<IAmazonS3, DisposeTrackingS3Client>();
        var driver = new S3StorageDriver(client, "test-bucket", "https://s3.example.test");

        var url = await driver.GetUrlAsync("folder/file.txt");

        url.Should().Be("https://s3.example.test/test-bucket/folder/file.txt");
    }

    [Fact]
    public async Task Expiring_url_is_created_from_the_expected_bucket_and_key()
    {
        var client = DispatchProxy.Create<IAmazonS3, DisposeTrackingS3Client>();
        var trackingClient = (DisposeTrackingS3Client)(object)client;
        var driver = new S3StorageDriver(client, "test-bucket", "https://s3.example.test");

        var url = await driver.GetUrlAsync("folder/file.txt", TimeSpan.FromMinutes(5));

        url.Should().Be("https://signed.example.test/object");
        trackingClient.LastPresignedRequest.Should().NotBeNull();
        trackingClient.LastPresignedRequest!.BucketName.Should().Be("test-bucket");
        trackingClient.LastPresignedRequest.Key.Should().Be("folder/file.txt");
        trackingClient.LastPresignedRequest.Expires.Should().BeAfter(DateTime.UtcNow.AddMinutes(4));
    }

    public class DisposeTrackingS3Client : DispatchProxy
    {
        public bool WasDisposed { get; private set; }
        public GetPreSignedUrlRequest? LastPresignedRequest { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDisposable.Dispose))
            {
                WasDisposed = true;
                return null;
            }

            if (targetMethod?.Name == nameof(IAmazonS3.GetPreSignedURL) && args?[0] is GetPreSignedUrlRequest request)
            {
                LastPresignedRequest = request;
                return "https://signed.example.test/object";
            }

            if (targetMethod?.ReturnType == typeof(Task)) return Task.CompletedTask;
            throw new NotSupportedException($"Unexpected S3 client call: {targetMethod?.Name}");
        }
    }
}