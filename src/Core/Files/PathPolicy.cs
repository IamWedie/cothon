using System.Text.RegularExpressions;

namespace Cothon.Core.Files;

/// <summary>Outcome of validating a user- or model-supplied path.</summary>
public sealed record PathDecision(bool Allowed, string Canonical = "", string? Reason = null);

/// <summary>
/// Blanket rule for every path argument: it lands inside one sandbox root, or
/// it does not run. Canonicalises with the OS separator so the caller can
/// always compare with FileSystem APIs afterwards.
/// </summary>
public sealed class PathPolicy
{
    private static readonly Regex WindowsDrive = new(@"^[a-zA-Z]:[\\/]", RegexOptions.Compiled);
    private static readonly Regex WindowsUnc = new(@"^\\\\", RegexOptions.Compiled);

    private readonly string _root;

    public PathPolicy(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public string Root => _root;

    public PathDecision Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new PathDecision(false, Reason: "A path is required.");

        if (path.StartsWith('/') || path.StartsWith('\\') || WindowsDrive.IsMatch(path) || WindowsUnc.IsMatch(path))
            return new PathDecision(false, Reason: "Absolute paths must stay inside the sandbox.");

        var candidate = Path.GetFullPath(Path.Combine(_root, path));
        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate, _root, StringComparison.OrdinalIgnoreCase))
            return new PathDecision(false, Reason: "Path escapes the sandbox root.");

        return new PathDecision(true, candidate);
    }
}