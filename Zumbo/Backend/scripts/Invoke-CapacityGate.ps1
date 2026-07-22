[CmdletBinding()]
param(
    [ValidateSet("smoke", "demo", "performance")]
    [string]$Profile = "smoke",
    [ValidatePattern('^[a-z0-9][a-z0-9-]{2,39}$')]
    [string]$ProjectName = "zumbo-ops006",
    [ValidatePattern('^[a-z0-9][a-z0-9-]{2,39}$')]
    [string]$RunId = "ops006-gate",
    [ValidateRange(0, 65536)]
    [int]$MinimumFreeMemoryMiB = 0,
    [ValidateRange(0, 1024)]
    [int]$MinimumFreeDiskGiB = 0,
    [string]$EvidenceDirectory
)

$ErrorActionPreference = "Stop"
$backendRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path (Split-Path -Parent $backendRoot) "artifacts/operations/OPS-006"
}
elseif (-not [IO.Path]::IsPathRooted($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $backendRoot $EvidenceDirectory
}
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
[IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null

$profileMinimums = @{
    smoke = @{ MemoryMiB = 2048; DiskGiB = 2 }
    demo = @{ MemoryMiB = 4096; DiskGiB = 8 }
    performance = @{ MemoryMiB = 8192; DiskGiB = 16 }
}
if ($MinimumFreeMemoryMiB -eq 0) { $MinimumFreeMemoryMiB = $profileMinimums[$Profile].MemoryMiB }
if ($MinimumFreeDiskGiB -eq 0) { $MinimumFreeDiskGiB = $profileMinimums[$Profile].DiskGiB }

$ports = [ordered]@{
    Api = 59117
    Mongo = 59118
    Redis = 59119
    MinioApi = 59120
    MinioConsole = 59121
    OpenSearch = 59122
}
$composeFiles = @(
    (Join-Path $backendRoot "docker-compose.yml"),
    (Join-Path $backendRoot "docker-compose.host-access.yml"),
    (Join-Path $backendRoot "docker-compose.capacity.yml")
)
$capacityProject = Join-Path $backendRoot "tools/Zumbo.Capacity/Zumbo.Capacity.csproj"
$capacityAssembly = Join-Path $backendRoot "tools/Zumbo.Capacity/bin/Release/net8.0/Zumbo.Capacity.dll"
$degradedRunId = "$RunId-degraded"
$image = "zumbo-capacity-api:$ProjectName"
$environmentNames = @(
    "ZUMBO_API_PORT",
    "ZUMBO_MONGO_HOST_PORT",
    "ZUMBO_REDIS_HOST_PORT",
    "ZUMBO_MINIO_API_HOST_PORT",
    "ZUMBO_MINIO_CONSOLE_HOST_PORT",
    "ZUMBO_OPENSEARCH_HOST_PORT",
    "ZUMBO_JWT_SIGNING_KEY",
    "ZUMBO_MINIO_ROOT_USER",
    "ZUMBO_MINIO_ROOT_PASSWORD",
    "ZUMBO_CAPACITY_API_IMAGE",
    "ZUMBO_CAPACITY_PASSWORD",
    "ZUMBO_MONGO_URL",
    "ZUMBO_OPENSEARCH_URL",
    "ZUMBO_API_URL"
)
$originalEnvironment = @{}
foreach ($name in $environmentNames) {
    $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

function New-SyntheticSecret {
    param([int]$ByteCount = 48)

    $bytes = [byte[]]::new($ByteCount)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) }
    finally { $generator.Dispose() }
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Write-JsonEvidence {
    param([string]$Name, [object]$Value)

    $path = Join-Path $EvidenceDirectory $Name
    $json = $Value | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
    return $path
}

function Invoke-DockerCompose {
    param([string[]]$Arguments, [switch]$IgnoreExitCode)

    $dockerArguments = @("compose", "--project-name", $ProjectName)
    foreach ($file in $composeFiles) { $dockerArguments += @("--file", $file) }
    $dockerArguments += $Arguments
    $output = & docker @dockerArguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and -not $IgnoreExitCode) {
        throw "docker compose failed ($exitCode): $($output -join [Environment]::NewLine)"
    }
    return [ordered]@{ exitCode = $exitCode; output = @($output) }
}

function Invoke-Capacity {
    param(
        [string]$Command,
        [string]$CapacityRunId,
        [string]$EvidenceName,
        [switch]$IgnoreExitCode
    )

    if (-not (Test-Path -LiteralPath $capacityAssembly)) {
        throw "Capacity assembly is missing: $capacityAssembly"
    }
    $output = & dotnet $capacityAssembly $Command $Profile --run-id $CapacityRunId 2>&1
    $exitCode = $LASTEXITCODE
    $path = Join-Path $EvidenceDirectory $EvidenceName
    [IO.File]::WriteAllLines($path, @($output), [Text.UTF8Encoding]::new($false))
    if ($exitCode -ne 0 -and -not $IgnoreExitCode) {
        throw "Capacity command '$Command' failed with exit code $exitCode. Evidence: $path"
    }
    return [ordered]@{ exitCode = $exitCode; evidence = $path }
}

function Wait-ContainerHealthy {
    param([string]$Service, [int]$TimeoutSeconds = 120)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $containerId = (& docker ps -q --filter "label=com.docker.compose.project=$ProjectName" --filter "label=com.docker.compose.service=$Service").Trim()
        if (-not [string]::IsNullOrWhiteSpace($containerId)) {
            $status = (& docker inspect --format "{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}" $containerId).Trim()
            if ($status -eq "healthy") { return }
        }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Service '$Service' did not become healthy within $TimeoutSeconds seconds."
}

function Get-ContainerResourceEvidence {
    $resources = [System.Collections.Generic.List[object]]::new()
    $containerIds = @(& docker ps -q --filter "label=com.docker.compose.project=$ProjectName")
    foreach ($containerId in $containerIds) {
        if ([string]::IsNullOrWhiteSpace($containerId)) { continue }
        $inspection = (& docker inspect $containerId | ConvertFrom-Json)[0]
        $statsText = (& docker stats --no-stream --format "{{json .}}" $containerId)
        $stats = if ([string]::IsNullOrWhiteSpace($statsText)) { $null } else { $statsText | ConvertFrom-Json }
        $cpuStat = @(& docker exec $containerId sh -c "cat /sys/fs/cgroup/cpu.stat 2>/dev/null || true")
        $memoryPeak = (& docker exec $containerId sh -c "cat /sys/fs/cgroup/memory.peak 2>/dev/null || true").Trim()
        $resources.Add([ordered]@{
            service = [string]$inspection.Config.Labels.'com.docker.compose.service'
            container = [string]$inspection.Name.TrimStart('/')
            status = [string]$inspection.State.Status
            health = [string]$inspection.State.Health.Status
            restartCount = [int]$inspection.RestartCount
            cpuPercent = if ($null -eq $stats) { $null } else { [string]$stats.CPUPerc }
            memoryUsage = if ($null -eq $stats) { $null } else { [string]$stats.MemUsage }
            memoryPercent = if ($null -eq $stats) { $null } else { [string]$stats.MemPerc }
            memoryPeakBytes = if ($memoryPeak -match '^\d+$') { [int64]$memoryPeak } else { $null }
            cpuStat = $cpuStat
        })
    }
    return @($resources)
}

function Test-PortCanBind {
    param([int]$Port)

    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    try { $listener.Start() }
    finally { $listener.Stop() }
}

$env:ZUMBO_API_PORT = [string]$ports.Api
$env:ZUMBO_MONGO_HOST_PORT = [string]$ports.Mongo
$env:ZUMBO_REDIS_HOST_PORT = [string]$ports.Redis
$env:ZUMBO_MINIO_API_HOST_PORT = [string]$ports.MinioApi
$env:ZUMBO_MINIO_CONSOLE_HOST_PORT = [string]$ports.MinioConsole
$env:ZUMBO_OPENSEARCH_HOST_PORT = [string]$ports.OpenSearch
$env:ZUMBO_JWT_SIGNING_KEY = New-SyntheticSecret 64
$env:ZUMBO_MINIO_ROOT_USER = "capacity_$(New-SyntheticSecret 12)"
$env:ZUMBO_MINIO_ROOT_PASSWORD = New-SyntheticSecret 32
$env:ZUMBO_CAPACITY_PASSWORD = New-SyntheticSecret 32
$env:ZUMBO_CAPACITY_API_IMAGE = $image
$env:ZUMBO_MONGO_URL = "mongodb://127.0.0.1:$($ports.Mongo)/?replicaSet=rs0"
$env:ZUMBO_OPENSEARCH_URL = "http://127.0.0.1:$($ports.OpenSearch)"
$env:ZUMBO_API_URL = "http://127.0.0.1:$($ports.Api)"

$preflightPath = $null
$gate = $null
$degradedSeed = $null
$degraded = $null
$resourcesPath = $null
$cleanupPath = $null
$exitCode = 1
$started = $false
try {
    $savedErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & docker info *> $null
    $dockerInfoExitCode = $LASTEXITCODE
    $ErrorActionPreference = $savedErrorPreference
    if ($dockerInfoExitCode -ne 0) { throw "Docker Engine is not available." }

    foreach ($port in $ports.Values) { Test-PortCanBind -Port $port }
    $operatingSystem = Get-CimInstance Win32_OperatingSystem
    $freeMemoryMiB = [math]::Floor([double]$operatingSystem.FreePhysicalMemory / 1024)
    $drive = Get-PSDrive -Name ([IO.Path]::GetPathRoot($backendRoot).TrimEnd(':', '\'))
    $freeDiskGiB = [math]::Round($drive.Free / 1GB, 3)
    $preflight = [ordered]@{
        schemaVersion = 1
        task = "OPS-006"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        profile = $Profile
        runId = $RunId
        project = $ProjectName
        freeMemoryMiB = $freeMemoryMiB
        minimumFreeMemoryMiB = $MinimumFreeMemoryMiB
        freeDiskGiB = $freeDiskGiB
        minimumFreeDiskGiB = $MinimumFreeDiskGiB
        ports = $ports
        passed = $freeMemoryMiB -ge $MinimumFreeMemoryMiB -and $freeDiskGiB -ge $MinimumFreeDiskGiB
    }
    $preflightPath = Write-JsonEvidence "preflight.json" $preflight
    if (-not $preflight.passed) {
        throw "Capacity preflight failed: free memory $freeMemoryMiB/$MinimumFreeMemoryMiB MiB; free disk $freeDiskGiB/$MinimumFreeDiskGiB GiB."
    }

    & dotnet build $capacityProject --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Capacity build failed with exit code $LASTEXITCODE." }
    & dotnet build-server shutdown | Out-Null
    & docker build --tag $image --file (Join-Path $backendRoot "src/Zumbo.Api/Dockerfile") $backendRoot
    if ($LASTEXITCODE -ne 0) { throw "Capacity API image build failed with exit code $LASTEXITCODE." }

    Invoke-DockerCompose @("up", "--detach", "--wait", "api") | Out-Null
    $started = $true
    $gate = Invoke-Capacity "gate" $RunId "capacity-gate.json" -IgnoreExitCode

    $degradedSeed = Invoke-Capacity "seed" $degradedRunId "degraded-seed.json" -IgnoreExitCode
    if ($degradedSeed.exitCode -eq 0) {
        Invoke-DockerCompose @("stop", "opensearch") | Out-Null
        $degraded = Invoke-Capacity "degraded" $degradedRunId "degraded.json" -IgnoreExitCode
        Invoke-DockerCompose @("start", "opensearch") | Out-Null
        Wait-ContainerHealthy "opensearch"
        Invoke-Capacity "clean" $degradedRunId "degraded-cleanup.json" -IgnoreExitCode | Out-Null
    }
    else {
        $degraded = [ordered]@{ exitCode = 1; evidence = $null }
    }

    $resourcesPath = Write-JsonEvidence "container-resources.json" ([ordered]@{
        schemaVersion = 1
        task = "OPS-006"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        profile = $Profile
        runId = $RunId
        containers = @(Get-ContainerResourceEvidence)
    })
    $exitCode = if ($gate.exitCode -eq 0 -and $degraded.exitCode -eq 0) { 0 } else { 1 }
}
catch {
    Write-Warning $_.Exception.Message
    $exitCode = 1
}
finally {
    if ($started) {
        try {
            Invoke-DockerCompose @("start", "opensearch") -IgnoreExitCode | Out-Null
            Wait-ContainerHealthy "opensearch" 120
            Invoke-Capacity "clean" $RunId "final-gate-cleanup.json" -IgnoreExitCode | Out-Null
            Invoke-Capacity "clean" $degradedRunId "final-degraded-cleanup.json" -IgnoreExitCode | Out-Null
        }
        catch { Write-Warning "Final dataset cleanup could not complete: $_" }
    }
    Invoke-DockerCompose @("down", "--volumes", "--remove-orphans") -IgnoreExitCode | Out-Null
    $taskImageId = (& docker image ls --quiet $image 2>$null | Select-Object -First 1)
    if (-not [string]::IsNullOrWhiteSpace($taskImageId)) {
        & docker image rm --force $image 2>$null | Out-Null
    }

    $remainingContainers = @(& docker ps -a -q --filter "label=com.docker.compose.project=$ProjectName")
    $remainingNetworks = @(& docker network ls -q --filter "label=com.docker.compose.project=$ProjectName")
    $remainingVolumes = @(& docker volume ls -q --filter "label=com.docker.compose.project=$ProjectName")
    $remainingListeners = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalPort -in $ports.Values })
    $cleanupPassed = $remainingContainers.Count -eq 0 -and
        $remainingNetworks.Count -eq 0 -and
        $remainingVolumes.Count -eq 0 -and
        $remainingListeners.Count -eq 0
    $cleanupPath = Write-JsonEvidence "orchestrator-cleanup.json" ([ordered]@{
        schemaVersion = 1
        task = "OPS-006"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        project = $ProjectName
        remainingContainers = $remainingContainers.Count
        remainingNetworks = $remainingNetworks.Count
        remainingVolumes = $remainingVolumes.Count
        remainingListeners = $remainingListeners.Count
        passed = $cleanupPassed
    })
    if (-not $cleanupPassed) { $exitCode = 1 }

    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name], "Process")
    }
}

$summary = [ordered]@{
    schemaVersion = 1
    task = "OPS-006"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    profile = $Profile
    runId = $RunId
    project = $ProjectName
    preflight = $preflightPath
    gate = $gate
    degradedSeed = $degradedSeed
    degraded = $degraded
    resources = $resourcesPath
    cleanup = $cleanupPath
    passed = $exitCode -eq 0
}
Write-JsonEvidence "orchestrator-result.json" $summary | Out-Null
$summary | ConvertTo-Json -Depth 8
exit $exitCode
