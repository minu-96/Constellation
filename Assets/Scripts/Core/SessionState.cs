namespace Constellation
{
    /// <summary>세션 상태 8개. 한 방향으로만 지나간다(docs/spec.md "상태 정의").</summary>
    public enum SessionState
    {
        Idle,
        HandCheck,
        KeywordSelect,
        Drawing,
        Capture,
        AiRequest,
        Result,
        Card
    }
}
