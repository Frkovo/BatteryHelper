using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BatteryHelper.Core;

namespace BatteryHelper.App;

internal sealed class PowerView : UserControl
{
    private readonly TextBlock _label = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _value = new() { FontSize = 15, FontFamily = new("Consolas"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _secondary = new() { FontSize = 10.5, FontFamily = new("Consolas"), VerticalAlignment = VerticalAlignment.Center };
    private readonly Path _icon = new() { Width = 17, Height = 17, Stretch = Stretch.Uniform, StrokeThickness = 1.5, Margin = new(0, 0, 7, 0) };
    public event Action? DetailsRequested;
    public string PrimaryText => $"{_label.Text} {_value.Text}";
    public string SecondaryText => _secondary.Text;
    public void RequestDetails() => DetailsRequested?.Invoke();

    public PowerView()
    {
        Width = 248; Height = 44;
        Background = Brushes.Transparent;
        var panel = new Border { Padding = new Thickness(10, 3, 10, 3) };
        panel.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = new(22) }); grid.RowDefinitions.Add(new() { Height = new(15) });
        var first = new StackPanel { Orientation = Orientation.Horizontal };
        _icon.SetResourceReference(Shape.StrokeProperty, "TextBrush");
        first.Children.Add(_icon); first.Children.Add(_label);
        _value.Margin = new(8, 0, 0, 0); first.Children.Add(_value);
        grid.Children.Add(first);
        _secondary.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Grid.SetRow(_secondary, 1); grid.Children.Add(_secondary);
        panel.Child = grid; Content = panel;
        MouseLeftButtonUp += (_, _) => DetailsRequested?.Invoke();
        MouseRightButtonUp += (_, _) => DetailsRequested?.Invoke();
        Cursor = Cursors.Hand;
        Update(PowerSnapshot.Empty("正在连接采集服务。"));
    }

    public void Update(PowerSnapshot sample)
    {
        var view = PowerPresentation.From(sample);
        _label.Text = view.Label; _value.Text = view.Value;
        _secondary.Text = view.SecondaryText;
        _icon.Data = Geometry.Parse(view.Icon switch {
            "bolt" => "M 10,0 L 2,10 L 8,10 L 5,18 L 15,6 L 9,6 Z",
            "plug" => "M 5,0 L 5,5 M 12,0 L 12,5 M 3,5 L 14,5 L 14,9 Q 14,13 8.5,13 Q 3,13 3,9 Z M 8.5,13 L 8.5,18",
            _ => "M 1,4 L 15,4 L 15,14 L 1,14 Z M 15,7 L 18,7 L 18,11 L 15,11 M 5,7 L 5,11"
        });
        ToolTip = $"{view.PrimaryText}\n{view.SecondaryText}\n点击查看详情与设置";
        System.Windows.Automation.AutomationProperties.SetName(this, $"{view.PrimaryText}，{view.SecondaryText}");
    }
}
