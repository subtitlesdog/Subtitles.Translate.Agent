using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Subtitles.Translate.Agent.Core.Models;
using System.Text.Json;

namespace Subtitles.Translate.Agent.Core.Agents
{
    /// <summary>
    /// Step 4: 语义准确性审计 Agent
    /// 使用回译法检测语义错误（错译/漏译/幻觉），不负责术语和润色
    /// </summary>
    public class Step4_ReviewerAgent : AgentBase
    {
        public const string AgentName = nameof(Step4_ReviewerAgent);
        private readonly AIAgent _agent;
        private int _currentBatchIndex;

        public Step4_ReviewerAgent(WorkflowContext context)
            : base(context, AgentName)
        {
            // 创建 ChatClient 并初始化 Agent
            var client = CreateChatClient(_agentConfig.ModelId);
            _agent = client.CreateAIAgent().AsBuilder().Build();
        }

        /// <summary>
        /// 设置批次索引（用于 Token 统计）
        /// </summary>
        public void SetBatchIndex(int batchIndex)
        {
            _currentBatchIndex = batchIndex;
        }

        /// <summary>
        /// 审校一批翻译
        /// </summary>
        /// <param name="context">工作流上下文</param>
        /// <param name="draftTranslations">待审校的翻译初稿</param>
        /// <returns>审校后的结果列表</returns>
        public async Task<List<ReviewItem>> ReviewBatchAsync(WorkflowContext context,
            List<TranslationItem> draftTranslations)
        {
            _logger?.LogDebug("审校批次 {BatchIndex}，共 {Count} 条", _currentBatchIndex, draftTranslations.Count);

            var prompt = GetPrompt(context, draftTranslations);
            _logger?.LogDebug("Prompt 长度: {Length} 字符", prompt.Length);

            _logger?.LogDebug("正在调用 LLM (模型: {ModelId})...", _agentConfig.ModelId);
            var response = await _agent.RunAsync(prompt);

            // 记录 Token 使用量到上下文
            RecordTokenUsage(AgentName, response.Usage, _currentBatchIndex);
            _logger?.LogDebug("Token 使用量 - 输入: {Input}, 输出: {Output}",
                response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);

            // 解析结果
            var reviewItems = ParseJsonResponse<List<ReviewItem>>(response.Text);

            // 如果解析失败，抛出异常
            if (reviewItems == null || reviewItems.Count == 0 ||
                draftTranslations.Count != reviewItems.Count)
            {
                throw new InvalidOperationException(
                    $"批次 {_currentBatchIndex} 审校解析失败：LLM 返回内容无法解析为有效的 JSON 数组");
            }

            _logger?.LogDebug("批次 {BatchIndex} 审校完成，修正 {Fixed} 条",
                _currentBatchIndex, reviewItems.Count(r => r.IsFixed));

            return reviewItems;
        }

        /// <summary>
        /// 生成审校提示词
        /// </summary>
        public string GetPrompt(WorkflowContext context, List<TranslationItem> draftTranslations)
        {
            var request = context.Request;

            // 序列化 Step 3 的翻译初稿（Step 4 只需要原文和译文，不需要术语表）
            var draftJson = JsonSerializer.Serialize(draftTranslations
                .Select(i => new { i.Id, i.Original, draft = i.InitialTranslation }), DefaultJsonOptions);

            var prompt = $$"""
# Role
你是一位专注于**上下文语义准确性**的字幕审计员（Context-Aware Semantic Auditor）。你非常熟悉**意群跨行（Enjambment）**的字幕风格。

# Core Philosophy
当前的译文采用了**意群跨行**处理（即一句话被切分在多行字幕中）。
**你的核心原则是：** 不要因为单行译文“不完整”或“像是半句话”而修改它。只要该行译文与前后行组合后语义正确，即视为合格。

# Task
你将接收 **[初稿字幕]**。你需要逐行审计，**仅在发现实质性语义错误时修改**：
- **错译 (Mistranslation)**：译文核心含义与原文背道而驰。
- **漏译 (Omission)**：丢失了必须翻译的关键实词（名词、动词、核心修饰语）。
- **幻觉 (Hallucination)**：添加了原文完全不存在的含义。

# Strict Rules (Enjambment Protocol)
1.  **禁止补全 (No Auto-Completion)**：如果原文是 "The U.S." (Line 1) "military says" (Line 2)，译文若是 "美国" (Line 1)，**必须 PASS**。严禁将其改为 "美国军方"！
2.  **容忍碎片 (Tolerate Fragments)**：如果译文以形容词、介词或“的”结尾（等待下一行的名词），这是意群跨行的特征，**必须 PASS**。
3.  **仅修语义 (Semantics Only)**：如果回译含义正确，即使不符合中文常规语序（因为是为了配合时间轴切分），也**必须 PASS**。
4.  **润色在下一步**：不要管通顺度，不要管优美度，不要管标点符号。

# Source Language
**Auto-Detect**

# Target Language
**{{request.TargetLanguage}}**

# Input

## Draft Subtitles
{{draftJson}}

# Audit Workflow (Chain of Thought)

对于每一行字幕，必须严格执行以下 **[上下文-回译]** 流程：

1.  **Step A: Context Check (上下文检查)**
    * *Look*: 查看当前行 `Draft`。
    * *Judgment*: 这是一个完整的句子吗？还是一个跨行片段？
    * *Action*: 如果是片段（例如以“的”、“在”结尾），**立即查看下一行**。如果在逻辑上能接上，则视为“结构正确”。

2.  **Step B: Mental Back-Translation (脑海回译)**
    * *Action*: 将 `Draft` 直译回源语言。
    * *Specific Technique*: 如果是跨行片段，仅回译该片段。
        * Example Draft: "美国" -> Back: "The U.S." (Matches Original "The U.S.") -> **PASS**
        * Example Draft: "针对船只的" -> Back: "against the boat's" or "on the boat" (Matches Original "on an alleged drug boat" partially but correctly) -> **PASS**

3.  **Step C: Semantic Comparison (语义比对)**
    * *Criteria*:
        * **幻觉**: 回译是否多出了信息？(例如：Draft写了"美国军方"，Original只有"The U.S." -> **FIXED**: 删去"军方")
        * **漏译**: 回译是否少了**当前片段内**的关键信息？(例如：Original "four people"，Draft "三人" -> **FIXED**)
    * *Pass Condition*: 只要**当前片段**的语义涵盖了**当前原文片段**的含义 -> **PASS**。

# Output Requirements
1.  **Format**: 纯 JSON 数组。
2.  **Count**: 输出数量必须与输入完全一致（**恰好 20 条**）。
3.  **Critique Requirement**: `FIXED` 时必须说明具体的语义错误（而非语法错误）。
4.  **Structure**:
[
  {
    "id": "原始序号，string类型",
    "original": "原文",
    "draft": "Step3初稿",
    "status": "PASS / FIXED",
    "critique": "PASS留空；FIXED说明语义偏差（例如：原文是'4人'，误译为'3人'）",
    "final_translation": "修正后的译文（PASS时与draft完全一致）"
  }
]
""";

            return prompt;
        }

        public override Task ExecuteAsync()
        {
            throw new NotImplementedException();
        }
    }
}