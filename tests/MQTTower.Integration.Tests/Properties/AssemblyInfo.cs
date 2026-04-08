using Xunit;

// Integration tests share one Dockerized broker; avoid cross-test races on shared topics and DynSec state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
