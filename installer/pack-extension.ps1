<#
.SYNOPSIS
    Packs the Chrome extension as a CRX2 file and writes the extension ID.
    Uses openssl for RSA operations — compatible with Windows PowerShell 5.1+.

.PARAMETER ExtensionDir   Folder containing unpacked extension files.
.PARAMETER KeyFile        PKCS#8 PEM private key (from generate-extension-key.ps1).
.PARAMETER OutputCrx      Destination path for the .crx file.
.PARAMETER OutputIdFile   Path to write the 32-char extension ID (one line).
#>
param(
    [Parameter(Mandatory)][string]$ExtensionDir,
    [Parameter(Mandatory)][string]$KeyFile,
    [Parameter(Mandatory)][string]$OutputCrx,
    [Parameter(Mandatory)][string]$OutputIdFile
)

$ErrorActionPreference = "Stop"

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

$tmp = [System.IO.Path]::GetTempPath()
$zipPath = Join-Path $tmp "court-meta-ext.zip"
$sigPath = Join-Path $tmp "court-meta-ext.sig"
$derPath = Join-Path $tmp "court-meta-ext.der"

try {
    # ── Zip the extension folder ──────────────────────────────────────────────
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$ExtensionDir\*" -DestinationPath $zipPath -Force
    $zipBytes = [System.IO.File]::ReadAllBytes($zipPath)

    # ── Export SubjectPublicKeyInfo (DER) ─────────────────────────────────────
    & $openssl pkey -in $KeyFile -pubout -outform DER -out $derPath 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "openssl: failed to export public key" }
    $spki = [System.IO.File]::ReadAllBytes($derPath)

    # ── RSA-SHA1 sign the zip bytes ───────────────────────────────────────────
    & $openssl dgst -sha1 -sign $KeyFile -out $sigPath $zipPath 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "openssl: signing failed" }
    $signature = [System.IO.File]::ReadAllBytes($sigPath)

    # ── Build CRX2 binary ─────────────────────────────────────────────────────
    # Format: "Cr24" | version=2 (LE uint32) | pubKeyLen | sigLen | pubKey | sig | zip
    $ms = [System.IO.MemoryStream]::new()
    $ms.Write([System.Text.Encoding]::ASCII.GetBytes("Cr24"), 0, 4)
    $ms.Write([BitConverter]::GetBytes([uint32]2),            0, 4)
    $ms.Write([BitConverter]::GetBytes([uint32]$spki.Length), 0, 4)
    $ms.Write([BitConverter]::GetBytes([uint32]$signature.Length), 0, 4)
    $ms.Write($spki,      0, $spki.Length)
    $ms.Write($signature, 0, $signature.Length)
    $ms.Write($zipBytes,  0, $zipBytes.Length)

    [System.IO.File]::WriteAllBytes($OutputCrx, $ms.ToArray())
    $ms.Dispose()
    Write-Host "CRX written : $OutputCrx"

    # ── Compute extension ID ──────────────────────────────────────────────────
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $hash   = $sha256.ComputeHash($spki)
    $extId  = -join ($hash[0..15] | ForEach-Object {
        [char]([byte][char]'a' + (($_ -shr 4) -band 0x0F))
        [char]([byte][char]'a' + ($_ -band 0x0F))
    })

    Set-Content -Path $OutputIdFile -Value $extId -Encoding UTF8 -NoNewline
    Write-Host "Extension ID: $extId"
}
finally {
    @($zipPath, $sigPath, $derPath) | Where-Object { Test-Path $_ } | Remove-Item -Force
}
