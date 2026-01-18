<div align="center">

[English](README.md) | [简体中文](README_CN.md) | [日本語](README_JP.md) | [Español](README_ES.md) | [Français](README_FR.md) | [Deutsch](README_DE.md)

</div>

---

### 📖 Project Introduction
**Disruptive Intelligent Subtitle Engine**. Based on **Multi-Agent Collaboration** technology, it automates the entire process, delivering **near-human level** translation works.

### ✨ Highlights
- **Unified Style**: Generates a full-film style guide (tone/style/addressing strategy) first, avoiding the "two different translators" feeling.
- **Consistent Terminology**: Automatically generates and enforces a glossary, unifying names/places/proper nouns and pronoun gender (he/she).
- **Context Disambiguation**: Uses a sliding window to reference previous translations + future previews, reducing ambiguous references and sentence segmentation errors.
- **Semantic Audit**: Built-in review loop specifically checking for mistranslations/omissions/hallucinations; also adheres to enjambment protocols, not randomly completing half-sentences.
- **More Human-Like**: Enjambment + multi-line sliding translation, making the tone more coherent and reducing "machine translation flavor".
- **Multi-Format Subtitles**: Supports multiple common subtitle format inputs (auto-detection).
- **Token Saving**: Compact subtitle format, reducing Token consumption and improving processing efficiency.


### Quick Start

#### Environment Requirements
- .NET SDK 10.0 (This project TargetFramework is net10.0)
- Available LLM API Key and Endpoint (customizable in the startup entry)

#### Run
Execute in the project root directory:

```powershell
cd src
dotnet restore
dotnet run --project .\Subtitles.Translate.Agent\Subtitles.Translate.Agent.csproj
```

#### Interactive Use
After startup, the program will prompt sequentially:
- Enter local subtitle file path (supports drag and drop to terminal)
- Enter target language (Enter defaults to: Simplified Chinese)
- Enter API Key (prompts if not configured)

#### Output Files
- After translation, it will generate in the same directory as the original subtitle: `OriginalFileName.<targetLanguage>.srt`
- Defaults to writing monolingual translation (`GenerateTranslatedSrt()`); for bilingual subtitles, change the entry point to `GenerateBilingualSrt()`

#### Custom Models and Endpoints
Entry configuration is located at `AgentSystemConfig` initialization in [Program.cs:L90-L105](src/Subtitles.Translate.Agent/Program.cs#L90-L105), where `ModelId`, `Endpoint`, `ApiKey` etc. can be modified.

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

Modification example (using long-context models for Agent 1/2, lower cost model for translation):

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

#### Recommended Models (Cost/Context Trade-off)
- Agent 1 (Director) / Agent 2 (Glossary): Recommend `gemini-3-flash` (longer context, lower cost, better for global scanning and terminology extraction)
- Translation Stage (Translator): Can use lower cost models, e.g., `gpt-oss-120b`



### 1. Step1_DirectorAgent (Global Understanding / Style Guide)
- **What it does**: Reads the full subtitle first, generating a "full-film guidebook" for subsequent translations to strictly follow, solving style drift and addressing confusion.
- **Input**: Full subtitles (compact formatted), target language, and other request parameters.

### 2. Step2_GlossaryAgent (Terminology Extraction / Consistency Constraint)
- **What it does**: Based on Step 1's strategy and full subtitles, extracts key entities and builds a controlled glossary, ensuring "one noun, one translation".
- **Input**: Step 1 Style Guide + Full Subtitles.
- **Output**: Character table (including aliases and gender inference), location table, terminology table (including domain and definition), and provides a Markdown version directly embeddable in prompts.

### 3. Step3_TranslatorAgent (Sliding Window Translation / Strong Format Validation)
- **What it does**: Translates subtitles in batches using a sliding window, referencing previous context and future previews to reduce segmentation, reference, and ambiguity errors.
- **Input**: Step 1 Style Guide + Step 2 Glossary + Previous Translated Context + Current Batch + Future Preview.
- **Output**: Line-by-line initial translation (forced to keep original IDs and quantity consistent); optionally triggers Step 4 for semantic audit before writing final draft.

### 4. Step4_ReviewerAgent [To Be Open Sourced] (Semantic Audit / Back-Translation Protocol)
- **What it does**: Only performs "audit-level" corrections for semantic accuracy, specifically checking for mistranslations, omissions, and hallucinations; no polishing, no terminology beautification.
- **Input**: Step 3 Initial Translation Batch.
- **Output**: Line-by-line PASS/FIXED, error reason (critique), and final adopted translation (final_translation), strictly aligned with Step 3 quantity.

### 5. Step5_PolisherAgent [To Be Open Sourced] (Terminology Compliance + Flow Polishing)
- **What it does**: Without breaking timeline cut points, first enforces terminology and pronoun corrections, then performs more native expression polishing and rhythm optimization.
- **Input**: Step 2 Glossary + Step 1 Style Guide + Current Batch Translation + Previous Polished Result (for coherent transition).
- **Output**: polished_text, optional note (slang/terminology explanation), optimization_tag (terminology correction/context polishing/style adaptation/no change).

### 6. Step6_TimingAdjusterAgent [To Be Open Sourced] (Reading Comfort Timeline Fine-tuning)
- **What it does**: Automatically extends end_time based on translation length and next sentence start time to improve readability; only changes end time, does not touch start time, overlap not allowed.
- **Input**: Translated text, original start/end, next sentence start (including 50ms safety buffer).
- **Output**: KEEP/EXTEND, adjusted_end, reason, and applies adjustments back to subtitle object.

## 📅 Open Source Plan
- **February 2026**: Open Step6_TimingAdjusterAgent
- **March 2026**: Develop Windows / macOS / Web UI

## 🙏 Acknowledgements

This project uses the following excellent open-source projects:

- **[Subtitle Edit](https://github.com/SubtitleEdit/subtitleedit)** (libse): Powerful subtitle editing and processing core library.
- **[Microsoft Agents](https://github.com/microsoft/agents)**: Foundation framework for building intelligent Agents.
- **[Mscc.GenerativeAI](https://github.com/mscirts/Mscc.GenerativeAI)**: Provides .NET support for Google Gemini models.
