namespace Subtitles.Translate.Agent.Core.Models;

public enum WorkflowStage
{
    Initialized,
    IntroductionCompleted,
    GlossaryCompleted,
    TranslationCompleted,
    PolishCompleted,
    TimingAdjusted,
    // Add subsequent stages here as they are implemented
}
