[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$required = @(
    'EquipmentTrackingPlatform.sln',
    'global.json',
    'NuGet.Config',
    'Directory.Build.props',
    'SECURITY.md',
    'STIG-READINESS.md',
    'THREAT-MODEL.md',
    'AFNET-DEPLOYMENT-CHECKLIST.md',
    'scripts\security-scan.ps1',
    'src\EquipmentTracking.App\EquipmentTracking.App.csproj',
    'src\EquipmentTracking.App\App.xaml',
    'src\EquipmentTracking.App\MainWindow.xaml',
    'src\EquipmentTracking.App\Assets\EquipmentTrackingPlatform.ico',
    'src\EquipmentTracking.App\Assets\EquipmentTrackingPlatform.png',
    'src\EquipmentTracking.App\Themes\LayoutStyles.xaml',
    'src\EquipmentTracking.App\Services\ThemeService.cs',
    'src\EquipmentTracking.App\Services\DashboardSearchInputRouter.cs',
    'src\EquipmentTracking.App\Controls\PieChart.cs',
    'src\EquipmentTracking.App\Controls\PieChartHitTester.cs',
    'src\EquipmentTracking.App\Models\PieChartSlice.cs',
    'src\EquipmentTracking.App\Views\DashboardView.xaml',
    'src\EquipmentTracking.App\Views\DashboardView.xaml.cs',
    'src\EquipmentTracking.App\appsettings.default.json',
    'src\EquipmentTracking.App\Models\CloseoutRequest.cs',
    'src\EquipmentTracking.App\Models\CloseoutResult.cs',
    'src\EquipmentTracking.App\Services\CacCertificateService.cs',
    'src\EquipmentTracking.App\Services\DatabaseService.cs',
    'src\EquipmentTracking.App\Services\RankCatalog.cs',
    'src\EquipmentTracking.App\Services\TransactionWorkflowService.cs',
    'src\EquipmentTracking.App\ViewModels\CloseoutDialogViewModel.cs',
    'src\EquipmentTracking.App\Views\CloseoutDialog.xaml',
    'src\EquipmentTracking.App\Views\CloseoutDialog.xaml.cs',
    'RELEASE-READINESS.md',
    'DEPLOYMENT.md',
    'FIELD-TEST-CHECKLIST.md',
    'scripts\verify-offline.ps1',
    'scripts\sign-release.ps1',
    'scripts\build-release.ps1',
    'installer\EquipmentTracking.Installer\EquipmentTracking.Installer.wixproj',
    'installer\EquipmentTracking.Installer\Package.wxs',
    'src\EquipmentTracking.App\Properties\PublishProfiles\PortableSingleFile.pubxml',
    'src\EquipmentTracking.App\Services\BackupService.cs',
    'src\EquipmentTracking.App\Services\ScheduledBackupService.cs',
    'src\EquipmentTracking.App\Models\BackupFileEntry.cs',
    'src\EquipmentTracking.App\Models\BackupCreationResult.cs',
    'src\EquipmentTracking.App\Models\BackupScheduleState.cs',
    'src\EquipmentTracking.App\Services\WorkflowJournalService.cs',
    'src\EquipmentTracking.App\Services\PreflightService.cs',
    'src\EquipmentTracking.App\Services\SingleInstanceService.cs',
    'src\EquipmentTracking.App\Services\SupportPackageService.cs',
    'src\EquipmentTracking.App\Services\PrintJobService.cs',
    'src\EquipmentTracking.App\ViewModels\RecoveryViewModel.cs',
    'src\EquipmentTracking.App\Views\RecoveryView.xaml',
    'src\EquipmentTracking.App\Views\HowToUseView.xaml',
    'tests\EquipmentTracking.Tests\DashboardSearchInputRouterTests.cs',
    'tests\EquipmentTracking.Tests\PieChartHitTesterTests.cs',
    'tests\EquipmentTracking.Tests\PieChartLifecycleTests.cs',
    'tests\EquipmentTracking.Tests\TextBoxLayoutTests.cs',
    'tests\EquipmentTracking.Tests\PrintJobServiceTests.cs',
    'src\EquipmentTracking.App\Templates\1297-58SOW-SC-TEMPLATE.pdf'
)

foreach ($relative in $required) {
    $path = Join-Path $root $relative
    if (-not (Test-Path $path -PathType Leaf)) {
        throw "Required file missing: $relative"
    }
}

$repositoryFiles = @(
    Get-ChildItem -LiteralPath $root -Recurse -File |
        Where-Object {
            $_.FullName -notmatch '[\\/](?:artifacts|bin|obj)[\\/]'
        }
)

$repositoryFiles | Where-Object { $_.Extension -eq '.json' } | ForEach-Object {
    $file = $_
    try {
        Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json | Out-Null
    }
    catch {
        throw "Invalid JSON: $($file.FullName): $($_.Exception.Message)"
    }
}

$repositoryFiles |
    Where-Object { $_.Extension -in @('.xaml', '.csproj', '.config', '.props', '.pubxml', '.wixproj', '.wxs') } |
    ForEach-Object {
        $file = $_
        try {
            [xml](Get-Content -LiteralPath $file.FullName -Raw) | Out-Null
        }
        catch {
            throw "Invalid XML/XAML: $($file.FullName): $($_.Exception.Message)"
        }
    }

$settingsPath = Join-Path $root 'src\EquipmentTracking.App\appsettings.default.json'
$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
if ([string]$settings.Theme -notin @('Dark', 'Light', 'System')) {
    throw "Theme must be Dark, Light, or System. Found '$($settings.Theme)'."
}
if (-not $settings.Organizations -or $settings.Organizations.Count -lt 1) {
    throw 'At least one default organization is required.'
}

$expectedMappings = [ordered]@{
    TechnicianSignature = 'ISSUED BY SIGNATURE'
    PhoneNumber = 'DUTY PHONE'
    CustomerSignature = 'ISSUED TO SIGNATURE'
    TechnicianNameGrade = 'TechnicianName'
    Organization = 'ORGN'
    IssueDate = 'DATE OF ISSUE'
    ReturnDate = 'RETURN DATE'
    TicketNumber = 'TicketNumber'
    PickupSignature = 'Pickup Signature'
    Quantity = 'QNTY'
}
for ($index = 1; $index -le 10; $index++) {
    $expectedMappings["Device$index"] = "Device$index"
}
foreach ($entry in $expectedMappings.GetEnumerator()) {
    $property = $settings.PdfFieldMappings.PSObject.Properties[$entry.Key]
    $actual = if ($null -ne $property) { [string]$property.Value } else { $null }
    if ($actual -ne $entry.Value) {
        throw "PDF mapping mismatch for $($entry.Key). Expected '$($entry.Value)', found '$actual'."
    }
}

$nugetConfigPath = Join-Path $root 'NuGet.Config'
[xml]$nugetSettings = Get-Content $nugetConfigPath -Raw
$nugetOrgSource = $nugetSettings.SelectSingleNode("/configuration/packageSources/add[@key='nuget.org']")
if ($null -eq $nugetOrgSource -or $nugetOrgSource.GetAttribute('value') -ne 'https://api.nuget.org/v3/index.json') {
    throw 'NuGet.Config must contain the official nuget.org v3 package source for non-managed development. Use setup -NuGetSource for an approved mirror.'
}

$projectPath = Join-Path $root 'src\EquipmentTracking.App\EquipmentTracking.App.csproj'
[xml]$project = Get-Content $projectPath -Raw
$expectedApplicationVersion = '0.9.6-alpha.1'
$expectedVersionMatch = [regex]::Match(
    $expectedApplicationVersion,
    '^(?<msi>\d+\.\d+\.\d+)-alpha\.(?<revision>\d+)$')
if (-not $expectedVersionMatch.Success) {
    throw "Validation configuration contains an invalid application version '$expectedApplicationVersion'."
}
$expectedMsiVersion = $expectedVersionMatch.Groups['msi'].Value
$expectedFileVersion = "$expectedMsiVersion.$($expectedVersionMatch.Groups['revision'].Value)"
$expectedAssemblyVersion = "$expectedMsiVersion.0"

$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
$version = if ($null -ne $versionNode) { [string]$versionNode.InnerText } else { '' }
if ($version -ne $expectedApplicationVersion) {
    throw "Unexpected application version '$version'. Expected '$expectedApplicationVersion'."
}

$fileVersionNode = $project.SelectSingleNode('/Project/PropertyGroup/FileVersion')
$fileVersion = if ($null -ne $fileVersionNode) { [string]$fileVersionNode.InnerText } else { '' }
if ($fileVersion -ne $expectedFileVersion) {
    throw "File version '$fileVersion' is inconsistent with application version '$version'. Expected '$expectedFileVersion'."
}

$assemblyVersionNode = $project.SelectSingleNode('/Project/PropertyGroup/AssemblyVersion')
$assemblyVersion = if ($null -ne $assemblyVersionNode) { [string]$assemblyVersionNode.InnerText } else { '' }
if ($assemblyVersion -ne $expectedAssemblyVersion) {
    throw "Assembly version '$assemblyVersion' is inconsistent with application version '$version'. Expected '$expectedAssemblyVersion'."
}

$mainWindowViewModelText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\ViewModels\MainWindowViewModel.cs') -Raw
$expectedVersionFallback = "?? `"$expectedApplicationVersion`""
if ($mainWindowViewModelText -notmatch [regex]::Escape($expectedVersionFallback)) {
    throw "Main-window fallback version must match application version '$expectedApplicationVersion'."
}

$applicationIconNode = $project.SelectSingleNode('/Project/PropertyGroup/ApplicationIcon')
if ($null -eq $applicationIconNode -or
    [string]$applicationIconNode.InnerText -ne 'Assets\EquipmentTrackingPlatform.ico') {
    throw 'The application project must embed the approved Equipment Tracking Platform icon.'
}

$resourceIncludes = @(
    $project.SelectNodes('/Project/ItemGroup/Resource') |
        ForEach-Object { [string]$_.GetAttribute('Include') }
)
foreach ($requiredIconResource in @(
    'Assets\EquipmentTrackingPlatform.ico',
    'Assets\EquipmentTrackingPlatform.png')) {
    if ($requiredIconResource -notin $resourceIncludes) {
        throw "The application project is missing icon resource '$requiredIconResource'."
    }
}

$globalJsonPath = Join-Path $root 'global.json'
$globalJson = Get-Content $globalJsonPath -Raw | ConvertFrom-Json
if ([string]$globalJson.sdk.version -ne '10.0.110' -or [string]$globalJson.sdk.rollForward -ne 'latestPatch' -or [bool]$globalJson.sdk.allowPrerelease) {
    throw 'global.json must pin SDK 10.0.110, roll forward only to the latest patch in that feature band, and disable prerelease SDKs.'
}

$packageReferenceNodes = $project.SelectNodes('/Project/ItemGroup/PackageReference')
$packageNames = @($packageReferenceNodes | ForEach-Object { [string]$_.GetAttribute('Include') })
foreach ($requiredPackage in @('PDFsharp-WPF', 'QRCoder', 'Microsoft.Data.Sqlite.Core', 'SQLitePCLRaw.bundle_winsqlite3')) {
    if ($requiredPackage -notin $packageNames) {
        throw "Required package reference missing: $requiredPackage"
    }
}
foreach ($prohibitedPackage in @('Microsoft.Data.Sqlite', 'SQLitePCLRaw.lib.e_sqlite3')) {
    if ($prohibitedPackage -in $packageNames) {
        throw "Prohibited package reference found: $prohibitedPackage"
    }
}

$requiredAppPackageVersions = [ordered]@{
    ClosedXML = '0.105.1'
    'Microsoft.Data.Sqlite.Core' = '10.0.10'
    'SQLitePCLRaw.bundle_winsqlite3' = '2.1.11'
    'PDFsharp-WPF' = '6.2.4'
    QRCoder = '1.8.0'
}
foreach ($entry in $requiredAppPackageVersions.GetEnumerator()) {
    $node = $packageReferenceNodes | Where-Object { $_.GetAttribute('Include') -eq $entry.Key } | Select-Object -First 1
    $actual = if ($null -ne $node) { [string]$node.GetAttribute('Version') } else { '' }
    if ($actual -ne $entry.Value) {
        throw "Package $($entry.Key) must be version $($entry.Value); found '$actual'."
    }
}

$testProjectPath = Join-Path $root 'tests\EquipmentTracking.Tests\EquipmentTracking.Tests.csproj'
[xml]$testProject = Get-Content $testProjectPath -Raw
$testSdkNode = $testProject.SelectSingleNode("/Project/ItemGroup/PackageReference[@Include='Microsoft.NET.Test.Sdk']")
if ($null -eq $testSdkNode -or [string]$testSdkNode.GetAttribute('Version') -ne '18.8.1') {
    throw 'Microsoft.NET.Test.Sdk must be version 18.8.1.'
}

$propsText = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
foreach ($requiredText in @('NuGetAuditMode', 'NU1901', 'NU1902', 'NU1903', 'NU1904')) {
    if ($propsText -notmatch [regex]::Escape($requiredText)) {
        throw "Directory.Build.props is missing security setting '$requiredText'."
    }
}

$dashboardText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Views\DashboardView.xaml') -Raw
foreach ($requiredText in @('Close out selected 1297', "Print 1297's", 'PrintTwoCopiesCommand', 'TransactionScopes', 'Archived 1297s', 'Search 1297 records', 'Type anywhere or scan a 1297 code')) {
    if ($dashboardText -notmatch [regex]::Escape($requiredText)) {
        throw "Dashboard is missing '$requiredText'."
    }
}

$dashboardCodeText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Views\DashboardView.xaml.cs') -Raw
foreach ($requiredText in @('PreviewTextInput += HostWindow_OnPreviewTextInput', 'PreviewTextInput -= HostWindow_OnPreviewTextInput', 'SearchCommand.CanExecuteChanged += SearchCommand_OnCanExecuteChanged', 'SearchCommand.CanExecuteChanged -= SearchCommand_OnCanExecuteChanged', 'DashboardSearchInputRouter.TryCreateInitialQuery', 'TransactionSearchBox.Focus', 'Keyboard.Focus(TransactionSearchBox)', 'TransactionSearchBox.SetCurrentValue(TextBox.TextProperty, initialQuery)', 'ModifierKeys.Control', 'ModifierKeys.Alt', 'ModifierKeys.Windows', 'TextBoxBase or PasswordBox or ComboBox or ComboBoxItem', 'TryExecutePendingExactRecordSearch', 'GetBindingExpression(TextBox.TextProperty)?.UpdateSource()', 'TransactionSearchBox.SelectAll')) {
    if ($dashboardCodeText -notmatch [regex]::Escape($requiredText)) {
        throw "Dashboard type-to-search handling is missing '$requiredText'."
    }
}
if ($dashboardCodeText -match [regex]::Escape('TransactionSearchBox.Text = initialQuery')) {
    throw 'Dashboard type-to-search must preserve the SearchQuery binding by using SetCurrentValue instead of assigning Text directly.'
}
if ($dashboardCodeText -match 'DispatcherTimer') {
    throw 'Dashboard exact QR lookup must not be delayed by a timer after the record code is complete.'
}

$invalidRecordBranch = [regex]::Match(
    $dashboardCodeText,
    'if\s*\(\s*!_recordCodes\.TryParse\(textBox\.Text,\s*out var recordId\)\s*\)\s*\{(?<body>.*?)\}',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $invalidRecordBranch.Success -or
    $invalidRecordBranch.Groups['body'].Value -notmatch [regex]::Escape('_pendingExactRecordSearch = false;') -or
    $invalidRecordBranch.Groups['body'].Value -notmatch [regex]::Escape('_pendingRecordId = null;')) {
    throw 'Changing a queued exact-record scan to non-record text must cancel the stale deferred Dashboard search.'
}

$dashboardRouterText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\DashboardSearchInputRouter.cs') -Raw
foreach ($requiredText in @('MaximumQueryLength = 256', 'commandModifierPressed', 'focusedControlOwnsTextInput', 'string.IsNullOrWhiteSpace(input)', 'input.Any(char.IsControl)')) {
    if ($dashboardRouterText -notmatch [regex]::Escape($requiredText)) {
        throw "Dashboard printable-input routing policy is missing '$requiredText'."
    }
}

[xml]$dashboardXml = $dashboardText
$dashboardSearchBox = $dashboardXml.SelectSingleNode(
    "//*[local-name()='TextBox' and @*[local-name()='Name']='TransactionSearchBox']")
if ($null -eq $dashboardSearchBox -or $dashboardSearchBox.GetAttribute('MaxLength') -ne '256') {
    throw 'Dashboard search input and DashboardSearchInputRouter must retain the shared 256-character limit.'
}
$dashboardSearchBinding = $dashboardSearchBox.GetAttribute('Text')
if ($dashboardSearchBinding -notmatch [regex]::Escape('Binding SearchQuery') -or
    $dashboardSearchBinding -notmatch [regex]::Escape('UpdateSourceTrigger=PropertyChanged') -or
    $dashboardSearchBox.GetAttribute('TextChanged') -ne 'TransactionSearchBox_OnTextChanged' -or
    $dashboardSearchBox.GetAttribute('PreviewKeyDown') -ne 'TransactionSearchBox_OnPreviewKeyDown') {
    throw 'Dashboard search input must retain its two-way live SearchQuery and immediate record-code/Enter event wiring.'
}

$searchOverlayGrid = $dashboardSearchBox.ParentNode
if ($null -eq $searchOverlayGrid -or $searchOverlayGrid.LocalName -ne 'Grid' -or
    $dashboardSearchBox.GetAttribute('Padding') -ne '34,11,10,11') {
    throw 'Dashboard search input must retain the shared overlay grid and its 34-pixel left text inset.'
}
$searchIcon = $searchOverlayGrid.SelectSingleNode(
    "./*[local-name()='TextBlock' and contains(@FontFamily, 'Segoe Fluent Icons')]")
if ($null -eq $searchIcon -or
    $searchIcon.GetAttribute('Margin') -ne '12,0,0,0' -or
    $searchIcon.GetAttribute('HorizontalAlignment') -ne 'Left' -or
    $searchIcon.GetAttribute('VerticalAlignment') -ne 'Center' -or
    $searchIcon.GetAttribute('IsHitTestVisible') -ne 'False') {
    throw 'Dashboard search icon must remain centered, pointer-transparent, and aligned inside the search inset.'
}
$searchPlaceholder = $searchOverlayGrid.SelectSingleNode(
    "./*[local-name()='TextBlock' and @Text='Type anywhere or scan a 1297 code']")
if ($null -eq $searchPlaceholder -or
    $searchPlaceholder.GetAttribute('Margin') -ne '34,0,0,0' -or
    $searchPlaceholder.GetAttribute('VerticalAlignment') -ne 'Center' -or
    $searchPlaceholder.GetAttribute('IsHitTestVisible') -ne 'False') {
    throw 'Dashboard search placeholder must remain centered, pointer-transparent, and aligned with the text inset.'
}

$pieChartText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Controls\PieChart.cs') -Raw
foreach ($requiredText in @(
    'protected override void OnMouseMove',
    'protected override void OnMouseLeave',
    'PieChartHitTester.FindSliceIndex',
    'SetHoveredSlice',
    'PlacementMode.MousePoint',
    'PlacementTarget = this',
    'Focusable = false',
    'IsHitTestVisible = false',
    'StaysOpen = true',
    'slice.DisplayText',
    'CreateHoverOutline',
    'FocusBrush',
    'HoverOutlineThickness = 5d',
    'CollectionChangedEventManager.RemoveHandler',
    'CollectionChangedEventManager.AddHandler',
    'protected override AutomationPeer OnCreateAutomationPeer',
    'new FrameworkElementAutomationPeer(this)')) {
    if ($pieChartText -notmatch [regex]::Escape($requiredText)) {
        throw "Dashboard pie-chart hover handling is missing '$requiredText'."
    }
}
if ($pieChartText -match 'e\.Handled\s*=\s*true') {
    throw 'Dashboard pie-chart hover must not consume mouse events or interfere with surrounding Dashboard behavior.'
}
if ($pieChartText -match '\.CollectionChanged\s*(?:\+=|-=)') {
    throw 'Dashboard pie chart must use the weak CollectionChangedEventManager instead of a direct collection event subscription.'
}
if ($pieChartText -notmatch '(?s)CollectionChangedEventManager\.RemoveHandler\(\s*oldCollection,\s*chart\.ItemsCollectionChanged\)' -or
    $pieChartText -notmatch '(?s)CollectionChangedEventManager\.AddHandler\(\s*newCollection,\s*chart\.ItemsCollectionChanged\)') {
    throw 'Dashboard pie chart must detach and attach ItemsCollectionChanged through the weak collection event manager.'
}
if ($pieChartText -notmatch '(?s)protected override AutomationPeer OnCreateAutomationPeer\(\)\s*=>\s*new FrameworkElementAutomationPeer\(this\);') {
    throw 'Dashboard pie chart must create a FrameworkElementAutomationPeer so its automation name and help text are exposed.'
}
if ($pieChartText -notmatch '(?s)drawingContext\.DrawGeometry\(slice\.Brush,\s*outline,\s*geometry\);.*?if\s*\(hoveredGeometry is not null\).*?CreateHoverOutline\(\)') {
    throw 'Dashboard pie-chart hover emphasis must be drawn after the normal wedges so neighboring slices cannot cover it.'
}
if ([regex]::Matches($pieChartText, 'CreateHoverOutline\(\)').Count -lt 3) {
    throw 'Dashboard pie-chart hover emphasis must cover both single-slice and multi-slice rendering.'
}
$staleHoverClearPatterns = [ordered]@{
    'mouse leave' = '(?s)OnMouseLeave\s*\([^)]*\).*?ClearHover\(\);'
    'render-size change' = '(?s)OnRenderSizeChanged\s*\([^)]*\).*?ClearHover\(\);'
    'items-source replacement' = '(?s)ItemsSourceChanged\s*\([^)]*\).*?chart\.ClearHover\(\);'
    'items collection change' = '(?s)ItemsCollectionChanged\s*\([^)]*\).*?ClearHover\(\);'
    'control unload' = '(?s)PieChart_OnUnloaded\s*\([^)]*\).*?ClearHover\(\)'
}
foreach ($entry in $staleHoverClearPatterns.GetEnumerator()) {
    if ($pieChartText -notmatch $entry.Value) {
        throw "Dashboard pie-chart hover must clear stale state on $($entry.Key)."
    }
}

$pieHitTesterText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Controls\PieChartHitTester.cs') -Raw
foreach ($requiredText in @('DefaultPadding = 6d', 'FindSliceIndex', 'double.IsFinite', 'values[index] <= 0', 'Math.Atan2', 'angleFromTwelveClockwise', 'index == lastPositiveIndex')) {
    if ($pieHitTesterText -notmatch [regex]::Escape($requiredText)) {
        throw "Dashboard pie-chart hit testing is missing '$requiredText'."
    }
}

$pieSliceText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Models\PieChartSlice.cs') -Raw
foreach ($requiredText in @('public string DisplayText', '{Label}', '{Value}', '{Percentage:0.#}')) {
    if ($pieSliceText -notmatch [regex]::Escape($requiredText)) {
        throw "Dashboard pie-chart category identification is missing '$requiredText'."
    }
}

$pieChartNode = $dashboardXml.SelectSingleNode("//*[local-name()='PieChart']")
$automationNamespace = 'clr-namespace:System.Windows.Automation;assembly=PresentationCore'
if ($null -eq $pieChartNode -or
    $pieChartNode.GetAttribute('ItemsSource') -ne '{Binding ChartSlices}' -or
    $pieChartNode.GetAttribute('AutomationProperties.Name', $automationNamespace) -ne 'Active device distribution chart') {
    throw 'Dashboard pie chart must retain its ChartSlices binding and accessible automation name.'
}
$pieChartHelpText = $pieChartNode.GetAttribute('AutomationProperties.HelpText', $automationNamespace)
foreach ($requiredText in @('Hover a slice', 'category, count, and percentage', 'without requiring a pointer')) {
    if ($pieChartHelpText -notmatch [regex]::Escape($requiredText)) {
        throw "Dashboard pie-chart accessibility help is missing '$requiredText'."
    }
}
$pieChartLegend = $pieChartNode.ParentNode.SelectSingleNode(
    "./*[local-name()='ItemsControl' and @ItemsSource='{Binding ChartSlices}']")
if ($null -eq $pieChartLegend -or
    $null -eq $pieChartLegend.SelectSingleNode(".//*[local-name()='TextBlock' and @Text='{Binding DisplayText}']")) {
    throw 'Dashboard pie chart must retain the labeled ChartSlices list as its non-pointer accessible equivalent.'
}

$dashboardRouterTestsText = Get-Content (Join-Path $root 'tests\EquipmentTracking.Tests\DashboardSearchInputRouterTests.cs') -Raw
foreach ($requiredText in @('AcceptsPrintableTypingAndScannerText', 'RejectsEmptyWhitespaceAndControlInput', 'PreservesApplicationShortcuts', 'PreservesFocusedTextAndComboBoxInput', 'EnforcesDashboardSearchLength')) {
    if ($dashboardRouterTestsText -notmatch [regex]::Escape($requiredText)) {
        throw "Dashboard printable-input routing tests are missing '$requiredText'."
    }
}

$pieChartTestsText = Get-Content (Join-Path $root 'tests\EquipmentTracking.Tests\PieChartHitTesterTests.cs') -Raw
foreach ($requiredText in @(
    'MapsClockwiseQuarterSlicesFromTwelveOClock',
    'UsesHalfOpenBoundariesForWeightedSlices',
    'IgnoresNonPositiveValuesAndReturnsOriginalIndex',
    'HandlesSingleSliceCenterAndOutsideCircle',
    'UsesMinimumDimensionAndRejectsInvalidGeometry')) {
    if ($pieChartTestsText -notmatch [regex]::Escape($requiredText)) {
        throw "Dashboard pie-chart hit-testing coverage is missing '$requiredText'."
    }
}

$pieChartLifecycleTestsText = Get-Content (Join-Path $root 'tests\EquipmentTracking.Tests\PieChartLifecycleTests.cs') -Raw
foreach ($requiredText in @(
    'PieChart_ExposesAutomationNameAndHelpText',
    'UIElementAutomationPeer.CreatePeerForElement(chart)',
    'peer?.GetName()',
    'peer?.GetHelpText()',
    'PieChart_CollectionSubscriptionDoesNotRetainUnloadedChart',
    'WeakReference',
    'GC.WaitForPendingFinalizers()',
    'Assert.False(chartReference.IsAlive)',
    'retainedSlices.Add')) {
    if ($pieChartLifecycleTestsText -notmatch [regex]::Escape($requiredText)) {
        throw "Dashboard pie-chart lifecycle coverage is missing '$requiredText'."
    }
}

$textBoxLayoutTestsText = Get-Content (Join-Path $root 'tests\EquipmentTracking.Tests\TextBoxLayoutTests.cs') -Raw
foreach ($requiredText in @(
    'SharedTextBoxTemplate_AppliesPaddingOnceAndCentersCaret',
    'Padding = new Thickness(34, 11, 10, 11)',
    'SharedTextBoxTemplate_KeepsMultilineContentHostStretched',
    'Height = 100',
    'VerticalContentAlignment = VerticalAlignment.Top',
    'textBox.Template.FindName("PART_ContentHost", textBox)',
    'Assert.InRange(contentHostHeight, 95, 99)')) {
    if ($textBoxLayoutTestsText -notmatch [regex]::Escape($requiredText)) {
        throw "Shared TextBox layout coverage is missing '$requiredText'."
    }
}
$layoutThreadCount = [regex]::Matches($textBoxLayoutTestsText, 'new Thread\s*\(').Count
$backgroundLayoutThreadCount = [regex]::Matches(
    $textBoxLayoutTestsText,
    'thread\.IsBackground\s*=\s*true;').Count
if ($layoutThreadCount -ne 2 -or $backgroundLayoutThreadCount -ne $layoutThreadCount) {
    throw "Both shared TextBox STA layout-test threads must be background threads. Found $backgroundLayoutThreadCount background assignment(s) for $layoutThreadCount thread(s)."
}

$intakeText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Views\IntakeView.xaml') -Raw
foreach ($requiredText in @('DeviceEntry_OnPreviewKeyDown', 'ScanTextBox_OnTextChanged', 'ModelName', 'RankOptions', 'ActionLabel')) {
    if ($intakeText -notmatch [regex]::Escape($requiredText)) {
        throw "New intake is missing '$requiredText'."
    }
}

$barcodeParseResultText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Models\BarcodeParseResult.cs') -Raw
if ($barcodeParseResultText -notmatch [regex]::Escape('public string CageCode')) {
    throw 'Barcode parsing must preserve the exact CAGE component for conservative device recognition.'
}

$barcodeParserText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\BarcodeParser.cs') -Raw
foreach ($requiredText in @('TryParse18S', 'out string cageCode', 'CageCode = cageCode.Trim()', 'RawValue = raw')) {
    if ($barcodeParserText -notmatch [regex]::Escape($requiredText)) {
        throw "Barcode CAGE/raw preservation is missing '$requiredText'."
    }
}

$knownDeviceCatalogText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\KnownDeviceCatalog.cs') -Raw
foreach ($requiredText in @('TryResolveExactDevice', '7ESQ7', '2MQ5390WTS', 'A4TH1AV', 'HP EliteBook 645', 'StringComparer.OrdinalIgnoreCase')) {
    if ($knownDeviceCatalogText -notmatch [regex]::Escape($requiredText)) {
        throw "Exact offline device recognition is missing '$requiredText'."
    }
}

$deviceRecognitionText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\DeviceRecognitionService.cs') -Raw
foreach ($requiredText in @('TryResolveExactDevice', 'GetDeviceIdentityHistoryAsync', 'ResolveUnambiguousHistory', 'ResolveModelNameAsync', 'historicalCages.Length > 1')) {
    if ($deviceRecognitionText -notmatch [regex]::Escape($requiredText)) {
        throw "Conservative catalog/history recognition is missing '$requiredText'."
    }
}

$intakeViewModelText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\ViewModels\IntakeViewModel.cs') -Raw
foreach ($requiredText in @('Completion.RecordCompletion(result.Transaction.Id', 'Completion.Clear()', 'PrintCompletedIntakeAsync', 'CreateCurrentCopyAsync(transactionId, forPrinting: true)', 'DeleteTemporaryPrintJobAsync')) {
    if ($intakeViewModelText -notmatch [regex]::Escape($requiredText)) {
        throw "Completed-intake printing is missing '$requiredText'."
    }
}
$intakePrintView = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Views\IntakeView.xaml') -Raw
foreach ($requiredText in @('HasCompletedIntake', 'Print two 1297 copies', 'Binding PrintCommand')) {
    if ($intakePrintView -notmatch [regex]::Escape($requiredText)) {
        throw "Completed-intake print action is missing '$requiredText'."
    }
}
if (-not (Test-Path (Join-Path $root 'tests\EquipmentTracking.Tests\IntakeCompletionViewModelTests.cs'))) {
    throw 'Completed-intake print regression coverage is missing.'
}
foreach ($requiredText in @('_deviceRecognition.RecognizeAsync', 'string.IsNullOrWhiteSpace(entry.PartNumber)', 'string.IsNullOrWhiteSpace(entry.ModelName)')) {
    if ($intakeViewModelText -notmatch [regex]::Escape($requiredText)) {
        throw "New Intake recognition wiring/edit preservation is missing '$requiredText'."
    }
}

$recognitionTestsText = Get-Content (Join-Path $root 'tests\EquipmentTracking.Tests\DeviceRecognitionServiceTests.cs') -Raw
foreach ($requiredText in @('RecognizesExactHpIdentityAcrossScannerSeparatorVariants', 'ExactKnownDeviceDoesNotGuessFromCageOrSerialAlone', 'OperatorModelCatalogOverridesBuiltInModelName', 'ReusesOnlyConsistentExactSerialHistory', 'ConflictingHistoryIsNotReused', 'HistoryWithDifferentKnownCageIsNotReused')) {
    if ($recognitionTestsText -notmatch [regex]::Escape($requiredText)) {
        throw "Device-recognition regression coverage is missing '$requiredText'."
    }
}

$recognitionDatabaseTestsText = Get-Content (Join-Path $root 'tests\EquipmentTracking.Tests\DeviceRecognitionDatabaseTests.cs') -Raw
if ($recognitionDatabaseTestsText -notmatch [regex]::Escape('ExactSerialHistoryReturnsOnlyMatchingStoredIdentity')) {
    throw 'Exact SQLite device-history recognition coverage is missing.'
}

$cacText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\CacCertificateService.cs') -Raw
foreach ($requiredText in @('SCardListReaders', 'SCardGetStatusChange', 'ScardStatePresent', 'PpUserCertStore', 'ScardScopeUser', 'X509CertificateLoader.LoadCertificate')) {
    if ($cacText -notmatch [regex]::Escape($requiredText)) {
        throw "Active smart-card enumeration is missing '$requiredText'."
    }
}
if ($cacText -match 'StoreName\.My') {
    throw 'CAC discovery must not enumerate the cached CurrentUser personal certificate store.'
}

$databaseText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\DatabaseService.cs') -Raw
foreach ($requiredText in @('IsArchived', 'CloseoutPreparedAt', 'ClosedAt', 'ArchiveTransactionAsync', 'trusted_schema=OFF', 'TRIM(SerialNumber) COLLATE NOCASE')) {
    if ($databaseText -notmatch [regex]::Escape($requiredText)) {
        throw "Database closeout/security implementation is missing '$requiredText'."
    }
}

$workflowText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\TransactionWorkflowService.cs') -Raw
foreach ($requiredText in @('FinalizeCloseoutAsync', 'Archived 1297s', 'CopyFileVerifiedAsync', 'WaitForStableExclusiveReadAsync', 'OriginalSignedIntake', 'FinalSignedCloseout')) {
    if ($workflowText -notmatch [regex]::Escape($requiredText)) {
        throw "Closeout workflow is missing '$requiredText'."
    }
}

$backupText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\BackupService.cs') -Raw
foreach ($requiredText in @('DeletedRecordPaths', 'ValidateProtectedRecordPathsAsync', 'IsReservedWindowsFileName', "character is '<' or '>' or ':'")) {
    if ($backupText -notmatch [regex]::Escape($requiredText)) {
        throw "Backup safety implementation is missing '$requiredText'."
    }
}

$partialPickupText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\TransactionWorkflowService.PartialPickup.cs') -Raw
foreach ($requiredText in @('BeginPartialPickupAsync', 'FinalizePartialPickupAsync', 'OriginalSignedIntake', 'CreatePartialPickupPdf', 'ExistingSignatureFingerprints', 'ExtractNewSignatureAsync', 'CommitPickupReceiptAsync', 'EnsureNoPendingCloseoutOrPartialPickupAsync', 'RollbackPartialPickupAsync', 'DestinationSha256')) {
    if ($partialPickupText -notmatch [regex]::Escape($requiredText)) {
        throw "Signed partial-pickup workflow is missing '$requiredText'."
    }
}
if ($workflowText -notmatch 'EnsureNoPendingPartialPickupAsync' -or $workflowText -notmatch 'FinalizePartialPickupAsync') {
    throw 'Closeout and Recovery must coordinate with the signed partial-pickup workflow.'
}
if ($databaseText -notmatch 'SignedPartialPickupArtifactType' -or $backupText -notmatch 'SignedPartialPickup') {
    throw 'Signed pickup child PDFs must be recorded and protected by backup validation.'
}
$pickupPresenterText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Views\PartialPickupDialogService.cs') -Raw
foreach ($requiredText in @('BeginPartialPickupAsync', 'FinalizePartialPickupAsync', 'isPartialPickup: true', 'preparation.PreparedPdfPath')) {
    if ($pickupPresenterText -notmatch [regex]::Escape($requiredText)) {
        throw "Customer-signed pickup screen is missing '$requiredText'."
    }
}
$returnsViewModelText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\ViewModels\ReturnsViewModel.cs') -Raw
if ($returnsViewModelText -match 'ApplyDeviceStrikeThroughs\s*\(' -or $returnsViewModelText -notmatch 'PartialPickupDialogService') {
    throw 'Device status pickup actions must use the customer-signed child-document workflow, not mutate the parent PDF directly.'
}
$documentsViewText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Views\TransactionDocumentsDialog.xaml') -Raw
foreach ($requiredText in @('PrintSelectedCommand', 'OpenPreservedCommand', 'View selected', 'Print readable copies')) {
    if ($documentsViewText -notmatch [regex]::Escape($requiredText)) {
        throw "Pickup document presentation is missing '$requiredText'."
    }
}
$documentServiceText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\TransactionDocumentService.cs') -Raw
foreach ($requiredText in @('CreateCurrentCopyAsync', 'CreatePreservedCopyAsync', 'IsAlreadyReturned', 'receipt.DeviceIds', 'CreateReadableStatusView', 'CreateTwoCopyLetterSheet')) {
    if ($documentServiceText -notmatch [regex]::Escape($requiredText)) {
        throw "Current/pickup document presentation is missing '$requiredText'."
    }
}
if (-not (Test-Path (Join-Path $root 'tests\EquipmentTracking.Tests\TransactionDocumentServiceTests.cs'))) {
    throw 'Current/pickup PDF presentation regression coverage is missing.'
}
if ($documentsViewText -notmatch 'Transaction.Customer.DisplayName, Mode=OneWay') {
    throw 'The documents header must use a OneWay binding for the read-only customer display name.'
}
foreach ($newTest in @('PartialPickupSelectionTests.cs', 'TransactionDocumentsDialogViewModelTests.cs', 'PickupDialogLayoutTests.cs', 'PickupReceiptDatabaseTests.cs')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root "tests\EquipmentTracking.Tests\$newTest"))) {
        throw "Signed pickup coverage is missing '$newTest'."
    }
}

$printJobText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\PrintJobService.cs') -Raw
foreach ($requiredText in @('CreateTwoCopyLetterSheet', 'LetterWidthPoints = 612', 'LetterHeightPoints = 792', 'DuplicateWidgetAppearances', 'DrawReadableTextFields', 'ReadableTextFontFamily = "Arial"', 'ResolveTextStyle', 'NormalizePrintValue', 'WrapParagraph', 'DeleteTemporaryPrintJobAsync', 'QueueDeferredDeletion', 'ComputeSha256', 'ValidateGeneratedSheet')) {
    if ($printJobText -notmatch [regex]::Escape($requiredText)) {
        throw "Two-copy 1297 printing is missing '$requiredText'."
    }
}


$publishText = Get-Content (Join-Path $root 'scripts\publish.ps1') -Raw
foreach ($requiredText in @('security-scan.ps1', 'verify-offline.ps1', 'SHA256SUMS.txt', 'PublishSingleFile=false', 'PublishSingleFile=true', 'win-x64-single-file', 'IncludeNativeLibrariesForSelfExtract=true', 'IncludeAllContentForSelfExtract=true', 'PublishTrimmed=false')) {
    if ($publishText -notmatch [regex]::Escape($requiredText)) {
        throw "Publish hardening is missing '$requiredText'."
    }
}
if ($publishText -notmatch '(?s)verify-offline\.ps1.*?\$offlineReviewExitCode\s*=\s*\$LASTEXITCODE.*?if\s*\(\$offlineReviewExitCode\s*-ne\s*0\)\s*\{\s*throw.*?\}.*?Invoke-DotNetCommand') {
    throw 'Publish must stop immediately when the mandatory offline-runtime review returns a nonzero exit code.'
}

$releaseBuildText = Get-Content (Join-Path $root 'scripts\build-release.ps1') -Raw
foreach ($requiredText in @('A signed field candidate cannot skip tests', 'AllowUnsignedPilotBuild cannot be combined', 'TestsSkipped = [bool]$SkipTests', 'DatabaseSchemaVersion = 6')) {
    if ($releaseBuildText -notmatch [regex]::Escape($requiredText)) {
        throw "Release-build safety is missing '$requiredText'."
    }
}


$templatePath = Join-Path $root 'src\EquipmentTracking.App\Templates\1297-58SOW-SC-TEMPLATE.pdf'
$expectedTemplateHash = '00daa4ac652d592f03554e1d8e2ea9ec6083b30e9c9659330fe1dad32e599051'
$actualTemplateHash = (Get-FileHash -LiteralPath $templatePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualTemplateHash -ne $expectedTemplateHash) {
    throw "The included 1297 template hash does not match the validated cleaned template. Expected $expectedTemplateHash, found $actualTemplateHash."
}

$mainWindowText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\MainWindow.xaml') -Raw
foreach ($requiredText in @('Recovery', 'How to use', 'HowToUse', 'Icon="Assets/EquipmentTrackingPlatform.ico"', 'Source="Assets/EquipmentTrackingPlatform.png"')) {
    if ($mainWindowText -notmatch [regex]::Escape($requiredText)) {
        throw "Main navigation is missing '$requiredText'."
    }
}

$layoutStylesPath = Join-Path $root 'src\EquipmentTracking.App\Themes\LayoutStyles.xaml'
[xml]$layoutStylesXml = Get-Content $layoutStylesPath -Raw
$xamlNamespace = 'http://schemas.microsoft.com/winfx/2006/xaml'
$semanticBrushes = @(
    $layoutStylesXml.SelectNodes(
        '/*[local-name()="ResourceDictionary"]/*[local-name()="SolidColorBrush"]') |
        ForEach-Object { $_.GetAttribute('Key', $xamlNamespace) } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
$requiredSemanticBrushes = @(
    'AppBackgroundBrush', 'NavigationBackgroundBrush', 'SurfaceBrush',
    'SurfaceSecondaryBrush', 'SurfaceTertiaryBrush', 'InputSurfaceBrush',
    'HoverSurfaceBrush', 'BorderNeutralBrush', 'BorderStrongBrush',
    'TextPrimaryBrush', 'TextSecondaryBrush', 'TextMutedBrush',
    'TextDisabledBrush', 'AccentBrush', 'AccentFillBrush', 'AccentHoverBrush',
    'AccentPressedBrush', 'AccentLightBrush', 'SelectionBrush',
    'SameTransactionBrush', 'FocusBrush',
    'SuccessBrush', 'SuccessSurfaceBrush', 'WarningBrush', 'WarningSurfaceBrush',
    'DangerBrush', 'DangerSurfaceBrush', 'InfoBrush', 'InfoSurfaceBrush',
    'OverlayBrush', 'ButtonTextOnAccentBrush'
)
foreach ($brushName in $requiredSemanticBrushes) {
    if ($brushName -notin $semanticBrushes) {
        throw "The shared design system is missing semantic brush '$brushName'."
    }
}

$layoutStylesText = Get-Content $layoutStylesPath -Raw
foreach ($requiredStyle in @(
    'DefaultFocusVisualStyle', 'PrimaryButtonStyle', 'SecondaryButtonStyle',
    'DangerButtonStyle', 'NavigationButtonStyle', 'CardStyle',
    'EmptyStatePanelStyle', 'DialogWindowStyle')) {
    if ($layoutStylesText -notmatch ('x:Key="' + [regex]::Escape($requiredStyle) + '"')) {
        throw "The shared design system is missing style '$requiredStyle'."
    }
}

$sharedTextBoxStyle = $layoutStylesXml.SelectSingleNode(
    '/*[local-name()="ResourceDictionary"]/*[local-name()="Style" and @TargetType="TextBox" and not(@*[local-name()="Key"])]')
if ($null -eq $sharedTextBoxStyle) {
    throw 'The shared TextBox style is missing.'
}
$verticalContentAlignmentSetter = $sharedTextBoxStyle.SelectSingleNode(
    './*[local-name()="Setter" and @Property="VerticalContentAlignment"]')
if ($null -eq $verticalContentAlignmentSetter -or
    $verticalContentAlignmentSetter.GetAttribute('Value') -ne 'Center') {
    throw 'The shared TextBox style must keep text vertically centered.'
}
$sharedTextBoxContentHosts = @(
    $sharedTextBoxStyle.SelectNodes(
        './/*[local-name()="ScrollViewer" and @*[local-name()="Name"]="PART_ContentHost"]'))
if ($sharedTextBoxContentHosts.Count -ne 1) {
    throw "The shared TextBox template must contain exactly one PART_ContentHost. Found $($sharedTextBoxContentHosts.Count)."
}
$sharedTextBoxContentHost = $sharedTextBoxContentHosts[0]
if ($sharedTextBoxContentHost.HasAttribute('Padding') -or
    $sharedTextBoxContentHost.HasAttribute('Margin')) {
    throw 'The shared TextBox PART_ContentHost must not reapply TextBox.Padding as Padding or Margin.'
}
if ($sharedTextBoxContentHost.HasAttribute('VerticalAlignment')) {
    throw 'The shared TextBox PART_ContentHost must remain stretched; host-level VerticalAlignment breaks multiline inputs.'
}

$themeText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\ThemeService.cs') -Raw
foreach ($brushName in $semanticBrushes) {
    $mappingCount = [regex]::Matches(
        $themeText,
        '\["' + [regex]::Escape($brushName) + '"\]\s*=').Count
    if ($mappingCount -ne 2) {
        throw "Semantic brush '$brushName' must be mapped exactly once in both light and dark palettes. Found $mappingCount mapping(s)."
    }
}

function Get-RelativeLuminance {
    param([Parameter(Mandatory)][string]$Color)

    $hex = $Color.TrimStart('#')
    if ($hex.Length -ne 6) {
        throw "Expected a six-digit RGB color, found '$Color'."
    }

    [double[]]$channels = foreach ($offset in @(0, 2, 4)) {
        $normalizedChannel = [Convert]::ToInt32($hex.Substring($offset, 2), 16) / 255.0
        if ($normalizedChannel -le 0.04045) {
            [double]($normalizedChannel / 12.92)
        }
        else {
            [double]([Math]::Pow(($normalizedChannel + 0.055) / 1.055, 2.4))
        }
    }

    return [double](0.2126 * $channels[0] + 0.7152 * $channels[1] + 0.0722 * $channels[2])
}

function Get-ContrastRatio {
    param(
        [Parameter(Mandatory)][string]$Foreground,
        [Parameter(Mandatory)][string]$Background
    )

    $first = Get-RelativeLuminance $Foreground
    $second = Get-RelativeLuminance $Background
    $lighter = [Math]::Max($first, $second)
    $darker = [Math]::Min($first, $second)
    return ($lighter + 0.05) / ($darker + 0.05)
}

$paletteMatches = [regex]::Matches(
    $themeText,
    '(?s)(?<name>LightColors|DarkColors)\s*=\s*.*?\{(?<body>.*?)\n\s*\};')
if ($paletteMatches.Count -ne 2) {
    throw 'Theme validation could not identify both light and dark color palettes.'
}

$contrastPairs = @(
    @('TextPrimaryBrush', 'AppBackgroundBrush'),
    @('TextPrimaryBrush', 'SurfaceBrush'),
    @('TextSecondaryBrush', 'SurfaceBrush'),
    @('TextMutedBrush', 'SurfaceBrush'),
    @('ButtonTextOnAccentBrush', 'AccentFillBrush'),
    @('ButtonTextOnAccentBrush', 'AccentHoverBrush'),
    @('ButtonTextOnAccentBrush', 'AccentPressedBrush'),
    @('DangerBrush', 'DangerSurfaceBrush'),
    @('DangerBrush', 'HoverSurfaceBrush'),
    @('WarningBrush', 'WarningSurfaceBrush'),
    @('SuccessBrush', 'SuccessSurfaceBrush'),
    @('InfoBrush', 'InfoSurfaceBrush')
)
foreach ($paletteMatch in $paletteMatches) {
    $palette = @{}
    foreach ($entry in [regex]::Matches(
        $paletteMatch.Groups['body'].Value,
        '\["(?<key>[^"]+)"\]\s*=\s*"(?<value>#[0-9A-Fa-f]{6})"')) {
        $palette[$entry.Groups['key'].Value] = $entry.Groups['value'].Value
    }

    foreach ($pair in $contrastPairs) {
        if (-not $palette.ContainsKey($pair[0]) -or -not $palette.ContainsKey($pair[1])) {
            throw "Theme palette '$($paletteMatch.Groups['name'].Value)' is missing a contrast-test color."
        }

        $foreground = [string]$palette[$pair[0]]
        $background = [string]$palette[$pair[1]]
        $ratio = Get-ContrastRatio -Foreground $foreground -Background $background
        if ($ratio -lt 4.5) {
            throw "Theme palette '$($paletteMatch.Groups['name'].Value)' fails WCAG AA contrast for $($pair[0]) on $($pair[1]): $([Math]::Round($ratio, 2)):1."
        }
    }
}

$recoveryViewText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Views\RecoveryView.xaml') -Raw
if ($recoveryViewText -notmatch 'SelectedDetails,\s*Mode=OneWay') {
    throw 'Recovery SelectedDetails must remain a OneWay binding.'
}
if ($intakeText -match 'remain controlled by the PDF JavaScript') {
    throw 'New Intake contains obsolete PDF JavaScript guidance.'
}

$journalText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\WorkflowJournalService.cs') -Raw
foreach ($requiredText in @('IntakeFinalization', 'Closeout', 'MarkCompletedAsync', 'MarkAbandonedAsync', 'CommitTemporaryFile')) {
    if ($journalText -notmatch [regex]::Escape($requiredText)) {
        throw "Workflow recovery implementation is missing '$requiredText'."
    }
}

$backupText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\BackupService.cs') -Raw
foreach ($requiredText in @('DatabaseSha256', 'SettingsSha256', 'VerifyDatabaseFileAsync', 'ApplyPendingRestoreIfAnyAsync', 'StageRestoreAsync', 'CreateScheduledFullBackupAsync', 'CreateScheduledDifferentialBackupAsync', 'ApplyScheduledRetentionAsync', 'CompletedPdfRoot', 'RecordFiles', 'DeletedRecordPaths', 'ValidateProtectedRecordPathsAsync', 'EnsureNoChildReparsePoint')) {
    if ($backupText -notmatch [regex]::Escape($requiredText)) {
        throw "Backup/restore implementation is missing '$requiredText'."
    }
}
$backupManifestText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Models\BackupManifest.cs') -Raw
foreach ($requiredText in @('CurrentFormatVersion = 3', 'DeletedRecordPaths')) {
    if ($backupManifestText -notmatch [regex]::Escape($requiredText)) {
        throw "Backup manifest v3 is missing '$requiredText'."
    }
}

$databaseText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\DatabaseService.cs') -Raw
foreach ($requiredText in @('CurrentSchemaVersion = 6', 'SchemaMigrations', 'FileArtifacts', 'ModelCatalog', 'PartNumber', 'ResetOperationalDataAsync', 'PartialPickups', 'PartialPickupDevices', 'CommitPickupReceiptAsync')) {
    if ($databaseText -notmatch [regex]::Escape($requiredText)) {
        throw "Migration/release-readiness database implementation is missing '$requiredText'."
    }
}

$recordCodeText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\RecordCodeService.cs') -Raw
foreach ($requiredText in @('ETP1297:', 'GeneratePng', 'TransactionIdRegex')) {
    if ($recordCodeText -notmatch [regex]::Escape($requiredText)) {
        throw "Printed 1297 record-code support is missing '$requiredText'."
    }
}

$updateText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\UpdateService.cs') -Raw
foreach ($requiredText in @('ExpectedUpgradeCode', 'release-manifest.json', 'SHA256.HashDataAsync', 'msiexec.exe', '/passive')) {
    if ($updateText -notmatch [regex]::Escape($requiredText)) {
        throw "Offline MSI update support is missing '$requiredText'."
    }
}
$authenticodeText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\AuthenticodeVerifier.cs') -Raw
foreach ($requiredText in @('WinVerifyTrust', 'WTD_STATEACTION_VERIFY', 'WTD_STATEACTION_CLOSE')) {
    if ($authenticodeText -notmatch [regex]::Escape($requiredText)) {
        throw "Offline update signature verification is missing '$requiredText'."
    }
}

$settingsText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Models\AppSettings.cs') -Raw
foreach ($requiredText in @('BackupRetentionCount', 'ScheduledBackupsEnabled', 'ScheduledBackupFolder', 'TemporaryFileRetentionDays', 'AllowTestDataReset', 'PickupSignatureFieldName = "Pickup Signature"')) {
    if ($settingsText -notmatch [regex]::Escape($requiredText)) {
        throw "Release-readiness setting is missing '$requiredText'."
    }
}

$scheduledBackupText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\ScheduledBackupService.cs') -Raw
foreach ($requiredText in @('MondayFullBackupTime = new(9, 0, 0)', 'WeekdayDifferentialBackupTime = new(16, 0, 0)', 'RunDueBackupsAsync', 'ApplyScheduledRetentionAsync', 'GetLatestDueDifferentialDate', 'previousWeekMonday', 'StateFullBackupIsUsableAsync', 'VerifyBackupAsync', 'BackupRunConfiguration')) {
    if ($scheduledBackupText -notmatch [regex]::Escape($requiredText)) {
        throw "Scheduled backup implementation is missing '$requiredText'."
    }
}

if (-not [bool]$settings.ScheduledBackupsEnabled) {
    throw 'Scheduled backups must be enabled in the default field-test configuration.'
}
if ([string]::IsNullOrWhiteSpace([string]$settings.ScheduledBackupFolder)) {
    throw 'A default scheduled backup folder is required.'
}

$installerText = Get-Content (Join-Path $root 'installer\EquipmentTracking.Installer\Package.wxs') -Raw
foreach ($requiredText in @('Scope="perMachine"', 'MajorUpgrade', 'Schedule="afterInstallInitialize"', 'UpgradeCode=', 'ProgramFiles64Folder', 'ComponentGroup Id="ApplicationFiles"', 'EquipmentTrackingPlatform.exe', 'Icon Id="EquipmentTrackingPlatform.ico"', 'Property Id="ARPPRODUCTICON" Value="EquipmentTrackingPlatform.ico"')) {
    if ($installerText -notmatch [regex]::Escape($requiredText)) {
        throw "Machine-wide MSI authoring is missing '$requiredText'."
    }
}
if ($installerText -match 'AllowSameVersionUpgrades') {
    throw 'MSI authoring must not permit same-version major upgrades. Increment the first three numeric ProductVersion fields for every release.'
}

[xml]$installerXml = $installerText
foreach ($shortcutComponentId in @('StartMenuShortcutComponent', 'DesktopShortcutComponent')) {
    $componentNode = $installerXml.SelectSingleNode("//*[local-name()='Component' and @Id='$shortcutComponentId']")
    if ($null -eq $componentNode) {
        throw "MSI shortcut component '$shortcutComponentId' is missing."
    }

    $registryNode = $componentNode.SelectSingleNode("*[local-name()='RegistryValue']")
    if ($null -eq $registryNode -or
        $registryNode.GetAttribute('Root') -ne 'HKCU' -or
        $registryNode.GetAttribute('KeyPath') -ne 'yes') {
        throw "MSI shortcut component '$shortcutComponentId' must use an HKCU registry value as its key path for ICE38/ICE43 compliance."
    }

    $shortcutNode = $componentNode.SelectSingleNode("*[local-name()='Shortcut']")
    if ($null -eq $shortcutNode -or
        $shortcutNode.GetAttribute('Target') -ne '[INSTALLFOLDER]EquipmentTrackingPlatform.exe') {
        throw "MSI shortcut component '$shortcutComponentId' must use the formatted INSTALLFOLDER target to avoid an ICE69 cross-component reference."
    }
}

$installerProjectText = Get-Content (Join-Path $root 'installer\EquipmentTracking.Installer\EquipmentTracking.Installer.wixproj') -Raw
if ($installerProjectText -notmatch [regex]::Escape('WixToolset.Sdk/6.0.2')) {
    throw 'The installer project must pin the reviewed WiX Toolset SDK version 6.0.2.'
}
[xml]$installerProject = $installerProjectText
$installerProductVersionNode = $installerProject.SelectSingleNode('/Project/PropertyGroup/ProductVersion')
$installerProductVersion = if ($null -ne $installerProductVersionNode) { [string]$installerProductVersionNode.InnerText } else { '' }
if ($installerProductVersion -ne $expectedMsiVersion) {
    throw "Fallback MSI product version '$installerProductVersion' is inconsistent with application version '$version'. Expected '$expectedMsiVersion'."
}

$releaseScriptText = Get-Content (Join-Path $root 'scripts\build-release.ps1') -Raw
foreach ($requiredText in @('AllowUnsignedPilotBuild', 'UNSIGNED-PILOT', 'AuthenticodeSigned', "ReportPrefix 'installer'", 'major/minor 255, build 65535', "@('build-server', 'shutdown')")) {
    if ($releaseScriptText -notmatch [regex]::Escape($requiredText)) {
        throw "Release build safety is missing '$requiredText'."
    }
}
$releaseDisabledServerCommands = [regex]::Matches(
    $releaseScriptText,
    [regex]::Escape("'--disable-build-servers'"))
if ($releaseDisabledServerCommands.Count -lt 4) {
    throw 'Release restore, test, installer restore, and installer build must all disable persistent build servers.'
}

$publishScriptText = Get-Content (Join-Path $root 'scripts\publish.ps1') -Raw
$publishDisabledServerCommands = [regex]::Matches(
    $publishScriptText,
    [regex]::Escape("'--disable-build-servers'"))
if ($publishDisabledServerCommands.Count -lt 3) {
    throw 'Publish restore and both publish modes must disable persistent build servers.'
}

$preflightText = Get-Content (Join-Path $root 'src\EquipmentTracking.App\Services\PreflightService.cs') -Raw
foreach ($requiredText in @('ApprovedTemplateSha256', 'SHA256.HashData', 'sharesDataDrive || sharesCompletedDrive')) {
    if ($preflightText -notmatch [regex]::Escape($requiredText)) {
        throw "Field preflight safety is missing '$requiredText'."
    }
}

Write-Host 'Project structure, JSON, XML/XAML, semantic themes, WCAG AA contrast, keyboard focus styles, Dashboard search alignment and accessible pie-chart hover, portable single-file and conventional MSI packaging, active-CAC enumeration, resumable workflows, scheduled full/differential backup and restore, database migrations, offline verification, support diagnostics, clean template, two-copy Letter printing, archive workflow, intake editing, organization configuration, and PDF mappings passed validation.'
