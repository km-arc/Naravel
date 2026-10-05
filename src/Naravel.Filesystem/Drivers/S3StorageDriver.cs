namespace Naravel.Filesystem.Drivers;

using Amazon.S3;
using Amazon.S3.Model;

public sealed class S3StorageDriver : IStorageDriver, IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _serviceUrl;

    public S3StorageDriver(string key, string secret, string bucket, string? endpoint, string? region = null)
        : this(new AmazonS3Client(key, secret, CreateConfig(endpoint, region)), bucket, endpoint ?? "https://s3.amazonaws.com")
    {
    }

    internal S3StorageDriver(IAmazonS3 s3Client, string bucket, string serviceUrl = "https://s3.amazonaws.com")
    {
        ArgumentNullException.ThrowIfNull(s3Client);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUrl);
        _bucketName = bucket;
        _s3Client = s3Client;
        _serviceUrl = serviceUrl;
    }

    internal static AmazonS3Config CreateConfig(string? endpoint, string? region)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = true
        };

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            if (!string.IsNullOrWhiteSpace(region))
            {
                config.AuthenticationRegion = region;
            }

            config.ServiceURL = endpoint;
        }
        else if (!string.IsNullOrWhiteSpace(region))
        {
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);
        }
        else
        {
            config.ServiceURL = "https://s3.amazonaws.com";
        }

        return config;
    }

    public void Dispose() => _s3Client.Dispose();

    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await _s3Client.GetObjectMetadataAsync(_bucketName, path, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<byte[]> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = await GetStreamAsync(path, cancellationToken);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    public async Task<Stream> GetStreamAsync(string path, CancellationToken cancellationToken = default)
    {
        var response = await _s3Client.GetObjectAsync(_bucketName, path, cancellationToken);
        return response.ResponseStream;
    }

    public async Task PutAsync(string path, Stream contents, CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = path,
            InputStream = contents
        };
        await _s3Client.PutObjectAsync(request, cancellationToken);
    }

    public async Task PutAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(contents);
        await PutAsync(path, stream, cancellationToken);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest { BucketName = _bucketName, Key = path };
        await _s3Client.DeleteObjectAsync(request, cancellationToken);
    }

    public async Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var request = new CopyObjectRequest
        {
            SourceBucket = _bucketName,
            SourceKey = sourcePath,
            DestinationBucket = _bucketName,
            DestinationKey = destinationPath
        };
        await _s3Client.CopyObjectAsync(request, cancellationToken);
    }

    public async Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        await CopyAsync(sourcePath, destinationPath, cancellationToken);
        await DeleteAsync(sourcePath, cancellationToken);
    }

    public async Task<long> SizeAsync(string path, CancellationToken cancellationToken = default)
    {
        var metadata = await _s3Client.GetObjectMetadataAsync(_bucketName, path, cancellationToken);
        return metadata.ContentLength;
    }

    public Task<string> GetUrlAsync(string path, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expiration.HasValue)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = path,
                Expires = DateTime.UtcNow.Add(expiration.Value)
            };
            return Task.FromResult(_s3Client.GetPreSignedURL(request));
        }

        return Task.FromResult($"{_serviceUrl.TrimEnd('/')}/{_bucketName}/{path}");
    }
}