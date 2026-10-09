using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BatteryHelper.Core;
using Forms = System.Windows.Forms;

namespace BatteryHelper.App;

internal sealed class DetailsWindow : Window
{
    private readonly TextBlock _status = new() { FontSize = 14 };
    private readonly TextBlock _updated = new() { FontSize = 11 };
    private readonly PowerCard _input = new("整机输入", "电脑运行与电池充电的总输入");
    private readonly PowerCard _battery = new("电池", "电池自身充放电功率");
    private readonly PowerCard _cpu = new("CPU", "处理器封装功率");
    private readonly PowerCard _gpu = new("GPU", "图形处理器功率");
    private readonly ComboBox _corner = new() { Foreground = Brushes.Black, Background = Brushes.White };
    private readonly ComboBox _monitor = new() { Foreground = Brushes.Black, Background = Brushes.White };
    private readonly CheckBox _visible = new() { Content = "在任务栏显示功率" };
    private readonly CheckBox _fullscreen = new() { Content = "游戏或视频全屏时自动隐藏" };
    private readonly CheckBox _startup = new() { Content = "登录后自动启动" };
    private readonly TabControl _tabs = new() { BorderThickness = new(0), Padding = new(0, 16, 0, 0) };
    private readonly Action<AppSettings> _save;
    private AppSettings _settings;
    private bool _setting;

    public DetailsWindow(AppSettings settings, Action<AppSettings> save)
    {
        _settings = settings; _save = save;
        Title = "BatteryHelper · 功率详情与设置";
        Width = 650; Height = Math.Min(730, SystemParameters.WorkArea.Height - 60);
        MinWidth = 540; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new("Segoe UI"); FontSize = 13;
        SetResourceReference(BackgroundProperty, "BackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new(24) };
        var heading = new StackPanel { Margin = new(0, 0, 0, 22) };
        heading.Children.Add(new TextBlock { Text = "BatteryHelper", FontSize = 25, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "实时查看功率，知道每一项读数来自哪里。", FontSize = 12, Margin = new(0, 6, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new TextBlock { Text = "BatteryHelper 1.0  ·  本地采集  ·  每秒刷新", FontSize = 11, Margin = new(0, 18, 0, 0) };
        footer.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        _tabs.SetResourceReference(Control.BackgroundProperty, "BackgroundBrush");
        _tabs.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _tabs.Items.Add(new TabItem { Header = "实时功率", Content = BuildOverview() });
        _tabs.Items.Add(new TabItem { Header = "显示与启动", Content = BuildSettings() });
        root.Children.Add(_tabs); Content = root;
        SetSettings(settings);
    }

    private UIElement BuildOverview()
    {
        var panel = new StackPanel();
        var status = new StackPanel { Margin = new(0, 0, 0, 16) };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _updated.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _updated.Margin = new(0, 5, 0, 0);
        status.Children.Add(_status); status.Children.Add(_updated); panel.Children.Add(status);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new());
        AddCard(grid, _input, 0, 0); AddCard(grid, _battery, 0, 1);
        AddCard(grid, _cpu, 1, 0); AddCard(grid, _gpu, 1, 1);
        panel.Children.Add(grid);
        var note = new TextBlock { Margin = new(0, 10, 0, 0), FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Text = "输入功率缺失时，小窗会改为展示电池充电功率。CPU 封装和 GPU 的测量范围可能重叠，各项读数独立展示。" };
        note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(note);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private static void AddCard(Grid grid, PowerCard card, int row, int column)
    {
        card.Margin = new(column == 0 ? 0 : 6, 0, column == 0 ? 6 : 0, 12);
        Grid.SetRow(card, row); Grid.SetColumn(card, column); grid.Children.Add(card);
    }

    private UIElement BuildSettings()
    {
        var panel = new StackPanel();
        panel.Children.Add(Label("任务栏位置"));
        foreach (var combo in new[] { _corner, _monitor }) {
            var textStyle = new Style(typeof(TextBlock));
            textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.Black));
            combo.Resources[typeof(TextBlock)] = textStyle;
            combo.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <TextBlock Text="{Binding}" Foreground="#202020"/>
                </DataTemplate>
                """);
        }
        _corner.Items.Add("左下角"); _corner.Items.Add("右下角");
        _corner.Width = 230; _corner.HorizontalAlignment = HorizontalAlignment.Left;
        _corner.SelectionChanged += (_, _) => SaveFromControls(); panel.Children.Add(_corner);
        panel.Children.Add(Label("显示器"));
        _monitor.Items.Add(new MonitorChoice(null, "主显示器（自动跟随）"));
        foreach (var screen in Forms.Screen.AllScreens) _monitor.Items.Add(new MonitorChoice(screen.DeviceName, $"{screen.DeviceName} · {screen.Bounds.Width}×{screen.Bounds.Height}"));
        _monitor.Width = 350; _monitor.HorizontalAlignment = HorizontalAlignment.Left;
        _monitor.SelectionChanged += (_, _) => SaveFromControls(); panel.Children.Add(_monitor);
        panel.Children.Add(Label("常驻行为"));
        foreach (var check in new[] { _visible, _fullscreen, _startup }) {
            check.Margin = new(0, 0, 0, 16); check.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            check.Checked += (_, _) => SaveFromControls(); check.Unchecked += (_, _) => SaveFromControls();
            panel.Children.Add(check);
        }
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12, Margin = new(0, 16, 0, 0),
            Text = "功率直接嵌入任务栏。空间不足时保留托盘入口，空位恢复后自动重新显示。关闭本窗口后继续常驻；在托盘菜单中选择“退出”可结束程序。" };
        note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(note);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new(0, 12, 0, 9) };

    private void SaveFromControls()
    {
        if (_setting) return;
        _save(_settings with { Corner = _corner.SelectedIndex == 1 ? Corner.Right : Corner.Left,
            MonitorDevice = (_monitor.SelectedItem as MonitorChoice)?.Device,
            Visible = _visible.IsChecked == true, HideOnFullscreen = _fullscreen.IsChecked == true,
            AutoStart = _startup.IsChecked == true });
    }

    public void SetSettings(AppSettings settings)
    {
        _setting = true; _settings = settings;
        _corner.SelectedIndex = settings.Corner == Corner.Left ? 0 : 1;
        _monitor.SelectedItem = _monitor.Items.Cast<MonitorChoice>().FirstOrDefault(m => m.Device == settings.MonitorDevice) ?? _monitor.Items[0];
        _visible.IsChecked = settings.Visible; _fullscreen.IsChecked = settings.HideOnFullscreen; _startup.IsChecked = settings.AutoStart;
        _setting = false;
    }

    public void SelectSettings() => _tabs.SelectedIndex = 1;

    public void Update(PowerSnapshot raw)
    {
        var sample = raw.Fresh(DateTimeOffset.UtcNow);
        var state = sample.Supply switch { SupplyState.Charging => "正在充电", SupplyState.Discharging => "电池放电中",
            SupplyState.ExternalPowerIdle => "已接电 · 未充电", SupplyState.NoBattery => "无电池设备", _ => "供电状态未知" };
        _status.Text = state + (sample.BatteryPercent is { } percent ? $"  ·  电量 {percent:F0}%" : "");
        _updated.Text = $"最近采样 {raw.SampledAt.ToLocalTime():HH:mm:ss}" + (sample.StateReason is { } reason ? $"  ·  {reason}" : "");
        _input.Update(sample.InputPower); _battery.Update(sample.BatteryPower);
        _cpu.Update(sample.CpuPower); _gpu.Update(sample.GpuPower);
    }

    private sealed record MonitorChoice(string? Device, string Label) { public override string ToString() => Label; }
}

internal sealed class PowerCard : Border
{
    private readonly TextBlock _value = new() { FontSize = 29, FontFamily = new("Consolas"), Margin = new(0, 8, 0, 8) };
    private readonly TextBlock _source = new() { FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _coverage = new() { FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 0) };
    private readonly TextBlock _reason = new() { FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) };
    public PowerCard(string title, string subtitle)
    {
        CornerRadius = new(10); Padding = new(16); MinHeight = 177; BorderThickness = new(1);
        SetResourceReference(BackgroundProperty, "PanelBrush"); SetResourceReference(BorderBrushProperty, "BorderBrush");
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        panel.Children.Add(_value); panel.Children.Add(_source); panel.Children.Add(_coverage); panel.Children.Add(_reason);
        foreach (var text in new[] { _source, _coverage, _reason }) text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        ToolTip = subtitle; Child = panel;
    }
    public void Update(PowerReading reading)
    {
        _value.Text = PowerPresentation.Format(reading);
        _source.Text = reading.Source;
        _coverage.Text = reading.Coverage;
        _reason.Text = reading.Reason ?? "";
    }
}
