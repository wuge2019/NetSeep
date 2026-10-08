# 更新日志

本项目的重要变更都记录在这里。版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.1.0] - 2026-10-08

### 新增

- **主题切换**：右键菜单 → **主题**，一键换肤，共 8 个选项
  - 内置 6 套配色：**深色**（默认）/ **浅色** / **午夜蓝** / **石墨灰** / **高对比** / **极简透明**
  - **跟随系统**：读取 Windows 的浅色 / 深色应用模式（`AppsUseLightTheme`），并响应
    `WM_SETTINGCHANGE`、`WM_DWMCOLORIZATIONCOLORCHANGED`，系统切换主题时浮窗立即变色
  - **自定义**：沿用配置文件里的 `bgcolor` / `bgalpha` / `upcolor` / `downcolor`
  - 主题与「不透明度」是两个独立维度，可叠加（例如「极简透明」+ 60% 不透明度）
- 主题现在同时控制**底板颜色/透明度**与**边框颜色/透明度**（此前这两个值是写死的）
- 命令行新增 `--themes 目录 [缩放]`：每个内置主题各渲染一张预览图，用于生成主题画廊
- `--preview` 新增第 4 个参数指定主题，并额外输出一张棋盘格（透明底）预览图
- 配置文件新增 `theme=` 键；`--help` 会列出所有可用主题 id
- 新增 `tools/make_theme_gallery.py`：把 `--themes` 的输出拼成 `assets/themes.png`
- README 增加「主题」一节与主题一览图

### 变更

- exe 体积：83 KB → **87 KB**
- 版本号：`1.0.0` → `1.1.0`（程序集与 exe 版本信息同步）

## [1.0.0] - 2026-10-08

### 新增

- 首个公开版本：实时上传 / 下载速率悬浮窗，单文件、零第三方依赖
- 基于 `GetIfTable2` 的 64 位收发计数器差分测速；启动时自动探测 `MIB_IF_ROW2` 行布局
- 逐像素 alpha 分层窗口自绘（圆角半透明底板、矢量箭头、最近 60 秒迷你曲线）
- 「全部网卡合计」或指定网卡；自动排除回环、隧道与 NDIS 过滤层，同一份流量不重复计数
- 托盘常驻、深色右键菜单、鼠标穿透（`Ctrl+Alt+N`）、全屏时自动隐藏、可选开机自启
- 逐显示器 DPI 感知（PerMonitorV2）、便携模式、单实例
- `build.ps1` 一键构建：`-Minimal` 精简版、`-Pfx` 代码签名、`-Clean/-Run/-Preview`
- `tools/check-false-positive.ps1` 误报自检与处置助手
- GitHub Actions 自动构建（`windows-latest`，无需任何 SDK）

### 开发期验证修复

- **`MIB_IF_ROW2` 行步长错误**：`PermanentPhysicalAddress` 实为 `UCHAR[32]`，整行 1352 而非 1448 字节，
  原先第 0 行正常、后续行全是乱码。改为运行时用 `NET_LUID` 位域等特征自动探测行布局
- **收发方向颠倒**：下载被显示成上传
- **NDIS 过滤层被重复统计**：一块网卡挂 7 个过滤层且计数相同，合计会翻数倍；
  改用 `InterfaceAndOperStatusFlags` 的 `FilterInterface` 位剔除
- **下行箭头画成了向上**（预览图发现）
- **启动即崩**：`CreateParams` 在基类构造期间访问尚未赋值的字段
- **`SHQueryUserNotificationState` 声明错了 DLL**（应 `shell32.dll`），导致「全屏时自动隐藏」静默失效

### 安全 / 兼容

- 默认行为面收敛：不创建控制台、不枚举前台窗口、全局热键仅在鼠标穿透期间注册、
  注册表仅在主动勾选开机自启时写入；完整版本信息 + 图标 + 清单 + `TargetFramework` 元数据
- 已知问题：小体积免签名程序容易被安全软件的云查杀 / 启发式误报，
  详见 README「杀软误报处理」与 `tools/check-false-positive.ps1`

[1.1.0]: https://github.com/wuge2019/NetSeep/releases/tag/v1.1.0
[1.0.0]: https://github.com/wuge2019/NetSeep/releases/tag/v1.0.0
