using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class SignatureExtractionService
{
    private const long MaximumPdfBytes = 100L * 1024L * 1024L;
    private readonly CertificateNameParser _nameParser;
    private readonly FileLogger _logger;

    public SignatureExtractionService(CertificateNameParser nameParser, FileLogger logger)
    {
        _nameParser = nameParser;
        _logger = logger;
    }

    public async Task<SignatureInfo> ExtractAsync(
        string pdfPath,
        string? preferredSignatureFieldName = null,
        bool preferredFieldOnly = false,
        CancellationToken cancellationToken = default)
    {
        var pdfText = await ReadPdfTextAsync(pdfPath, cancellationToken);
        var candidateHexValues = PdfSignatureContentLocator.FindCandidateHexContents(
            pdfText,
            preferredSignatureFieldName,
            preferredFieldOnly);

        var signature = TryDecodeCandidates(candidateHexValues, cancellationToken);
        if (signature is not null)
        {
            return signature;
        }

        return new SignatureInfo
        {
            SignatureFound = false,
            DiagnosticMessage =
                preferredFieldOnly && !string.IsNullOrWhiteSpace(preferredSignatureFieldName)
                    ? $"No readable signature was found in the '{preferredSignatureFieldName}' field. " +
                      "Confirm that field was signed and the PDF was saved in Adobe."
                    : "No readable CMS/PKCS#7 signature container was found. " +
                      "Confirm the PDF was signed and saved in Adobe."
        };
    }

    public async Task<IReadOnlyCollection<string>> GetSignatureFingerprintsAsync(
        string pdfPath,
        CancellationToken cancellationToken = default)
    {
        var pdfText = await ReadPdfTextAsync(pdfPath, cancellationToken);
        return PdfSignatureContentLocator.FindCandidateHexContents(
                pdfText,
                preferredFieldName: null,
                preferredFieldOnly: false)
            .Select(ComputeFingerprint)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<SignatureInfo> ExtractNewSignatureAsync(
        string pdfPath,
        IReadOnlyCollection<string> existingSignatureFingerprints,
        CancellationToken cancellationToken = default)
    {
        return await ExtractNewSignatureAsync(
            pdfPath,
            existingSignatureFingerprints,
            preferredSignatureFieldName: null,
            preferredFieldOnly: false,
            cancellationToken: cancellationToken);
    }

    public async Task<SignatureInfo> ExtractNewSignatureAsync(
        string pdfPath,
        IReadOnlyCollection<string> existingSignatureFingerprints,
        string? preferredSignatureFieldName,
        bool preferredFieldOnly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(existingSignatureFingerprints);

        var existing = new HashSet<string>(
            existingSignatureFingerprints,
            StringComparer.OrdinalIgnoreCase);
        var pdfText = await ReadPdfTextAsync(pdfPath, cancellationToken);
        var newCandidates = PdfSignatureContentLocator.FindCandidateHexContents(
                pdfText,
                preferredSignatureFieldName,
                preferredFieldOnly)
            .Where(candidate => !existing.Contains(ComputeFingerprint(candidate)))
            .ToArray();

        var signature = TryDecodeCandidates(newCandidates, cancellationToken);
        if (signature is not null)
        {
            return new SignatureInfo
            {
                SignatureFound = true,
                SignerName = signature.SignerName,
                CertificateSubject = signature.CertificateSubject,
                CertificateThumbprint = signature.CertificateThumbprint,
                SigningTime = signature.SigningTime,
                Identity = signature.Identity,
                DiagnosticMessage =
                    signature.DiagnosticMessage +
                    " The signature was newly added after the operation started."
            };
        }

        return new SignatureInfo
        {
            SignatureFound = false,
                DiagnosticMessage =
                    preferredFieldOnly &&
                    !string.IsNullOrWhiteSpace(preferredSignatureFieldName)
                        ? $"No new readable signature was added to the '{preferredSignatureFieldName}' field after the operation started. " +
                          "Sign that field, save the PDF, close Adobe, and try again."
                        : "No new readable CMS/PKCS#7 signature was added after the operation started. " +
                          "Sign the 'Pickup Signature' field, save the PDF, close Adobe, and try again."
        };
    }

    public async Task<SignatureDiagnosticReport> DiagnoseAsync(
        string pdfPath,
        string expectedFieldName,
        IReadOnlyCollection<string>? existingSignatureFingerprints = null,
        DateTimeOffset? searchStartedAt = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pdfPath))
        {
            return new SignatureDiagnosticReport
            {
                PdfPath = pdfPath,
                ExpectedFieldName = expectedFieldName,
                PdfExists = false,
                Details = "The PDF file does not exist at the recorded path."
            };
        }

        var pdfText = await ReadPdfTextAsync(pdfPath, cancellationToken);
        var fieldCandidates = PdfSignatureContentLocator.FindCandidateHexContents(
            pdfText,
            expectedFieldName,
            preferredFieldOnly: true);
        var allCandidates = PdfSignatureContentLocator.FindCandidateHexContents(
            pdfText,
            preferredFieldName: null,
            preferredFieldOnly: false);
        var baseline = new HashSet<string>(
            existingSignatureFingerprints ?? [],
            StringComparer.OrdinalIgnoreCase);
        var newCandidates = allCandidates
            .Where(candidate => !baseline.Contains(ComputeFingerprint(candidate)))
            .ToArray();
        var fieldSignature = TryDecodeCandidates(fieldCandidates, cancellationToken);
        var newSignature = TryDecodeCandidates(newCandidates, cancellationToken);
        var changed = searchStartedAt is null ||
            File.GetLastWriteTimeUtc(pdfPath) >= searchStartedAt.Value.UtcDateTime.AddSeconds(-2);

        var details = new StringBuilder()
            .AppendLine("The signature detector checks the exact field first, then compares all CMS/PKCS#7 containers against the baseline captured before Adobe opened.")
            .AppendLine("A raw field-name miss can occur when Adobe stores the form hierarchy in compressed object streams; a newly added readable signature can still be accepted through the baseline comparison.")
            .ToString().Trim();

        return new SignatureDiagnosticReport
        {
            PdfPath = pdfPath,
            ExpectedFieldName = expectedFieldName,
            PdfExists = true,
            PdfChangedSinceCloseoutStarted = changed,
            ExpectedFieldNameFoundInRawObjects =
                PdfSignatureContentLocator.ContainsFieldName(pdfText, expectedFieldName),
            FieldSpecificCandidateCount = fieldCandidates.Count,
            TotalCandidateCount = allCandidates.Count,
            NewCandidateCount = newCandidates.Length,
            ReadableFieldSpecificSignatureFound = fieldSignature?.SignatureFound == true,
            FieldSpecificSignerName = fieldSignature?.SignerName ?? string.Empty,
            FieldSpecificSigningTime = fieldSignature?.SigningTime,
            ReadableNewSignatureFound = newSignature?.SignatureFound == true,
            NewSignatureSignerName = newSignature?.SignerName ?? string.Empty,
            NewSignatureSigningTime = newSignature?.SigningTime,
            Details = details
        };
    }

    private static async Task<string> ReadPdfTextAsync(
        string pdfPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("The signed PDF could not be found.", pdfPath);
        }

        byte[] pdfBytes;
        await using (var stream = new FileStream(
            pdfPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.Length <= 0)
            {
                throw new InvalidOperationException("The PDF is empty.");
            }

            if (stream.Length > MaximumPdfBytes)
            {
                throw new InvalidOperationException(
                    $"The PDF exceeds the {MaximumPdfBytes / (1024 * 1024)} MB safety limit.");
            }

            pdfBytes = new byte[checked((int)stream.Length)];
            var totalRead = 0;

            while (totalRead < pdfBytes.Length)
            {
                var read = await stream.ReadAsync(
                    pdfBytes.AsMemory(totalRead, pdfBytes.Length - totalRead),
                    cancellationToken);

                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead != pdfBytes.Length)
            {
                Array.Resize(ref pdfBytes, totalRead);
            }
        }

        return Encoding.Latin1.GetString(pdfBytes);
    }

    private SignatureInfo? TryDecodeCandidates(
        IEnumerable<string> candidateHexValues,
        CancellationToken cancellationToken)
    {
        foreach (var candidateHex in candidateHexValues)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var hex = candidateHex;
                if (hex.Length < 2)
                {
                    continue;
                }

                if ((hex.Length & 1) == 1)
                {
                    hex = hex[..^1];
                }

                var cmsBytes = Convert.FromHexString(hex);
                cmsBytes = TrimToEncodedObjectLength(cmsBytes);

                if (cmsBytes.Length == 0)
                {
                    continue;
                }

                var signedCms = new SignedCms();
                signedCms.Decode(cmsBytes);

                var signerInfo = signedCms.SignerInfos.Count > 0
                    ? signedCms.SignerInfos[0]
                    : null;

                var certificate = signerInfo?.Certificate ??
                    signedCms.Certificates.Cast<X509Certificate2>().FirstOrDefault();

                if (certificate is null)
                {
                    continue;
                }

                var simpleName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
                var identity = _nameParser.Parse(simpleName, certificate.Subject);
                var signingTime = TryGetSigningTime(signerInfo);

                return new SignatureInfo
                {
                    SignatureFound = true,
                    SignerName = string.IsNullOrWhiteSpace(simpleName)
                        ? identity.FullName
                        : simpleName,
                    CertificateSubject = certificate.Subject,
                    CertificateThumbprint = certificate.Thumbprint ?? string.Empty,
                    SigningTime = signingTime,
                    Identity = identity,
                    DiagnosticMessage =
                        "A CMS/PKCS#7 signature container was found. Trust and revocation were not validated."
                };
            }
            catch (Exception ex) when (
                ex is CryptographicException or FormatException or ArgumentException or OverflowException)
            {
                _logger.Warning($"A PDF /Contents value was not a readable signature: {ex.Message}");
            }
        }

        return null;
    }

    private static string ComputeFingerprint(string normalizedHex)
    {
        var bytes = Encoding.ASCII.GetBytes(normalizedHex);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static DateTimeOffset? TryGetSigningTime(SignerInfo? signerInfo)
    {
        if (signerInfo is null)
        {
            return null;
        }

        const string signingTimeOid = "1.2.840.113549.1.9.5";

        foreach (var attribute in signerInfo.SignedAttributes.Cast<CryptographicAttributeObject>())
        {
            if (!string.Equals(attribute.Oid?.Value, signingTimeOid, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var value in attribute.Values.Cast<AsnEncodedData>())
            {
                try
                {
                    return new DateTimeOffset(new Pkcs9SigningTime(value.RawData).SigningTime);
                }
                catch (CryptographicException)
                {
                    // Continue looking for another signing-time value.
                }
            }
        }

        return null;
    }

    private static byte[] TrimToEncodedObjectLength(byte[] bytes)
    {
        if (bytes.Length < 2)
        {
            return bytes;
        }

        var lengthByte = bytes[1];
        int contentLength;
        int headerLength;

        if ((lengthByte & 0x80) == 0)
        {
            contentLength = lengthByte;
            headerLength = 2;
        }
        else
        {
            var lengthByteCount = lengthByte & 0x7F;
            if (lengthByteCount is 0 or > 4 || bytes.Length < 2 + lengthByteCount)
            {
                return TrimTrailingPadding(bytes);
            }

            contentLength = 0;
            for (var index = 0; index < lengthByteCount; index++)
            {
                contentLength = checked((contentLength << 8) | bytes[2 + index]);
            }

            headerLength = 2 + lengthByteCount;
        }

        var encodedLength = checked(headerLength + contentLength);
        if (encodedLength <= 0 || encodedLength > bytes.Length)
        {
            return TrimTrailingPadding(bytes);
        }

        return encodedLength == bytes.Length ? bytes : bytes[..encodedLength];
    }

    private static byte[] TrimTrailingPadding(byte[] bytes)
    {
        var length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0)
        {
            length--;
        }

        return length == bytes.Length ? bytes : bytes[..length];
    }
}
