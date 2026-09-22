using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MyGears.Views;

// Opt-in scope: affects the account inspector, not unrelated modules or WebView content.
public static class CheckAccountMotion
{
    public static readonly DependencyProperty ScopeProperty = DependencyProperty.RegisterAttached("Scope", typeof(bool), typeof(CheckAccountMotion), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, ScopeChanged));
    public static void SetScope(DependencyObject obj, bool value) => obj.SetValue(ScopeProperty, value);
    public static bool GetScope(DependencyObject obj) => (bool)obj.GetValue(ScopeProperty);
    public static readonly DependencyProperty CardProperty = DependencyProperty.RegisterAttached("Card", typeof(bool), typeof(CheckAccountMotion), new PropertyMetadata(false, CardChanged));
    public static void SetCard(DependencyObject obj, bool value) => obj.SetValue(CardProperty, value);
    public static bool GetCard(DependencyObject obj) => (bool)obj.GetValue(CardProperty);
    private sealed class State
    {
        public bool Hooked;
        public Color? FieldColor;
        public SolidColorBrush? FieldBrush;
        public ScaleTransform Scale = new();
        public TranslateTransform Lift = new();
        public TranslateTransform Entry = new();
    }
    private static readonly ConditionalWeakTable<FrameworkElement, State> States = new();
    private static State StateFor(FrameworkElement element)
    {
        return States.GetValue(element, e => {
            var state = new State();
            var transforms = new TransformGroup();
            if (e.RenderTransform != Transform.Identity) transforms.Children.Add(e.RenderTransform.CloneCurrentValue());
            transforms.Children.Add(state.Scale); transforms.Children.Add(state.Lift); transforms.Children.Add(state.Entry);
            e.RenderTransform = transforms; e.RenderTransformOrigin = new Point(.5,.5);
            return state;
        });
    }
    private static void ScopeChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is true && obj is FrameworkElement element && ((obj is ButtonBase && obj is not RepeatButton) || obj is TextBoxBase || obj is PasswordBox)) Hook(element);
    }
    private static void CardChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is true && obj is FrameworkElement element) Hook(element);
    }
    private static void Hook(FrameworkElement element)
    {
        var state = StateFor(element);
        if (state.Hooked) return;
        state.Hooked = true;
        if ((element is TextBoxBase || element is PasswordBox) && element is Control field && field.BorderBrush is SolidColorBrush original)
        {
            state.FieldColor = original.Color;
            state.FieldBrush = original.CloneCurrentValue();
            field.SetCurrentValue(Control.BorderBrushProperty, state.FieldBrush);
        }
        element.MouseEnter += (_, _) => Interact(element, true);
        element.MouseLeave += (_, _) => Interact(element, false);
        if (element is ButtonBase)
        {
            element.PreviewMouseLeftButtonDown += (_, _) => Interact(element, true, true);
            element.PreviewMouseLeftButtonUp += (_, _) => Interact(element, element.IsMouseOver);
            element.LostMouseCapture += (_, _) => Interact(element, element.IsMouseOver);
        }
        element.GotKeyboardFocus += (_, _) => Interact(element, true);
        element.LostKeyboardFocus += (_, _) => Interact(element, element.IsMouseOver);
        element.Loaded += (_, _) => { if (GetCard(element)) Reveal(element); };
        element.Unloaded += (_, _) => {
            state.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); state.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            state.Lift.BeginAnimation(TranslateTransform.YProperty, null); state.Entry.BeginAnimation(TranslateTransform.YProperty, null);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            state.FieldBrush?.BeginAnimation(SolidColorBrush.ColorProperty, null);
        };
    }
    public static void Interact(FrameworkElement element, bool hover, bool pressed = false)
    {
        if (!SystemParameters.ClientAreaAnimation || !element.IsEnabled) return;
        var state = StateFor(element);
        var card = GetCard(element);
        var field = element is TextBoxBase || element is PasswordBox;
        if (field && state.FieldBrush != null && state.FieldColor.HasValue)
            state.FieldBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hover || element.IsKeyboardFocusWithin ? Color.FromRgb(246,199,104) : state.FieldColor.Value, TimeSpan.FromMilliseconds(150)));
        var scale = field ? 1 : pressed ? .96 : hover ? (card ? 1.018 : 1.045) : 1;
        Animate(state.Scale, ScaleTransform.ScaleXProperty, scale, pressed ? 70 : 150);
        Animate(state.Scale, ScaleTransform.ScaleYProperty, scale, pressed ? 70 : 150);
        Animate(state.Lift, TranslateTransform.YProperty, hover && !pressed ? (card ? -4 : -1) : 0, 150);
    }
    private static void Animate(Animatable target, DependencyProperty property, double to, int ms)
        => target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    public static void Reveal(FrameworkElement element)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var state = StateFor(element);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
        state.Entry.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(210)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
}
