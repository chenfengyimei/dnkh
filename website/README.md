# 电脑开荒官网（website/）

纯静态单页官网，无任何构建步骤与外部依赖——文件夹即站点，可直接部署到
**www.cyhaoka.vip**（任何静态托管：nginx / IIS / 对象存储 / Cloudflare Pages 均可）。

## 目录结构

```
website/
├── index.html                    # 单页站点（功能/截图/视频/下载/FAQ）
├── feedback.html                 # 反馈页：Bug 反馈 / 申请收录（静态表单）
├── dl/                           # 下载计数与多渠道系统（PHP，仅正式服务器生效）
│   ├── config.php                # ★ 渠道配置：网盘链接填这里 + 统计页密钥
│   ├── download.php              # 计数跳板：dl/download.php?id=<渠道> → 302 到真实地址
│   ├── status.php                # 前端接口：返回启用渠道 + 累计下载（聚合数字）
│   ├── stats.php                 # 统计页：dl/stats.php?key=<statsKey>
│   └── data/stats.json           # 计数数据（服务器运行时生成；nginx 已 deny 直读）
├── assets/
│   ├── css/style.css             # Fluent 风格，明暗主题跟随系统（含反馈页表单样式）
│   ├── js/main.js                # 截图 Tab / 大图查看 / 复制 SHA256（无依赖）
│   ├── js/feedback.js            # 反馈页：类型切换 / 实时生成 / 必填校验 / mailto
│   └── img/
│       ├── favicon.svg          # 站点图标（软件同款蓝绿渐变显示器）
│       ├── promo_poster.jpg     # 视频封面（从宣传视频 2s 提取）
│       └── screenshots/*.png    # 10 张真实界面截图（源自 docs/screenshots，含电脑开荒网/设置/关于）
│   └── video/dnkhw_promo.mp4    # 38.5 秒宣传视频（HyperFrames 管线，源自 promo/）
├── download/Dnkhw.exe            # ← 发版时手动放入最新安装包（已 gitignore）
└── README.md
```

## 下载统计与多渠道下载（dl/）

自托管轻量计数（无第三方统计，符合官网「不收集数据」承诺——仅聚合计数，无 Cookie、无个人信息）：

- **工作方式**：所有下载按钮指向 `dl/download.php?id=<渠道>` → PHP 以 flock 并发安全方式
  累加 `dl/data/stats.json`（总量 / 各渠道 / 每日）→ 302 跳转真实地址
  （直连 302 到 `/download/Dnkhw.exe` 由 nginx 继续提供，支持断点续传；网盘 302 到分享页）
- **前端**：`main.js` fetch `dl/status.php` 渲染渠道按钮（未配置链接的渠道自动隐藏）
  与「累计下载 N 次」；**本地预览无 PHP 时自动回退为静态直连链接**，不影响预览
- **配网盘链接**：编辑 `dl/config.php` 把 `lanzou`/`baidu`/`quark` 的 `url` 填上分享地址即可，
  前端自动出现按钮；新增渠道加一行数组即可（前端自动渲染）
- **看统计**：`dl/stats.php?key=dnkhw-7f3a9c2e51b8d406`（各渠道累计 + 近 30 天每日明细）
- **部署注意**：① 宝塔建站必须选 **PHP-82**（不是纯静态），否则 .php 不执行、计数不生效；
  ② vhost 需加 `location ^~ /dl/data/ { deny all; }` 防直读计数文件；
  ③ `dl/data/` 目录需 `chown www:www` 可写；④ 直连渠道的 url 是站点根相对路径，勿改
- **统计口径**：网盘渠道计的是「点击跳转次数」，实际完成下载以网盘后台为准

## 反馈页（feedback.html）

静态表单，无后端：填写 → 实时生成格式化反馈文本 → 两种送达方式：

1. **发送邮件**：mailto 拉起邮件客户端，收件人 `2079937150@qq.com`（git 提交邮箱），
   主题 / 正文自动填好
2. **复制文本**：复制按钮兜底，用户自行粘贴到任意渠道

必填校验（Bug = 问题描述；收录 = 软件名称 + 官网地址）红框提示，纯前端。

## 作者与联系渠道（页脚展示）

- **哔哩哔哩**：尘风一枚 · https://space.bilibili.com/508302628
  （两页页脚「关注作者」卡 + 版权行署名 + 反馈页 fb-note 的私信渠道）
- **反馈邮箱**：2079937150@qq.com（反馈页 mailto）

改作者信息需同步 5 处：index.html 页脚 author-card + 版权行、feedback.html 页脚
author-card + 版权行 + fb-note 渠道说明。

## 界面组件备忘

- 回到顶部按钮：两页底部 `#toTop`，滚动 >600px 显示（main.js 控制，勿删按钮标记）

## 本地预览

直接双击 `index.html` 即可（复制按钮在 file:// 下有 execCommand 回退）。
更接近真实效果：`python -m http.server 8080` 后访问 http://localhost:8080。

## 发版更新清单（重要）

每次发布新版本，改 `index.html` 中 4 处数据（全文搜索即可定位）：

1. **版本号** `v1.0.1`（Hero 徽章 + 下载卡 2 处 + feedback.html Bug 表单预填 1 处）
2. **大小** `67.60 MB` / `70,881,606 字节`
3. **SHA256** `E0A5B939...C119F5`（下载卡与 data-copy 各 1 处，共 2 个字符串）
4. 复制最新产物：`Copy-Item publish\Dnkhw.exe website\download\Dnkhw.exe`
5. **更新 `dnkhw\version.json`**（应用内「关于 → 检查更新」的数据源）：
   `version` 换为新版本号、`sha256`/`sizeBytes` 与发布物一致、
   `notes` 一句话亮点（会显示在应用的更新提示里）；
   随站点部署到 `https://www.cyhaoka.vip/dnkhw/version.json`
6. **网盘渠道**：若新版本也在蓝奏云/百度/夸克上传了安装包，更新 `dl/config.php` 各渠道的
   `url`（直连 url 始终指向 `/download/Dnkhw.exe` 不需要改）

> SHA256 获取：`Get-FileHash publish\Dnkhw.exe -Algorithm SHA256`
> 历史教训（publish.ps1）：必须确认产物是新版再复制，防止杀软/旧进程锁文件导致发旧包。

## 素材更新

- 界面截图：用 `promo\capture.ps1 --page <页面>` 重采后覆盖
  `assets/img/screenshots/`，并同步回 `docs/screenshots/`（README 也在用）
- 宣传视频：当前为 **HyperFrames 版 v4**（HTML/GSAP MG 动画 + 夏小晨旁白 + 复用 BGM，38.5s，2026-10-13）。改文案/分镜：
  1. 编辑 `promo\video_v4\index.html`（分镜与解说字幕都在这一个文件里；旁白文案改动需用 generate_tts 重出对应分段，替换 `promo\video_v4\assets\audio\voice0*.mp3`）
  2. 校验：`cd promo\video_v4 && npm run check`（需先把 `tools\ffmpeg\ffmpeg-9.0.2-essentials_build\bin` 加入 PATH）
  3. 渲染：`npx hyperframes render --video-frame-format png -q high -o render/dnkhw_promo_bilibili.mp4`
  4. 转正为 `promo\dnkhw_promo_bilibili.mp4` 并覆盖 `assets/video/dnkhw_promo.mp4`，
     重提取封面：`tools\ffmpeg\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe -ss 2 -i promo\dnkhw_promo_bilibili.mp4 -frames:v 1 -q:v 2 website\assets\img\promo_poster.jpg`
  5. 管线细节见 `promo\video_v4\README.md`
  （历史版本：v1 解说版、v2 静音 Ken Burns 版、v3 展示版管线 make_promo.ps1 / make_promo_silent.ps1 / make_showcase.ps1 + make_promo_v2.ps1 均保留可用）
