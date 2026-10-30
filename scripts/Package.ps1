param(
    [string]$DalamudLibPath = $env:DALAMUD_HOME,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) {
    if ([string]::IsNullOrWhiteSpace($DalamudLibPath)) { throw '请指定 -DalamudLibPath 或设置 DALAMUD_HOME。' }
    & dotnet build (Join-Path $projectRoot 'MakoMaster.csproj') -c Release --nologo "-p:DalamudLibPath=$DalamudLibPath"
    if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
}
$dll = Join-Path $projectRoot 'bin/Release/net10.0-windows/MakoMaster.dll'
$manifestFile = Join-Path $projectRoot 'MakoMaster.json'
$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
$identity = [Reflection.AssemblyName]::GetAssemblyName($dll)
if ($identity.Name -ne 'MakoMaster' -or $manifest.InternalName -ne 'MakoMaster' -or $manifest.Name -ne 'MakoMaster') { throw '项目名称不一致。' }
if ($identity.Version.ToString() -ne $manifest.AssemblyVersion) { throw 'DLL 与插件清单版本不一致。' }
$bytes = [IO.File]::ReadAllBytes($dll)
$profilePath = [Environment]::GetFolderPath('UserProfile')
foreach ($text in @([Text.Encoding]::UTF8.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes))) {
    foreach ($prefix in @($profilePath, $profilePath.Replace('\', '/'))) {
        if ($prefix.Length -gt 4 -and $text.Contains($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'DLL 包含本机用户目录，取消打包。' }
    }
    if ($text -match '[A-Za-z]:[\\/]Users[\\/]|/Users/|/home/') { throw 'DLL 包含个人目录路径，取消打包。' }
}
$dist = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$version = $identity.Version.ToString()
$zipName = "MakoMaster-$version.zip"
$zip = Join-Path $dist $zipName
$files = @($dll, $manifestFile) + @('dictionary.json', 'learned-dictionary.json', 'interruptible-skills.json', 'README.md', 'README.en.md' | ForEach-Object { Join-Path $projectRoot $_ })
Compress-Archive -LiteralPath $files -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $dist "$zipName.sha256"), "$hash  $zipName`n", [Text.UTF8Encoding]::new($false))
$repository = 'https://github.com/DocherPap/MakoMaster'
$download = "https://raw.githubusercontent.com/DocherPap/MakoMaster/main/dist/$zipName"
$entry = $manifest | ConvertTo-Json -Depth 10 | ConvertFrom-Json
foreach ($pair in @{
    RepoUrl = $repository
    DownloadLinkInstall = $download
    DownloadLinkUpdate = $download
    IsHide = $false
    IsTestingExclusive = $false
    LastUpdate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
}.GetEnumerator()) { $entry | Add-Member -NotePropertyName $pair.Key -NotePropertyValue $pair.Value -Force }
[IO.File]::WriteAllText((Join-Path $projectRoot 'repo.json'), (ConvertTo-Json -InputObject @($entry) -Depth 10) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "已生成 dist/$zipName 和 repo.json；尚需提交并推送仓库才可在线安装。"
