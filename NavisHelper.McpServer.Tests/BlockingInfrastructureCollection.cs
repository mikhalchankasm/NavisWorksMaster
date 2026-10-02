using Xunit;

namespace NavisHelper.McpServer.Tests;

// These tests deliberately occupy pool threads or fill OS pipes. Scheduling them
// beside other deadline tests tests machine load rather than the intended boundary.
[CollectionDefinition("Blocking infrastructure", DisableParallelization = true)]
public sealed class BlockingInfrastructureCollection { }
