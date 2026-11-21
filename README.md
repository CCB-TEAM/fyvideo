# fyvideo — FYServer 介绍片生成器

用 C# + SkiaSharp 逐帧绘制，原始像素直接灌进 ffmpeg 编码成 MP4。
没有浏览器、没有 Node、没有前端依赖；同一份代码出横屏与竖屏两版。

- 横屏 `1920×1080 @30fps · 38s · 1140 帧`
- 竖屏 `1080×1920 @30fps · 38s · 1140 帧`

## 环境

| 依赖 | 版本 | 说明 |
|---|---|---|
| .NET SDK | 10.0+ | `dotnet --version` 能跑就行 |
| ffmpeg | 7+ | 必须在 PATH 上（或设 `FFMPEG` 环境变量指向 exe） |
| SkiaSharp | 4.154.0 | NuGet 自动还原 |
| 字体 | Arial + Microsoft YaHei UI | Windows 自带；换机器要确认存在 |

## 快速开始

```powershell
dotnet build

# 排版验证：重叠 / 骑线 / 对比度 / 越界，0 违规才算过
dotnet run -- validate --scene fy
dotnet run -- validate --scene fyv

# 出片
dotnet run -- video --scene fy  --out out\fyserver-landscape.mp4
dotnet run -- video --scene fyv --out out\fyserver-vertical.mp4

# 单帧 PNG（调排版时最常用）
dotnet run -- still --scene fy --at 12.5 --out out\frame.png

# 静态交付物：七拍预览图 / 综合海报
dotnet run -- sheet  --scene fy  --out out\preview-landscape.png
dotnet run -- sheet  --scene fyv --out out\preview-vertical.png
dotnet run -- poster --scene fy  --out out\poster.png
```

渲染耗时参考：1140 帧 1080p 约 110–140 秒（1.7 倍实时），瓶颈是每帧四层纸纹混合。

## 目录

```
fyvideo/
├─ Program.cs          命令行：video / still / validate / fonts；逐帧循环；把 SKSurface 快照灌给 ffmpeg
├─ FyPosterDesign.cs   ★ 片子本体：七拍的构图、文案、动效、转场
├─ Swiss.cs            Typeset（中英混排 + 400/700 真粗体）、Paper（四层纸纹 + 给红色块加压纹）
├─ Overlap.cs          ★ 排版验证器：包围盒/多边形登记 + 四条规则
├─ FfmpegPipe.cs       ffmpeg 子进程，rawvideo 从 stdin 灌帧，stderr 异步排空
├─ Graphics.cs         IScene 接口、缓动函数、字体探测
├─ GlobalUsings.cs     global using SkiaSharp;
├─ assets/             后台界面截图（双色网点处理前的原图）
├─ reference/          七拍预览、综合海报、单帧、720p 预览片
└─ DESIGN.md           设计说明：为什么这么排、验证器规则、时间轴
```

## 改哪里

| 想改的东西 | 位置 |
|---|---|
| 某一拍的文案/构图 | `FyPosterDesign.cs` 里对应的方法：`Cover` / `Numbers` / `Verbs` / `Spec` / `Console` / `Versus` / `Colophon` |
| 每拍时长 | 构造函数里的 `_beats.Add(new(开始, 结束, 方法))`，同时改 `Duration` |
| 转场 | `Draw()` 末尾的两段式红板（出场 0.5s 从左扫满、入场 0.6s 收到右侧） |
| 配色 | `Swiss.cs` 的 `Swiss` 类（纸黄 / 墨黑 / 焦点红） |
| 纸感 | `Swiss.cs` 的 `Paper.DrawBackground`（斑驳 / 颗粒 / 纤维 / 边缘压暗四层） |
| 红色块的质感 | `Paper.Texture()`，`FyPosterDesign.RedFill()` 会调它 |
| 后台截图 | 换 `assets/admin-dashboard.png`，或设环境变量 `FY_SHOT` 指向别的图 |
| 字体 | `Swiss.cs` 的 `Typeset` 构造函数 |

### 两条硬约定

1. **压在斜面色条上的文字，必须在色条的坐标系里画**。
   用 `Band` 描述色条（中心 / 角度 / 半长 / 半厚），用 `BandText()` 写字。
   这样文字永远平行于色条、永远落在色条内部，不会出现"横字压斜线"或"半边字掉在纸底上"。

2. **每个绘制元素都要登记给验证器**。
   用 `T()` / `TR()` / `RotText()` / `BandText()` 画文字（它们内部会 `Log.Text`），
   用 `RedFill()` / `Field()` 画色块。
   自己新写绘制代码时，记得调 `Log.Text(...)` 或 `Log.Decor(...)`，否则验证器看不见它。

   故意出血的元素（被画布边缘裁掉的巨字）传 `bleed: true`，否则 R4 会报越界。

## 验证器

`dotnet run -- validate --scene fy` 会逐拍渲染（不落盘）并检查：

| 规则 | 内容 |
|---|---|
| R1 | 文字之间不得重叠（多边形求交 + 采样面积比，斜排文字也准；> 15% 判违规） |
| R2 | 文字不得骑在色块边缘（斜置色条按坐标系判定） |
| R3 | 文字压在色块上时对比度 ≥ 3.0（WCAG） |
| R4 | 非出血元素不得越过安全区 |

它抓到的都是肉眼要盯很久的问题：副标题被斜条切掉一半、数字与注脚相撞、
竖排字压到截图、反白字跨在斜切边上。改完排版跑一遍，比盯着看快。

## 时间轴（38 秒 / 七拍）

| 秒 | 拍 | 主体 |
|---|---|---|
| 0.0–5.0 | 封面 | FY 700px + SERVER + 红条内反白副标题（竖屏底部另有三个数字） |
| 5.0–11.0 | 数字 | 24 / 0 / 1 斜向级联；右侧粗描边框选 + 框内 `3×` |
| 11.0–17.5 | 六个词 | 登录·卡组·商店·对局·重连·后台 + 竖排红字 |
| 17.5–23.5 | 技术 | 四条规格（标题左、注脚右）+ 背景 AOT |
| 23.5–29.5 | 后台 | 双色网点截图斜置 + 竖排 ADMIN |
| 29.5–35.0 | 对比 | 20° 斜带分镜，两侧文字同角度 |
| 35.0–38.0 | 落版 | 红潮 + MIT 360px + 仓库地址 |

## 加音乐

渲染器本身不做混音，先把音轨切好再喂进去：

```powershell
# 从原曲切 38 秒（41.81s 是拍网格点），先做 EQ 再归一响度：
#   highpass 40Hz ×2 —— 去掉听不见但吃动态的次低频
#   -7dB @115Hz      —— 原曲低频偏重，压掉一档
#   +2dB @7.5kHz     —— 原曲高频偏暗，提一点通透度
ffmpeg -y -ss 41.81 -t 38 -i "原曲.flac" ``
  -af "highpass=f=40,highpass=f=40,equalizer=f=115:width_type=o:width=1.6:g=-7,equalizer=f=7500:width_type=o:width=1:g=2,afade=t=in:st=0:d=0.25,afade=t=out:st=35.5:d=2.5,loudnorm=I=-16:TP=-1.5:LRA=11" ``
  -ar 48000 -ac 2 -c:a pcm_s16le assets\bgm-bed.wav
```

**为什么要 EQ**：实测原曲在 41.81s 起的 38 秒里，<200Hz 的能量和 200–1000Hz 基本持平、>4kHz 比中频低 8dB，
是典型"低频厚、高频暗"的混音。直接 `loudnorm` 只是整体增益，音色不变，所以低频会显得更沉。
压 7dB 低频、提 2dB 高频之后，低频落到中频下方 2dB，听感才平衡。

# 2) 渲染时挂上
dotnet run -- video --scene fy  --out out\landscape.mp4 --audio assets\bgm-bed.wav
dotnet run -- video --scene fyv --out out\vertical.mp4  --audio assets\bgm-bed.wav
```

混流参数在 `FfmpegPipe.cs`：`-c:a aac -b:a 192k -shortest`。
音轨长度必须 ≥ 视频长度，短了会被 `-shortest` 截断画面。

拍点对齐：片子总长 38 秒，如果原曲有固定拍速，建议先把每拍时长改成拍的整数倍再切音乐，
否则切点会和鼓点错开（当前转场有 1.1 秒的红色擦除，能吃掉 0.1–0.2 秒的误差）。

## 注意

- 不要删 `assets/`：`FyPosterDesign` 默认从这里读后台截图，缺图会画成空面板。
- 换机器如果字体不全，先跑 `dotnet run -- fonts` 看有没有缺字形（`fonts` 命令会打印 `.notdef` 的字符）。
- 视频码率主要被纸纹吃掉。要小文件就把 `--crf` 抬到 20–22（16 MB → 8 MB 左右）。
