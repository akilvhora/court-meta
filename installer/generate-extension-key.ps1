<#
.SYNOPSIS
    Run ONCE to generate a stable RSA key pair for the Court Meta extension.

.OUTPUTS
    extension-private-key.pem  — Add this as a Jenkins Secret File credential
                                 (ID: court-meta-extension-key). Do NOT commit.
    Prints the base64 public key → paste into extension/manifest.json "key" field.
    Prints the extension ID      → paste into installer/court-meta-setup.iss #define ExtensionID.
#>

$ErrorActionPreference = "Stop"
$outDir = $PSScriptRoot

# Generate 2048-bit RSA key pair
$rsa = [System.Security.Cryptography.RSA]::Create(2048)

# Export private key as PEM (PKCS#8)
$privKeyBytes = $rsa.ExportPkcs8PrivateKey()
$privKeyPem = "-----BEGIN PRIVATE KEY-----`n" +
              [Convert]::ToBase64String($privKeyBytes, [Base64FormattingOptions]::InsertLineBreaks) +
              "`n-----END PRIVATE KEY-----"
$privKeyPath = Join-Path $outDir "extension-private-key.pem"
Set-Content -Path $privKeyPath -Value $privKeyPem -Encoding UTF8
Write-Host "Private key written to: $privKeyPath" -ForegroundColor Yellow
Write-Host ">>> Add this file as Jenkins Secret File credential (ID: court-meta-extension-key) <<<" -ForegroundColor Red

# Export SubjectPublicKeyInfo (SPKI) DER — this is what Chrome uses
$spki = $rsa.ExportSubjectPublicKeyInfo()

# Base64 public key for manifest.json "key" field
$pubKeyBase64 = [Convert]::ToBase64String($spki)

# Compute Chrome extension ID:
#   SHA256 of SPKI bytes → first 16 bytes → each nibble mapped to a-p (high nibble first)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$hash   = $sha256.ComputeHash($spki)
$extId  = -join ($hash[0..15] | ForEach-Object {
    [char]([byte][char]'a' + (($_ -shr 4) -band 0x0F))
    [char]([byte][char]'a' + ($_ -band 0x0F))
})

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "1. Add this to extension/manifest.json as the `"key`" field:" -ForegroundColor Cyan
Write-Host "   `"key`": `"$pubKeyBase64`"" -ForegroundColor Green
Write-Host ""
Write-Host "2. Set this extension ID in installer/court-meta-setup.iss:" -ForegroundColor Cyan
Write-Host "   #define ExtensionID `"$extId`"" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
