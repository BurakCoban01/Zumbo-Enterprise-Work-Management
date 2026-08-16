using System.Runtime.CompilerServices;
using MongoDB.Bson.Serialization;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Infrastructure.Persistence.MongoDb;

/// <summary>
/// Document-scoped BSON mapping for WorkItemRecurrenceOccurrenceDocument.
///
/// The default MongoDB .NET driver serializes DateTimeOffset as a BSON array
/// [UtcTicks, OffsetMinutes]. That legacy representation made the compound unique index
/// ux_workitem_recurrence_occurrence_schedule (RecurrenceId, ScheduledForUtc) MULTIKEY:
/// every UTC occurrence contributed an extra (RecurrenceId, 0) index entry, so a second
/// occurrence for the same recurrence always failed with E11000 on the offset element.
///
/// This registration keeps the whole document mapping at its default conventions but
/// replaces ONLY the ScheduledForUtc member serializer with a scalar Int64 UTC ticks
/// serializer, so NEW documents contribute exactly one (RecurrenceId, UtcTicks) index
/// entry. Legacy array documents remain readable (the scalar serializer also reads the
/// legacy array form).
///
/// The live unique index and existing documents are NOT modified.
/// </summary>
public static class WorkItemRecurrenceOccurrenceBsonConfiguration
{
    private static readonly object SyncRoot = new();
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_registered)
            {
                return;
            }

            if (!BsonClassMap.IsClassMapRegistered(typeof(WorkItemRecurrenceOccurrenceDocument)))
            {
                BsonClassMap.RegisterClassMap<WorkItemRecurrenceOccurrenceDocument>(classMap =>
                {
                    classMap.AutoMap();
                    classMap.GetMemberMap(nameof(WorkItemRecurrenceOccurrenceDocument.ScheduledForUtc))
                        ?.SetSerializer(ScalarUtcTicksDateTimeOffsetSerializer.Instance);
                });
            }
            else
            {
                var classMap = BsonClassMap.LookupClassMap(typeof(WorkItemRecurrenceOccurrenceDocument));
                try
                {
                    classMap.GetMemberMap(nameof(WorkItemRecurrenceOccurrenceDocument.ScheduledForUtc))
                        ?.SetSerializer(ScalarUtcTicksDateTimeOffsetSerializer.Instance);
                }
                catch (InvalidOperationException)
                {
                    // The member map was already frozen in this process (another test/role
                    // serialized the type before registration). Registration is best-effort
                    // and must never break the host; the API entry point always registers
                    // before any Mongo usage.
                }
            }

            _registered = true;
        }
    }

    [ModuleInitializer]
    internal static void ModuleInitialize()
    {
        EnsureRegistered();
    }
}
