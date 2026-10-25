using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Naravel.Encryption;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration for <c>Naravel.Encryption</c>.</summary>
public static class EncryptionServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IEncrypter"/> (singleton) and, when the application has not registered Data Protection yet, calls <c>AddDataProtection()</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> the encryption service provider and its <c>APP_KEY</c> setup.</para>
    /// <para><b>Why it exists:</b> one registration line over ASP.NET Core Data Protection. Configure the key ring yourself with
    /// <c>AddDataProtection().SetApplicationName(...).PersistKeysTo...</c>; the default key ring of a throw-away file system does not survive a redeploy.</para>
    /// <para><b>Not ported on purpose:</b> an <c>APP_KEY</c> setting and algorithm choice.</para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddNaravelEncryption(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IDataProtectionProvider)))
        {
            services.AddDataProtection();
        }

        services.TryAddSingleton<IEncrypter>(provider => new Encrypter(provider.GetRequiredService<IDataProtectionProvider>(), Encrypter.DefaultPurpose));
        return services;
    }
}
