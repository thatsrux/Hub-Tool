using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HubTool;

/// <summary>A small borderless badge. Device modules exist only while expanded.</summary>
internal sealed class OverlayWindow : Window
{
    private readonly Func<StackPanel> modules;
    private readonly Action savePosition;
    private readonly Action returnFocus;
    private bool dragging;
    private Point pointerStart;
    private Point windowStart;
    private Point badgeAnchor;
    private Point dragAnchor;
    public bool Expanded { get; private set; }
    public Point BadgePosition => Expanded ? badgeAnchor : new Point(Left, Top);
    public const double BadgeSize = 52;

    public OverlayWindow(Func<StackPanel> modules, Action savePosition, Action returnFocus)
    {
        this.modules = modules; this.savePosition = savePosition; this.returnFocus = returnFocus;
        Title = "Hub overlay";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = Brushes.White;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        UseLayoutRounding = true;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { SetExpanded(false); returnFocus(); e.Handled = true; } };
        Deactivated += (_, _) => { if (Expanded && !IsMouseOver) SetExpanded(false); };
        SetExpanded(false);
    }

    public void RefreshModules() { if (Expanded) Render(); }

    public void SetExpanded(bool expanded)
    {
        bool wasExpanded = Expanded;
        if (expanded && !wasExpanded) badgeAnchor = new Point(Left, Top);
        Expanded = expanded;
        Width = expanded ? 352 : BadgeSize;
        SizeToContent = expanded ? SizeToContent.Height : SizeToContent.Manual;
        if (!expanded) Height = BadgeSize;
        Render();
        if (wasExpanded && !expanded) { Left = badgeAnchor.X; Top = badgeAnchor.Y; }
        if (IsVisible)
        {
            UpdateLayout();
            if (expanded && !wasExpanded) { Left = badgeAnchor.X + BadgeSize - ActualWidth; Top = badgeAnchor.Y + BadgeSize - ActualHeight; }
            if (App.PreviewDirectory == null) KeepOnScreen();
        }
    }

    public void KeepOnScreen()
    {
        var area = WorkArea();
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
        if (Expanded) badgeAnchor = new Point(Math.Clamp(badgeAnchor.X, area.Left, area.Right - BadgeSize), Math.Clamp(badgeAnchor.Y, area.Top, area.Bottom - BadgeSize));
        savePosition();
    }

    private Rect WorkArea()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return SystemParameters.WorkArea;
        var area = Native.WorkArea(hwnd);
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(transform.Transform(new Point(area.Left, area.Top)), transform.Transform(new Point(area.Right, area.Bottom)));
    }

    private void AddDrag(UIElement target)
    {
        target.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragging = false; pointerStart = PointToScreen(e.GetPosition(this)); windowStart = new Point(Left, Top); dragAnchor = BadgePosition;
        };
        target.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || !target.IsMouseCaptured) return;
            var point = PointToScreen(e.GetPosition(this)); var delta = point - pointerStart;
            if (!dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) < 5) return;
            dragging = true;
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = windowStart.X + delta.X / dpi.DpiScaleX; Top = windowStart.Y + delta.Y / dpi.DpiScaleY;
            if (Expanded) badgeAnchor = new Point(dragAnchor.X + delta.X / dpi.DpiScaleX, dragAnchor.Y + delta.Y / dpi.DpiScaleY);
        };
        target.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            target.ReleaseMouseCapture(); KeepOnScreen(); dragging = false; e.Handled = true;
        };
    }

    private void Render()
    {
        if (!Expanded)
        {
            var badge = new Button
            {
                Width = 48, Height = 48, Padding = new Thickness(0), Margin = new Thickness(2),
                Background = (Brush)new BrushConverter().ConvertFromString("#173B39")!,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#76F4D3")!,
                Content = new TextBlock { Text = "H", FontSize = 24, FontWeight = FontWeights.Bold },
                ToolTip = "Hub · clic per aprire, trascina per spostare"
            };
            System.Windows.Automation.AutomationProperties.SetName(badge, "Apri moduli Hub");
            badge.SetResourceReference(StyleProperty, "BadgeButton");
            var menu = new ContextMenu();
            var hide = new MenuItem { Header = "Nascondi overlay" }; hide.Click += (_, _) => Close();
            menu.Items.Add(hide); badge.ContextMenu = menu;
            badge.Click += (_, _) => { SetExpanded(true); if (App.PreviewDirectory == null) Activate(); };
            AddDrag(badge);
            Content = badge;
            return;
        }
        var panel = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(2, 0, 2, 8) };
        var collapse = new Button { Content = "−", ToolTip = "Comprimi overlay", Padding = new Thickness(10, 2, 10, 4), Margin = new Thickness(0) };
        collapse.Click += (_, _) => { SetExpanded(false); returnFocus(); };
        DockPanel.SetDock(collapse, Dock.Right); header.Children.Add(collapse);
        var drag = new Button { Content = "HUB", HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(4, 6, 4, 6),
            Margin = new Thickness(0), Background = Brushes.Transparent, FontWeight = FontWeights.Bold, FontSize = 12 };
        AddDrag(drag); header.Children.Add(drag); panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer { Content = modules(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Max(100, Math.Min(570, WorkArea().Height - 90)) });
        Content = new Border { Background = (Brush)new BrushConverter().ConvertFromString("#0F1825")!, CornerRadius = new CornerRadius(14),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#34485F")!, BorderThickness = new Thickness(1), Padding = new Thickness(12), Child = panel };
    }
}
