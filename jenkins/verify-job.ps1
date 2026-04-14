$pair = '{0}:{1}' -f 'admin', 'admin@123'
$basic = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
$h = @{ Authorization = $basic }
$r = Invoke-WebRequest -Uri 'http://192.168.1.111:8080/job/court-meta-ci/api/json' -Headers $h -UseBasicParsing -TimeoutSec 15
Write-Host "HTTP $($r.StatusCode)"
Write-Host $r.Content
