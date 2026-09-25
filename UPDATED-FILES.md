# Files changed in 0.9.6-alpha.1

- Added `IntakeCompletionViewModel` and completion-command tests. Intake records the successfully finalized transaction ID and offers a readable two-copy print action after clearing the completed input form.
- Wired existing transaction-document and print-job services into Intake, retaining signature preservation and temporary-copy cleanup. Starting/resetting an intake clears the old print target; busy/reentrancy guards protect printing.
- Added a rendered check of the actual completion panel and its bindings, plus repository validation rules and user help. Advanced application/MSI identity to 0.9.6; schema remains 6.

## Historical 0.9.5-alpha.1 changes

- Added `TransactionDocumentService` and its synthetic regression tests for current cumulative views versus per-pickup subsets.
- Updated shared PDF composition, Dashboard/Equipment status open actions, Documents readable viewing/printing and preserved-original access, temporary-view maintenance, and minimum-size layout coverage.
- Advanced application/MSI versions to 0.9.5 without a schema change. Updated validation rules, user help, README, changelog, and handoff documentation. No operational PDF or database is rewritten.

## Historical 0.9.4-alpha.1 changes

This release adds exact offline HP device recognition and signed partial-pickup receipts under one parent 1297. The complete canonical gate passed with 145 tests, zero failures, and zero skips. See `CHANGELOG.md`, `VALIDATION.md`, and `docs/PROJECT_STATUS.md` for behavior, evidence, and remaining workstation acceptance checks.

- Application: barcode/identity recognition, exact catalog/history resolution, partial-pickup preparation and signature verification, schema 6 receipt/device links, recovery, protected backup/restore, Dashboard selection, grouped documents, and in-app help.
- Tests: recognition, migrations, pickup persistence/workflow/recovery, status safeguards, selection, grouped documents, and synthetic PDF/WPF layout regressions.
- Release: application/MSI version 0.9.4, release-readiness documentation, and AI continuity files.
- GitHub publication: complete current source on `main`, the prior repository README preserved as `docs/LEGACY-README-0.8.md`, and generated release binaries distributed as release assets rather than checked into source.

## Historical 0.9.3-alpha.1 changes

This maintenance release is cumulative over the verified `0.9.2-alpha.1` baseline. It applies shared TextBox horizontal padding once while preserving vertical alignment and multiline stretching, gives the Dashboard search its corrected `34,11,10,11` inset, adds pie-slice hover outline/tooltips while preserving the labeled legend, exposes chart automation text through a peer, uses weak collection notifications so discarded charts are not retained, makes release publishing fail closed on a nonzero offline-verifier result before either publish mode, and advances the MSI identity so Windows Installer can replace `0.9.2`. The canonical gate passed in 54.9 seconds with 112 tests passed, 0 failed, and 0 skipped; validation, dependency audits, offline review, both publish modes, MSI creation, manifest, and checksum verification also passed.

## Added in 0.9.3

- `src/EquipmentTracking.App/Controls/PieChartHitTester.cs`
- `tests/EquipmentTracking.Tests/PieChartHitTesterTests.cs`
- `tests/EquipmentTracking.Tests/PieChartLifecycleTests.cs`
- `tests/EquipmentTracking.Tests/TextBoxLayoutTests.cs`

## Updated in 0.9.3

- `AGENTS.md`
- `AFNET-DEPLOYMENT-CHECKLIST.md`
- `CHANGELOG.md`
- `DEPLOYMENT.md`
- `FIELD-TEST-CHECKLIST.md`
- `README.md`
- `RELEASE-READINESS.md`
- `SECURITY.md`
- `UPDATED-FILES.md`
- `VALIDATION.md`
- `docs/PROJECT_STATUS.md`
- `installer/EquipmentTracking.Installer/EquipmentTracking.Installer.wixproj`
- `scripts/publish.ps1`
- `scripts/validate.ps1`
- `src/EquipmentTracking.App/Controls/PieChart.cs`
- `src/EquipmentTracking.App/EquipmentTracking.App.csproj`
- `src/EquipmentTracking.App/Themes/LayoutStyles.xaml`
- `src/EquipmentTracking.App/ViewModels/MainWindowViewModel.cs`
- `src/EquipmentTracking.App/Views/DashboardView.xaml`
- `src/EquipmentTracking.App/Views/HowToUseView.xaml`

## 0.9.3 deployment note

The verified gate produced `EquipmentTrackingPlatform-0.9.3-alpha.1-win-x64-UNSIGNED-PILOT.exe` (70,289,687 bytes; SHA-256 `7fc0b209c45404b955878a490f515510c249993e31dc696737bcb380735a5f61`) and the matching MSI (58,154,645 bytes; SHA-256 `71550a1059d586829ae32e96d44787038abe2d4579734d66cca7568c3b606872`). Both are `NotSigned` and restricted to synthetic-data pilot use. Retain the verified `0.9.2` package and compatible verified backup for rollback, and complete theme/DPI, representative single- and multiline TextBox, chart hover/legend, physical scanner, and disposable `0.9.2`-to-`0.9.3` upgrade acceptance before operational use.

## Historical cumulative 0.9.2-alpha.1 inventory

The following prior inventory and evidence are retained as verified release history.

### Added in 0.9.2

- `src/EquipmentTracking.App/Services/DashboardSearchInputRouter.cs`
- `tests/EquipmentTracking.Tests/DashboardSearchInputRouterTests.cs`
- `tests/EquipmentTracking.Tests/DeviceEntryRowTests.cs`

### Updated in 0.9.2

- `AGENTS.md`
- `AFNET-DEPLOYMENT-CHECKLIST.md`
- `CHANGELOG.md`
- `DEPLOYMENT.md`
- `FIELD-TEST-CHECKLIST.md`
- `README.md`
- `RELEASE-READINESS.md`
- `SECURITY.md`
- `UPDATED-FILES.md`
- `VALIDATION.md`
- `docs/PROJECT_STATUS.md`
- `installer/EquipmentTracking.Installer/EquipmentTracking.Installer.wixproj`
- `scripts/validate.ps1`
- `src/EquipmentTracking.App/EquipmentTracking.App.csproj`
- `src/EquipmentTracking.App/Models/DeviceEntryRow.cs`
- `src/EquipmentTracking.App/ViewModels/MainWindowViewModel.cs`
- `src/EquipmentTracking.App/Views/DashboardView.xaml.cs`
- `src/EquipmentTracking.App/Views/HowToUseView.xaml`

### 0.9.2 deployment note

Do not deploy another MSI with product version `0.9.1`. The verified gate produced `EquipmentTrackingPlatform-0.9.2-alpha.1-win-x64-UNSIGNED-PILOT.exe` (70,288,174 bytes; SHA-256 `6aec0fbc1427abf90dd0d2b78961eb13f2546e25d361c3a8f37c210c63667cf5`) and the matching MSI (58,142,357 bytes; SHA-256 `6a307652a38e198561002ab59ac33883be777c8fc557d892eaed2763b6056b6d`). They remain restricted to synthetic-data pilot use because they are unsigned. Retain the prior approved `0.9.1` package and verified backup for rollback, and complete physical scanner/WPF-focus and disposable `0.9.1`-to-`0.9.2` upgrade checks before operational use.

## Historical cumulative 0.9.1-alpha.1 inventory

The following prior inventory is retained unchanged as release history. The `0.9.1-alpha.1` maintenance delta corrected customer/signature semantics and gave that corrected package a strictly newer MSI version than `0.9.0`.

### Added in 0.9.1

- `src/EquipmentTracking.App/Models/ModelCatalogEntry.cs`
- `src/EquipmentTracking.App/Models/UpdatePackageInspection.cs`
- `src/EquipmentTracking.App/Services/AuthenticodeVerifier.cs`
- `src/EquipmentTracking.App/Services/RecordCodeService.cs`
- `src/EquipmentTracking.App/Services/UpdateService.cs`
- `src/EquipmentTracking.App/Views/ModelNameDialog.xaml`
- `src/EquipmentTracking.App/Views/ModelNameDialog.xaml.cs`
- `tests/EquipmentTracking.Tests/RecordCodeServiceTests.cs`
- `tests/EquipmentTracking.Tests/TransactionWorkflowServiceTests.cs`

### Updated in 0.9.1

- `AGENTS.md`
- `AFNET-DEPLOYMENT-CHECKLIST.md`
- `CHANGELOG.md`
- `DEPLOYMENT.md`
- `FIELD-TEST-CHECKLIST.md`
- `README.md`
- `RELEASE-READINESS.md`
- `SECURITY.md`
- `THIRD-PARTY-NOTICES.md`
- `THREAT-MODEL.md`
- `UPDATED-FILES.md`
- `VALIDATION.md`
- `docs/PROJECT_STATUS.md`
- `installer/EquipmentTracking.Installer/EquipmentTracking.Installer.wixproj`
- `scripts/build-release.ps1`
- `scripts/publish.ps1`
- `scripts/validate.ps1`
- `src/EquipmentTracking.App/EquipmentTracking.App.csproj`
- `src/EquipmentTracking.App/Infrastructure/AppServices.cs`
- `src/EquipmentTracking.App/Models/AppSettings.cs`
- `src/EquipmentTracking.App/Models/DeviceEntryRow.cs`
- `src/EquipmentTracking.App/Models/DeviceRecord.cs`
- `src/EquipmentTracking.App/Models/DeviceSearchResult.cs`
- `src/EquipmentTracking.App/Services/BarcodeParser.cs`
- `src/EquipmentTracking.App/Services/DatabaseService.cs`
- `src/EquipmentTracking.App/Services/ExcelExportService.cs`
- `src/EquipmentTracking.App/Services/PdfFormService.cs`
- `src/EquipmentTracking.App/Services/PdfLogicalValueBuilder.cs`
- `src/EquipmentTracking.App/Services/PrintJobService.cs`
- `src/EquipmentTracking.App/Services/SettingsService.cs`
- `src/EquipmentTracking.App/Services/TransactionWorkflowService.cs`
- `src/EquipmentTracking.App/Services/WorkflowJournalService.cs`
- `src/EquipmentTracking.App/ViewModels/DashboardViewModel.cs`
- `src/EquipmentTracking.App/ViewModels/IntakeViewModel.cs`
- `src/EquipmentTracking.App/ViewModels/MainWindowViewModel.cs`
- `src/EquipmentTracking.App/ViewModels/ReturnsViewModel.cs`
- `src/EquipmentTracking.App/ViewModels/SettingsViewModel.cs`
- `src/EquipmentTracking.App/Views/DashboardView.xaml`
- `src/EquipmentTracking.App/Views/DashboardView.xaml.cs`
- `src/EquipmentTracking.App/Views/HowToUseView.xaml`
- `src/EquipmentTracking.App/Views/IntakeView.xaml`
- `src/EquipmentTracking.App/Views/IntakeView.xaml.cs`
- `src/EquipmentTracking.App/Views/ReturnsView.xaml`
- `src/EquipmentTracking.App/Views/ReturnsView.xaml.cs`
- `src/EquipmentTracking.App/Views/SettingsView.xaml`
- `src/EquipmentTracking.App/appsettings.default.json`
- `tests/EquipmentTracking.Tests/AppSettingsTests.cs`
- `tests/EquipmentTracking.Tests/BarcodeParserTests.cs`
- `tests/EquipmentTracking.Tests/CleanTemplateTests.cs`
- `tests/EquipmentTracking.Tests/DatabaseMigrationTests.cs`
- `tests/EquipmentTracking.Tests/PdfLogicalValueBuilderTests.cs`
- `tests/EquipmentTracking.Tests/WorkflowJournalServiceTests.cs`

### 0.9.1 deployment note

Build and validate on Windows, then distribute the versioned signed MSI from `artifacts\release` with its `release-manifest.json`. The first move from an older build that lacks the Settings update command uses the normal authorized MSI installation path. Once 0.9 or later is installed, Settings → **Install update package…** provides the attended offline path for a strictly newer MSI. Preserve the prior signed package and verified pre-update backup for rollback.
