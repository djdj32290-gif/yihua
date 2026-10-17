param([string]$Version = '',[switch]$KeepStage)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'BuildEnvironment.ps1')
$taskRoot = Split-Path $PSScriptRoot -Parent
if (!$Version) { $Version=([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'OrbitLauncher\OrbitLauncher.csproj') -Raw)).Project.PropertyGroup.Version }
$taskPortable = Join-Path $taskRoot 'deliverables\一划便携版'
$taskVerification = Join-Path $taskRoot 'verification'
foreach ($taskKind in @('self-test','verify-hold','verify-ui')) {
    $taskResult = Get-Content -LiteralPath (Join-Path $taskVerification "final-$Version-$taskKind.json") -Raw | ConvertFrom-Json
    if (!$taskResult.Success) { throw "打包前验证未通过：$taskKind" }
}
$taskVersionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskPortable '一划.exe'))
if ($taskVersionInfo.ProductVersion.Split('+')[0] -ne $Version) { throw '运行版版本与打包版本不一致' }
$taskStage = Join-Path $taskRoot ('work\package-' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
$taskPortableStage = Join-Path $taskStage "一划-${Version}便携版"
$taskSourceStage = Join-Path $taskStage "一划-${Version}源码"
New-Item -ItemType Directory -Path $taskPortableStage,$taskSourceStage -Force | Out-Null
# Data is intentionally left in the live directory and excluded from the distribution.
Get-ChildItem -LiteralPath $taskPortable -File | Where-Object {$_.Name -notmatch '^(更新说明|修复说明|验证报告|快速开始|使用说明)'} | Copy-Item -Destination $taskPortableStage
$taskDocs = @('README.md','build.ps1','NuGet.Config','.gitignore','.gitattributes')
foreach ($taskDoc in $taskDocs) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $taskDoc) -Destination $taskSourceStage
    if ($taskDoc -eq 'README.md') { Copy-Item -LiteralPath (Join-Path $taskRoot $taskDoc) -Destination $taskPortableStage -Force }
}
Get-ChildItem -LiteralPath $PSScriptRoot -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object {
    $taskRelative = [IO.Path]::GetRelativePath($taskRoot,$_.FullName)
    $taskDestination = Join-Path $taskSourceStage $taskRelative
    New-Item -ItemType Directory -Path (Split-Path $taskDestination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $taskDestination
}
if (Test-Path -LiteralPath (Join-Path $taskRoot '.github')) { Copy-Item -LiteralPath (Join-Path $taskRoot '.github') -Destination $taskSourceStage -Recurse }
# README images belong to both packages so documentation renders after extraction too.
foreach ($taskDocsStage in @($taskSourceStage,$taskPortableStage)) {
    $taskScreenshots = Join-Path $taskRoot 'docs\screenshots'
    if (Test-Path -LiteralPath $taskScreenshots) {
        $taskDocsDestination = Join-Path $taskDocsStage 'docs\screenshots'
        New-Item -ItemType Directory -Path $taskDocsDestination -Force | Out-Null
        Get-ChildItem -LiteralPath $taskScreenshots -File -Filter '*.png' | Copy-Item -Destination $taskDocsDestination
    }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskPackages = @()
foreach ($taskPair in @(@($taskPortableStage,"一划-${Version}便携版.zip"),@($taskSourceStage,"一划-${Version}源码.zip"))) {
    # Generate a new file first so a packaging failure cannot remove an existing ZIP.
    $taskNewZip = Join-Path $taskStage $taskPair[1]
    [IO.Compression.ZipFile]::CreateFromDirectory($taskPair[0],$taskNewZip,[IO.Compression.CompressionLevel]::Optimal,$true)
    $taskArchive = [IO.Compression.ZipFile]::OpenRead($taskNewZip)
    try {
        $taskEntries = @($taskArchive.Entries | Where-Object { $_.Name.Length -gt 0 })
        foreach ($taskEntry in $taskEntries) {
            $taskEntryRelative = $taskEntry.FullName.Substring($taskEntry.FullName.IndexOf('/')+1)
            $taskExpected = Join-Path $taskPair[0] $taskEntryRelative
            $taskStream = $taskEntry.Open()
            try { $taskZipHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskStream)) } finally { $taskStream.Dispose() }
            if ($taskZipHash -ne (Get-FileHash -LiteralPath $taskExpected -Algorithm SHA256).Hash) { throw "ZIP 内容不一致：$($taskEntry.FullName)" }
        }
        if ($taskPair[1] -like '*便携版.zip' -and @($taskEntries | Where-Object FullName -Match '/Data/').Count -gt 0) { throw '便携分发包意外包含个人配置' }
        $taskPackages += [PSCustomObject]@{Name=$taskPair[1];Files=$taskEntries.Count;Bytes=(Get-Item -LiteralPath $taskNewZip).Length;ContentsVerified=$true}
    }
    finally { $taskArchive.Dispose() }
}
foreach ($taskPackage in $taskPackages) { Copy-Item -LiteralPath (Join-Path $taskStage $taskPackage.Name) -Destination (Join-Path $taskRoot ('deliverables\'+$taskPackage.Name)) -Force }
$taskPackages | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskVerification "final-$Version-packages.json") -Encoding utf8
$taskPackages | Format-Table -AutoSize
if (!$KeepStage) { Remove-ProjectBuildStage -Path $taskStage -Kind package }
