namespace MQTTower.Infrastructure.Automation;

public static class WatcherConditionEvaluator
{
    public static bool EvaluateCondition(string condition, string payload)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        return payload.Contains(condition, StringComparison.OrdinalIgnoreCase);
    }
}
