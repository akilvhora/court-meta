$ErrorActionPreference = 'Stop'
$pair = '{0}:{1}' -f 'admin', 'admin@123'
$basic = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
$h = @{ Authorization = $basic }
$base = 'http://192.168.1.111:8080'
$cfg  = 'E:\Projects\Court Meta\jenkins\_current-config.xml'

# Strip UTF-8 BOM if present (Jenkins config endpoint chokes on it)
$bytes = [System.IO.File]::ReadAllBytes($cfg)
if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
    $bytes = $bytes[3..($bytes.Length - 1)]
    [System.IO.File]::WriteAllBytes($cfg, $bytes)
    Write-Host "Stripped UTF-8 BOM from $cfg"
}

# Crumb + session
$cr = Invoke-WebRequest -Uri "$base/crumbIssuer/api/json" -Headers $h -UseBasicParsing -SessionVariable sv -TimeoutSec 15
$j = $cr.Content | ConvertFrom-Json
$h[$j.crumbRequestField] = $j.crumb
Write-Host "crumb acquired"

try {
    $res = Invoke-WebRequest -Uri "$base/job/court-meta-ci/config.xml" -Method POST -Headers $h -WebSession $sv -Body $bytes -ContentType 'application/xml; charset=utf-8' -UseBasicParsing -TimeoutSec 30
    Write-Host "config.xml POST HTTP $($res.StatusCode)"
} catch {
    Write-Host "FAILED: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        $sr = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $body = $sr.ReadToEnd()
        # only print a small window around any error message
        $m = [regex]::Match($body, '(?is)<h2[^>]*>(.*?)</h2>.*?Logging ID=([a-f0-9-]+)')
        if ($m.Success) { Write-Host ("ERROR: " + $m.Groups[1].Value + " | Logging ID: " + $m.Groups[2].Value) }
        else { Write-Host ("BODY (first 500): " + $body.Substring(0, [Math]::Min(500, $body.Length))) }
    }
    throw
}

# Verify
$chk = Invoke-WebRequest -Uri "$base/job/court-meta-ci/config.xml" -Headers $h -UseBasicParsing -TimeoutSec 15
if ($chk.Content -match '\*/main' -and $chk.Content -match 'jenkins/Jenkinsfile') {
    Write-Host "VERIFIED: branch=*/main, scriptPath=jenkins/Jenkinsfile"
} else {
    Write-Host "WARNING: changes may not have applied"
}
