[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$Version = '1.0.1',
    [switch]$SkipInstaller,
    [switch]$RequireInstaller,
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'AutoPilotInput\AutoPilotInput.csproj'
$artifactRoot = Join-Path $repoRoot 'artifacts'
$portablePath = Join-Path $artifactRoot 'keytempo-portable'
$releasePath = Join-Path $artifactRoot 'keytempo-release'
$zipPath = Join-Path $releasePath "KeyTempo-$Version-win-x64-portable.zip"
$sourceZipPath = Join-Path $releasePath "KeyTempo-$Version-source.zip"
$directExecutablePath = Join-Path $releasePath 'KeyTempo.exe'

if ($SkipInstaller -and $RequireInstaller) { throw 'SkipInstaller and RequireInstaller cannot be combined.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 8 SDK and open a new terminal before building.'
}

New-Item -ItemType Directory -Force -Path $portablePath, $releasePath | Out-Null
& dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    "-p:Version=$Version" -o $portablePath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath (Join-Path $portablePath 'KeyTempo.exe'))) {
    throw 'The expected portable executable was not created.'
}
Copy-Item -LiteralPath (Join-Path $portablePath 'KeyTempo.exe') -Destination $directExecutablePath -Force

$documentationPath = Join-Path $portablePath 'docs'
New-Item -ItemType Directory -Force -Path $documentationPath | Out-Null
foreach ($file in @('LICENSE', 'README.md', 'THIRD_PARTY_NOTICES.md', 'BUILD.md', 'CONTRIBUTING.md', 'CODE_OF_CONDUCT.md', 'CONTEXT.md')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $portablePath -Force
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\USER_GUIDE.md') -Destination $documentationPath -Force
$screenshotPath = Join-Path $repoRoot 'docs\screenshots'
if (Test-Path -LiteralPath $screenshotPath) {
    Copy-Item -LiteralPath $screenshotPath -Destination $documentationPath -Recurse -Force
}
$licensePath = Join-Path $repoRoot 'docs\licenses'
if (Test-Path -LiteralPath $licensePath) {
    Copy-Item -LiteralPath $licensePath -Destination $documentationPath -Recurse -Force
}
Compress-Archive -Path (Join-Path $portablePath '*') -DestinationPath $zipPath -Force
Write-Host "Portable executable: $(Join-Path $portablePath 'KeyTempo.exe')"
Write-Host "Direct download executable: $directExecutablePath"
Write-Host "Portable archive: $zipPath"

# Package only project files; build directories and local tools never enter the source archive.
$sourceStage = Join-Path $artifactRoot ('source-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sourceStage | Out-Null
try {
    $sourceDirectories = @('.github', 'AutoPilotInput', 'docs', 'installer', 'scripts', 'tools')
    $sourceDirectories += @(Get-ChildItem -LiteralPath $repoRoot -Directory -Filter '*Tests*' | Select-Object -ExpandProperty Name)
    foreach ($directory in $sourceDirectories) {
        $sourceDirectory = Join-Path $repoRoot $directory
        if (-not (Test-Path -LiteralPath $sourceDirectory)) { continue }
        $sourceFiles = Get-ChildItem -LiteralPath $sourceDirectory -File -Recurse -Force |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.vs)[\\/]' }
        foreach ($file in $sourceFiles) {
            $relativePath = $file.FullName.Substring($repoRoot.Length).TrimStart([char[]]'\/')
            $destinationPath = Join-Path $sourceStage $relativePath
            New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destinationPath)) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destinationPath
        }
    }
    Get-ChildItem -LiteralPath $repoRoot -File -Force |
        Where-Object { $_.Name -eq 'LICENSE' -or $_.Name -eq '.gitignore' -or $_.Extension -in '.md', '.sln', '.slnx', '.props', '.targets' } |
        Copy-Item -Destination $sourceStage
    Compress-Archive -Path (Join-Path $sourceStage '*') -DestinationPath $sourceZipPath -Force
}
finally {
    $resolvedSourceStage = [IO.Path]::GetFullPath($sourceStage)
    $expectedParent = [IO.Path]::GetFullPath($artifactRoot).TrimEnd([char[]]'\/')
    if ([IO.Path]::GetDirectoryName($resolvedSourceStage) -ne $expectedParent -or [IO.Path]::GetFileName($resolvedSourceStage) -notmatch '^source-[a-f0-9]{32}$') {
        throw 'Source archive staging directory validation failed.'
    }
    Remove-Item -LiteralPath $resolvedSourceStage -Recurse -Force
}
Write-Host "Source archive: $sourceZipPath"

if (-not $SkipInstaller) {
    if (-not $InnoCompiler) {
        $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        $compilerCandidates = @(
            $(if ($isccCommand) { $isccCommand.Source }),
            $(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }),
            $(if ($env:ProgramFiles) { Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe' }),
            $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }),
            (Join-Path $repoRoot '.tools\innosetup-6.4.3\tools\ISCC.exe')
        )
        $InnoCompiler = $compilerCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
    }
    if ($InnoCompiler) {
        if (-not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) { throw "Inno Setup compiler not found: $InnoCompiler" }
        & $InnoCompiler "/DMyAppVersion=$Version" "/DSourceDir=$portablePath" `
            "/DOutputDir=$releasePath" (Join-Path $repoRoot 'installer\AutoPilotInput.iss')
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
        $installerPath = Join-Path $releasePath "KeyTempo-$Version-win-x64-setup.exe"
        if (-not (Test-Path -LiteralPath $installerPath)) { throw 'The expected installer was not created.' }
        Write-Host "Installer: $installerPath"
    }
    elseif ($RequireInstaller) {
        throw 'Inno Setup 6 is required. Install it or supply -InnoCompiler with the full ISCC.exe path.'
    }
    else {
        Write-Warning 'Inno Setup 6 was not found. The portable release is ready; install Inno Setup to also build an installer.'
    }
}

$releaseFiles = @($directExecutablePath, $zipPath, $sourceZipPath)
$builtInstaller = Join-Path $releasePath "KeyTempo-$Version-win-x64-setup.exe"
if (-not $SkipInstaller -and $InnoCompiler -and (Test-Path -LiteralPath $builtInstaller)) { $releaseFiles += $builtInstaller }
$checksumLines = foreach ($releaseFile in $releaseFiles) {
    $hash = Get-FileHash -LiteralPath $releaseFile -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), [IO.Path]::GetFileName($releaseFile)
}
$checksumLines | Set-Content -LiteralPath (Join-Path $releasePath 'SHA256SUMS.txt') -Encoding ascii

