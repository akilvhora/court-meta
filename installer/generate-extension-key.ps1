<#
.SYNOPSIS
    Run ONCE to generate a stable RSA key pair for the Court Meta extension.
    Requires openssl.exe — included with Git for Windows (in PATH after Git install).

.OUTPUTS
    extension-private-key.pem  — Add as Jenkins Secret File credential
                                  (ID: court-meta-extension-key). Do NOT commit.
    Prints the base64 public key → paste into extension/manifest.json "key" field.
    Prints the extension ID      → paste into installer/court-meta-setup.iss #define ExtensionID.
#>

$ErrorActionPreference = "Stop"
$outDir = $PSScriptRoot

# ── Locate openssl ────────────────────────────────────────────────────────────
$openssl = Get-Command openssl -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $openssl) {
    $candidates = @(
        "C:\Program Files\Git\usr\bin\openssl.exe",
        "C:\Program Files (x86)\Git\usr\bin\openssl.exe",
        "C:\OpenSSL-Win64\bin\openssl.exe"
    )
    $openssl = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $openssl) {
    throw "openssl not found. Install Git for Windows or OpenSSL and ensure it is in PATH."
}
Write-Host "Using openssl: $openssl"

$privKeyPath = Join-Path $outDir "extension-private-key.pem"
$pubDerPath  = Join-Path $outDir "extension-public.der"

# ── Generate 2048-bit RSA private key (PKCS#8 PEM) ───────────────────────────
& $openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out $privKeyPath 2>$null
if ($LASTEXITCODE -ne 0) { throw "openssl genpkey failed" }

# ── Export SubjectPublicKeyInfo in DER format ─────────────────────────────────
& $openssl pkey -in $privKeyPath -pubout -outform DER -out $pubDerPath 2>$null
if ($LASTEXITCODE -ne 0) { throw "openssl pkey export failed" }

$spki = [System.IO.File]::ReadAllBytes($pubDerPath)
Remove-Item $pubDerPath -Force

# ── Base64 public key for manifest.json "key" field ──────────────────────────
$pubKeyBase64 = [Convert]::ToBase64String($spki)

# ── Compute Chrome extension ID ───────────────────────────────────────────────
# SHA256 of SPKI bytes → first 16 bytes → each nibble mapped to a-p (high nibble first)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$hash   = $sha256.ComputeHash($spki)
$extId  = -join ($hash[0..15] | ForEach-Object {
    [char]([byte][char]'a' + (($_ -shr 4) -band 0x0F))
    [char]([byte][char]'a' + ($_ -band 0x0F))
})

Write-Host ""
Write-Host "Private key written to: $privKeyPath" -ForegroundColor Yellow
Write-Host ">>> Upload this file as Jenkins Secret File credential (ID: court-meta-extension-key) <<<" -ForegroundColor Red
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "1. Add to extension/manifest.json:" -ForegroundColor Cyan
Write-Host "   `"key`": `"$pubKeyBase64`"" -ForegroundColor Green
Write-Host ""
Write-Host "2. Set in installer/court-meta-setup.iss:" -ForegroundColor Cyan
Write-Host "   #define ExtensionID `"$extId`"" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
