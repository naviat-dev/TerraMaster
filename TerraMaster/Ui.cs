using Microsoft.UI.Xaml.Media.Animation;

namespace TerraMaster;

public static class Ui
{
    public static Storyboard SlideOutAnimation(string axis, TimeSpan duration, DependencyObject elementOpacity, DependencyObject elementTrans, int offset = -200)
    {
        Storyboard storyboard = new();

        DoubleAnimation animationPos = new()
        {
            From = 0,
            To = offset,
            Duration = duration,
            EasingFunction = new ExponentialEase
            {
                EasingMode = EasingMode.EaseOut,
                Exponent = 5
            }
        };

        DoubleAnimation animationOpacity = new()
        {
            To = 0,
            Duration = duration,
            EasingFunction = new ExponentialEase
            {
                EasingMode = EasingMode.EaseOut,
                Exponent = 5
            }
        };

        Storyboard.SetTarget(animationOpacity, elementOpacity);
        Storyboard.SetTargetProperty(animationOpacity, "Opacity");

        storyboard.Children.Add(animationOpacity);

        Storyboard.SetTarget(animationPos, elementTrans);
        Storyboard.SetTargetProperty(animationPos, axis);

        storyboard.Children.Add(animationPos);

        return storyboard;
    }

    public static Storyboard SlideInAnimation(string axis, TimeSpan duration, DependencyObject elementOpacity, DependencyObject elementTrans, int offset = -200)
    {
        Storyboard storyboard = new();

        DoubleAnimation animationPos = new()
        {
            From = offset,
            To = 0,
            Duration = duration,
            EasingFunction = new ExponentialEase
            {
                EasingMode = EasingMode.EaseOut,
                Exponent = 5
            }
        };

        DoubleAnimation animationOpacity = new()
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = new ExponentialEase
            {
                EasingMode = EasingMode.EaseOut,
                Exponent = 5
            }
        };

        Storyboard.SetTarget(animationOpacity, elementOpacity);
        Storyboard.SetTargetProperty(animationOpacity, "Opacity");

        storyboard.Children.Add(animationOpacity);

        Storyboard.SetTarget(animationPos, elementTrans);
        Storyboard.SetTargetProperty(animationPos, axis);

        storyboard.Children.Add(animationPos);
        storyboard.Begin();

        return storyboard;
    }

    public static Storyboard FadeInAnimation(TimeSpan duration, DependencyObject elementColor, Windows.UI.Color fromColor, Windows.UI.Color toColor)
    {
        Storyboard storyboard = new();

        ColorAnimation animationColor = new()
        {
            From = fromColor,
            To = toColor,
            Duration = duration
        };

        Storyboard.SetTarget(animationColor, elementColor);
        Storyboard.SetTargetProperty(animationColor, "Color");

        storyboard.Children.Add(animationColor);
        storyboard.Begin();

        return storyboard;
    }
}