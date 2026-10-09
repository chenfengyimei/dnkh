<div align="center">

# 电脑开荒（dnkhw）

**纯净无广告的 Windows 开荒助手**

新机到手 / 重装系统后，体检、壁纸、下载路径换盘、纯净软件安装、启动项管理、
系统优化、磁盘分析——**一个绿色单文件全搞定**。

![License](https://img.shields.io/badge/license-MIT-green)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue)
![.NET](https://img.shields.io/badge/.NET-9.0-purple)
![Size](https://img.shields.io/badge/%E5%8D%95%E6%96%87%E4%BB%B6-%E5%85%8D%E5%AE%89%E8%A3%85-59MB-orange)

</div>

---

## 为什么是「电脑开荒」？

新电脑到手，你是不是也在经历这些：系统体检要开好几个设置页挨个看、壁纸去哪找高清的、
下载文件夹默认堆在 C 盘、装机软件一堆捆绑全家桶、开机启动项越攒越多……

**电脑开荒把这些事做成了一站式流程**，而且坚持三条底线：

- 🚫 **无广告、无弹窗、无捆绑**——软件本身不盈利，也不接任何推广
- 🔒 **不上传任何数据**——仅联网访问 Bing 壁纸接口与 winget 官方源，别无任何遥测
- ↩️ **一切皆可逆**——启动项禁用、优化项全部可一键恢复；磁盘分析只读，**绝不自动删文件**

## 界面预览

| 开荒检查 | 壁纸开荒 |
|:---:|:---:|
| ![开荒检查](docs/screenshots/check.png) | ![壁纸开荒](docs/screenshots/wallpaper.png) |
| **下载路径换盘** | **APP 推荐** |
| ![下载路径换盘](docs/screenshots/downloads.png) | ![APP 推荐](docs/screenshots/apps.png) |
| **启动项管理** | **Windows 优化** |
| ![启动项管理](docs/screenshots/startup.png) | ![Windows 优化](docs/screenshots/optimize.png) |
| **磁盘分析** | |
| ![磁盘分析](docs/screenshots/disk.png) | |

## 功能特性

### 🔍 开荒检查
一键体检：Windows 版本/CPU/内存/GPU/分区概览，激活状态、Defender 实时保护、防火墙、
异常驱动、待重启、网络连通、电源计划、休眠文件占用——WMI 并行查询，几秒出报告，支持导出 Markdown。

### 🖼 壁纸开荒
Bing 中国区每日壁纸（近一周）网格预览，一键下载并设为桌面；
支持填充/适应/拉伸/平铺/居中五种模式、本地图片、离线缓存。

### 📁 下载路径换盘
把「下载」文件夹从系统盘一键迁到其他盘：**先迁移文件、最后才改系统指向**（中断不产生不一致），
被占用文件跳过并列出清单；Chrome / Edge / Firefox 下载设置只读检测，跟随系统的自动生效。

### 📦 APP 推荐
精选 25 款软件、8 大类目，收录标准只有一条：**官方渠道、无捆绑安装器、无弹窗广告**。
已安装自动检测，勾选后 winget 静默批量安装；不在 winget 库的一键跳官网。

### 🚀 启动项管理
Run 键 + 启动文件夹全枚举，数字签名发布者解析；
禁用走任务管理器同款 StartupApproved 机制——**不删原始条目，随时可恢复**。

### ⚡ Windows 优化
9 项可逆优化：开始菜单推荐、任务栏小组件、广告 ID、使用建议、锁屏资讯、设置页推广、
Bing 搜索（Win10）、诊断遥测（需管理员）、高性能电源计划；按 Win10/11 自动显隐，一键优化。

### 💾 磁盘分析
一级目录占用排行 + 最大文件 Top 30，进度可取消；
回收站/Temp/Windows.old/更新缓存/休眠文件等常见可清理位置**只展示、不删除**，一键在资源管理器定位。

## 下载与安装

- **系统要求**：Windows 10（1709+）/ Windows 11，64 位；**无需安装 .NET 运行时**
- **安装方式**：绿色单文件——下载 `Dnkhw.exe` 放到任意目录（建议非系统盘）双击即用
- **校验**：发布说明提供 SHA256，可用 `certutil -hashfile Dnkhw.exe SHA256` 核对

> **首次运行提示**：本工具未购买代码签名证书，Windows 可能弹出 SmartScreen——
> 点「更多信息」→「仍要运行」即可；或右键文件 → 属性 → 勾选「解除锁定」。
> **管理员权限**：默认普通权限运行，需要提权的操作页面内会提示并提供「以管理员身份重启」。

## 技术栈

.NET 9 · WPF · [WPF-UI 4.3](https://github.com/lepoco/wpfui)（Fluent 风格，MIT） ·
[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/WindowsCommunityToolkit) ·
System.Management (WMI) · P/Invoke（SystemParametersInfo / SHGetKnownFolderPath / SHChangeNotify） ·
[winget](https://learn.microsoft.com/windows/package-manager/)

没有重型框架、没有注入 DLL、没有全局钩子——全部通过官方公开机制完成。

## 从源码构建

前置：Windows + [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

```powershell
git clone <本仓库地址>
cd dnkhw

dotnet build                     # 构建
dotnet test                      # 运行全部单元测试
dotnet run --project src/Dnkhw   # 启动应用

.\publish.ps1                    # 产出自包含单文件 publish\Dnkhw.exe（约 59MB）
```

测试覆盖 Core 业务库的纯逻辑：StartupApproved 编解码（含任务管理器真机样本）、
winget 输出解析、注册表路径构造、下载迁移（临时目录/锁定文件/进度）、
磁盘扫描聚合、清单完整性校验等，72 项全部通过。

## 项目结构

```
dnkhw/
├── src/Dnkhw/              # WPF 主程序（WPF-UI Fluent 壳、MVVM、页面与 ViewModel）
├── src/Dnkhw.Core/         # 业务库（UI 无关，可独立复用）
│   ├── SystemCheck/        #   开荒检查（WMI 并行体检）
│   ├── Wallpaper/          #   壁纸（Bing 接口 + SPI 设置）
│   ├── Downloads/          #   下载路径换盘（已知文件夹迁移）
│   ├── AppCatalog/         #   APP 推荐（清单 + winget 封装）
│   ├── Startup/            #   启动项（StartupApproved 编解码）
│   ├── Optimize/           #   Windows 优化（注册表/服务/电源）
│   └── DiskScan/           #   磁盘分析（只读聚合）
├── src/Dnkhw.Core.Tests/   # xUnit 单元测试
├── tools/Probe/             # 开发探针：真机调用各 Core 服务的控制台工具
├── docs/screenshots/        # 界面截图
└── promo/                   # 宣传物料（采集/合成脚本、发布说明）
```

## 参与贡献

欢迎 Issue 与 PR：

- 提交前请确保 `dotnet test` 全绿、`dotnet build` 零警告
- 新功能请附带单元测试（Core 库的纯逻辑均可测）
- APP 推荐清单（`src/Dnkhw.Core/AppCatalog/apps.json`）接受新条目，但必须满足收录标准：官方渠道、无捆绑、无广告
- UI/UX 改进建议请附截图或原型

如果这个工具帮到了你，欢迎点一个 ⭐ Star，这是持续维护的最大动力。

## 许可证

[MIT](LICENSE) © 2026 dnkhw

## 免责声明

- 本工具对系统设置的修改均通过官方公开机制（注册表 / StartupApproved / powercfg / winget），
  并在应用内提供一键恢复；磁盘分析为只读展示，**不删除任何文件**。
- 请仅从本仓库作者发布的官方渠道获取安装包，并通过 SHA256 校验完整性；
  从其他来源获取的文件安全性与作者无关。
- 本软件按「现状」提供，使用产生的一切后果由使用者自行承担。
