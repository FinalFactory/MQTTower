using MQTTower.Core.TopicExplorer;

namespace MQTTower.Core.Interfaces;

public interface ITopicExplorerService
{
    IReadOnlyList<TopicTreeNode> GetRoots();
    event EventHandler? Changed;
}
