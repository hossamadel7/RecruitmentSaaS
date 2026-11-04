namespace RecruitmentSaaS.Services;
public sealed record AiSalesCostAssumptions(int DailyReplies = 1000, int ReplyInputTokens = 1500, int ReplyOutputTokens = 150,
    int DailyLearningChunks = 100, int LearningInputTokens = 4000, int LearningOutputTokens = 800,
    int DailyEmbeddingTokens = 200000, int Days = 30);
public static class AiSalesCostEstimator
{
    public static object Estimate(AiSalesCostAssumptions a, AiSalesLearningOptions rates)
    {
        var values = new[] { a.DailyReplies, a.ReplyInputTokens, a.ReplyOutputTokens, a.DailyLearningChunks, a.LearningInputTokens, a.LearningOutputTokens, a.DailyEmbeddingTokens, a.Days };
        if (values.Any(v => v is < 0 or > 10000000) || a.Days is < 1 or > 366) throw new AiSalesValidation("Invalid cost volume assumptions.");
        decimal input = (decimal)a.DailyReplies * a.ReplyInputTokens + (decimal)a.DailyLearningChunks * a.LearningInputTokens;
        decimal output = (decimal)a.DailyReplies * a.ReplyOutputTokens + (decimal)a.DailyLearningChunks * a.LearningOutputTokens;
        var daily = (input * rates.InputCostPerMillion + output * rates.OutputCostPerMillion + a.DailyEmbeddingTokens * rates.EmbeddingCostPerMillion) / 1000000m;
        return new { assumptions = a, dailyInputTokens = input, dailyOutputTokens = output, dailyEmbeddingTokens = a.DailyEmbeddingTokens,
            dailyEstimatedModelCost = daily, periodEstimatedModelCost = daily * a.Days,
            ratesConfigured = rates.InputCostPerMillion > 0 || rates.OutputCostPerMillion > 0 || rates.EmbeddingCostPerMillion > 0,
            excludes = "Transcription, media interpretation, retries/unknown interrupted usage, hosting, taxes and WhatsApp charges. Rates use the operator's configured currency per million tokens, not a claimed current vendor price." };
    }
}
