using MQTTower.Core.Models;
using MQTTower.Web.Models;
using Xunit;

namespace MQTTower.Web.Tests;

public sealed class AclPermissionRowTests
{
    [Fact]
    public void ToAclEntries_full_row_emits_publish_and_subscribe_bundle()
    {
        var row = new AclPermissionRow
        {
            TopicPattern = "devices/#",
            CanPublish = true,
            CanSubscribe = true,
            Allow = true,
            Priority = 0,
        };

        var flat = row.ToAclEntries().ToList();
        Assert.Equal(4, flat.Count);
        Assert.Contains(flat, HasAcl(AclType.Publish));
        Assert.Contains(flat, HasAcl(AclType.Subscribe));
        Assert.Contains(flat, HasAcl(AclType.PublishReceive));
        Assert.Contains(flat, HasAcl(AclType.Unsubscribe));
    }

    [Fact]
    public void ToAclEntries_read_only_emits_subscribe_bundle_only()
    {
        var row = new AclPermissionRow
        {
            TopicPattern = "#",
            CanPublish = false,
            CanSubscribe = true,
            Allow = true,
            Priority = 0,
        };

        var flat = row.ToAclEntries().ToList();
        Assert.Equal(3, flat.Count);
        Assert.DoesNotContain(flat, HasAcl(AclType.Publish));
    }

    [Fact]
    public void FromAclEntries_round_trips_full_access_row()
    {
        var row = new AclPermissionRow
        {
            TopicPattern = "#",
            CanPublish = true,
            CanSubscribe = true,
            Allow = true,
            Priority = 0,
        };

        var flat = row.ToAclEntries().ToList();
        var rows = AclPermissionRow.FromAclEntries(flat);
        Assert.Single(rows);
        Assert.Equal("#", rows[0].TopicPattern);
        Assert.True(rows[0].CanPublish);
        Assert.True(rows[0].CanSubscribe);
    }

    [Fact]
    public void FlattenForDiff_expands_publish_subscribe_enum()
    {
        var acls = new List<AclEntry>
        {
            new()
            {
                TopicPattern = "t",
                AclType = AclType.PublishSubscribe,
                Allow = true,
                Priority = 1,
            },
        };

        var flat = AclPermissionRow.FlattenForDiff(acls).ToList();
        Assert.Equal(2, flat.Count);
        Assert.Contains(flat, HasAcl(AclType.Publish));
        Assert.Contains(flat, HasAcl(AclType.Subscribe));
    }

    private static Predicate<AclEntry> HasAcl(AclType t) => e => e.AclType == t;
}
