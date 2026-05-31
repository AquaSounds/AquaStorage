# AquaStorage

用于音乐制作的采样管理器 — 浏览、试听、标记你的音频素材库。

## 功能

- 添加本地文件夹，递归浏览音频文件（WAV / MP3 / OGG / FLAC）
- 选中即播，双声道实时波形，拖拽波形可跳转播放位置
- `Ctrl + 滚轮` 调节字体大小，`Esc` 清除搜索
- 文件/文件夹标星收藏，一键过滤仅看收藏项
- 防抖异步搜索，自动展开匹配节点
- 从文件树直接拖拽音频到 DAW 或文件管理器
- 自定义主题色（HSL 环形取色器）、暗色/亮色/跟随系统、背景图片
- 音量旋钮（-60dB ~ 0dB），悬停显示当前值
- English / 简体中文 / 繁體中文 / 日本語

## 技术栈

C# 13 · .NET 10.0 · [Avalonia UI](https://avaloniaui.net/) 11.3 (Fluent Theme, Mica) · [NAudio](https://github.com/naudio/NAudio) 2.2 · System.Text.Json

## 构建

需要 [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```bash
dotnet build
dotnet run --project AquaStorage
```

发布单文件：

```bash
dotnet publish AquaStorage -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish
```

## 配置

设置保存在 `%AppData%\AquaStorage\`，包括主题、语言、文件夹列表、收藏、波形缓存。可在设置面板调整缓存上限或一键清除。
