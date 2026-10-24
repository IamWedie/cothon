using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Cothon.Core.Sessions;

namespace Cothon.Ui.Controls;

/// <summary>
/// Siri-style breathing orb: three staggered translucent rings around a
/// glowing core; tempo and amplitude follow the voice session phase.
/// </summary>
public sealed class SiriOrb : Control
{
    public static readonly StyledProperty<VoiceSessionState> StateProperty =
        AvaloniaProperty.Register<SiriOrb, VoiceSessionState>(
            nameof(State), VoiceSessionState.Idle);

    public VoiceSessionState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private static readonly IBrush RingFar = new SolidColorBrush(Color.Parse("#0A84FF"));
    private static readonly IBrush RingMid = new SolidColorBrush(Color.Parse("#5AC8FA"));
    private static readonly IBrush RingNear = new SolidColorBrush(Color.Parse("#64D2FF"));
    private static readonly IBrush Core = new SolidColorBrush(Color.Parse("#0A84FF"));

    private static readonly double[] RingRadii = [1.00, 0.80, 0.62];
    private static readonly IBrush[] RingBrushes = [RingFar, RingMid, RingNear];
    private static readonly double[] RingOpacity = [0.16, 0.26, 0.38];

    private double _phase;
    private DispatcherTimer? _timer;

    public SiriOrb()
    {
        StateProperty.Changed.AddClassHandler<SiriOrb>((o, _) => o.InvalidateVisual());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background,
            (_, _) =>
            {
                _phase += 0.055;
                InvalidateVisual();
            });
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
    }

    public override void Render(DrawingContext context)
    {
        var baseR = Math.Min(Bounds.Width, Bounds.Height) / 2 * 0.78;
        if (baseR <= 1)
            return;

        var cx = Bounds.Width / 2;
        var cy = Bounds.Height / 2;

        var (amplitude, tempo, brightness) = State switch
        {
            VoiceSessionState.Idle => (0.35, 1.0, 0.45),
            VoiceSessionState.Listening => (1.00, 2.2, 1.00),
            VoiceSessionState.Thinking => (1.40, 3.6, 0.90),
            VoiceSessionState.Speaking => (1.60, 5.0, 1.00),
            _ => (0.35, 1.0, 0.45),
        };

        var t = _phase * tempo;

        for (var i = 0; i < RingRadii.Length; i++)
        {
            var scale = 1 + amplitude * 0.10 * Math.Sin(t * (1 + 0.18 * i) + i * 1.05);
            var r = baseR * RingRadii[i] * scale;
            using (context.PushOpacity(Math.Clamp(brightness * RingOpacity[i], 0, 1)))
            {
                context.DrawEllipse(RingBrushes[i], null, new Point(cx, cy), r, r);
            }
        }

        var coreR = baseR * 0.34 * (1 + 0.05 * Math.Sin(2 * t));
        using (context.PushOpacity(Math.Clamp(0.55 + brightness * 0.45, 0, 1)))
        {
            context.DrawEllipse(Core, null, new Point(cx, cy), coreR, coreR);
        }
    }
}
