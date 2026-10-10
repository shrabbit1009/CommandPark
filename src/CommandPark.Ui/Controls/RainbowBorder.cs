using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace CommandPark.Ui.Controls;

/// <summary>
/// 带彩虹渐变描边的容器。
/// </summary>
/// <remarks>
/// 描边渐变的旋转由 Avalonia 关键帧动画驱动：动画作用于控件自身的
/// <see cref="GradientAngle"/>（声明见 RainbowBorderResources.axaml 中的隐式 ControlTheme），
/// 控件再把角度同步到自己私有的 <see cref="ConicGradientBrush"/>。
/// 这样既不需要手动逐帧刷新，多个实例之间也不会互相干扰。
/// </remarks>
public sealed class RainbowBorder : Border
{
    /// <summary>默认渐变画刷的资源键。</summary>
    public const string DefaultGradientBrushKey = "RainbowGradientBrush";

    /// <summary>定义 <see cref="GradientAngle"/> 属性。</summary>
    public static readonly StyledProperty<double> GradientAngleProperty =
        AvaloniaProperty.Register<RainbowBorder, double>(nameof(GradientAngle));

    private ConicGradientBrush? _gradient;

    static RainbowBorder()
    {
        GradientAngleProperty.Changed.AddClassHandler<RainbowBorder>(
            static (border, args) => border.OnGradientAngleChanged(args));
    }

    /// <summary>
    /// 获取或设置描边渐变的旋转角度（单位：度）。
    /// 默认由主题中的无限关键帧动画驱动，也可以手动赋值。
    /// </summary>
    public double GradientAngle
    {
        get => GetValue(GradientAngleProperty);
        set => SetValue(GradientAngleProperty, value);
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        EnsureGradientBrush();
    }

    private void OnGradientAngleChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (_gradient is not null)
        {
            _gradient.Angle = args.GetNewValue<double>();
        }
    }

    /// <summary>
    /// 为当前实例准备一份私有的描边画刷。
    /// 资源里的画刷是共享实例，直接复用会让所有 RainbowBorder 共用同一个旋转角度。
    /// </summary>
    private void EnsureGradientBrush()
    {
        if (_gradient is not null || BorderBrush is not null)
        {
            return;
        }

        if (!this.TryFindResource(DefaultGradientBrushKey, out var resource) ||
            resource is not ConicGradientBrush template)
        {
            return;
        }

        _gradient = Clone(template);
        _gradient.Angle = GradientAngle;
        BorderBrush = _gradient;
    }

    private static ConicGradientBrush Clone(ConicGradientBrush source)
    {
        var clone = new ConicGradientBrush
        {
            Center = source.Center,
            Angle = source.Angle,
            Opacity = source.Opacity,
            SpreadMethod = source.SpreadMethod,
            Transform = source.Transform,
            TransformOrigin = source.TransformOrigin,
        };

        foreach (var stop in source.GradientStops)
        {
            clone.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
        }

        return clone;
    }
}
