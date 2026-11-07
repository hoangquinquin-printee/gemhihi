using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace PatternDigitizerPro.Core.Licensing;

public sealed record HPhotoLicensePayload(string LicenseId, string Customer, string MachineId, DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc);
public sealed record HPhotoLicenseEnvelope(HPhotoLicensePayload Payload, string SignatureBase64);
public sealed record LicenseStatus(bool IsValid, string Message, DateTimeOffset? ExpiresUtc = null, string? Customer = null);

/// <summary>Offline, machine-bound license verifier. The signing private key is never shipped.</summary>
public static class LicenseService
{
    private const string ProductFolderName = "GEMHIHI";
    private static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromMinutes(5);
    private const string PublicKeyPem = """
-----BEGIN RSA PUBLIC KEY-----
MIIBigKCAYEA7jzRGqqTGk7GUTjYHWwuY1DK6gbTrbE/qY0BUgRB4gEBT1LlZeKF
Tfdw+mizntM4ZLQ3PgDP0DWD0tqC7Qx6irKVmETn98DaLBqu8K4XOcHB2cJtGrl/
pxnjT4tXx4dPeo0ZDMd5EoAfvLERUPM8CJ0HWVuHiUDtDoGA3l/qcODqsuf83n55
CadqVHlgM3I/EUmJiMZhX/JTRa9hUrVOlJjUMk/T2yOr5qETDLhCPgmhy/d/y5yb
9l8fpyZpawecEjW+R5tgI1KNrCH1fmFDtFfRD5EvCsAeNuDtPMkyyE7G98rPlVYA
v/hrY6D4eDTto6vIt0bMG7dJSSPemY3NGmjcHCHlywMtOMyFStZDHoN3ae8QLG1U
rxg3K+GD+F6sSkOSH16XBlu3wMbbtmftObRyIkpGo0VZo/8Bg5J1nrp/e0UfmtU6
mcpkSIjjrUmA39TAQP/OVMh2F1/aEZ9rcW+pnzhRrjXXfPJ5ymKe7cvDJrHETRzf
0pYNGeeyLGyBAgMBAAE=
-----END RSA PUBLIC KEY-----
""";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static string ApplicationDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductFolderName);
    private static string LicensePath => Path.Combine(ApplicationDataDirectory, "license.hphoto-license");
    // Keep already-issued licenses usable after the product rename. The content
    // is still verified with the same signature and machine binding; this is
    // only a read-only migration path.
    private static string LegacyLicensePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "H-Photo", "license.hphoto-license");
    private static string TrustedClockPath => Path.Combine(ApplicationDataDirectory, "last-verified.bin");

    public static string MachineId
    {
        get
        {
            string source = Environment.MachineName;
            try
            {
                source = (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null)?.ToString() ?? source).Trim();
            }
            catch { /* Use machine name only when the registry cannot be read. */ }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("H-PHOTO|" + source)))[..24];
        }
    }

    public static LicenseStatus GetStatus(DateTimeOffset? now = null)
    {
        DateTimeOffset checkTime = now ?? DateTimeOffset.UtcNow;
        DateTimeOffset? lastTrustedTime = ReadLastTrustedTime();
        if (lastTrustedTime.HasValue && checkTime < lastTrustedTime.Value - ClockRollbackTolerance)
        {
            return new LicenseStatus(false, "System clock moved backwards. Restore the correct date and time before starting GEMHIHI.");
        }
        string installedLicensePath = File.Exists(LicensePath)
            ? LicensePath
            : LegacyLicensePath;
        if (!File.Exists(installedLicensePath))
        {
            return new LicenseStatus(false, "No GEMHIHI license is installed.");
        }
        LicenseStatus status = Validate(File.ReadAllText(installedLicensePath), checkTime);
        if (status.IsValid)
        {
            RecordTrustedTime(checkTime);
        }
        return status;
    }

    public static LicenseStatus Activate(string licenseText)
    {
        LicenseStatus status = Validate(licenseText, DateTimeOffset.UtcNow);
        if (!status.IsValid) return status;
        Directory.CreateDirectory(ApplicationDataDirectory);
        File.WriteAllText(LicensePath, licenseText.Trim());
        RecordTrustedTime(DateTimeOffset.UtcNow);
        return status;
    }

    public static LicenseStatus Validate(string licenseText, DateTimeOffset now)
    {
        try
        {
            HPhotoLicenseEnvelope? envelope = JsonSerializer.Deserialize<HPhotoLicenseEnvelope>(licenseText, JsonOptions);
            if (envelope?.Payload is null || string.IsNullOrWhiteSpace(envelope.SignatureBase64))
                return new LicenseStatus(false, "License file format is invalid.");
            if (!Guid.TryParse(envelope.Payload.LicenseId, out _) || string.IsNullOrWhiteSpace(envelope.Payload.Customer) ||
                envelope.Payload.ExpiresUtc <= envelope.Payload.IssuedUtc ||
                envelope.Payload.ExpiresUtc > envelope.Payload.IssuedUtc.AddYears(10))
            {
                return new LicenseStatus(false, "License payload is invalid.");
            }
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);
            bool signed = rsa.VerifyData(Encoding.UTF8.GetBytes(Canonical(envelope.Payload)), Convert.FromBase64String(envelope.SignatureBase64), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (!signed)
            {
                return new LicenseStatus(false, "License signature is invalid.");
            }
            if (!string.Equals(envelope.Payload.MachineId, MachineId, StringComparison.OrdinalIgnoreCase))
                return new LicenseStatus(false, "This license belongs to a different computer.");
            if (envelope.Payload.IssuedUtc > now.AddDays(2))
                return new LicenseStatus(false, "License issue date is in the future.");
            if (envelope.Payload.ExpiresUtc <= now)
                return new LicenseStatus(false, $"License expired on {envelope.Payload.ExpiresUtc.LocalDateTime:d}.");
            return new LicenseStatus(true, $"Licensed to {envelope.Payload.Customer} until {envelope.Payload.ExpiresUtc.LocalDateTime:d}.", envelope.Payload.ExpiresUtc, envelope.Payload.Customer);
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException or IOException)
        {
            return new LicenseStatus(false, "License cannot be verified.");
        }
    }

    private static DateTimeOffset? ReadLastTrustedTime()
    {
        try
        {
            if (!File.Exists(TrustedClockPath)) return null;
            byte[] protectedBytes = File.ReadAllBytes(TrustedClockPath);
            byte[] clearBytes = ProtectedData.Unprotect(protectedBytes, Encoding.UTF8.GetBytes(MachineId), DataProtectionScope.LocalMachine);
            string[] parts = Encoding.UTF8.GetString(clearBytes).Split('\n');
            return parts.Length == 2 && string.Equals(parts[0], MachineId, StringComparison.Ordinal) &&
                   DateTimeOffset.TryParse(parts[1], out DateTimeOffset parsed)
                ? parsed.ToUniversalTime()
                : null;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or FormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void RecordTrustedTime(DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(ApplicationDataDirectory);
            byte[] clearBytes = Encoding.UTF8.GetBytes($"{MachineId}\n{now.ToUniversalTime():O}");
            byte[] protectedBytes = ProtectedData.Protect(clearBytes, Encoding.UTF8.GetBytes(MachineId), DataProtectionScope.LocalMachine);
            File.WriteAllBytes(TrustedClockPath, protectedBytes);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            // A restricted Windows account may not write ProgramData. Signature,
            // expiry and machine binding remain enforced in that case.
        }
    }

    public static string Canonical(HPhotoLicensePayload payload) => string.Join("\n", payload.LicenseId, payload.Customer, payload.MachineId, payload.IssuedUtc.ToUniversalTime().ToString("O"), payload.ExpiresUtc.ToUniversalTime().ToString("O"));
}
