using Cothon.Core.Tools;

namespace Cothon.Core.Policy;

/// <summary>What the policy engine decides about a proposed tool call.</summary>
public sealed record PolicyDecision(
    bool Allowed,
    string? DenyReason = null)
{
    public static PolicyDecision Allow() => new(true);
    public static PolicyDecision Deny(string reason) => new(false, reason);
}