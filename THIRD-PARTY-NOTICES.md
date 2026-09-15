# Third-Party Components

This project references the following NuGet packages. Review each package license and the organization’s approved-software policy before operational deployment.

| Package | Version | Purpose |
|---|---:|---|
| ClosedXML | 0.105.1 | Excel workbook export |
| Microsoft.Data.Sqlite.Core | 10.0.10 | Managed SQLite data access without bundled native SQLite |
| SQLitePCLRaw.bundle_winsqlite3 | 2.1.11 | Uses the SQLite implementation serviced with Windows (`winsqlite3.dll`) |
| PDFsharp-WPF | 6.2.4 | PDF form reading/writing for the WPF application |
| QRCoder | 1.8.0 | Offline PNG QR-code generation for printed 1297 record IDs |
| Microsoft.NET.Test.Sdk | 18.8.1 | Test execution |
| xunit | 2.9.3 | Unit tests; build/test-only legacy package pending a separately tested xUnit v3 migration |
| xunit.runner.visualstudio | 3.1.5 | Visual Studio/VS Code test adapter |
| WixToolset.Sdk | 6.0.2 | Build-time creation of the Windows Installer MSI; review and accept applicable Open Source Maintenance Fee terms before use |

Transitive dependencies are resolved by NuGet and are included in the generated dependency inventory and vulnerability audit. Run:

```powershell
.\scripts\security-scan.ps1
```

Release packaging writes separate application and installer dependency inventories/audits below `artifacts\security`.

The use of `SQLitePCLRaw.bundle_winsqlite3` means the application depends on the SQLite component supplied and patched by Windows. Endpoint patch compliance is therefore part of the operational security boundary. The xUnit packages are excluded from published runtime artifacts; migrate the deprecated xUnit v2 test dependency only in a change that can be compiled and exercised on Windows. WiX is a build-time dependency and is not a runtime component of the installed application; the owning organization must still review its license/support terms and distribution use.
