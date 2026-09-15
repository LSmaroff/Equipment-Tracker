using System.IO;
using System.Diagnostics;
using EquipmentTracking.App.Infrastructure;

namespace EquipmentTracking.App.Services;

public sealed class AdobeService
{
    private readonly SettingsService _settings;
    private readonly FileLogger _logger;

    public AdobeService(SettingsService settings, FileLogger logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void OpenPdf(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("The PDF could not be found.", pdfPath);
        }

        var configuredAdobePath = _settings.ResolvePath(_settings.Current.AdobeExecutablePath);

        try
        {
            ProcessStartInfo startInfo;

            if (!string.IsNullOrWhiteSpace(configuredAdobePath) && File.Exists(configuredAdobePath))
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = configuredAdobePath,
                    Arguments = $"\"{pdfPath}\"",
                    UseShellExecute = true
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                };
            }

            Process.Start(startInfo);
            _logger.Information($"Opened PDF: {Path.GetFileName(pdfPath)}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not open PDF: {Path.GetFileName(pdfPath)}", ex);
            throw new InvalidOperationException(
                "The PDF could not be opened. Configure Adobe Acrobat in Settings or verify the default PDF application.",
                ex);
        }
    }

    public bool OpenPdfForPrinting(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("The print-ready PDF could not be found.", pdfPath);
        }

        var configuredAdobePath = _settings.ResolvePath(_settings.Current.AdobeExecutablePath);
        if (string.IsNullOrWhiteSpace(configuredAdobePath) || !File.Exists(configuredAdobePath))
        {
            OpenPdf(pdfPath);
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = configuredAdobePath,
                Arguments = $"/p \"{pdfPath}\"",
                UseShellExecute = true
            });
            _logger.Information($"Opened two-copy print dialog: {Path.GetFileName(pdfPath)}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not open the PDF print dialog: {Path.GetFileName(pdfPath)}", ex);
            throw new InvalidOperationException(
                "The print-ready PDF was created, but the Adobe print dialog could not be opened. " +
                "Verify the Adobe executable in Settings.",
                ex);
        }
    }

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
