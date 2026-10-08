# NetSeep 误报自检 / 处置助手
#   用法：
#     powershell -ExecutionPolicy Bypass -File tools\check-false-positive.ps1
#     powershell -ExecutionPolicy Bypass -File tools\check-false-positive.ps1 -Path dist\NetSeep.exe -Unblock
#   -Unblock  去掉“来自 Internet”标记（Mark-of-the-Web），这是被拦的常见原因之一
[CmdletBinding()]
param(
    [string]$Path = "",
    [switch]$Unblock
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

if ([string]::IsNullOrEmpty($Path)) {
    $candidates = @(
        (Join-Path $root 'dist\NetSeep.exe'),
        (Join-Path $root 'dist\NetSeep-minimal.exe')
    )
    foreach ($c in $candidates) { if (Test-Path $c) { $Path = $c; break } }
}

Write-Host '=============================================='
Write-Host ' NetSeep 误报自检'
Write-Host '=============================================='

if (-not (Test-Path $Path)) {
    Write-Host "找不到文件：$Path" -ForegroundColor Red
    Write-Host '请先用 build.ps1 编译，或用 -Path 指定 exe 路径。'
    exit 1
}

$file = Get-Item $Path
Write-Host ''
Write-Host '[1] 文件信息'
Write-Host ("    路径   : " + $file.FullName)
Write-Host ("    大小   : {0:N0} 字节" -f $file.Length)
Write-Host ("    SHA256 : " + (Get-FileHash $file.FullName -Algorithm SHA256).Hash)

$vi = $file.VersionInfo
Write-Host ("    产品名 : " + $vi.ProductName)
Write-Host ("    版本   : " + $vi.FileVersion)
Write-Host ("    描述   : " + $vi.FileDescription)
Write-Host ("    公司   : " + $vi.CompanyName)
if ([string]::IsNullOrEmpty($vi.ProductName)) {
    Write-Host '    [!] 没有版本信息，启发式查杀概率会明显上升（请用 build.ps1 重新编译）' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '[2] 数字签名'
$sig = Get-AuthenticodeSignature $file.FullName
Write-Host ("    状态   : " + $sig.Status)
if ($sig.Status -ne 'Valid') {
    Write-Host '    [!] 未签名。这是小工具被杀软误报最主要的原因，处理办法见文末 [5]。' -ForegroundColor Yellow
} else {
    Write-Host ("    签名者 : " + $sig.SignerCertificate.Subject)
}

Write-Host ''
Write-Host '[3] 文件来源标记（Mark-of-the-Web）'
$zoneStreams = @(Get-Item $file.FullName -Stream * -ErrorAction SilentlyContinue | Where-Object { $_.Stream -eq 'Zone.Identifier' })
if ($zoneStreams.Count -gt 0) {
    Write-Host '    [!] 该文件带“来自 Internet”标记，容易被 SmartScreen / 杀软拦下。' -ForegroundColor Yellow
    if ($Unblock) {
        try {
            Unblock-File -Path $file.FullName -ErrorAction Stop
            Write-Host '    已解除锁定（Unblock-File）' -ForegroundColor Green
        } catch {
            Write-Host ('    解除失败：' + $_.Exception.Message) -ForegroundColor Red
        }
    } else {
        Write-Host '    加 -Unblock 参数可自动解除；或右键 exe → 属性 → 勾选“解除锁定”。'
    }
} else {
    Write-Host '    干净：没有 Internet 来源标记。'
}

Write-Host ''
Write-Host '[4] 本机安装的安全软件'
try {
    $avs = Get-CimInstance -Namespace root\SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop
    foreach ($av in $avs) {
        $state = $av.productState
        $on = (($state -shr 12) -band 0xF) -eq 1
        Write-Host ("    - {0}  实时防护：{1}" -f $av.displayName, $(if ($on) { '开启' } else { '关闭/未知' }))
    }
    $names = ($avs | ForEach-Object { $_.displayName }) -join ' / '
    Write-Host ("    提示：本机同时装了 " + $avs.Count + " 个安全软件（" + $names + '）。')
    Write-Host '          多套引擎会互相“抢答”，未知小工具被报的概率明显更高，建议只保留一套。'
} catch {
    Write-Host ('    查询失败：' + $_.Exception.Message)
}

Write-Host ''
Write-Host '[5] 处置办法（按优先级）'
Write-Host '    ① 加入信任/排除（立刻见效）'
Write-Host '       火绒：主界面 → 防护中心 → 信任区 → 添加文件/目录 → 选中本 exe'
Write-Host '       360 ：木马查杀 → 恢复区/信任区 → 添加信任；或 360 设置 → 白名单'
Write-Host '       Defender：Windows 安全中心 → 病毒和威胁防护 → 管理设置 → 排除项 → 添加文件'
Write-Host '       通用命令（需管理员 PowerShell，仅对 Defender 生效）：'
Write-Host ('         Add-MpPreference -ExclusionPath "' + (Split-Path -Parent $file.FullName) + '"')
Write-Host ''
Write-Host '    ② 上报误报（彻底解决，通常 1~2 个工作日）'
Write-Host '       火绒：火绒论坛“误报反馈”版块 / 客户端“上报样本”，附本 exe 与下面的 SHA256'
Write-Host '       360 ：https://fuwu.360.cn/shensu  （站长/开发者申诉，附 exe 与说明）'
Write-Host '       Microsoft：https://www.microsoft.com/wdsi/filesubmission'
Write-Host '       说明文字可直接用：'
Write-Host '         “NetSeep 是开源的 Windows 网速悬浮窗小工具，纯 C# 编写，不联网、不注入、'
Write-Host '          不释放文件，仅调用 GetIfTable2 读取系统网卡收发字节数用于显示速率。”'
Write-Host ('       样本 SHA256：' + (Get-FileHash $file.FullName -Algorithm SHA256).Hash)
Write-Host ''
Write-Host '    ③ 数字签名（根治，需要一张代码签名证书）'
Write-Host '       .\build.ps1 -Pfx 你的证书.pfx -PfxPassword 密码'
Write-Host '       签名后 SmartScreen 与大多数杀软的“未知程序”判定都会消失。'
Write-Host ''
Write-Host '    ④ 确认是哪种触发（行为型还是文件型）'
Write-Host '       .\build.ps1 -Minimal   生成 NetSeep-minimal.exe：'
Write-Host '       该版本编译掉了注册表自启、全局热键、鼠标穿透、控制台输出等行为特征。'
Write-Host '       若精简版不报而完整版报 → 是行为特征引起；若两者都报 → 是文件信誉/签名问题（走 ①②③）。'
Write-Host ''
