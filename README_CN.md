
<div align="center">

[English](README.md) | [简体中文](README_CN.md) | [日本語](README_JP.md) | [Español](README_ES.md) | [Français](README_FR.md) | [Deutsch](README_DE.md)

</div>

---

### 📖 项目介绍
**颠覆性智能字幕引擎**。基于 **Multi-Agent 协同**技术，实现全流程自动化接管，交付**准人工级**的翻译作品。

### ✨ 亮点
- **风格统一**：先产出全片风格指南（基调/文风/称呼策略），避免“前后两个人在翻”。
- **术语一致**：自动生成术语表并强制执行，统一人名/地名/专有名词与代词性别（他/她）。
- **上下文消歧**：滑动窗口引用前文译文 + 后文预览，降低指代不明与断句误译。
- **语义审计**：内置审校回路，专查错译/漏译/幻觉；同时遵守意群跨行协议，不乱补全半句。
- **更像人翻**：意群跨行（Enjambment）+ 多行滑动翻译，语气更连贯，减少“机翻味”。
- **多格式字幕**：支持多种常见字幕格式输入（自动识别）。
- **省 Token**：紧凑型字幕格式，降低 Token 消耗并提升处理效率。


### 快速开始

#### 环境要求
- .NET SDK 10.0（本项目 TargetFramework 为 net10.0）
- 可用的大模型 API Key 与 Endpoint（启动入口里可自定义）

#### 运行
在项目根目录执行：

```powershell
cd src
dotnet restore
dotnet run --project .\Subtitles.Translate.Agent\Subtitles.Translate.Agent.csproj
```

#### 交互式使用
程序启动后会依次提示：
- 输入字幕文件本地路径（支持拖拽到终端）
- 输入目标语言（回车默认：Simplified Chinese）
- 输入 API Key（未配置时会提示输入）

#### 输出文件
- 翻译完成后，会在原字幕同目录生成：`原文件名.<targetLanguage>.srt`
- 默认写出单语译文（`GenerateTranslatedSrt()`）；如需双语字幕，可将入口处改为 `GenerateBilingualSrt()`

#### 自定义模型与端点
入口配置位于 [Program.cs:L90-L105](file:///d:/code/SubtitlesDog/Subtitles.Translate.Agent/src/Subtitles.Translate.Agent/Program.cs#L90-L105) 的 `AgentSystemConfig` 初始化处，可修改 `ModelId`、`Endpoint`、`ApiKey` 等参数。

```csharp
// src/Subtitles.Translate.Agent/Program.cs
var systemConfig = new AgentSystemConfig();
systemConfig.AddDefaultConfig(new AgentConfig
{
    ModelId = "gpt-oss-120b",
    ApiKey = “apiKey”,
    Endpoint = "https://<your-endpoint>/v1"
});
```

修改示例（将 Agent 1/2 用长上下文模型，翻译用更低成本模型）：

```csharp
var systemConfig = new AgentSystemConfig();
systemConfig.AddConfig(nameof(Step1_DirectorAgent), new AgentConfig
{
    ModelId = "gemini-3-flash",
    ApiKey = “apiKey”,
    Endpoint = "https://<your-endpoint>/v1"
});
systemConfig.AddConfig(nameof(Step2_GlossaryAgent), new AgentConfig
{
    ModelId = "gemini-3-flash",
    ApiKey = “apiKey”,
    Endpoint = "https://<your-endpoint>/v1"
});
systemConfig.AddConfig(nameof(Step3_TranslatorAgent), new AgentConfig
{
    ModelId = "gpt-oss-120b",
    ApiKey = “apiKey”,
    Endpoint = "https://<your-endpoint>/v1"
});
```

#### 推荐模型（成本/上下文权衡）
- Agent 1（Director）/ Agent 2（Glossary）：推荐 `gemini-3-flash`（上下文更长、成本更低，更适合全局扫描与术语提取）
- 翻译阶段（Translator）：可用更低成本的模型，例如 `gpt-oss-120b`



### 1. Step1_DirectorAgent（全局理解 / 风格指南）
- **做什么**：先读完整字幕，生成后续翻译要严格遵守的“全片指导书”，解决风格漂移与称呼混乱。
- **输入**：全量字幕（已做紧凑格式化）、目标语言等请求参数。

### 2. Step2_GlossaryAgent（术语提取 / 一致性约束）
- **做什么**：基于 Step1 的策略与全片字幕，提取关键实体并构建受控术语表，确保“一个名词一条译法”。
- **输入**：Step1 风格指南 + 全量字幕。
- **输出**：角色表（含别名与性别推断）、地名表、术语表（含领域与定义），并提供可直接嵌入提示词的 Markdown 版本。

### 3. Step3_TranslatorAgent（滑动窗口翻译 / 格式强校验）
- **做什么**：以滑动窗口分批翻译字幕，同时引用前文译文与后文预览，降低断句、指代与歧义错误。
- **输入**：Step1 风格指南 + Step2 术语表 + 前文已译上下文 + 当前批次 + 后文预览。
- **输出**：逐行初译（强制保持原 ID 与数量一致）；可选触发 Step4 进行语义审计后回写终稿。

### 4. Step4_ReviewerAgent[待开源]（语义审计 / 回译协议）
- **做什么**：只针对语义准确性做“审计级”修正，专查错译、漏译与幻觉；不做润色，不做术语美化。
- **输入**：Step3 的初译批次。
- **输出**：逐行 PASS/FIXED、错误原因（critique）与最终采用译文（final_translation），并与 Step3 数量严格对齐。

### 5. Step5_PolisherAgent[待开源]（术语合规 + 流式润色）
- **做什么**：在不破坏时间轴切分点的前提下，先做术语强制执行与代词修正，再进行更地道的表达润色与节奏优化。
- **输入**：Step2 术语表 + Step1 风格指南 + 当前批次译文 + 前文润色结果（用于连贯衔接）。
- **输出**：polished_text、可选 note（俚语/术语说明）、optimization_tag（术语修正/上下文润色/风格适配/无修改）。

### 6. Step6_TimingAdjusterAgent[待开源]（阅读舒适度时间轴微调）
- **做什么**：根据译文长度与下一句起始时间，自动延长 end_time，提升可读性；只改结束时间，不动开始时间，不允许重叠。
- **输入**：译文文本、原 start/end、下一句 start（含 50ms 安全缓冲）。
- **输出**：KEEP/EXTEND、adjusted_end、reason，并将调整应用回字幕对象。

## 📅 开源计划
- **2026年2月**：开放 Step6_TimingAdjusterAgent
- **2026年3月**：开发 Windows / macOS / Web UI

## 🙏 致谢

本项目使用了以下优秀的开源项目：

- **[Subtitle Edit](https://github.com/SubtitleEdit/subtitleedit)** (libse): 强大的字幕编辑与处理核心库。
- **[Microsoft Agents](https://github.com/microsoft/agents)**: 构建智能 Agent 的基础框架。
- **[Mscc.GenerativeAI](https://github.com/mscirts/Mscc.GenerativeAI)**: 提供了对 Google Gemini 模型的 .NET 支持。



