<div align="center">

[English](README.md) | [简体中文](README_CN.md) | [日本語](README_JP.md) | [Español](README_ES.md) | [Français](README_FR.md) | [Deutsch](README_DE.md)

</div>

---

### 📖 プロジェクト紹介
**破壊的なインテリジェント字幕エンジン**。**Multi-Agent 協調**技術に基づき、全プロセスの自動化を実現し、**準人工レベル**の翻訳作品を提供します。

### ✨ 特徴
- **スタイルの統一**：最初に全編のスタイルガイド（基調/文体/呼称戦略）を作成し、「前後で翻訳者が違う」ような違和感を回避します。
- **用語の一貫性**：用語集を自動生成して強制適用し、人名/地名/固有名詞や代名詞の性別（彼/彼女）を統一します。
- **文脈の曖昧性解消**：スライディングウィンドウで前文の訳文 + 後文のプレビューを参照し、指示語の不明確さや区切りによる誤訳を低減します。
- **意味論的監査**：誤訳/訳抜け/幻覚（ハルシネーション）を専門にチェックする校正ループを内蔵。意味のまとまり（Enjambment）プロトコルを遵守し、半端な文を勝手に補完しません。
- **より人間らしい翻訳**：意味のまとまり（Enjambment）+ 複数行スライディング翻訳により、口調がより自然になり、「機械翻訳臭さ」を軽減します。
- **多形式字幕対応**：一般的な多くの字幕形式の入力に対応（自動認識）。
- **トークン節約**：コンパクトな字幕形式により、トークン消費を抑え、処理効率を向上させます。


### クイックスタート

#### 環境要件
- .NET SDK 10.0（本プロジェクトの TargetFramework は net10.0 です）
- 利用可能な大規模言語モデルの API Key と Endpoint（起動エントリでカスタマイズ可能）

#### 実行
プロジェクトのルートディレクトリで実行してください：

```powershell
cd src
dotnet restore
dotnet run --project .\Subtitles.Translate.Agent\Subtitles.Translate.Agent.csproj
```

#### インタラクティブな使用法
プログラム起動後、順次プロンプトが表示されます：
- 字幕ファイルのローカルパスを入力（ターミナルへのドラッグ＆ドロップ対応）
- ターゲット言語を入力（Enterキーでデフォルト：Simplified Chinese）
- API Key を入力（未設定の場合に入力が求められます）

#### 出力ファイル
- 翻訳完了後、元の字幕と同じディレクトリに生成されます：`元のファイル名.<targetLanguage>.srt`
- デフォルトでは単言語訳を出力します（`GenerateTranslatedSrt()`）。二言語字幕が必要な場合は、エントリ部分を `GenerateBilingualSrt()` に変更してください。

#### モデルとエンドポイントのカスタマイズ
エントリ設定は [Program.cs:L90-L105](src/Subtitles.Translate.Agent/Program.cs#L90-L105) の `AgentSystemConfig` 初期化箇所にあり、`ModelId`、`Endpoint`、`ApiKey` などのパラメータを変更できます。

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

変更例（Agent 1/2 にロングコンテキストモデルを使用し、翻訳により低コストなモデルを使用する場合）：

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

#### 推奨モデル（コスト/コンテキストのトレードオフ）
- Agent 1（Director）/ Agent 2（Glossary）：`gemini-3-flash` 推奨（コンテキストが長く、低コストで、全体スキャンや用語抽出に適しています）
- 翻訳フェーズ（Translator）：より低コストなモデルが使用可能です（例：`gpt-oss-120b`）



### 1. Step1_DirectorAgent（全体理解 / スタイルガイド）
- **役割**：最初に字幕全体を読み、後続の翻訳が厳密に遵守すべき「全編ガイドブック」を生成し、スタイルのブレや呼称の混乱を解決します。
- **入力**：全字幕（コンパクト形式化済み）、ターゲット言語などのリクエストパラメータ。

### 2. Step2_GlossaryAgent（用語抽出 / 一貫性制約）
- **役割**：Step1 の戦略と全字幕に基づき、重要なエンティティを抽出し、管理された用語集を構築して、「一つの名詞に一つの訳」を保証します。
- **入力**：Step1 スタイルガイド + 全字幕。
- **出力**：キャラクター表（別名と性別推論を含む）、地名表、用語表（分野と定義を含む）。プロンプトに直接埋め込める Markdown バージョンも提供します。

### 3. Step3_TranslatorAgent（スライディングウィンドウ翻訳 / フォーマット強検証）
- **役割**：スライディングウィンドウで字幕を分割して翻訳し、同時に前文の訳と後文のプレビューを参照することで、区切り、指示語、曖昧性のエラーを低減します。
- **入力**：Step1 スタイルガイド + Step2 用語集 + 既訳コンテキスト + 現在のバッチ + 後文プレビュー。
- **出力**：行ごとの一次翻訳（元のIDと数量の一致を強制）。オプションで Step4 の意味論的監査をトリガーし、最終稿を書き戻します。

### 4. Step4_ReviewerAgent [オープンソース化予定]（意味論的監査 / バックトランスレーションプロトコル）
- **役割**：意味の正確性に対してのみ「監査レベル」の修正を行い、誤訳、訳抜け、幻覚を専門にチェックします。潤色や用語の美化は行いません。
- **入力**：Step3 の一次翻訳バッチ。
- **出力**：行ごとの PASS/FIXED、エラー原因（critique）、最終採用訳文（final_translation）。Step3 の数量と厳密に一致させます。

### 5. Step5_PolisherAgent [オープンソース化予定]（用語準拠 + 流暢な潤色）
- **役割**：タイムラインの区切り位置を破壊しない前提で、まず用語の強制適用と代名詞の修正を行い、その後、より自然な表現への潤色とリズムの最適化を行います。
- **入力**：Step2 用語集 + Step1 スタイルガイド + 現在のバッチ訳文 + 前文の潤色結果（一貫した接続のため）。
- **出力**：polished_text、オプションの note（スラング/用語説明）、optimization_tag（用語修正/文脈潤色/スタイル適合/変更なし）。

### 6. Step6_TimingAdjusterAgent [オープンソース化予定]（読書快適度のためのタイムライン微調整）
- **役割**：訳文の長さと次の文の開始時間に基づいて、end_time を自動的に延長し、可読性を向上させます。終了時間のみを変更し、開始時間は動かさず、重複を許可しません。
- **入力**：訳文テキスト、元の start/end、次の文の start（50ms の安全バッファを含む）。
- **出力**：KEEP/EXTEND、adjusted_end、reason。調整を字幕オブジェクトに適用します。

## 📅 オープンソース計画
- **2026年2月**：Step6_TimingAdjusterAgent を公開
- **2026年3月**：Windows / macOS / Web UI を開発

## 🙏 謝辞

本プロジェクトは、以下の素晴らしいオープンソースプロジェクトを使用しています：

- **[Subtitle Edit](https://github.com/SubtitleEdit/subtitleedit)** (libse): 強力な字幕編集および処理コアライブラリ。
- **[Microsoft Agents](https://github.com/microsoft/agents)**: インテリジェント Agent を構築するための基礎フレームワーク。
- **[Mscc.GenerativeAI](https://github.com/mscirts/Mscc.GenerativeAI)**: Google Gemini モデルの .NET サポートを提供します。
