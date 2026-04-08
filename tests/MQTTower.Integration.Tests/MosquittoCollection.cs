using MQTTower.Integration.Tests.Fixtures;

namespace MQTTower.Integration.Tests;

/// <summary>One shared Mosquitto container + broker per integration test run (all classes in this collection).</summary>
[CollectionDefinition("Mosquitto")]
public sealed class MosquittoCollection : ICollectionFixture<MosquittoFixture>
{
}
