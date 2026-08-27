# Betty Translate 一键发布脚本
# 用法示例：
#   .\build-release.ps1                                  # 自动递增 patch 版本（0.5.2 -> 0.5.3）
#   .\build-release.ps1 -Version 0.6.0                   # 指定版本号
#   .\build-release.ps1 -Version 0.6.0 -Notes "新增功能A`n修复BugB"
#   .\build-release.ps1 -SkipPush -SkipRelease            # 只编译打包，不推送不打 Release
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Notes,
    [switch]$SkipPush,
    [switch]$SkipRelease
)

$ErrorActionPreference = 'Stop'

$repo = 'ljh5866/BettyTranslate'
$projectDir = Join-Path $PSScriptRoot 'src\BettyTranslate.App'
$csproj = Join-Path $projectDir 'BettyTranslate.App.csproj'
$outDir = Join-Path $PSScriptRoot 'release\BettyTranslate'

# 1. 确定版本号：优先用传入的，否则读取 csproj 并 patch+1
[xml]$xml = Get-Content -Raw -Path $csproj
$curVersion = $xml.Project.PropertyGroup.Version
if (-not $curVersion) {
    Write-Error '未能在 csproj 中读取到 <Version>，请检查。'
}
if (-not $Version) {
    $parts = $curVersion.Split('.')
    $parts[2] = [int]$parts[2] + 1
    $Version = ($parts -join '.')
}
if (-not $Notes) {
    $Notes = '自动发布 v' + $Version
}

Write-Host "==> 版本：$curVersion -> $Version" -ForegroundColor Cyan

# 2. 更新 csproj 中的 Version 与 InformationalVersion
$buildSuffix = 'build' + (Get-Date -Format 'yyyyMMdd')
$xml.Project.PropertyGroup.Version = $Version
$xml.Project.PropertyGroup.InformationalVersion = "$Version-$buildSuffix"
$xml.Save($csproj)
Write-Host "==> 已更新 csproj：$Version / $Version-$buildSuffix" -ForegroundColor Green

# 3. 关闭可能正在运行的旧版本，避免输出目录被占用导致压缩失败
Get-Process | Where-Object { $_.ProcessName -like 'BettyTranslate*' } -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }

# 4. 编译自包含便携包
Write-Host '==> 正在编译便携包...' -ForegroundColor Cyan
& dotnet publish $projectDir -c Release -r win-x64 --self-contained true -o $outDir
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish 失败，退出码 $LASTEXITCODE" }

# 4. 压缩为 zip
$zip = Join-Path $PSScriptRoot "release\BettyTranslate-v$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $zip
Write-Host "==> 已生成安装包：$zip" -ForegroundColor Green

if (-not $SkipPush) {
    # 5. 提交并推送源码（版本号改动都在 src 下）
    Write-Host '==> 提交并推送源码...' -ForegroundColor Cyan
    git add (Join-Path $PSScriptRoot 'src')
    if ($LASTEXITCODE -ne 0) { Write-Error 'git add 失败' }
    git commit -m "更新至 v$Version"
    if ($LASTEXITCODE -ne 0) { Write-Warning 'git commit 失败（可能没有改动），继续后续步骤。' }
    git push origin main
    if ($LASTEXITCODE -ne 0) { Write-Error "git push 失败，退出码 $LASTEXITCODE" }
}

if (-not $SkipRelease) {
    # 6. 创建 GitHub Release 并上传 zip
    Write-Host "==> 创建 Release v$Version 并上传安装包..." -ForegroundColor Cyan
    gh release create "v$Version" $zip --repo $repo --title "Betty Translate v$Version" --notes $Notes
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'gh release create 失败（可能 tag 已存在）。尝试查看并发布该 Release。'
        gh release view "v$Version" --repo $repo --json isDraft,name,assets --jq '{isDraft,name,assets:[.assets[].name]}'
    }
}

# 7. 验证
Write-Host '==> 验证 /releases/latest ...' -ForegroundColor Cyan
try {
    $r = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'BettyTranslate' }
    "tagName: $($r.tag_name)"
    "name: $($r.name)"
    $r.assets | ForEach-Object { "  $($_.name) -> $($_.browser_download_url)" }
} catch {
    Write-Warning "验证失败：$($_.Exception.Message)"
}

Write-Host '==> 发布完成。' -ForegroundColor Green
