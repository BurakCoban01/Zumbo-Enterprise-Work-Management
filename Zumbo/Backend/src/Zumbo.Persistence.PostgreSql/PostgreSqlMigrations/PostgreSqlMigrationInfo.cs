using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed record PostgreSqlMigrationInfo(long Version, string Name, string Checksum);
