using System.Text;
using Amazon.S3;
using Naravel.Filesystem.Drivers;
using Naravel.Filesystem.Testing;
using Naravel.Testing;

namespace Naravel.Filesystem.Tests;

/// <summary>
/// The behaviour every <see cref="IStorageDriver"/> must share (AUDIT D-22). Only what the local disk, the in-memory
/// fake and S3 can all do is asserted here: the kind of exception for a missing file, URL shape and path rules stay
/// in each driver's own tests.
/// </summary>
internal static class StorageDriverContract
{
    public static async Task AssertAsync(IStorageDriver driver)
    {
        var prefix = "contract-" + Guid.NewGuid().ToString("N") + "/";

        // Put and read back: bytes, stream, empty and binary content.
        var text = Encoding.UTF8.GetBytes("storage contract");
        await driver.PutAsync(prefix + "docs/readme.txt", text);
        (await driver.ExistsAsync(prefix + "docs/readme.txt")).Should().BeTrue();
        (await driver.GetAsync(prefix + "docs/readme.txt")).Should().Equal(text);
        (await driver.SizeAsync(prefix + "docs/readme.txt")).Should().Be(text.Length);
        await using (var stream = await driver.GetStreamAsync(prefix + "docs/readme.txt"))
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            copy.ToArray().Should().Equal(text);
        }

        var binary = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        await driver.PutAsync(prefix + "binary.bin", new MemoryStream(binary));
        (await driver.GetAsync(prefix + "binary.bin")).Should().Equal(binary, "every byte value must survive a round trip");

        await driver.PutAsync(prefix + "empty.txt", Array.Empty<byte>());
        (await driver.ExistsAsync(prefix + "empty.txt")).Should().BeTrue();
        (await driver.SizeAsync(prefix + "empty.txt")).Should().Be(0);

        // A second put replaces the first.
        await driver.PutAsync(prefix + "docs/readme.txt", "second"u8.ToArray());
        (await driver.GetAsync(prefix + "docs/readme.txt")).Should().Equal("second"u8.ToArray());

        // The URL names the path.
        (await driver.GetUrlAsync(prefix + "docs/readme.txt")).Should().EndWith(prefix + "docs/readme.txt");

        // Copy keeps the source and replaces an existing destination.
        await driver.PutAsync(prefix + "source.txt", "payload"u8.ToArray());
        await driver.PutAsync(prefix + "copies/target.txt", "old"u8.ToArray());
        await driver.CopyAsync(prefix + "source.txt", prefix + "copies/target.txt");
        (await driver.GetAsync(prefix + "source.txt")).Should().Equal("payload"u8.ToArray());
        (await driver.GetAsync(prefix + "copies/target.txt")).Should().Equal("payload"u8.ToArray());

        // Move removes the source and replaces an existing destination.
        await driver.PutAsync(prefix + "archive/moved.txt", "old"u8.ToArray());
        await driver.MoveAsync(prefix + "copies/target.txt", prefix + "archive/moved.txt");
        (await driver.ExistsAsync(prefix + "copies/target.txt")).Should().BeFalse();
        (await driver.GetAsync(prefix + "archive/moved.txt")).Should().Equal("payload"u8.ToArray());

        // Delete removes the file, and deleting a missing file is not an error.
        await driver.DeleteAsync(prefix + "archive/moved.txt");
        (await driver.ExistsAsync(prefix + "archive/moved.txt")).Should().BeFalse();
        await driver.DeleteAsync(prefix + "archive/moved.txt");
        await driver.DeleteAsync(prefix + "never-existed.txt");

        // A missing file is reported by an exception on every read-like call.
        (await driver.ExistsAsync(prefix + "missing.txt")).Should().BeFalse();
        await FluentActions.Awaiting(() => driver.GetAsync(prefix + "missing.txt")).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => driver.GetStreamAsync(prefix + "missing.txt")).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => driver.SizeAsync(prefix + "missing.txt")).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => driver.CopyAsync(prefix + "missing.txt", prefix + "copy-of-missing.txt")).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => driver.MoveAsync(prefix + "missing.txt", prefix + "moved-missing.txt")).Should().ThrowAsync<Exception>();
        (await driver.ExistsAsync(prefix + "copy-of-missing.txt")).Should().BeFalse();
        (await driver.ExistsAsync(prefix + "moved-missing.txt")).Should().BeFalse();
    }
}

public sealed class LocalStorageDriverSharedContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "naravel-fs-shared-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Shared_contract() =>
        await StorageDriverContract.AssertAsync(new LocalStorageDriver(_root, "/files"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

public sealed class StorageFakeSharedContractTests
{
    [Fact]
    public async Task Shared_contract() =>
        await StorageDriverContract.AssertAsync(new StorageFake());
}

/// <summary>
/// Runs the shared contract against a real S3-compatible service. Set <c>NARAVEL_TEST_S3</c> to
/// <c>endpoint;accessKey;secretKey;bucket</c> (for example <c>http://localhost:9000;key;secret;naravel-test</c>);
/// the bucket is created when it does not exist. Skipped otherwise.
/// </summary>
public sealed class S3StorageDriverSharedContractTests
{
    [ServiceFact("NARAVEL_TEST_S3")]
    public async Task Shared_contract()
    {
        var parts = Environment.GetEnvironmentVariable("NARAVEL_TEST_S3")!.Split(';');
        parts.Should().HaveCount(4, "NARAVEL_TEST_S3 must be endpoint;accessKey;secretKey;bucket");
        var (endpoint, key, secret, bucket) = (parts[0], parts[1], parts[2], parts[3]);

        using (var admin = new AmazonS3Client(key, secret, S3StorageDriver.CreateConfig(endpoint, region: null)))
        {
            try
            {
                await admin.PutBucketAsync(bucket);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
                // The bucket is already there.
            }
        }

        using var driver = new S3StorageDriver(key, secret, bucket, endpoint);
        await StorageDriverContract.AssertAsync(driver);
    }
}
