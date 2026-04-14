$pair = '{0}:{1}' -f 'admin', 'admin@123'
$basic = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
$h = @{ Authorization = $basic }
$r = Invoke-WebRequest -Uri 'http://192.168.1.111:8080/job/court-meta-ci/config.xml' -Headers $h -UseBasicParsing -TimeoutSec 15
$r.Content | Out-File -FilePath 'E:\Projects\Court Meta\jenkins\_current-config.xml' -Encoding UTF8
Write-Host "HTTP $($r.StatusCode) - saved"
