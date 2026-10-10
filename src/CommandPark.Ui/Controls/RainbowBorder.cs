using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace CommandPark.Ui.Controls;

public sealed class RainbowBorder : Border
{
    private ConicGradientBrush _brush;
    private Task? _task;
    private CancellationTokenSource _cts = new();
    
    protected override void OnLoaded(RoutedEventArgs e)
    {
        Console.WriteLine(this.TryFindResource("RainbowGradientBrush",  out var brush));
        
        base.OnLoaded(e);
        BorderBrush = _brush = (ConicGradientBrush)brush!;
        Console.WriteLine(_brush.GetType());

        // var anim = CreateAnimation();
        // Styles.Add(new Style
        // {
        //     Animations = { anim }
        // });
        
        Tick();
    }

    private async void Tick()
    {
        while (true)
        {
            Dispatcher.Post(() => _brush.Angle += .5);
            await Task.Delay(1);
        }
    }

    // protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    // {
    //     Console.WriteLine("detach");
    //     base.OnDetachedFromVisualTree(e);
    //     if (_task == null) return;
    //     
    //     _cts.Cancel();
    //     _cts.Dispose();
    //     _task = null;
    //     _cts  = new CancellationTokenSource();
    // }

    private Animation CreateAnimation()
    {
        return new Animation
        {
            Duration = TimeSpan.FromSeconds(10),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(ConicGradientBrush.AngleProperty, 0),
                        // new Setter(OpacityProperty, 0),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(ConicGradientBrush.AngleProperty, 365),
                        // new Setter(OpacityProperty, 1),
                    }
                }
            }
        };
    }

    private async Task DoAnimation(Animation animation, CancellationToken cancellationToken = default)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await animation.RunAsync(_brush, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 忽略取消的动画。
        }
    }
}