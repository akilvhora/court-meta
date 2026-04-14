param(
    [string]$JenkinsUrl = 'http://192.168.1.111:8080',
    [string]$User       = 'admin',
    [string]$Pass       = 'admin@123',
    [string]$JobName    = 'court-meta-ci',
    [string]$ConfigFile = "$PSScriptRoot\court-meta-ci.config.xml"
)

$ErrorActionPreference = 'Stop'
$pair = '{0}:{1}' -f $User, $Pass
$basic = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
$headers = @{ Authorization = $basic }
$session = $null

function Invoke-Jenkins {
    param($Method, $Path, $Body, $ContentType)
    $uri = "$JenkinsUrl$Path"
    $h = @{} + $headers
    if ($script:crumb) { $h[$script:crumbField] = $script:crumb }
    $params = @{ Uri = $uri; Method = $Method; Headers = $h; UseBasicParsing = $true; TimeoutSec = 30 }
    if ($script:session) { $params.WebSession = $script:session }
    if ($Body)        { $params.Body = $Body }
    if ($ContentType) { $params.ContentType = $ContentType }
    return Invoke-WebRequest @params
}

Write-Host "==> Probing Jenkins at $JenkinsUrl"
$whoami = Invoke-WebRequest -Uri "$JenkinsUrl/whoAmI/api/json" -Headers $headers -UseBasicParsing -TimeoutSec 15
Write-Host "    whoAmI HTTP $($whoami.StatusCode)"
Write-Host "    $($whoami.Content)"

Write-Host "==> Fetching CSRF crumb"
try {
    $cr = Invoke-WebRequest -Uri "$JenkinsUrl/crumbIssuer/api/json" -Headers $headers -UseBasicParsing -TimeoutSec 15 -SessionVariable sv
    $script:session = $sv
    $j = $cr.Content | ConvertFrom-Json
    $script:crumb      = $j.crumb
    $script:crumbField = $j.crumbRequestField
    Write-Host "    crumb acquired ($($script:crumbField))"
} catch {
    Write-Host "    crumb not required: $($_.Exception.Message)"
}

Write-Host "==> Checking if job '$JobName' already exists"
$exists = $false
try {
    $chk = Invoke-WebRequest -Uri "$JenkinsUrl/job/$JobName/api/json" -Headers $headers -UseBasicParsing -TimeoutSec 15
    if ($chk.StatusCode -eq 200) { $exists = $true }
} catch { $exists = $false }

$xmlBytes = [System.IO.File]::ReadAllBytes($ConfigFile)
Write-Host "==> Loaded config: $ConfigFile ($($xmlBytes.Length) bytes)"

try {
    if ($exists) {
        Write-Host "==> Job exists - updating config via POST /job/$JobName/config.xml"
        $res = Invoke-Jenkins -Method POST -Path "/job/$JobName/config.xml" -Body $xmlBytes -ContentType 'application/xml; charset=utf-8'
    } else {
        Write-Host "==> Creating job via POST /createItem?name=$JobName"
        $res = Invoke-Jenkins -Method POST -Path "/createItem?name=$JobName" -Body $xmlBytes -ContentType 'application/xml; charset=utf-8'
    }
} catch {
    Write-Host "==> REQUEST FAILED: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        $resp = $_.Exception.Response
        Write-Host "    Status: $([int]$resp.StatusCode) $($resp.StatusDescription)"
        $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $body = $sr.ReadToEnd()
        Write-Host "    Body: $body"
    }
    throw
}

Write-Host "==> Response HTTP $($res.StatusCode)"
Write-Host "==> Job URL: $JenkinsUrl/job/$JobName/"
Write-Host "DONE"
