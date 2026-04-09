using MQTTower.Core.Models;

namespace MQTTower.Web.Models;

/// <summary>User-facing permission row (topic + publish/subscribe intent) mapped to Mosquitto DynSec ACL entries.</summary>
public sealed class AclPermissionRow
{
    public string TopicPattern { get; set; } = "#";

    public bool CanPublish { get; set; }

    public bool CanSubscribe { get; set; }

    public bool Allow { get; set; } = true;

    public int Priority { get; set; }

    /// <summary>Expands to publishClientSend, subscribePattern, publishClientReceive, unsubscribePattern as needed.</summary>
    public IReadOnlyList<AclEntry> ToAclEntries()
    {
        var list = new List<AclEntry>();
        if (CanPublish)
        {
            list.Add(new AclEntry
            {
                TopicPattern = TopicPattern,
                AclType = AclType.Publish,
                Allow = Allow,
                Priority = Priority,
            });
        }

        if (CanSubscribe)
        {
            list.Add(new AclEntry
            {
                TopicPattern = TopicPattern,
                AclType = AclType.Subscribe,
                Allow = Allow,
                Priority = Priority,
            });
            list.Add(new AclEntry
            {
                TopicPattern = TopicPattern,
                AclType = AclType.PublishReceive,
                Allow = Allow,
                Priority = Priority,
            });
            list.Add(new AclEntry
            {
                TopicPattern = TopicPattern,
                AclType = AclType.Unsubscribe,
                Allow = Allow,
                Priority = Priority,
            });
        }

        return list;
    }

    /// <summary>Expands <see cref="AclType.PublishSubscribe"/> for diff against broker ACLs.</summary>
    public static IReadOnlyList<AclEntry> FlattenForDiff(IReadOnlyList<AclEntry> acls) =>
        ExpandPublishSubscribe(acls);

    /// <summary>Collapses flat broker ACLs into permission rows (grouped by topic, priority, allow).</summary>
    public static List<AclPermissionRow> FromAclEntries(IReadOnlyList<AclEntry> acls)
    {
        if (acls.Count == 0)
        {
            return new List<AclPermissionRow>();
        }

        var expanded = ExpandPublishSubscribe(acls);
        var rows = new List<AclPermissionRow>();
        foreach (var g in expanded.GroupBy(a => (a.TopicPattern, a.Priority, a.Allow)))
        {
            var types = g.Select(x => x.AclType).ToHashSet();
            var canPublish = types.Contains(AclType.Publish);
            var canSubscribe = types.Contains(AclType.Subscribe)
                || types.Contains(AclType.PublishReceive)
                || types.Contains(AclType.Unsubscribe);

            if (!canPublish && !canSubscribe)
            {
                continue;
            }

            rows.Add(new AclPermissionRow
            {
                TopicPattern = g.Key.TopicPattern,
                Priority = g.Key.Priority,
                Allow = g.Key.Allow,
                CanPublish = canPublish,
                CanSubscribe = canSubscribe,
            });
        }

        return rows;
    }

    /// <summary>Split <see cref="AclType.PublishSubscribe"/> into publish + subscribe halves for grouping.</summary>
    private static List<AclEntry> ExpandPublishSubscribe(IReadOnlyList<AclEntry> acls)
    {
        var list = new List<AclEntry>();
        foreach (var a in acls)
        {
            if (a.AclType == AclType.PublishSubscribe)
            {
                list.Add(new AclEntry
                {
                    TopicPattern = a.TopicPattern,
                    AclType = AclType.Publish,
                    Allow = a.Allow,
                    Priority = a.Priority,
                });
                list.Add(new AclEntry
                {
                    TopicPattern = a.TopicPattern,
                    AclType = AclType.Subscribe,
                    Allow = a.Allow,
                    Priority = a.Priority,
                });
            }
            else
            {
                list.Add(a);
            }
        }

        return list;
    }
}
