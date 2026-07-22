[CmdletBinding()]
param(
    [string]$ProjectName = "zumbo-production-like",
    [switch]$Runtime,
    [switch]$IncludeSecurityAv,
    [string]$TlsCaPath,
    [string]$EvidencePath
)

$ErrorActionPreference = "Stop"
$backendRoot = Split-Path -Parent $PSScriptRoot
$composeFiles = @(
    (Join-Path $backendRoot "docker-compose.yml"),
    (Join-Path $backendRoot "docker-compose.hardened.yml"),
    (Join-Path $backendRoot "docker-compose.production-like.yml")
)
$checks = [System.Collections.Generic.List[object]]::new()

function Assert-Contract {
    param([bool]$Condition, [string]$Name, [string]$Detail)

    $checks.Add([ordered]@{ name = $Name; passed = $Condition; detail = $Detail })
    if (-not $Condition) {
        throw "Container contract failed: $Name ($Detail)"
    }
}

function Get-PropertyValue {
    param([object]$Object, [string]$Name)

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

$requiredEnvironment = [ordered]@{
    ZUMBO_API_IMAGE = "example.invalid/zumbo-api@sha256:$('a' * 64)"
    ZUMBO_GATEWAY_IMAGE = "example.invalid/zumbo-gateway@sha256:$('b' * 64)"
    ZUMBO_JWT_SIGNING_KEY = "contract-only-signing-key-with-at-least-sixty-four-characters-123456789"
    ZUMBO_MINIO_ROOT_USER = "contract_minio"
    ZUMBO_MINIO_ROOT_PASSWORD = "contract-only-minio-password"
    ZUMBO_TLS_PFX_PASSWORD = "contract-only-pfx-password"
    ZUMBO_MONGO_REPLICA_KEY = "contract-only-replica-key-12345678901234567890"
    ZUMBO_MONGO_ROOT_USERNAME = "contract_root"
    ZUMBO_MONGO_ROOT_PASSWORD = "contract-only-mongo-password"
    ZUMBO_REDIS_PASSWORD = "contract-only-redis-password"
    ZUMBO_OPENSEARCH_ADMIN_PASSWORD = "contract-only-opensearch-password"
}

foreach ($entry in $requiredEnvironment.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($entry.Key, "Process"))) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
    }
}

$composeArguments = @("compose", "--profile", "security-av")
foreach ($file in $composeFiles) {
    $composeArguments += @("-f", $file)
}
$composeArguments += @("config", "--format", "json")
$configOutput = & docker @composeArguments 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Compose config failed: $($configOutput -join [Environment]::NewLine)"
}
$config = ($configOutput -join [Environment]::NewLine) | ConvertFrom-Json

$digestPattern = '@sha256:[0-9a-f]{64}$'
foreach ($serviceProperty in $config.services.PSObject.Properties) {
    $image = [string](Get-PropertyValue $serviceProperty.Value "image")
    Assert-Contract ($image -match $digestPattern) "image.$($serviceProperty.Name).digest" $image
}

foreach ($dockerfile in @(
    (Join-Path $backendRoot "src/Zumbo.Api/Dockerfile"),
    (Join-Path $backendRoot "src/Zumbo.Gateway/Dockerfile")
)) {
    $fromLines = Get-Content -LiteralPath $dockerfile | Where-Object { $_ -match '^FROM\s+' }
    Assert-Contract ($fromLines.Count -gt 0) "dockerfile.$([IO.Path]::GetFileName((Split-Path $dockerfile -Parent))).from" "FROM lines exist"
    $stageAliases = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in $fromLines) {
        $parts = $line -split '\s+'
        $source = $parts[1]
        if (-not $stageAliases.Contains($source)) {
            Assert-Contract ($source -match '@sha256:[0-9a-f]{64}$') "dockerfile.base.digest" $line
        }
        if ($line -match '(?i)\s+AS\s+([a-zA-Z0-9_.-]+)\s*$') {
            [void]$stageAliases.Add($Matches[1])
        }
    }
}

$runtimeServices = @("api", "gateway", "worker", "mongo", "redis", "minio", "opensearch")
$hardenedServices = @($runtimeServices) + @("clamav")
foreach ($serviceName in $hardenedServices) {
    $service = Get-PropertyValue $config.services $serviceName
    $user = [string](Get-PropertyValue $service "user")
    $logging = Get-PropertyValue $service "logging"
    $logOptions = Get-PropertyValue $logging "options"
    Assert-Contract (-not [string]::IsNullOrWhiteSpace($user) -and $user -notmatch '^0(?::0)?$') "runtime.$serviceName.non_root" $user
    Assert-Contract ((Get-PropertyValue $service "read_only") -eq $true) "runtime.$serviceName.read_only" "read_only=true"
    Assert-Contract (@(Get-PropertyValue $service "cap_drop") -contains "ALL") "runtime.$serviceName.cap_drop" "ALL"
    Assert-Contract (@(Get-PropertyValue $service "security_opt") -contains "no-new-privileges:true") "runtime.$serviceName.nnp" "no-new-privileges"
    Assert-Contract ([int64](Get-PropertyValue $service "mem_limit") -gt 0) "runtime.$serviceName.memory" ([string](Get-PropertyValue $service "mem_limit"))
    Assert-Contract ([double](Get-PropertyValue $service "cpus") -gt 0) "runtime.$serviceName.cpu" ([string](Get-PropertyValue $service "cpus"))
    Assert-Contract ([int](Get-PropertyValue $service "pids_limit") -gt 0) "runtime.$serviceName.pids" ([string](Get-PropertyValue $service "pids_limit"))
    Assert-Contract ($null -ne (Get-PropertyValue $service "healthcheck")) "runtime.$serviceName.healthcheck" "configured"
    Assert-Contract ((Get-PropertyValue $logging "driver") -eq "json-file") "runtime.$serviceName.log_driver" "json-file"
    Assert-Contract ((Get-PropertyValue $logOptions "max-size") -eq "10m") "runtime.$serviceName.log_size" "10m"
    Assert-Contract ((Get-PropertyValue $logOptions "max-file") -eq "3") "runtime.$serviceName.log_files" "3"
}

$defaultNetwork = Get-PropertyValue $config.networks "default"
$edgeNetwork = Get-PropertyValue $config.networks "edge"
$gateway = Get-PropertyValue $config.services "gateway"
$gatewayPorts = @(Get-PropertyValue $gateway "ports")
Assert-Contract ((Get-PropertyValue $defaultNetwork "internal") -eq $true) "network.data_plane.internal" "true"
Assert-Contract ((Get-PropertyValue $edgeNetwork "internal") -ne $true) "network.edge.bridge" "host edge is separate"
Assert-Contract ($gatewayPorts.Count -eq 1) "network.gateway.single_port" ([string]$gatewayPorts.Count)
Assert-Contract ((Get-PropertyValue $gatewayPorts[0] "host_ip") -eq "127.0.0.1") "network.gateway.loopback" "127.0.0.1"
Assert-Contract ([int](Get-PropertyValue $gatewayPorts[0] "target") -eq 8443) "network.gateway.https_target" "8443"
foreach ($serviceName in $hardenedServices | Where-Object { $_ -ne "gateway" }) {
    $ports = Get-PropertyValue (Get-PropertyValue $config.services $serviceName) "ports"
    Assert-Contract ($null -eq $ports -or @($ports).Count -eq 0) "network.$serviceName.no_host_port" "none"
}

$mongoCommand = (@(Get-PropertyValue (Get-PropertyValue $config.services "mongo") "command") -join " ")
$redisCommand = (@(Get-PropertyValue (Get-PropertyValue $config.services "redis") "command") -join " ")
$minioCommand = (@(Get-PropertyValue (Get-PropertyValue $config.services "minio") "command") -join " ")
$apiEnvironment = Get-PropertyValue (Get-PropertyValue $config.services "api") "environment"
$openSearchEnvironment = Get-PropertyValue (Get-PropertyValue $config.services "opensearch") "environment"
Assert-Contract ($mongoCommand -match '--auth' -and $mongoCommand -match '--tlsMode requireTLS' -and $mongoCommand -match '--keyFile') "auth.mongo" "auth+keyfile+requireTLS"
Assert-Contract ($redisCommand -match '--port 0' -and $redisCommand -match '--tls-port 6379' -and $redisCommand -match '--requirepass') "auth.redis" "TLS-only+password"
Assert-Contract ($minioCommand -match '--certs-dir') "auth.minio.tls" "TLS cert directory"
Assert-Contract ((Get-PropertyValue $apiEnvironment "MongoDb__ConnectionString") -match 'tls=true') "auth.api.mongo_tls" "tls=true"
Assert-Contract ((Get-PropertyValue $apiEnvironment "RateLimiting__Redis__ConnectionString") -match 'ssl=true' -and (Get-PropertyValue $apiEnvironment "RateLimiting__Redis__ConnectionString") -match 'password=') "auth.api.redis_tls" "ssl+password"
Assert-Contract ((Get-PropertyValue $apiEnvironment "Storage__Minio__Endpoint") -match '^https://') "auth.api.minio_tls" "https"
Assert-Contract ((Get-PropertyValue $apiEnvironment "Search__OpenSearch__BaseUrl") -match '^https://' -and -not [string]::IsNullOrWhiteSpace((Get-PropertyValue $apiEnvironment "Search__OpenSearch__Password"))) "auth.api.opensearch_tls" "https+password"
Assert-Contract ((Get-PropertyValue $openSearchEnvironment "plugins.security.ssl.http.enabled") -eq "true") "auth.opensearch.security" "plugin TLS enabled"

$internalUsersTemplate = Get-Content -Raw -LiteralPath (Join-Path $backendRoot "production-like/opensearch/internal_users.yml.template")
$declaredUsers = @([regex]::Matches($internalUsersTemplate, '(?m)^([a-zA-Z0-9_]+):\s*$') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -ne "_meta" })
Assert-Contract ($declaredUsers.Count -eq 1 -and $declaredUsers[0] -eq "zumbo_admin") "auth.opensearch.users" ($declaredUsers -join ",")

$runtimeSummary = $null
if ($Runtime) {
    $containers = [System.Collections.Generic.List[object]]::new()
    $liveServices = if ($IncludeSecurityAv) { @($runtimeServices) + @("clamav") } else { $runtimeServices }
    foreach ($serviceName in $liveServices) {
        $containerId = (& docker ps -q --filter "label=com.docker.compose.project=$ProjectName" --filter "label=com.docker.compose.service=$serviceName").Trim()
        Assert-Contract (-not [string]::IsNullOrWhiteSpace($containerId)) "live.$serviceName.exists" "container found"
        $inspection = (& docker inspect $containerId | ConvertFrom-Json)[0]
        $health = [string]$inspection.State.Health.Status
        $uid = (& docker exec $containerId id -u).Trim()
        Assert-Contract ($inspection.State.Status -eq "running" -and $health -eq "healthy") "live.$serviceName.healthy" "$($inspection.State.Status)/$health"
        Assert-Contract ([string]$inspection.Config.Image -match $digestPattern) "live.$serviceName.image_digest" ([string]$inspection.Config.Image)
        Assert-Contract ($uid -ne "0") "live.$serviceName.uid" $uid
        Assert-Contract ($inspection.HostConfig.ReadonlyRootfs -eq $true) "live.$serviceName.read_only" "true"
        Assert-Contract (@($inspection.HostConfig.CapDrop) -contains "ALL") "live.$serviceName.cap_drop" "ALL"
        Assert-Contract (@($inspection.HostConfig.SecurityOpt) -contains "no-new-privileges:true") "live.$serviceName.nnp" "true"
        $containers.Add([ordered]@{
            service = $serviceName
            image = [string]$inspection.Config.Image
            uid = $uid
            health = $health
            readOnlyRoot = [bool]$inspection.HostConfig.ReadonlyRootfs
            memoryBytes = [int64]$inspection.HostConfig.Memory
            nanoCpus = [int64]$inspection.HostConfig.NanoCpus
            pidsLimit = [int]$inspection.HostConfig.PidsLimit
        })
    }

    $network = (& docker network inspect "${ProjectName}_default" | ConvertFrom-Json)[0]
    Assert-Contract ($network.Internal -eq $true) "live.network.internal" "true"
    $gatewayInspection = (& docker inspect ((& docker ps -q --filter "label=com.docker.compose.project=$ProjectName" --filter "label=com.docker.compose.service=gateway").Trim()) | ConvertFrom-Json)[0]
    $binding = @($gatewayInspection.HostConfig.PortBindings.'8443/tcp')[0]
    Assert-Contract ($binding.HostIp -eq "127.0.0.1") "live.gateway.loopback" "$($binding.HostIp):$($binding.HostPort)"

    $readyResult = "health-only"
    if (-not [string]::IsNullOrWhiteSpace($TlsCaPath)) {
        $publishedPort = [string]$binding.HostPort
        $curlArguments = @("--silent", "--show-error", "--fail", "--cacert", $TlsCaPath)
        $runningOnWindows = $env:OS -eq "Windows_NT"
        if ($runningOnWindows) { $curlArguments += "--ssl-revoke-best-effort" }
        $curlArguments += "https://127.0.0.1:$publishedPort/health/ready"
        $curlCommand = if ($runningOnWindows) { Get-Command curl.exe -CommandType Application } else { Get-Command curl -CommandType Application }
        $readyOutput = & $curlCommand @curlArguments 2>&1
        Assert-Contract ($LASTEXITCODE -eq 0 -and ($readyOutput -join "") -eq "Healthy") "live.gateway.https_ready" "Healthy"
        $readyResult = "Healthy"
    }

    $runtimeSummary = [ordered]@{
        project = $ProjectName
        containers = $containers
        internalNetwork = $true
        gatewayLoopback = "127.0.0.1:$($binding.HostPort)"
        gatewayReady = $readyResult
    }
}

$summary = [ordered]@{
    schemaVersion = 1
    task = "OPS-005"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    composeFiles = @($composeFiles | ForEach-Object { [IO.Path]::GetFileName($_) })
    checksPassed = @($checks | Where-Object passed).Count
    checksFailed = @($checks | Where-Object { -not $_.passed }).Count
    logBudgetPerRuntimeServiceMiB = 30
    runtime = $runtimeSummary
    checks = $checks
}

$json = $summary | ConvertTo-Json -Depth 12
if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $absoluteEvidencePath = if ([IO.Path]::IsPathRooted($EvidencePath)) { $EvidencePath } else { Join-Path (Get-Location) $EvidencePath }
    $parent = Split-Path -Parent $absoluteEvidencePath
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [IO.File]::WriteAllText($absoluteEvidencePath, $json, [Text.UTF8Encoding]::new($false))
}

$json
