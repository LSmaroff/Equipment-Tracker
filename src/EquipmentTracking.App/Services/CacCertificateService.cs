using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class CacCertificateService
{
    private const string SmartCardLogonOid = "1.3.6.1.4.1.311.20.2.2";
    private const uint ScardScopeUser = 0;
    private const uint ScardStatePresent = 0x20;
    private const uint ProvRsaFull = 1;
    private const uint CryptSilent = 0x40;
    private const uint PpUserCertStore = 42;
    private const int ScardSuccess = 0;
    private const int ScardErrorNoReadersAvailable = unchecked((int)0x8010002E);
    private const string BaseSmartCardProvider = "Microsoft Base Smart Card Crypto Provider";

    private readonly CertificateNameParser _nameParser;
    private readonly FileLogger _logger;

    public CacCertificateService(CertificateNameParser nameParser, FileLogger logger)
    {
        _nameParser = nameParser;
        _logger = logger;
    }

    public IReadOnlyList<CacCertificateCandidate> GetCandidates()
    {
        var results = new List<CacCertificateCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IntPtr context = IntPtr.Zero;

        try
        {
            ThrowOnScardError(SCardEstablishContext(
                ScardScopeUser,
                IntPtr.Zero,
                IntPtr.Zero,
                out context));

            foreach (var readerName in GetReaderNames(context))
            {
                if (!IsCardPresent(context, readerName))
                {
                    continue;
                }

                CacCertificateCandidate? bestCandidate = null;
                foreach (var certificate in GetCertificatesFromInsertedCard(readerName))
                {
                    using (certificate)
                    {
                        if (!IsUsableCertificate(certificate))
                        {
                            continue;
                        }

                        var thumbprint = certificate.Thumbprint ?? string.Empty;
                        var simpleName = certificate.GetNameInfo(
                            X509NameType.SimpleName,
                            forIssuer: false);
                        var identity = _nameParser.Parse(simpleName, certificate.Subject);
                        var candidate = new CacCertificateCandidate
                        {
                            ReaderName = readerName,
                            DisplayName = identity.HasUsableName
                                ? $"{identity.DisplayName} — {readerName} — expires {certificate.NotAfter:d}"
                                : $"{simpleName} — {readerName} — expires {certificate.NotAfter:d}",
                            Subject = certificate.Subject,
                            Issuer = certificate.Issuer,
                            Thumbprint = thumbprint,
                            NotAfter = certificate.NotAfter,
                            Score = ScoreCertificate(certificate, identity),
                            Identity = identity
                        };

                        if (bestCandidate is null ||
                            candidate.Score > bestCandidate.Score ||
                            (candidate.Score == bestCandidate.Score && candidate.NotAfter > bestCandidate.NotAfter))
                        {
                            bestCandidate = candidate;
                        }
                    }
                }

                if (bestCandidate is not null && seen.Add($"{readerName}|{bestCandidate.Thumbprint}"))
                {
                    results.Add(bestCandidate);
                }
            }
        }
        catch (Win32Exception ex)
        {
            _logger.Warning($"Active CAC discovery was unavailable: {ex.NativeErrorCode}.");
            throw new InvalidOperationException(
                "Windows could not read an inserted smart card. Confirm the Smart Card service, " +
                "reader driver, and CAC middleware are available.",
                ex);
        }
        finally
        {
            if (context != IntPtr.Zero)
            {
                var releaseResult = SCardReleaseContext(context);
                if (releaseResult != ScardSuccess)
                {
                    _logger.Warning(
                        "SCardReleaseContext returned error 0x" +
                        releaseResult.ToString("X8", CultureInfo.InvariantCulture) +
                        " while releasing the smart-card context.");
                }
            }
        }

        return results
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ReaderName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(candidate => candidate.NotAfter)
            .ToArray();
    }

    public IReadOnlyList<string> GetReaderNamesForDiagnostics(bool insertedCardsOnly = false)
    {
        IntPtr context = IntPtr.Zero;
        try
        {
            ThrowOnScardError(SCardEstablishContext(
                ScardScopeUser,
                IntPtr.Zero,
                IntPtr.Zero,
                out context));
            var readers = GetReaderNames(context);
            return insertedCardsOnly
                ? readers.Where(reader => IsCardPresent(context, reader)).ToArray()
                : readers;
        }
        finally
        {
            if (context != IntPtr.Zero)
            {
                var releaseResult = SCardReleaseContext(context);
                if (releaseResult != ScardSuccess)
                {
                    _logger.Warning(
                        "SCardReleaseContext returned error 0x" +
                        releaseResult.ToString("X8", CultureInfo.InvariantCulture) +
                        " during smart-card diagnostics.");
                }
            }
        }
    }

    private static string[] GetReaderNames(IntPtr context)
    {
        uint requiredLength = 0;
        var result = SCardListReaders(context, null, null, ref requiredLength);
        if (result == ScardErrorNoReadersAvailable)
        {
            return [];
        }

        ThrowOnScardError(result);
        if (requiredLength <= 1)
        {
            return [];
        }

        var buffer = new char[requiredLength];
        ThrowOnScardError(SCardListReaders(context, null, buffer, ref requiredLength));

        return new string(buffer)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsCardPresent(IntPtr context, string readerName)
    {
        var states = new[]
        {
            new ScardReaderState
            {
                ReaderName = readerName,
                Atr = new byte[36]
            }
        };

        var result = SCardGetStatusChange(context, 0, states, 1);
        if (result != ScardSuccess)
        {
            return false;
        }

        return (states[0].EventState & ScardStatePresent) != 0;
    }

    private static List<X509Certificate2> GetCertificatesFromInsertedCard(string readerName)
    {
        IntPtr provider = IntPtr.Zero;
        var certificates = new List<X509Certificate2>();
        var qualifiedContainer = $@"\\.\{readerName}\";

        if (!CryptAcquireContext(
                out provider,
                qualifiedContainer,
                BaseSmartCardProvider,
                ProvRsaFull,
                CryptSilent))
        {
            return certificates;
        }

        try
        {
            uint dataLength = (uint)IntPtr.Size;
            var pointerBytes = new byte[IntPtr.Size];
            if (!CryptGetProvParam(provider, PpUserCertStore, pointerBytes, ref dataLength, 0))
            {
                return certificates;
            }

            var storeHandle = IntPtr.Size == 8
                ? new IntPtr(BitConverter.ToInt64(pointerBytes, 0))
                : new IntPtr(BitConverter.ToInt32(pointerBytes, 0));
            if (storeHandle == IntPtr.Zero)
            {
                return certificates;
            }

            using var cardStore = new X509Store(storeHandle);
            foreach (var certificate in cardStore.Certificates)
            {
                certificates.Add(X509CertificateLoader.LoadCertificate(certificate.RawData));
            }
        }
        finally
        {
            CryptReleaseContext(provider, 0);
        }

        return certificates;
    }

    private static bool IsUsableCertificate(X509Certificate2 certificate)
    {
        var now = DateTime.Now;
        return certificate.NotBefore <= now &&
               certificate.NotAfter >= now &&
               AllowsDigitalSignature(certificate);
    }

    private static bool AllowsDigitalSignature(X509Certificate2 certificate)
    {
        var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (keyUsage is null)
        {
            return true;
        }

        const X509KeyUsageFlags accepted =
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation;
        return (keyUsage.KeyUsages & accepted) != 0;
    }

    private static int ScoreCertificate(X509Certificate2 certificate, CustomerIdentity identity)
    {
        var score = identity.HasUsableName ? 30 : 0;
        if (certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>())
            .Any(oid => string.Equals(oid.Value, SmartCardLogonOid, StringComparison.Ordinal)))
        {
            score += 100;
        }

        var combined = $"{certificate.Subject} {certificate.Issuer}";
        if (combined.Contains("DOD", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("U.S. Government", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        return score;
    }

    private static void ThrowOnScardError(int result)
    {
        if (result != ScardSuccess)
        {
            throw new Win32Exception(result);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ScardReaderState
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string ReaderName;
        public IntPtr UserData;
        public uint CurrentState;
        public uint EventState;
        public uint AtrLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
        public byte[] Atr;
    }

    [DllImport("winscard.dll")]
    private static extern int SCardEstablishContext(
        uint scope,
        IntPtr reserved1,
        IntPtr reserved2,
        out IntPtr context);

    [DllImport("winscard.dll")]
    private static extern int SCardReleaseContext(IntPtr context);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode, EntryPoint = "SCardListReadersW")]
    private static extern int SCardListReaders(
        IntPtr context,
        string? groups,
        [Out] char[]? readers,
        ref uint readerLength);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode, EntryPoint = "SCardGetStatusChangeW")]
    private static extern int SCardGetStatusChange(
        IntPtr context,
        uint timeout,
        [In, Out] ScardReaderState[] states,
        uint readerCount);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CryptAcquireContextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptAcquireContext(
        out IntPtr provider,
        string container,
        string providerName,
        uint providerType,
        uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptGetProvParam(
        IntPtr provider,
        uint parameter,
        [Out] byte[] data,
        ref uint dataLength,
        uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptReleaseContext(IntPtr provider, uint flags);
}
