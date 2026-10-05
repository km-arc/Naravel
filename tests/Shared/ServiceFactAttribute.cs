using Xunit;

namespace Naravel.Testing;

public sealed class ServiceFactAttribute : FactAttribute
{
    public ServiceFactAttribute(string environmentVariable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable)))
        {
            Skip = $"Set {environmentVariable} to run this service integration test.";
        }
    }
}