# NetSeep 一键构建脚本
#   只用 Windows 自带的 .NET Framework 编译器（csc.exe），无需安装 Visual Studio / .NET SDK。
#   如能找到 Windows SDK 的 rc.exe，会额外写入图标、清单和完整版本信息（有利于降低杀软误报）。
#   -Minimal  构建“精简版”：编译掉注册表自启、全局热键、鼠标穿透、控制台输出等敏感功能，
#             用于排查杀软误报到底是不是由这些行为特征引起的。
#   -Pfx      构建后用 signtool 做 Authenticode 签名（需要你自己的代码签名证书；这是根治误报的办法）。
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Run,
    [switch]$Preview,
    [switch]$StopRunning,
    [switch]$Minimal,
    [string]$Pfx = "",
    [string]$PfxPassword = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$OutDir = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($OutDir)) { $OutDir = Join-Path $root 'dist' }
$obj = Join-Path $root 'obj'

function Find-Csc {
    $candidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw '未找到 C# 编译器 csc.exe（需要 .NET Framework 4.x，Windows 7 及以上系统自带）'
}

function Find-Rc {
    $bases = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path $env:ProgramFiles 'Windows Kits\10\bin')
    )
    foreach ($b in $bases) {
        if (-not (Test-Path $b)) { continue }
        $hit = Get-ChildItem $b -Recurse -Filter rc.exe -ErrorAction SilentlyContinue |
               Where-Object { $_.FullName -match '\\x64\\rc\.exe$' } |
               Sort-Object FullName -Descending | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

$csc = Find-Csc
Write-Host "编译器 : $csc"

# 目标 exe 正在运行时无法被覆盖或删除，先提示（或按 -StopRunning 自动结束）
$running = Get-Process -Name 'NetSeep' -ErrorAction SilentlyContinue
if ($running) {
    if ($StopRunning) {
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 500
        Write-Host '已结束正在运行的 NetSeep 实例' -ForegroundColor Yellow
    } else {
        Write-Host '提示 : NetSeep 正在运行，编译会失败。可加 -StopRunning 自动结束它。' -ForegroundColor Yellow
    }
}

if ($Clean) {
    foreach ($d in @($OutDir, $obj)) {
        if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    }
    Write-Host '已清理输出目录'
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
New-Item -ItemType Directory -Force -Path $obj | Out-Null

$exeName = 'NetSeep.exe'
if ($Minimal) { $exeName = 'NetSeep-minimal.exe' }
$exe = Join-Path $OutDir $exeName
$icon = Join-Path $root 'assets\netseep.ico'
$manifest = Join-Path $root 'src\app.manifest'
$rcScript = Join-Path $root 'src\app.rc'

$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | Sort-Object Name
if ($sources.Count -eq 0) { throw 'src 目录下没有找到 .cs 源码' }

$cscArgs = @(
    '/nologo'
    '/noconfig'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/warn:4'
    '/codepage:65001'
    '/reference:System.dll'
    '/reference:System.Core.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
    "/out:$exe"
)

# ---- 资源：优先用 rc.exe 打包成完整 .res（图标 + 清单 + 版本信息）----
$res = $null
$rc = Find-Rc
if ($rc -and (Test-Path $rcScript) -and (Test-Path $icon) -and (Test-Path $manifest)) {
    $resDir = Join-Path $obj 'res'
    New-Item -ItemType Directory -Force -Path $resDir | Out-Null
    Copy-Item $icon (Join-Path $resDir 'netseep.ico') -Force
    Copy-Item $manifest (Join-Path $resDir 'app.manifest') -Force
    # rc.exe 对 UTF-8 的支持依赖 code_page 指令，这里统一转成 UTF-16LE 最稳妥
    $rcText = [System.IO.File]::ReadAllText($rcScript, [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText((Join-Path $resDir 'app.rc'), $rcText, [System.Text.Encoding]::Unicode)

    $res = Join-Path $obj 'app.res'
    & $rc /nologo /fo $res (Join-Path $resDir 'app.rc')
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'rc.exe 编译资源失败，回退为只嵌图标+清单' -ForegroundColor Yellow
        $res = $null
    } else {
        $cscArgs += "/win32res:$res"
        Write-Host "资源   : 图标 + 清单 + 版本信息（$rc）"
    }
}

if (-not $res) {
    if (Test-Path $manifest) { $cscArgs += "/win32manifest:$manifest" }
    if (Test-Path $icon) { $cscArgs += "/win32icon:$icon" }
    else { Write-Host '提示 : 未找到 assets\netseep.ico，将使用默认图标（可运行 tools\make_icon.py 生成）' -ForegroundColor Yellow }
    Write-Host '资源   : 图标 + 清单（无版本信息）' -ForegroundColor Yellow
}

$cscArgs += ($sources | ForEach-Object { $_.FullName })

if ($Minimal) {
    $cscArgs += '/define:MINIMAL_BUILD'
    Write-Host '模式   : 精简构建（编译掉注册表自启 / 全局热键 / 鼠标穿透 / 控制台输出）' -ForegroundColor Yellow
}

Write-Host "源码   : $($sources.Count) 个文件"
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }

# ---- 可选：Authenticode 数字签名（根治杀软误报与 SmartScreen 提示的唯一可靠办法）----
if (-not [string]::IsNullOrEmpty($Pfx)) {
    $signtool = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) {
        Write-Host '未找到 signtool.exe（需要 Windows SDK），跳过签名' -ForegroundColor Yellow
    } else {
        $signArgs = @('sign', '/fd', 'SHA256', '/f', $Pfx)
        if (-not [string]::IsNullOrEmpty($PfxPassword)) { $signArgs += @('/p', $PfxPassword) }
        if (-not [string]::IsNullOrEmpty($TimestampUrl)) { $signArgs += @('/tr', $TimestampUrl, '/td', 'SHA256') }
        $signArgs += $exe
        & $signtool.Source @signArgs
        if ($LASTEXITCODE -ne 0) { Write-Host '签名失败，产物未签名' -ForegroundColor Yellow }
        else { Write-Host '已用证书签名（可有效消除 SmartScreen 与大部分杀软误报）' -ForegroundColor Green }
    }
}

$item = Get-Item $exe
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
$sig = (Get-AuthenticodeSignature $exe).Status
Write-Host ("完成   : {0}  ({1:N1} KB)" -f $item.FullName, ($item.Length / 1KB)) -ForegroundColor Green
Write-Host ("SHA256 : {0}" -f $hash)
Write-Host ("签名   : {0}" -f $sig)
"$hash  $exeName" | Set-Content -Path (Join-Path $OutDir ($exeName + '.sha256')) -Encoding ASCII

if ($Preview) { & $exe --preview (Join-Path $OutDir 'netseep-preview.png') 2 }
if ($Run) { Start-Process -FilePath $exe }
