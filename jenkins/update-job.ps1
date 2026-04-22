$ErrorActionPreference = 'Stop'
$pair = '{0}:{1}' -f 'admin', 'admin@123'
$basic = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
$h = @{ Authorization = $basic }
$base = 'http://192.168.1.111:8080'
$workDir = 'E:\Projects\Court Meta\jenkins'
$cfg = Join-Path $workDir 'court-meta-ci.config.xml'

if (-not (Test-Path $cfg)) {
    throw "Config file not found: $cfg"
}

# Re-save as UTF-8 without BOM (Jenkins rejects BOM on some endpoints)
$xml = Get-Content -Raw -Path $cfg
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$normalized = Join-Path $workDir '_current-config.xml'
[System.IO.File]::WriteAllText($normalized, $xml, $utf8NoBom)
$cfg = $normalized
Write-Host "Prepared config: $cfg"

# 4) Crumb + session
$cr = Invoke-WebRequest -Uri "$base/crumbIssuer/api/json" -Headers $h -UseBasicParsing -SessionVariable sv -TimeoutSec 15
$j = $cr.Content | ConvertFrom-Json
$h[$j.crumbRequestField] = $j.crumb
Write-Host "crumb acquired"

# 5) POST updated config
$bytes = [System.IO.File]::ReadAllBytes($cfg)
try {
    $res = Invoke-WebRequest -Uri "$base/job/court-meta-ci/config.xml" -Method POST -Headers $h -WebSession $sv -Body $bytes -ContentType 'application/xml; charset=utf-8' -UseBasicParsing -TimeoutSec 30
    Write-Host "config.xml POST HTTP $($res.StatusCode)"
} catch {
    Write-Host "FAILED: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        $sr = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $body = $sr.ReadToEnd()
        $m = [regex]::Match($body, '(?is)<h2[^>]*>(.*?)</h2>.*?Logging ID=([a-f0-9-]+)')
        if ($m.Success) { Write-Host ("ERROR: " + $m.Groups[1].Value + " | Logging ID: " + $m.Groups[2].Value) }
    }
    throw
}

# 6) Verify
$chk = Invoke-WebRequest -Uri "$base/job/court-meta-ci/config.xml" -Headers $h -UseBasicParsing -TimeoutSec 15
if ($chk.Content -match 'CpsScmFlowDefinition' -and $chk.Content -match '<scriptPath>Jenkinsfile</scriptPath>') {
    Write-Host "VERIFIED: job now loads Jenkinsfile from SCM"
} else {
    Write-Host "WARNING: change may not have applied"
}
