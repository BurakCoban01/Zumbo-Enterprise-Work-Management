using System.Runtime.CompilerServices;
using Zumbo.Api.Infrastructure.Persistence.MongoDb;

namespace Zumbo.PersistenceIntegrationTests;

/// <summary>
/// Registers the occurrence schedule BSON mapping before ANY test in this assembly
/// executes. Test classes run in parallel within the same process, so relying on the
/// API entry-point initializer alone is not sufficient here.
/// </summary>
public static class ZumboIntegrationTestBsonRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        WorkItemRecurrenceOccurrenceBsonConfiguration.EnsureRegistered();
    }
}
