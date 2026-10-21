using System;

namespace WindowMark.Core;

internal sealed class LabelRule
{
    public string ExePath { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string NativeTitle { get; set; } = "";
    public string Label { get; set; } = "";
    public string Color { get; set; } = "#FFD166";

    public bool Matches(TargetWindow target)
        => string.Equals(ExePath, target.ExePath, StringComparison.OrdinalIgnoreCase)
           && string.Equals(ClassName, target.ClassName, StringComparison.Ordinal)
           && string.Equals(NativeTitle, target.NativeTitle, StringComparison.Ordinal);

    public bool SameIdentity(LabelRule other)
        => string.Equals(ExePath, other.ExePath, StringComparison.OrdinalIgnoreCase)
           && string.Equals(ClassName, other.ClassName, StringComparison.Ordinal)
           && string.Equals(NativeTitle, other.NativeTitle, StringComparison.Ordinal);

    public LabelRule Clone() => (LabelRule)MemberwiseClone();
}
