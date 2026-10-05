namespace Naravel.Filesystem;

/// <summary>Settings bound from one named filesystem store.</summary>
public class DiskOptions
{
    /// <summary>The registered driver name, such as <c>local</c> or <c>s3</c>.</summary>
    public string Driver { get; set; } = "local";
    /// <summary>The local filesystem root.</summary>
    public string? Root { get; set; }
    /// <summary>The base URL used by the local driver.</summary>
    public string? BaseUrl { get; set; }
    /// <summary>The S3 access key.</summary>
    public string? Key { get; set; }
    /// <summary>The S3 secret key.</summary>
    public string? Secret { get; set; }
    /// <summary>The AWS region system name for an S3 store.</summary>
    public string? Region { get; set; }
    /// <summary>The S3 bucket name.</summary>
    public string? Bucket { get; set; }
    /// <summary>An optional S3-compatible service endpoint.</summary>
    public string? Endpoint { get; set; }
}
