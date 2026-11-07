$ErrorActionPreference = 'Stop'

param(
    [Parameter(Mandatory)] [string] $Customer,
    [Parameter(Mandatory)] [string] $MachineId,
    [Parameter(Mandatory)] [datetime] $ExpiresUtc,
    [string] $PrivateKeyPath = "$env:USERPROFILE\Documents\H-Photo License Authority\hphoto-license-private.pem",
    [string] $OutputPath = ".\$($Customer -replace '[^a-zA-Z0-9_-]', '_').hphoto-license"
)

$privatePem = [IO.File]::ReadAllText($PrivateKeyPath)
$rsa = [System.Security.Cryptography.RSA]::Create()

# ImportFromPem is unavailable in Windows PowerShell 5.1 (.NET Framework).
# The issuer must work on a normal Windows PowerShell prompt, so parse the
# PKCS#1 RSA PRIVATE KEY DER only when the modern API is absent.
if ($rsa.PSObject.Methods.Name -contains 'ImportFromPem') {
    $rsa.ImportFromPem($privatePem)
} else {
    function Read-DerLength([byte[]] $Bytes, [ref] $Offset) {
        $first = $Bytes[$Offset.Value]; $Offset.Value++
        if (($first -band 0x80) -eq 0) { return [int]$first }
        $count = $first -band 0x7F
        if ($count -lt 1 -or $count -gt 4) { throw 'Unsupported DER length.' }
        $length = 0
        for ($i = 0; $i -lt $count; $i++) { $length = ($length -shl 8) -bor $Bytes[$Offset.Value]; $Offset.Value++ }
        return $length
    }
    function Read-DerInteger([byte[]] $Bytes, [ref] $Offset) {
        if ($Bytes[$Offset.Value] -ne 0x02) { throw 'Expected RSA DER integer.' }; $Offset.Value++
        $length = Read-DerLength $Bytes $Offset
        $value = $Bytes[$Offset.Value..($Offset.Value + $length - 1)]; $Offset.Value += $length
        while ($value.Length -gt 1 -and $value[0] -eq 0) { $value = $value[1..($value.Length - 1)] }
        return [byte[]]$value
    }
    $base64 = ($privatePem -replace '-----BEGIN RSA PRIVATE KEY-----','' -replace '-----END RSA PRIVATE KEY-----','' -replace '\s','')
    $der = [Convert]::FromBase64String($base64); $offset = 0
    if ($der[$offset] -ne 0x30) { throw 'Expected a PKCS#1 RSA PRIVATE KEY.' }; $offset++
    [void](Read-DerLength $der ([ref]$offset)); [void](Read-DerInteger $der ([ref]$offset))
    $parameters = New-Object System.Security.Cryptography.RSAParameters
    $parameters.Modulus = Read-DerInteger $der ([ref]$offset)
    $parameters.Exponent = Read-DerInteger $der ([ref]$offset)
    $parameters.D = Read-DerInteger $der ([ref]$offset)
    $parameters.P = Read-DerInteger $der ([ref]$offset)
    $parameters.Q = Read-DerInteger $der ([ref]$offset)
    $parameters.DP = Read-DerInteger $der ([ref]$offset)
    $parameters.DQ = Read-DerInteger $der ([ref]$offset)
    $parameters.InverseQ = Read-DerInteger $der ([ref]$offset)
    $rsa.ImportParameters($parameters)
}
$payload = [ordered]@{ LicenseId = [guid]::NewGuid().ToString(); Customer = $Customer.Trim(); MachineId = $MachineId.Trim().ToUpperInvariant(); IssuedUtc = [datetimeoffset]::UtcNow.ToString('O'); ExpiresUtc = ([datetimeoffset]$ExpiresUtc).ToUniversalTime().ToString('O') }
$canonical = "$($payload.LicenseId)`n$($payload.Customer)`n$($payload.MachineId)`n$($payload.IssuedUtc)`n$($payload.ExpiresUtc)"
$signature = [Convert]::ToBase64String($rsa.SignData([Text.Encoding]::UTF8.GetBytes($canonical), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1))
$license = [ordered]@{ Payload = $payload; SignatureBase64 = $signature } | ConvertTo-Json -Depth 4
$outputDirectory = Split-Path $OutputPath -Parent
if ([string]::IsNullOrWhiteSpace($outputDirectory)) { $outputDirectory = (Get-Location).Path }
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
[IO.File]::WriteAllText((Join-Path $outputDirectory (Split-Path $OutputPath -Leaf)), $license, [Text.UTF8Encoding]::new($false))
Write-Host "Created $OutputPath for $Customer, valid until $($payload.ExpiresUtc)."
