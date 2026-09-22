using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
namespace MyGears.Views;

public static class AccountCardAnimation
{
    public static void Apply(Border card, bool hover, bool pressed = false)
    {
        if (card.RenderTransform is not TransformGroup)
        {
            card.RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } };
            card.Background = new SolidColorBrush(Color.FromRgb(16,16,18));
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(35,35,38));
            card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 5, Opacity = 0 };
        }
        var transforms = (TransformGroup)card.RenderTransform;
        var scale = (ScaleTransform)transforms.Children[0];
        var move = (TranslateTransform)transforms.Children[1];
        var duration = TimeSpan.FromMilliseconds(pressed ? 75 : 180);
        void Animate(Animatable target, DependencyProperty property, double value) => target.BeginAnimation(property,
            new DoubleAnimation(value, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var size = pressed ? 0.985 : hover ? 1.025 : 1;
        Animate(scale, ScaleTransform.ScaleXProperty, size);
        Animate(scale, ScaleTransform.ScaleYProperty, size);
        Animate(move, TranslateTransform.YProperty, hover && !pressed ? -5 : 0);
        Animate((DropShadowEffect)card.Effect, DropShadowEffect.OpacityProperty, hover ? 0.55 : 0);
        ((SolidColorBrush)card.Background).BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(hover ? Color.FromRgb(29,32,40) : Color.FromRgb(16,16,18), duration));
        ((SolidColorBrush)card.BorderBrush).BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(hover ? Color.FromRgb(246,199,104) : Color.FromRgb(35,35,38), duration));
    }
}
