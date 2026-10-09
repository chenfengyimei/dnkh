# 电脑开荒官网（website/）

纯静态单页官网，无任何构建步骤与外部依赖——文件夹即站点，可直接部署到
**www.cyhaoka.vip**（任何静态托管：nginx / IIS / 对象存储 / Cloudflare Pages 均可）。

## 目录结构

```
website/
├── index.html                    # 单页站点（功能/截图/视频/下载/FAQ）
├── assets/
│   ├── css/style.css             # Fluent 风格，明暗主题跟随系统
│   ├── js/main.js                # 截图 Tab / 大图查看 / 复制 SHA256（无依赖）
│   └── img/
│       ├── favicon.svg          # 站点图标（软件同款蓝绿渐变显示器）
│       ├── promo_poster.jpg     # 视频封面（从宣传视频 3s 提取）
│       └── screenshots/*.png    # 7 张真实界面截图（源自 docs/screenshots）
│   └── video/dnkhw_promo.mp4    # 53 秒宣传视频（源自 promo/）
├── download/Dnkhw.exe            # ← 发版时手动放入最新安装包（已 gitignore）
└── README.md
```

## 本地预览

直接双击 `index.html` 即可（复制按钮在 file:// 下有 execCommand 回退）。
更接近真实效果：`python -m http.server 8080` 后访问 http://localhost:8080。

## 发版更新清单（重要）

每次发布新版本，改 `index.html` 中 4 处数据（全文搜索即可定位）：

1. **版本号** `v0.1.0`（Hero 徽章 + 下载卡 2 处）
2. **大小** `58.97 MB` / `61,834,356 字节`
3. **SHA256** `51D4A39A...74AC35`（下载卡与 data-copy 各 1 处，共 2 个字符串）
4. 复制最新产物：`Copy-Item publish\Dnkhw.exe website\download\Dnkhw.exe`

> SHA256 获取：`Get-FileHash publish\Dnkhw.exe -Algorithm SHA256`
> 历史教训（publish.ps1）：必须确认产物是新版再复制，防止杀软/旧进程锁文件导致发旧包。

## 素材更新

- 界面截图：用 `promo\capture.ps1 --page <页面>` 重采后覆盖
  `assets/img/screenshots/`，并同步回 `docs/screenshots/`（README 也在用）
- 宣传视频：重新合成后覆盖 `assets/video/dnkhw_promo.mp4`，并重新提取封面：
  `tools\ffmpeg\bin\ffmpeg.exe -ss 3 -i promo\dnkhw_promo_1080p.mp4 -frames:v 1 -q:v 2 website\assets\img\promo_poster.jpg`
