# Zumbo.DatabaseMigrator

PostgreSQL şema değişikliklerini API başlangıcından ayrı yürüten açık migration aracıdır. `status` ve `script` komutları şema veya ledger üzerinde yazma yapmaz.

```text
dotnet run --project Backend/tools/Zumbo.DatabaseMigrator -- status
dotnet run --project Backend/tools/Zumbo.DatabaseMigrator -- apply
dotnet run --project Backend/tools/Zumbo.DatabaseMigrator -- rollback --target-version <version>
dotnet run --project Backend/tools/Zumbo.DatabaseMigrator -- script --from-version <version> --to-version <version> --idempotent
```

Bağlantı dizesi öncelik sırasıyla `--connection-string`, `ZUMBO_POSTGRES_CONNECTION_STRING` veya `ConnectionStrings__PostgreSql` ile verilir. Script varsayılan olarak stdout'a yazılır; `--output <path>` verilirse yeni bir dosya oluşturulur ve mevcut dosyanın üzerine yazılmaz.

`apply` ve `rollback` her migration'ı ayrı transaction içinde, advisory lock ve checksum doğrulamasıyla yürütür. Üretim benzeri ortamda `apply` öncesinde hedef veritabanının yedeği alınmalı, önce `status` ve `script` çıktısı incelenmelidir. `rollback --target-version N`, `N` üzerindeki migration'ları ters sırada geri alır; veri kaybı oluşturabilecek bir rollback yedeksiz çalıştırılmamalıdır.
