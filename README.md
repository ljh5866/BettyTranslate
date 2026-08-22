# Betty Translate — Windows 屏幕翻译助手

一款基于 **WPF / C#（.NET 8）** 开发的 Windows 桌面屏幕翻译应用。打开 App 完成登录后，即可对屏幕上的外文界面进行 OCR 识别并实时翻译为中文，解决「全英文软件看不懂」的痛点。

本项目**自建精简工程**（`src/BettyTranslate.App` + `src/BettyTranslate.Core`），架构与代码模式参照开源项目 **STranslate**（[ZGGSONG/STranslate](https://github.com/ZGGSONG/STranslate)，MIT License），并借鉴 Translumo、pingyi 等项目的设计经验。上游代码保留在 `STranslate/` 目录作为参考。

---

## 产品定位

| 项 | 说明 |
|---|---|
| 产品名称 | Betty Translate（暂定） |
| 目标平台 | Windows 10/11 x64（仅 Windows） |
| 核心场景 | 打开任意英文界面软件，一键将屏幕内容翻译为中文 |
| 技术栈 | WPF + C# + .NET 8 + Supabase Auth |
| 授权方式 | 基于 MIT 开源项目二次开发（自有部分建议保持 MIT） |

## 核心功能

- **用户登录**：Supabase Auth 邮箱 + 密码注册/登录，登录后方可使用翻译功能
- **屏幕翻译（核心）**：按下快捷键（如 `F1`）→ 弹出「是否进行屏幕翻译」确认 → 点击「是」→ 全屏扫描识别文本 → 翻译为中文 → 悬浮窗展示结果
- **划词翻译**（增强）：选中文本一键翻译
- **截图翻译**（增强）：框选任意区域进行 OCR + 翻译
- **离线 OCR**：基于 PaddleOCR 本地识别，无网络也可识别
- **多翻译引擎**：百度/腾讯/有道/DeepL/Google/OpenAI 等，可配置切换

## 使用流程（用户视角）

```
启动 App → 登录（邮箱+密码）→ 打开任意英文软件
→ 按快捷键 F1 → 弹出确认框「是否进行屏幕翻译？」
→ 点击「是」→ 屏幕文本被扫描并翻译为中文 → 悬浮窗展示译文
```

## 文档索引

| 文档 | 说明 |
|---|---|
| [docs/01_产品需求文档PRD.md](docs/01_产品需求文档PRD.md) | 产品需求、功能清单、交互流程、非功能需求 |
| [docs/02_技术选型方案.md](docs/02_技术选型方案.md) | 技术栈决策、OCR/翻译引擎选型、关键依赖清单 |
| [docs/03_开源项目调研报告.md](docs/03_开源项目调研报告.md) | 开源项目调研、二次开发方案与许可证分析 |
| [docs/04_系统架构设计.md](docs/04_系统架构设计.md) | 分层架构、模块设计、核心流程时序、目录结构 |
| [docs/05_开发计划与里程碑.md](docs/05_开发计划与里程碑.md) | 里程碑规划、任务拆分、风险与验收标准 |

## 开发约定

- 遵循 MIT 开源精神，二次开发代码保持开源
- **只引入实际用到的库，不整包导入**（详见技术选型文档的依赖清单）
- 默认离线优先：OCR 与可选离线翻译不发送网络请求，截图与文本默认不落盘
- 代码注释使用中文

## 快速开始（开发环境准备）

1. 安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. 安装 Visual Studio 2022（勾选「.NET 桌面开发」工作负载）
3. Clone 上游参考项目：`git clone https://github.com/ZGGSONG/STranslate.git`
4. 按 [docs/05_开发计划与里程碑.md](docs/05_开发计划与里程碑.md) 逐步开展开发
