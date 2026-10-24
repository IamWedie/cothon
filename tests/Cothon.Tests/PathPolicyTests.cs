using Cothon.Core.Files;
using Xunit;

public class PathPolicyTests
{
    private static string Root() => Path.Combine(Path.GetTempPath(), $"cothon-test-{Guid.NewGuid():N}");

    [Fact]
    public void Inside_root_resolves_to_canonical_path()
    {
        var root = Root();
        var policy = new PathPolicy(root);

        var result = policy.Validate("notes/today.md");

        Assert.True(result.Allowed);
        Assert.Equal(Path.Combine(root, "notes", "today.md"), result.Canonical);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Outside_root_is_denied()
    {
        var policy = new PathPolicy(Root());
        var outside = Path.GetTempPath();

        var result = policy.Validate(outside);

        Assert.False(result.Allowed);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Traversal_escapes_are_denied()
    {
        var policy = new PathPolicy(Root());

        Assert.False(policy.Validate("../outside.txt").Allowed);
        Assert.False(policy.Validate("a/../../outside.txt").Allowed);
    }

    [Fact]
    public void Drive_anchors_are_denied()
    {
        var policy = new PathPolicy(Root());

        Assert.False(policy.Validate(@"C:\Windows\system.ini").Allowed);
        Assert.False(policy.Validate("\\\\server\\share\\file").Allowed);
    }

    [Fact]
    public void Empty_path_is_denied()
    {
        var policy = new PathPolicy(Root());
        Assert.False(policy.Validate("").Allowed);
        Assert.False(policy.Validate("   ").Allowed);
    }
}