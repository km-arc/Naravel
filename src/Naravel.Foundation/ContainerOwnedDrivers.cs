namespace Naravel.Foundation;

/// <summary>Tracks driver instances whose lifetime has been transferred to the dependency-injection container.</summary>
/// <typeparam name="TDriver">The driver contract.</typeparam>
internal sealed class ContainerOwnedDrivers<TDriver> where TDriver : notnull
{
    private readonly object _gate = new();
    private readonly HashSet<object> _instances = new(ReferenceEqualityComparer.Instance);

    public void Mark(TDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        lock (_gate)
        {
            _instances.Add(driver);
        }
    }

    public bool Contains(TDriver driver)
    {
        lock (_gate)
        {
            return _instances.Contains(driver);
        }
    }
}
