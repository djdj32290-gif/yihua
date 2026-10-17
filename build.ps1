param([string]$Dotnet = '', [string]$OutputDirectory = '',[switch]$KeepStage)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'src\BuildEnvironment.ps1')
if (!$Dotnet) { $Dotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'; if (!(Test-Path -LiteralPath $Dotnet)) { $Dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source } }
$project = Join-Path $PSScriptRoot 'src\OrbitLauncher\OrbitLauncher.csproj'
$out = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot 'deliverables\一划便携版' }
$stage = Join-Path $PSScriptRoot ('work\publish-' + [Guid]::NewGuid().ToString('N'))
$taskBuildCache=(Join-Path $PSScriptRoot 'work\build\OrbitLauncher').Replace('\','/')
& $Dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:UseSharedCompilation=false "-p:BaseOutputPath=$taskBuildCache/bin/" "-p:BaseIntermediateOutputPath=$taskBuildCache/obj/" -nodeReuse:false -o $stage --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
New-Item -ItemType Directory -Path $out -Force | Out-Null
$existingExe = Join-Path $out '一划.exe'
$running = @(Get-Process 一划 -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $existingExe })
if ($running.Count -gt 0) {
    Start-Process -FilePath $existingExe -ArgumentList '--exit' -WindowStyle Hidden -Wait
    foreach ($instance in $running) { if (!$instance.WaitForExit(7000)) { throw '一划尚未退出，未覆盖文件。请先退出程序再构建。' } }
}
Get-ChildItem -LiteralPath $stage | Where-Object { $_.Name -ne 'OrbitLauncher.exe' } | Copy-Item -Destination $out -Recurse -Force
Copy-Item (Join-Path $stage 'OrbitLauncher.exe') $existingExe -Force
Copy-Item (Join-Path $PSScriptRoot 'README.md') $out -Force
Write-Host "编译完成：$existingExe"
if (!$KeepStage) { Remove-ProjectBuildStage -Path $stage -Kind publish }
