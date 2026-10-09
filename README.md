# 电脑开荒（dnkhw）

纯净无广告的 Windows 开荒助手——新机 / 重装后一站式搞定：体检、壁纸、下载路径换盘、纯净软件安装、启动项、优化与磁盘分析。

## 下载与安装

- **系统要求**：Windows 10（1709+）/ Windows 11，64 位；**无需安装 .NET 运行时**
- **安装方式**：绿色单文件——下载 `Dnkhw.exe` 放到任意目录（建议非系统盘）双击即用
- **校验**：发布说明提供 SHA256，可用 `certutil -hashfile Dnkhw.exe SHA256` 核对
- 首次运行如遇 SmartScreen 提示：点「更多信息」→「仍要运行」（详见下方说明）

## 功能

| 模块 | 说明 |
|---|---|
| 开荒检查 | 系统信息概览（版本/CPU/内存/GPU/分区），激活状态、Defender 实时保护、防火墙、异常驱动、待重启、网络、电源计划、休眠文件占用；支持导出 Markdown 报告 |
| 壁纸开荒 | Bing 中国区每日壁纸（近一周）网格预览，一键下载并设为桌面（填充/适应/拉伸/平铺/居中），支持本地图片与离线缓存 |
| 下载路径换盘 | 把「下载」已知文件夹从系统盘迁到其他盘：先迁移文件（占用文件跳过并列出清单），最后才改注册表指向并广播刷新；Chrome/Edge/Firefox 下载设置只读检测，跟随系统的自动生效，已自定义的一键打开设置页；可还原 |
| APP 推荐 | 精选 25 款软件（8 大类目），收录标准：官方渠道、无捆绑安装器、无弹窗广告；已安装检测（卸载注册表），winget 静默一键/批量安装，不在 winget 库的回退官网直链 |
| 启动项管理 | Run 键 + 启动文件夹枚举，数字签名发布者解析；禁用/启用走任务管理器同款 StartupApproved 机制，不删原始条目、完全可逆 |
| Windows 优化 | 9 项可逆优化：开始菜单推荐、任务栏小组件/资讯和兴趣、广告 ID、使用建议、锁屏资讯、设置页推广、Bing 搜索（Win10）、诊断遥测（需管理员）、高性能电源计划；按系统版本（Win10/11）自动显隐 |
| 磁盘分析 | 一级目录占用排行（含子级）+ 最大文件 Top 30，可取消；常见可清理位置（回收站/Temp/Windows.old/更新缓存/休眠文件）**只展示、不删除**，一键在资源管理器定位 |

纯净原则：

- 无广告、无弹窗、无捆绑；不收集、不上传任何数据（仅联网访问 Bing 壁纸接口与 winget 官方源）
- 所有系统修改均可一键恢复；磁盘分析绝不动文件

## 构建

前置：Windows + [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

```powershell
dotnet build          # 构建
dotnet test           # 全部单元测试（Core 业务库）
dotnet run --project src/Dnkhw    # 运行
```

### 发布（自包含单文件）

```powershell
.\publish.ps1         # 产出 publish\Dnkhw.exe（约 50MB，目标机免装 .NET 运行时）
```

## 首次运行注意

- **SmartScreen**：本工具为个人使用、未做代码签名，首次运行 Windows 可能提示——点「更多信息」→「仍要运行」即可；或右键属性勾选「解除锁定」。
- **管理员权限**：默认普通权限运行；系统级启动项、遥测关闭等需要管理员的操作会在页面内提示并提供「以管理员身份重启」。
- **winget**：APP 一键安装依赖 Windows 自带的 App Installer（winget）；Win10 早期版本可在微软商店安装（应用内有引导按钮）。

## 项目结构

```
dnkhw/
├── src/Dnkhw/              # WPF 主程序（WPF-UI Fluent + CommunityToolkit.Mvvm）
├── src/Dnkhw.Core/         # 业务库（SystemCheck / Wallpaper / Downloads / AppCatalog / Startup / Optimize / DiskScan）
├── src/Dnkhw.Core.Tests/   # xUnit 单元测试（含临时目录集成测试）
└── tools/Probe/            # 开发探针：真机调用各 Core 服务的控制台工具
```

## 技术栈

.NET 9 · WPF · [WPF-UI 4.3](https://github.com/lepoco/wpfui)（MIT） · CommunityToolkit.Mvvm · System.Management (WMI) · P/Invoke（SystemParametersInfo / SHGetKnownFolderPath / SHChangeNotify） · winget

## 免责声明

- 本工具对系统设置的修改均通过官方公开机制（注册表 / StartupApproved / powercfg / winget），并在应用内提供一键恢复；磁盘分析为只读展示，**不删除任何文件**。
- 请仅从作者发布的官方渠道获取安装包，并通过 SHA256 校验完整性；从其他来源获取的文件安全性与作者无关。
- 本工具按「现状」提供（见 LICENSE），使用产生的一切后果由使用者自行承担。
