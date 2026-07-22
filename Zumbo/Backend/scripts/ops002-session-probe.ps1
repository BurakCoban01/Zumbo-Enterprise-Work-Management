param(
    [string]$GatewayBaseUrl = "http://127.0.0.1:58089",
    [string]$ApiOneContainer = "zumbo-ops002-api-1"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

$environment = @{}
Get-Content (Join-Path $PSScriptRoot "../.env") | ForEach-Object {
    if ($_ -match '^([^#=]+)=(.*)$') {
        $environment[$matches[1].Trim()] = $matches[2].Trim()
    }
}

$origin = $environment["ZUMBO_FRONTEND_URL"]
if ([string]::IsNullOrWhiteSpace($origin)) {
    $origin = "http://127.0.0.1:58177"
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.UseCookies = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.BaseAddress = [Uri]$GatewayBaseUrl
$cookies = @{}
$nodeStopped = $false

function Get-HeaderValue($response, [string]$name) {
    if ($response.Headers.Contains($name)) {
        return $response.Headers.GetValues($name) | Select-Object -First 1
    }

    return ""
}

function Invoke-Zumbo([string]$method, [string]$path, $body = $null, [string]$csrf = "") {
    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($method),
        $path)
    $request.Headers.TryAddWithoutValidation("Origin", $origin) | Out-Null
    if ($cookies.Count -gt 0) {
        $cookieHeader = ($cookies.GetEnumerator() | ForEach-Object {
            "$($_.Key)=$($_.Value)"
        }) -join "; "
        $request.Headers.TryAddWithoutValidation("Cookie", $cookieHeader) | Out-Null
    }

    if (-not [string]::IsNullOrWhiteSpace($csrf)) {
        $request.Headers.TryAddWithoutValidation("X-CSRF-Token", $csrf) | Out-Null
    }

    if ($null -ne $body) {
        $json = $body | ConvertTo-Json -Compress
        $request.Content = [System.Net.Http.StringContent]::new(
            $json,
            [System.Text.Encoding]::UTF8,
            "application/json")
    }

    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    if ($response.Headers.Contains("Set-Cookie")) {
        foreach ($line in $response.Headers.GetValues("Set-Cookie")) {
            if ($line -match '^(zumbo-(?:access|refresh|csrf))=([^;]+)') {
                $cookies[$matches[1]] = $matches[2]
            }
        }
    }

    $request.Dispose()
    return $response
}

function Wait-ForHealthy([string]$container, [TimeSpan]$timeout) {
    $deadline = [DateTimeOffset]::UtcNow.Add($timeout)
    do {
        $health = docker inspect --format='{{.State.Health.Status}}' $container
        if ($LASTEXITCODE -eq 0 -and $health -eq "healthy") {
            return
        }

        Start-Sleep -Seconds 3
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "$container did not become healthy."
}

try {
    $stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $registration = Invoke-Zumbo "POST" "/api/browser-auth/register" @{
        username = "ops002-$stamp"
        email = $environment["ZUMBO_IDENTITY_ADMIN_EMAIL"]
        password = "P@ssword123"
        organizationId = "ops002-org-$stamp"
        bootstrapToken = $environment["ZUMBO_IDENTITY_BOOTSTRAP_TOKEN"]
    }
    $registrationStatus = [int]$registration.StatusCode
    $registrationInstance = Get-HeaderValue $registration "X-Zumbo-Instance-Id"
    $registrationBody = $registration.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    $latestCsrf = $registrationBody.data.csrfToken
    Write-Output "REGISTER status=$registrationStatus instance=$registrationInstance cookies=$($cookies.Count)"
    $registration.Dispose()
    if ($registrationStatus -eq 409) {
        $registration = Invoke-Zumbo "POST" "/api/browser-auth/login" @{
            usernameOrEmail = $environment["ZUMBO_IDENTITY_ADMIN_EMAIL"]
            password = "P@ssword123"
        }
        $registrationStatus = [int]$registration.StatusCode
        $registrationInstance = Get-HeaderValue $registration "X-Zumbo-Instance-Id"
        $registrationBody = $registration.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        $latestCsrf = $registrationBody.data.csrfToken
        Write-Output "LOGIN_FALLBACK status=$registrationStatus instance=$registrationInstance cookies=$($cookies.Count)"
        $registration.Dispose()
    }
    if ($registrationStatus -ne 200 -or $cookies.Count -lt 3) {
        throw "Browser registration did not establish the expected cookie session."
    }

    $before = 1..8 | ForEach-Object {
        $response = Invoke-Zumbo "GET" "/api/browser-auth/session"
        $sessionBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        $latestCsrf = $sessionBody.data.csrfToken
        $result = [pscustomobject]@{
            Status = [int]$response.StatusCode
            Instance = Get-HeaderValue $response "X-Zumbo-Instance-Id"
        }
        $response.Dispose()
        $result
    }
    Write-Output "BEFORE_FAILOVER"
    $before | Group-Object Instance, Status | Select-Object Name, Count | Format-Table -AutoSize
    $failedBefore = @($before | Where-Object Status -ne 200).Count
    $replicaCountBefore = @($before | Group-Object Instance).Count
    if ($failedBefore -ne 0 -or $replicaCountBefore -ne 2) {
        throw "The initial cookie session was not accepted by both replicas."
    }

    docker stop --time 20 $ApiOneContainer | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not stop $ApiOneContainer."
    }
    $nodeStopped = $true
    Start-Sleep -Seconds 12

    $refresh = $null
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        $candidate = Invoke-Zumbo "POST" "/api/browser-auth/refresh" @{} $latestCsrf
        if ([int]$candidate.StatusCode -eq 200) {
            $refresh = $candidate
            break
        }

        $candidate.Dispose()
        Start-Sleep -Seconds 2
    }

    if ($null -eq $refresh) {
        throw "Session refresh did not recover after the api-1 outage."
    }
    $refreshStatus = [int]$refresh.StatusCode
    $refreshInstance = Get-HeaderValue $refresh "X-Zumbo-Instance-Id"
    Write-Output "DURING_FAILOVER_REFRESH status=$refreshStatus instance=$refreshInstance cookies=$($cookies.Count)"
    $refresh.Dispose()
    if ($refreshInstance -ne "api-2") {
        throw "The surviving replica did not refresh the browser session."
    }

    docker start $ApiOneContainer | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not restart $ApiOneContainer."
    }
    $nodeStopped = $false
    Wait-ForHealthy $ApiOneContainer ([TimeSpan]::FromMinutes(3))
    Start-Sleep -Seconds 8

    $after = 1..12 | ForEach-Object {
        $response = Invoke-Zumbo "GET" "/api/browser-auth/session"
        $result = [pscustomobject]@{
            Status = [int]$response.StatusCode
            Instance = Get-HeaderValue $response "X-Zumbo-Instance-Id"
        }
        $response.Dispose()
        $result
    }
    Write-Output "AFTER_RECOVERY_WITH_ROTATED_SESSION"
    $after | Group-Object Instance, Status | Select-Object Name, Count | Format-Table -AutoSize
    if (-not ($after | Where-Object { $_.Instance -eq "api-1" -and $_.Status -eq 200 })) {
        throw "Recovered api-1 did not accept the session rotated by api-2."
    }
}
finally {
    if ($nodeStopped) {
        docker start $ApiOneContainer | Out-Null
        Wait-ForHealthy $ApiOneContainer ([TimeSpan]::FromMinutes(3))
    }
    $client.Dispose()
    $handler.Dispose()
}
