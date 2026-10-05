namespace Naravel.Foundation;

/// <summary>
/// Thrown when a driver name is requested that has no registered factory.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>InvalidArgumentException("Driver [x] not supported.")</c> thrown by
/// <c>Illuminate\Support\Manager::driver()</c>.</para>
/// <para><b>Why a dedicated type:</b> the message and the <see cref="AvailableDrivers"/> list make
/// typos in configuration (<c>"redsi"</c>) obvious, and callers can catch this specific type.</para>
/// </remarks>
public sealed class DriverNotRegisteredException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="driverName">The name that was requested.</param>
    /// <param name="driverType">Display name of the driver contract (for the message).</param>
    /// <param name="availableDrivers">Names that are currently registered.</param>
    public DriverNotRegisteredException(string driverName, string driverType, IReadOnlyCollection<string> availableDrivers)
        : base(BuildMessage(driverName, driverType, availableDrivers))
    {
        DriverName = driverName;
        DriverType = driverType;
        AvailableDrivers = availableDrivers;
    }

    /// <summary>The driver name that was requested.</summary>
    public string DriverName { get; }

    /// <summary>Display name of the driver contract the lookup was for.</summary>
    public string DriverType { get; }

    /// <summary>Driver names that were registered at the time of the failure.</summary>
    public IReadOnlyCollection<string> AvailableDrivers { get; }

    private static string BuildMessage(string name, string type, IReadOnlyCollection<string> available) =>
        $"Driver '{name}' is not registered for {type}. Available drivers: " +
        (available.Count == 0 ? "(none)" : string.Join(", ", available)) + ".";
}
