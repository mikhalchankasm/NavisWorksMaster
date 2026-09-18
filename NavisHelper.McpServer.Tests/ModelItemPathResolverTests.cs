using NavisHelper.Core;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ModelItemPathResolverTests
{
    private sealed class Node
    {
        public string Name;
        public Node[] Children;
        public Node(string name, params Node[] children) { Name=name; Children=children; }
    }
    private static List<Node> Resolve(Node root,string path) => ModelItemPathResolver.Resolve(new[] {root},path,n=>new[] {n.Name},n=>n.Children);

    [Fact]
    public void AvevaLeadingSlashesRoundTripWithoutSplittingNames()
    {
        var target=new Node("/6501.5.АМ");
        var root=new Node("6501.5.nwd",new Node("/6501.5",target));
        Assert.Same(target,Assert.Single(Resolve(root,"6501.5.nwd / /6501.5 / /6501.5.АМ")));
        Assert.Empty(Resolve(root,"6501.5.nwd / 6501.5 / 6501.5.АМ"));
    }

    [Fact]
    public void DuplicatePathsAreReturnedAsAmbiguousInsteadOfSelectingFirst()
    {
        var a=new Node("same"); var b=new Node("same"); var root=new Node("root",a,b);
        var matches=Resolve(root,"root / same");
        Assert.Equal(2,matches.Count); Assert.Contains(a,matches); Assert.Contains(b,matches);
    }

    [Fact]
    public void ANameContainingTheWholeSeparatorStillRoundTripsAndExposesAmbiguity()
    {
        var a=new Node("a / b"); var b=new Node("b"); var root=new Node("root",a,new Node("a",b));
        Assert.Equal(2,Resolve(root,"root / a / b").Count);
    }

    [Fact]
    public void SlashOnlyNodeNameIsNotConfusedWithHierarchy()
    {
        var target=new Node("/equipment");
        Assert.Same(target,Assert.Single(Resolve(target,"/equipment")));
    }
}
