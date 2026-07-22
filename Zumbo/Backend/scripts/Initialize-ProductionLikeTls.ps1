[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\.production-like\tls'),
    [ValidateRange(1, 90)]
    [int]$ValidityDays = 30,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $backendRoot '.production-like'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$separator = [IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($allowedRoot + $separator, [StringComparison]::OrdinalIgnoreCase)) {
    throw "TLS output must remain below $allowedRoot."
}

if ($Clean) {
    if (Test-Path -LiteralPath $output) {
        $resolved = (Resolve-Path -LiteralPath $output).Path
        if ($resolved -ne $output -or -not $resolved.StartsWith($allowedRoot + $separator, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove TLS output outside $allowedRoot."
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    Write-Host "ProductionLike TLS material removed from $output."
    return
}

$pfxPassword = [Environment]::GetEnvironmentVariable('ZUMBO_TLS_PFX_PASSWORD')
if ([string]::IsNullOrWhiteSpace($pfxPassword) -or $pfxPassword.Length -lt 16 -or $pfxPassword -match 'replace-with') {
    throw 'ZUMBO_TLS_PFX_PASSWORD must be a non-placeholder value with at least 16 characters.'
}

if (Test-Path -LiteralPath $output) {
    $resolved = (Resolve-Path -LiteralPath $output).Path
    if (-not $resolved.StartsWith($allowedRoot + $separator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove TLS output outside $allowedRoot."
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

$image = 'alpine/openssl:3.5.1@sha256:6bbe4017ac088ed0eb07264ef9d6ff1364bad0bffdc142a195cb0e84fb0cbab1'
$script = @'
set -eu
umask 077
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out /out/ca.key 2>/dev/null
openssl req -x509 -new -sha256 -key /out/ca.key -days "$VALIDITY_DAYS" \
  -subj "/O=Zumbo/CN=Zumbo ProductionLike Local CA" -out /out/ca.crt

for name in api gateway mongo redis minio opensearch; do
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "/out/${name}.key" 2>/dev/null
  openssl req -new -sha256 -key "/out/${name}.key" -subj "/O=Zumbo/CN=${name}" -out "/out/${name}.csr"
  cat > "/tmp/${name}.ext" <<EOF
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:${name},DNS:localhost,IP:127.0.0.1
EOF
  openssl x509 -req -sha256 -in "/out/${name}.csr" -CA /out/ca.crt -CAkey /out/ca.key \
    -CAcreateserial -days "$VALIDITY_DAYS" -extfile "/tmp/${name}.ext" -out "/out/${name}.crt" 2>/dev/null
done

openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out /out/opensearch-admin.key 2>/dev/null
openssl req -new -sha256 -key /out/opensearch-admin.key \
  -subj "/O=Zumbo/CN=zumbo-admin" -out /out/opensearch-admin.csr
cat > /tmp/admin.ext <<EOF
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=clientAuth
EOF
openssl x509 -req -sha256 -in /out/opensearch-admin.csr -CA /out/ca.crt -CAkey /out/ca.key \
  -CAcreateserial -days "$VALIDITY_DAYS" -extfile /tmp/admin.ext -out /out/opensearch-admin.crt 2>/dev/null

cat /out/mongo.crt /out/mongo.key > /out/mongo.pem
openssl pkcs12 -export -out /out/api.pfx -inkey /out/api.key -in /out/api.crt \
  -certfile /out/ca.crt -passout env:PFX_PASSWORD
openssl pkcs12 -export -out /out/gateway.pfx -inkey /out/gateway.key -in /out/gateway.crt \
  -certfile /out/ca.crt -passout env:PFX_PASSWORD
rm -f /out/*.csr /out/ca.srl /out/ca.key
cd /out
sha256sum ca.crt api.crt gateway.crt mongo.crt redis.crt minio.crt opensearch.crt > certificate-manifest.sha256
'@
$generatorPath = Join-Path $output 'generate.sh'
[IO.File]::WriteAllText($generatorPath, $script, [Text.UTF8Encoding]::new($false))

& docker run --rm --user '0:0' --entrypoint /bin/sh `
    -e "PFX_PASSWORD=$pfxPassword" -e "VALIDITY_DAYS=$ValidityDays" `
    -v "${output}:/out" $image /out/generate.sh
if ($LASTEXITCODE -ne 0) {
    throw "TLS bootstrap container failed with exit code $LASTEXITCODE."
}
Remove-Item -LiteralPath $generatorPath -Force

$required = @(
    'ca.crt', 'api.pfx', 'gateway.pfx', 'mongo.pem', 'redis.crt', 'redis.key',
    'minio.crt', 'minio.key', 'opensearch.crt', 'opensearch.key',
    'opensearch-admin.crt', 'opensearch-admin.key', 'certificate-manifest.sha256'
)
$missing = $required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $output $_)) }
if ($missing.Count -gt 0) {
    throw "TLS bootstrap did not create required files: $($missing -join ', ')."
}
Write-Host "ProductionLike TLS material created below $output. This directory is git-ignored and task-local."
