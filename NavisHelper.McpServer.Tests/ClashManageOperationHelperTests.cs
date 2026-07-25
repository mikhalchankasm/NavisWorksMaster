using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashManageOperationHelperTests
{
    [Theory]
    [InlineData("run", "run")]
    [InlineData(" execute ", "run")]
    [InlineData("выполнить", "run")]
    [InlineData("запуск", "run")]
    [InlineData("reset", "reset")]
    [InlineData("clear", "reset")]
    [InlineData("сброс", "reset")]
    [InlineData("очистить", "reset")]
    [InlineData("compact", "compact")]
    [InlineData("compress", "compact")]
    [InlineData("сжать", "compact")]
    [InlineData("rename", "rename")]
    [InlineData("переименовать", "rename")]
    [InlineData("rename_batch", "rename_batch")]
    [InlineData("batch_rename", "rename_batch")]
    [InlineData("delete", "delete")]
    [InlineData("remove", "delete")]
    [InlineData("удалить", "delete")]
    [InlineData("move", "move")]
    [InlineData("reorder", "move")]
    [InlineData("переместить", "move")]
    [InlineData("порядок", "move")]
    [InlineData("sort", "sort")]
    [InlineData("sort_by_name", "sort")]
    [InlineData("sort_name", "sort")]
    [InlineData("сортировать", "sort")]
    [InlineData("set_settings", "set_settings")]
    [InlineData("settings", "set_settings")]
    [InlineData("configure", "set_settings")]
    [InlineData("config", "set_settings")]
    [InlineData("настройки", "set_settings")]
    [InlineData("настроить", "set_settings")]
    public void NormalizeOperation_ReturnsCanonicalOperationForAliases(string value, string expected)
    {
        Assert.Equal(expected, ClashManageOperationHelper.NormalizeOperation(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("run_tests")]
    public void NormalizeOperation_ReturnsNullForInvalidOperation(string value)
    {
        Assert.Null(ClashManageOperationHelper.NormalizeOperation(value));
    }
}
