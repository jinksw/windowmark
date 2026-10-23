using System;
using System.Text.Json.Serialization;

namespace WindowMark.Core;

internal enum LabelAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

internal sealed class LabelRule
{
    public string ExePath { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string NativeTitle { get; set; } = "";
    public string Label { get; set; } = "";
    public string Color { get; set; } = "#FFD166";

    public string Anchor { get; set; } = nameof(LabelAnchor.TopRight);
    public double OffsetX { get; set; } = -LabelLayout.DefaultRightInset;
    public double OffsetY { get; set; } = LabelLayout.DefaultTopInset;
    public double Scale { get; set; } = 1.0;

    [JsonIgnore]
    public LabelAnchor AnchorValue
    {
        get
        {
            if (Enum.TryParse<LabelAnchor>(Anchor, true, out var anchor) && Enum.IsDefined(anchor))
                return anchor;
            return LabelAnchor.TopRight;
        }
    }

    public bool Matches(TargetWindow target)
        => string.Equals(ExePath, target.ExePath, StringComparison.OrdinalIgnoreCase)
           && string.Equals(ClassName, target.ClassName, StringComparison.Ordinal)
           && string.Equals(NativeTitle, target.NativeTitle, StringComparison.Ordinal);

    public bool SameIdentity(LabelRule other)
        => string.Equals(ExePath, other.ExePath, StringComparison.OrdinalIgnoreCase)
           && string.Equals(ClassName, other.ClassName, StringComparison.Ordinal)
           && string.Equals(NativeTitle, other.NativeTitle, StringComparison.Ordinal);

    public LabelRule Clone() => (LabelRule)MemberwiseClone();

    public void ResetLayout()
    {
        Anchor = nameof(LabelAnchor.TopRight);
        OffsetX = -LabelLayout.DefaultRightInset;
        OffsetY = LabelLayout.DefaultTopInset;
        Scale = 1.0;
    }

    public void SanitizeLayout()
    {
        Anchor = AnchorValue.ToString();

        if (double.IsNaN(OffsetX) || double.IsInfinity(OffsetX))
            OffsetX = -LabelLayout.DefaultRightInset;
        if (double.IsNaN(OffsetY) || double.IsInfinity(OffsetY))
            OffsetY = LabelLayout.DefaultTopInset;
        if (double.IsNaN(Scale) || double.IsInfinity(Scale))
            Scale = 1.0;

        Scale = Math.Clamp(Scale, LabelLayout.MinScale, LabelLayout.MaxScale);
    }
}

internal static class LabelLayout
{
    public const double MinScale = 0.8;
    public const double MaxScale = 1.8;
    public const double DefaultRightInset = 150;
    public const double DefaultTopInset = 5;

    public static readonly (string Label, double Scale)[] SizePresets =
    {
        ("小", 0.8),
        ("标准", 1.0),
        ("大", 1.25),
        ("特大", 1.5)
    };

    public static (string Anchor, double OffsetX, double OffsetY) PositionPreset(LabelAnchor anchor)
    {
        const double edgeX = 8;
        const double edgeY = 5;

        return anchor switch
        {
            LabelAnchor.TopLeft => (nameof(LabelAnchor.TopLeft), edgeX, edgeY),
            LabelAnchor.TopCenter => (nameof(LabelAnchor.TopCenter), 0, edgeY),
            LabelAnchor.TopRight => (nameof(LabelAnchor.TopRight), -DefaultRightInset, edgeY),
            LabelAnchor.MiddleLeft => (nameof(LabelAnchor.MiddleLeft), edgeX, 0),
            LabelAnchor.Center => (nameof(LabelAnchor.Center), 0, 0),
            LabelAnchor.MiddleRight => (nameof(LabelAnchor.MiddleRight), -edgeX, 0),
            LabelAnchor.BottomLeft => (nameof(LabelAnchor.BottomLeft), edgeX, -edgeY),
            LabelAnchor.BottomCenter => (nameof(LabelAnchor.BottomCenter), 0, -edgeY),
            LabelAnchor.BottomRight => (nameof(LabelAnchor.BottomRight), -edgeX, -edgeY),
            _ => (nameof(LabelAnchor.TopRight), -DefaultRightInset, edgeY)
        };
    }

    public static string AnchorName(LabelAnchor anchor) => anchor switch
    {
        LabelAnchor.TopLeft => "左上",
        LabelAnchor.TopCenter => "上中",
        LabelAnchor.TopRight => "右上（避开标题栏按钮）",
        LabelAnchor.MiddleLeft => "左中",
        LabelAnchor.Center => "居中",
        LabelAnchor.MiddleRight => "右中",
        LabelAnchor.BottomLeft => "左下",
        LabelAnchor.BottomCenter => "下中",
        LabelAnchor.BottomRight => "右下",
        _ => ""
    };
}
