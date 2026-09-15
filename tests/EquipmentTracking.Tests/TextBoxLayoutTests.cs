using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace EquipmentTracking.Tests;

public sealed class TextBoxLayoutTests
{
    [Fact]
    public void SharedTextBoxTemplate_AppliesPaddingOnceAndCentersCaret()
    {
        Exception? layoutException = null;
        Rect caretRectangle = Rect.Empty;
        var thread = new Thread(() =>
        {
            try
            {
                var resourcePath = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    "src",
                    "EquipmentTracking.App",
                    "Themes",
                    "LayoutStyles.xaml"));
                using var resourceStream = File.OpenRead(resourcePath);
                var resources = (ResourceDictionary)XamlReader.Load(resourceStream);
                var textBox = new TextBox
                {
                    Style = (Style)resources[typeof(TextBox)],
                    Width = 240,
                    Height = 40,
                    Padding = new Thickness(34, 11, 10, 11),
                    Text = "X"
                };

                textBox.Measure(new Size(textBox.Width, textBox.Height));
                textBox.Arrange(new Rect(0, 0, textBox.Width, textBox.Height));
                textBox.UpdateLayout();
                caretRectangle = textBox.GetRectFromCharacterIndex(0);
            }
            catch (Exception ex)
            {
                layoutException = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF layout test did not complete.");
        if (layoutException is not null)
        {
            ExceptionDispatchInfo.Capture(layoutException).Throw();
        }

        Assert.InRange(caretRectangle.X, 35, 39);
        Assert.InRange(
            caretRectangle.Y + caretRectangle.Height / 2d,
            19,
            21);
    }

    [Fact]
    public void SharedTextBoxTemplate_KeepsMultilineContentHostStretched()
    {
        Exception? layoutException = null;
        double contentHostHeight = 0;
        var thread = new Thread(() =>
        {
            try
            {
                var resourcePath = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    "src",
                    "EquipmentTracking.App",
                    "Themes",
                    "LayoutStyles.xaml"));
                using var resourceStream = File.OpenRead(resourcePath);
                var resources = (ResourceDictionary)XamlReader.Load(resourceStream);
                var textBox = new TextBox
                {
                    Style = (Style)resources[typeof(TextBox)],
                    Width = 240,
                    Height = 100,
                    AcceptsReturn = true,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Text = "First line\nSecond line"
                };

                textBox.Measure(new Size(textBox.Width, textBox.Height));
                textBox.Arrange(new Rect(0, 0, textBox.Width, textBox.Height));
                textBox.UpdateLayout();
                var contentHost = Assert.IsAssignableFrom<FrameworkElement>(
                    textBox.Template.FindName("PART_ContentHost", textBox));
                contentHostHeight = contentHost.ActualHeight;
            }
            catch (Exception ex)
            {
                layoutException = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF layout test did not complete.");
        if (layoutException is not null)
        {
            ExceptionDispatchInfo.Capture(layoutException).Throw();
        }

        Assert.InRange(contentHostHeight, 95, 99);
    }
}
