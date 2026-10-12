# 电脑开荒官网（website/）

纯静态单页官网，无任何构建步骤与外部依赖——文件夹即站点，可直接部署到
**www.cyhaoka.vip**（任何静态托管：nginx / IIS / 对象存储 / Cloudflare Pages 均可）。

## 目录结构

```
website/
├── index.html                    # 单页站点（功能/截图/视频/下载/FAQ）
├── feedback.html                 # 反馈页：Bug 反馈 / 申请收录（静态表单）
├── assets/
│   ├── css/style.css             # Fluent 风格，明暗主题跟随系统（含反馈页表单样式）
│   ├── js/main.js                # 截图 Tab / 大图查看 / 复制 SHA256（无依赖）
│   ├── js/feedback.js            # 反馈页：类型切换 / 实时生成 / 必填校验 / mailto
│   └── img/
│       ├── favicon.svg          # 站点图标（软件同款蓝绿渐变显示器）
│       ├── promo_poster.jpg     # 视频封面（从宣传视频 3s 提取）
│       └── screenshots/*.png    # 8 张真实界面截图（源自 docs/screenshots，含电脑开荒网）
│   └── video/dnkhw_promo.mp4    # 53 秒宣传视频（源自 promo/）
├── download/Dnkhw.exe            # ← 发版时手动放入最新安装包（已 gitignore）
└── README.md
```

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

1. **版本号** `v0.1.0`（Hero 徽章 + 下载卡 2 处 + feedback.html Bug 表单预填 1 处）
2. **大小** `66.88 MB` / `70,131,072 字节`
3. **SHA256** `53537003...05D42B`（下载卡与 data-copy 各 1 处，共 2 个字符串）
4. 复制最新产物：`Copy-Item publish\Dnkhw.exe website\download\Dnkhw.exe`
5. **更新 `dnkhw\version.json`**（应用内「关于 → 检查更新」的数据源）：
   `version` 换为新版本号、`sha256`/`sizeBytes` 与发布物一致、
   `notes` 一句话亮点（会显示在应用的更新提示里）；
   随站点部署到 `https://www.cyhaoka.vip/dnkhw/version.json`

> SHA256 获取：`Get-FileHash publish\Dnkhw.exe -Algorithm SHA256`
> 历史教训（publish.ps1）：必须确认产物是新版再复制，防止杀软/旧进程锁文件导致发旧包。

## 素材更新

- 界面截图：用 `promo\capture.ps1 --page <页面>` 重采后覆盖
  `assets/img/screenshots/`，并同步回 `docs/screenshots/`（README 也在用）
- 宣传视频：当前为**无人声 + 字幕**版（无音轨，后期自行配乐）。改字幕文案：
  编辑 `promo\captions_silent.tsv`（UTF-8）后运行 `powershell promo\make_promo_silent.ps1`
  → 产出 `promo\dnkhw_promo_silent.mp4`，验证后转正为 `promo\dnkhw_promo_1080p.mp4`
  并覆盖 `assets/video/dnkhw_promo.mp4`，重新提取封面：
  `tools\ffmpeg\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe -ss 2 -i promo\dnkhw_promo_1080p.mp4 -frames:v 1 -q:v 2 website\assets\img\promo_poster.jpg`
  （旧解说版备份：promo\dnkhw_promo_v1_narrated.mp4；解说版管线 make_promo.ps1 保留可用）
