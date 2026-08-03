using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{

    private sealed record Migration(long Version, string Name, string UpSql, string DownSql, string Checksum)
    {
        public PostgreSqlMigrationInfo Info => new(Version, Name, Checksum);

        public static Migration Create(long version, string name, string upSql, string downSql)
        {
            var content = $"{version}\n{name}\n{upSql}\n-- DOWN\n{downSql}";
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            return new Migration(version, name, upSql, downSql, checksum);
        }
    }
}
