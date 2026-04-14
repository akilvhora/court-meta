param(
    [string]$JobName = 'court-meta-ci',
    [int]$BuildNumber = 0  # 0 = lastBuild
)
$pair = '{0}:{1}' -f 'admin', 'admin@123'
$basic = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
$h = @{ Authorization = $basic }
$base = 'http://192.168.1.111:8080'
$bn = if ($BuildNumber -eq 0) { 'lastBuild' } else { "$BuildNumber" }
Write-Host "=== Job config (summary) ==="
$cfg = Invoke-WebRequest -Uri "$base/job/$JobName/api/json?tree=builds[number,result],lastBuild[number,result]" -Headers $h -UseBasicParsing
Write-Host $cfg.Content
Write-Host ""
Write-Host "=== Console log: $JobName #$bn ==="
$log = Invoke-WebRequest -Uri "$base/job/$JobName/$bn/consoleText" -Headers $h -UseBasicParsing
Write-Host $log.Content
