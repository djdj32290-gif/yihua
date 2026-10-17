$ErrorActionPreference = 'Stop'
$taskProjectRoot = Split-Path $PSScriptRoot -Parent
$taskWorkRoot = Join-Path $taskProjectRoot 'work'
$taskEnvironment = @{
    TEMP = Join-Path $taskWorkRoot 'temp'
    TMP = Join-Path $taskWorkRoot 'temp'
    APPDATA = Join-Path $taskWorkRoot 'appdata'
    LOCALAPPDATA = Join-Path $taskWorkRoot 'localappdata'
    DOTNET_CLI_HOME = Join-Path $taskWorkRoot 'dotnet-home'
    DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $taskWorkRoot 'dotnet-bundles'
    NUGET_PACKAGES = Join-Path $taskWorkRoot 'nuget-packages'
    NUGET_HTTP_CACHE_PATH = Join-Path $taskWorkRoot 'nuget-http'
    NUGET_PLUGINS_CACHE_PATH = Join-Path $taskWorkRoot 'nuget-plugins'
    NUGET_SCRATCH = Join-Path $taskWorkRoot 'nuget-scratch'
    MSBUILDDEBUGPATH = Join-Path $taskWorkRoot 'msbuild'
}
foreach ($taskEntry in $taskEnvironment.GetEnumerator()) {
    if ([IO.Path]::GetPathRoot($taskEntry.Value) -eq 'C:\') { throw "构建目录不得指向 C 盘：$($taskEntry.Value)" }
    New-Item -ItemType Directory -Path $taskEntry.Value -Force | Out-Null
    [Environment]::SetEnvironmentVariable($taskEntry.Key, $taskEntry.Value, 'Process')
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_XMLDOC_MODE = 'skip'
$taskFallbackPackages = Join-Path $taskProjectRoot '.tools\packages'
if (Test-Path -LiteralPath $taskFallbackPackages) { $env:NUGET_FALLBACK_PACKAGES = $taskFallbackPackages }
else { Remove-Item Env:\NUGET_FALLBACK_PACKAGES -ErrorAction SilentlyContinue }

function Remove-ProjectBuildStage {
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][ValidateSet('publish','package')][string]$Kind)
    $taskStagePath=[IO.Path]::GetFullPath($Path)
    $taskExpectedParent=[IO.Path]::GetFullPath((Join-Path $taskProjectRoot 'work'))
    if ((Split-Path $taskStagePath -Parent) -ne $taskExpectedParent -or !(Split-Path $taskStagePath -Leaf).StartsWith($Kind+'-')) { throw "拒绝清理非构建暂存目录：$taskStagePath" }
    if (!(Test-Path -LiteralPath $taskStagePath)) { return }
    if ((Get-Item -LiteralPath $taskStagePath).Attributes -band [IO.FileAttributes]::ReparsePoint -or @(Get-ChildItem -LiteralPath $taskStagePath -Recurse -Force -Attributes ReparsePoint).Count -gt 0) { throw "暂存目录包含链接，未清理：$taskStagePath" }
    Remove-Item -LiteralPath $taskStagePath -Recurse -Force
}
