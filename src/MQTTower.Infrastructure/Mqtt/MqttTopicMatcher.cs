namespace MQTTower.Infrastructure.Mqtt;

/// <summary>MQTT topic vs subscription filter matching (+ and #, with # only valid as the last segment).</summary>
public static class MqttTopicMatcher
{
    public static bool TopicMatches(string topic, string filter)
    {
        if (filter == "#")
        {
            return true;
        }

        var tf = filter.Split('/');
        var tt = topic.Split('/');

        for (var i = 0; i < tf.Length; i++)
        {
            if (tf[i] == "#")
            {
                return i == tf.Length - 1;
            }

            if (i >= tt.Length)
            {
                return false;
            }

            if (tf[i] != "+" && tf[i] != tt[i])
            {
                return false;
            }
        }

        return tf.Length == tt.Length;
    }
}
