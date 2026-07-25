using System;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SearchSetConditionRulesHelperTests
{
    [Theory]
    [InlineData(null, "contains")]
    [InlineData(" equal ", "equals")]
    [InlineData("contain", "contains")]
    [InlineData("wildcards", "wildcard")]
    [InlineData("exists", "defined")]
    public void NormalizeComparison_PreservesSearchSetAliases(string value, string expected)
    {
        Assert.Equal(expected, SearchSetConditionRulesHelper.NormalizeComparison(value));
    }

    [Theory]
    [InlineData("not_equals", "Search sets cannot persist negative/manual-only operators: not_equals")]
    [InlineData("not_defined", "Search sets cannot persist negative/manual-only operators: not_defined")]
    [InlineData("starts_with", "Unsupported search set comparison: starts_with")]
    public void NormalizeComparison_PreservesValidationMessages(string value, string expected)
    {
        var exception = Assert.Throws<ArgumentException>(() => SearchSetConditionRulesHelper.NormalizeComparison(value));

        Assert.Equal(expected, exception.Message);
    }

    [Theory]
    [InlineData(null, "all")]
    [InlineData("AND", "all")]
    [InlineData("or", "any")]
    [InlineData(" any ", "any")]
    public void NormalizeCombineOperator_PreservesAliases(string value, string expected)
    {
        Assert.Equal(expected, SearchSetConditionRulesHelper.NormalizeCombineOperator(value));
    }

    [Fact]
    public void NormalizeCombineOperator_PreservesValidationMessage()
    {
        var exception = Assert.Throws<ArgumentException>(() => SearchSetConditionRulesHelper.NormalizeCombineOperator("xor"));

        Assert.Equal("Unsupported search set combine_operator: xor", exception.Message);
    }

    [Fact]
    public void NormalizeCondition_DefaultsToPortableItemNameTarget()
    {
        var normalized = SearchSetConditionRulesHelper.NormalizeCondition(new FindItemsCondition
        {
            Value = " Pump ",
        });

        Assert.Equal("Item", normalized.Category);
        Assert.Equal("Name", normalized.Property);
        Assert.Equal(SearchSetConditionRulesHelper.ItemInternalCategory, normalized.CategoryInternal);
        Assert.Equal(SearchSetConditionRulesHelper.ItemNameInternalProperty, normalized.PropertyInternal);
        Assert.Equal("contains", normalized.Operator);
        Assert.Equal("contains", normalized.Comparison);
        Assert.Equal("Pump", normalized.Value);
        Assert.Equal("and", normalized.LogicalOperator);
        Assert.False(normalized.Negate);
        Assert.True(normalized.IgnoreCase);
        Assert.False(normalized.IgnoreDiacritics);
        Assert.False(normalized.IgnoreCharWidth);
    }

    [Fact]
    public void NormalizeCondition_UsesOperatorBeforeComparisonAndDoesNotMutateInput()
    {
        var input = new FindItemsCondition
        {
            Category = " Item ",
            Property = " Name ",
            Operator = "equal",
            Comparison = "wildcard",
            Value = " Valve ",
            LogicalOperator = "OR",
            DataType = "STRING",
            Negate = true,
            IgnoreCase = false,
            IgnoreDiacritics = true,
            IgnoreCharWidth = true,
        };

        var normalized = SearchSetConditionRulesHelper.NormalizeCondition(input);

        Assert.NotSame(input, normalized);
        Assert.Equal(" Item ", input.Category);
        Assert.Equal("equals", normalized.Operator);
        Assert.Equal("equals", normalized.Comparison);
        Assert.Equal("Valve", normalized.Value);
        Assert.Equal("or", normalized.LogicalOperator);
        Assert.Equal("wstring", normalized.DataType);
        Assert.True(normalized.Negate);
        Assert.False(normalized.IgnoreCase);
        Assert.True(normalized.IgnoreDiacritics);
        Assert.True(normalized.IgnoreCharWidth);
    }

    [Fact]
    public void NormalizeCondition_DefinedDiscardsValue()
    {
        var normalized = SearchSetConditionRulesHelper.NormalizeCondition(new FindItemsCondition
        {
            CategoryInternal = "cat",
            PropertyInternal = "prop",
            Operator = "defined",
            Value = "ignored",
        });

        Assert.Null(normalized.Value);
    }

    [Theory]
    [InlineData("integer", "int32")]
    [InlineData("system.int32", "int32")]
    [InlineData("number", "double")]
    [InlineData("system.double", "double")]
    [InlineData("date_time", "datetime")]
    [InlineData("system.datetime", "datetime")]
    [InlineData("displaystring", "wstring")]
    [InlineData("GUID", "guid")]
    public void NormalizeCondition_PreservesPersistedDataTypeAliasesAndUnknownFallback(string value, string expected)
    {
        var normalized = SearchSetConditionRulesHelper.NormalizeCondition(new FindItemsCondition
        {
            CategoryInternal = "cat",
            PropertyInternal = "prop",
            DataType = value,
            Value = "sample",
        });

        Assert.Equal(expected, normalized.DataType);
    }

    [Theory]
    [InlineData(null, "Search condition cannot be null.")]
    public void NormalizeCondition_RejectsNull(FindItemsCondition condition, string expected)
    {
        var exception = Assert.Throws<ArgumentException>(() => SearchSetConditionRulesHelper.NormalizeCondition(condition));

        Assert.Equal(expected, exception.Message);
    }

    [Theory]
    [InlineData("equals")]
    [InlineData("contains")]
    [InlineData("wildcard")]
    public void NormalizeCondition_RequiresValueForValueComparisons(string comparison)
    {
        var exception = Assert.Throws<ArgumentException>(() => SearchSetConditionRulesHelper.NormalizeCondition(new FindItemsCondition
        {
            CategoryInternal = "cat",
            PropertyInternal = "prop",
            Operator = comparison,
            Value = " ",
        }));

        Assert.Equal("The operator \"" + comparison + "\" requires a non-empty value.", exception.Message);
    }

    [Theory]
    [InlineData("Item", "Name", null, null, true)]
    [InlineData("Элемент", "Имя", null, null, true)]
    [InlineData(null, null, "LcOaNode", "LcOaSceneBaseUserName", true)]
    [InlineData(null, null, null, "name", true)]
    [InlineData("Other", "Name", null, null, false)]
    [InlineData(null, null, "Other", "name", false)]
    public void IsDefaultItemNameTarget_RecognizesExistingAliases(
        string category,
        string property,
        string categoryInternal,
        string propertyInternal,
        bool expected)
    {
        Assert.Equal(expected, SearchSetConditionRulesHelper.IsDefaultItemNameTarget(new FindItemsCondition
        {
            Category = category,
            Property = property,
            CategoryInternal = categoryInternal,
            PropertyInternal = propertyInternal,
        }));
    }

    [Fact]
    public void PropertyNameClassifiers_ExcludeDefaultItemNameTarget()
    {
        Assert.True(SearchSetConditionRulesHelper.UsesInternalPropertyOnly(new FindItemsCondition
        {
            CategoryInternal = "custom_category",
            PropertyInternal = "custom_property",
        }));
        Assert.True(SearchSetConditionRulesHelper.HasDisplayAndInternalPropertyNames(new FindItemsCondition
        {
            Category = "Element",
            Property = "Code",
            CategoryInternal = "custom_category",
            PropertyInternal = "custom_property",
        }));
        Assert.False(SearchSetConditionRulesHelper.HasDisplayAndInternalPropertyNames(new FindItemsCondition
        {
            Category = "Item",
            Property = "Name",
            CategoryInternal = SearchSetConditionRulesHelper.ItemInternalCategory,
            PropertyInternal = SearchSetConditionRulesHelper.ItemNameInternalProperty,
        }));
    }
}
