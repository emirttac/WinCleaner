using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace WinCleaner.Behaviors;

/// <summary>
/// Plays the knob slide only for a real click. Rebinding a recycled row changes
/// <see cref="ToggleButton.IsChecked"/> without a click, and must not animate.
/// </summary>
public static class ToggleSwitchMotion
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(167);
    private static readonly ConditionalWeakTable<ToggleSwitch, Motion> Motions = new();
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;

        EventManager.RegisterClassHandler(typeof(ToggleSwitch), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        EventManager.RegisterClassHandler(typeof(ToggleSwitch), ToggleButton.CheckedEvent, new RoutedEventHandler(OnCheckedChanged));
        EventManager.RegisterClassHandler(typeof(ToggleSwitch), ToggleButton.UncheckedEvent, new RoutedEventHandler(OnCheckedChanged));
        EventManager.RegisterClassHandler(typeof(ToggleSwitch), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleSwitch)sender;
        var motion = Motions.GetOrCreateValue(toggle);
        if (!motion.WatchingContext)
        {
            motion.WatchingContext = true;
            toggle.DataContextChanged += OnDataContextChanged;
        }

        // Template is ready. Put the knob on the bound state with no slide.
        ScheduleSnap(toggle);
    }

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Same container, new row — including when the new row has the same checked state.
        ScheduleSnap((ToggleSwitch)sender);
    }

    private static void OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        // Binding updates land here. A click starts its slide afterwards.
        ScheduleSnap((ToggleSwitch)sender);
    }

    /// <summary>
    /// Recycling can rebuild the template after DataContext/IsChecked change.
    /// Snap once now and again after that layout pass, unless a click slide took over.
    /// </summary>
    private static void ScheduleSnap(ToggleSwitch toggle)
    {
        var motion = Motions.GetOrCreateValue(toggle);
        Snap(toggle);
        if (motion.SnapQueued)
            return;

        motion.SnapQueued = true;
        toggle.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            motion.SnapQueued = false;
            if (motion.Board is not null)
                return;
            Snap(toggle);
        });
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleSwitch)sender;
        if (!toggle.IsEnabled)
            return;

        var on = toggle.IsChecked == true;
        var offTrack = Find<Rectangle>(toggle, "ToggleRectangle");
        var onTrack = Find<Rectangle>(toggle, "ActiveToggleRectangle");
        var offKnob = Find<Rectangle>(toggle, "ToggleEllipse");
        var onKnob = Find<Rectangle>(toggle, "ActiveToggleEllipse");
        if (offTrack is null || onTrack is null || offKnob is null || onKnob is null)
            return;

        var board = new Storyboard();
        Add(board, offTrack, new PropertyPath(UIElement.OpacityProperty), on ? 1 : 0, on ? 0 : 1);
        Add(board, onTrack, new PropertyPath(UIElement.OpacityProperty), on ? 0 : 1, on ? 1 : 0);
        Add(board, offKnob, new PropertyPath(UIElement.OpacityProperty), on ? 1 : 0, on ? 0 : 1);
        Add(board, onKnob, new PropertyPath(UIElement.OpacityProperty), on ? 0 : 1, on ? 1 : 0);
        Add(board, offKnob, ShiftPath(), on ? -9 : 9, on ? 9 : -9);
        Add(board, onKnob, ShiftPath(), on ? -9 : 9, on ? 9 : -9);

        var motion = Motions.GetOrCreateValue(toggle);
        motion.Board = board;
        motion.SnapQueued = false;
        board.Begin(toggle, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private static void Snap(ToggleSwitch toggle)
    {
        Stop(toggle);

        var on = toggle.IsChecked == true;
        SetOpacity(toggle, "ToggleRectangle", on ? 0 : 1);
        SetOpacity(toggle, "ActiveToggleRectangle", on ? 1 : 0);
        SetOpacity(toggle, "ToggleEllipse", on ? 0 : 1);
        SetOpacity(toggle, "ActiveToggleEllipse", on ? 1 : 0);
        SetShift(toggle, "ToggleEllipseTranslate", on ? 9 : -9);
        SetShift(toggle, "ActiveToggleEllipseTranslate", on ? 9 : -9);
    }

    private static void SetOpacity(ToggleSwitch toggle, string name, double opacity)
    {
        if (Find<Rectangle>(toggle, name) is not Rectangle rectangle)
            return;

        rectangle.BeginAnimation(UIElement.OpacityProperty, null);
        rectangle.Opacity = opacity;
    }

    private static void SetShift(ToggleSwitch toggle, string name, double x)
    {
        if (Find<TranslateTransform>(toggle, name) is not TranslateTransform shift)
            return;

        shift.BeginAnimation(TranslateTransform.XProperty, null);
        shift.X = x;
    }

    private static PropertyPath ShiftPath() =>
        new("(0).(1)", UIElement.RenderTransformProperty, TranslateTransform.XProperty);

    private static void Stop(ToggleSwitch toggle)
    {
        if (!Motions.TryGetValue(toggle, out var motion) || motion.Board is null)
            return;

        var board = motion.Board;
        motion.Board = null;
        try
        {
            board.Remove(toggle);
        }
        catch (InvalidOperationException)
        {
            // The slide already finished or the switch left the tree.
        }
    }

    private static void Add(Storyboard board, DependencyObject target, PropertyPath path, double from, double to)
    {
        var animation = new DoubleAnimation(from, to, Duration) { FillBehavior = FillBehavior.HoldEnd };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, path);
        board.Children.Add(animation);
    }

    private static T? Find<T>(ToggleSwitch toggle, string name) where T : class =>
        toggle.Template?.FindName(name, toggle) as T;

    private sealed class Motion
    {
        public Storyboard? Board;
        public bool WatchingContext;
        public bool SnapQueued;
    }
}
