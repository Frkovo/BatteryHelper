using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using Microsoft.Win32;

namespace BatteryHelper.App;

internal static class Theme
{
    private static bool? _dark;
    public static void Apply(System.Windows.Application application, bool? forceDark = null)
    {
        var dark = forceDark ?? IsDark();
        if (_dark == dark && application.Resources.Contains("TextBrush")) return;
        _dark = dark;
        application.Resources["BackgroundBrush"] = Brush(dark ? "#202020" : "#F5F5F5");
        application.Resources["PanelBrush"] = Brush(dark ? "#2A2A2A" : "#FFFFFF");
        application.Resources["TextBrush"] = Brush(dark ? "#F3F3F3" : "#202020");
        application.Resources["MutedBrush"] = Brush(dark ? "#B1B1B8" : "#64646E");
        application.Resources["BorderBrush"] = Brush(dark ? "#414146" : "#DDDEE4");
        application.Resources["AccentBrush"] = Brush(dark ? "#B8A3FF" : "#6952C3");
        var button = new Style(typeof(Button));
        button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 8, 14, 8)));
        button.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0)));
        button.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("PanelBrush")));
        button.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        button.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("BorderBrush")));
        application.Resources[typeof(Button)] = button;
        var text = new Style(typeof(TextBlock));
        text.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        application.Resources[typeof(TextBlock)] = text;
        application.Resources[typeof(TabItem)] = XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TabItem">
              <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
              <Setter Property="FontSize" Value="13"/>
              <Setter Property="Template"><Setter.Value>
                <ControlTemplate TargetType="TabItem">
                  <Border x:Name="HeaderBorder" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource PanelBrush}"
                          BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="16,8" Margin="0,0,8,0">
                    <ContentPresenter ContentSource="Header" RecognizesAccessKey="True"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsSelected" Value="True">
                      <Setter TargetName="HeaderBorder" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
                      <Setter Property="Foreground" Value="{DynamicResource AccentBrush}"/>
                      <Setter Property="FontWeight" Value="SemiBold"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value></Setter>
            </Style>
            """);
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static bool IsDark()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return (int?)key?.GetValue("SystemUsesLightTheme") != 1; }
        catch { return true; }
    }
}
