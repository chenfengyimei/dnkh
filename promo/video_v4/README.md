# dnkhw B 站宣传视频 v4（HyperFrames 工程说明）

工程：`promo/video_v4/`（HyperFrames 0.8.150，"HTML 即视频"管线）
产出：`promo/video_v4/render/dnkhw_promo_bilibili.mp4`（1080p30，38.5s）

## 结构
- `index.html` — 全片合成（六场景 + 字幕 + 7 音轨，单文件）
- `assets/audio/` — voice01~06.mp3（夏小晨分段旁白）+ bgm.mp3（复用 promo/audio/bgm_promo.mp3）
- `assets/img/` — docs/screenshots 的 10 张 v1.0.1 真实 UI 截图

## 分镜（语音驱动时间轴）
| 场景 | 时间 | 内容 |
|---|---|---|
| S1 片头 | 0–3.5s | 「电脑开荒」标题 + v1.0.1 徽章 |
| S2 痛点 | 3.5–11.7s | 弹窗/全家桶/C盘/开机慢 四卡 + 结论 |
| S3 模块A | 11.7–20.0s | 体检 + 32款纯净软件（check/apps 截图） |
| S4 快切 | 20.0–27.8s | 换盘/优化/美化/磁盘（4 张各 1.9s） |
| S5 背书 | 27.8–33.4s | 免费开源/纯净无广告/在线更新 |
| S6 CTA | 33.4–38.5s | cyhaoka.vip + B站@尘风一枚 + 三连 |

## 常用命令（在 promo/video_v4 下，需 ffmpeg 便携版先入 PATH）
```powershell
$env:Path = "D:\xm\dnkhw\tools\ffmpeg\ffmpeg-9.0.2-essentials_build\bin;" + $env:Path
npm run check    # lint + runtime + layout + motion + contrast
npm run render   # 渲染 MP4
npx hyperframes render --video-frame-format png -q high -o render/dnkhw_promo_bilibili.mp4  # 高质量渲染
```

## 关键经验
- 渲染器对未声明字体会报 StaticGuard error：系统字体需 `@font-face { font-family:"Microsoft YaHei"; src: local("Microsoft YaHei"); }` 声明
- GSAP 官方支持属性白名单：opacity/x/y/scale/scaleX/scaleY/rotation/width/height/visibility，动效只用这些最稳
- audio clip 的 data-duration 应等于实际媒体时长（渲染时自动缩短，不截尾）
- 嵌套结构的 `nested_structure_needs_subcomposition` 是 Studio 编辑体验建议，不影响渲染正确性
- UI 截图色彩敏感 → `--video-frame-format png`；预览可用 draft，成片用 high
