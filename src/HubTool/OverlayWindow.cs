using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace HubTool;

/// <summary>A compact icon dock that opens one device panel at a time.</summary>
internal sealed class OverlayWindow : Window
{
    private readonly Func<IReadOnlyList<Device>> getDevices;
    private readonly Func<Device, FrameworkElement> createModule;
    private readonly Settings settings;
    private readonly Action savePosition;
    private readonly Action returnFocus;
    private readonly Action openSettings;
    private Device? selected;
    private bool dragging;
    private Point pointerStart;
    private Point windowStart;
    private Point dockAnchor;
    private Point dragAnchor;
    public bool Expanded => selected != null;
    public int IconCount { get; private set; }
    public Point BadgePosition => Expanded ? dockAnchor : new Point(Left, Top);

    public OverlayWindow(Func<IReadOnlyList<Device>> getDevices, Func<Device, FrameworkElement> createModule, Settings settings,
        Action savePosition, Action returnFocus, Action openSettings)
    {
        this.getDevices = getDevices; this.createModule = createModule; this.settings = settings;
        this.savePosition = savePosition; this.returnFocus = returnFocus; this.openSettings = openSettings;
        Title = "Hub overlay"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Foreground = Brushes.White;
        ShowInTaskbar = false; ShowActivated = false; UseLayoutRounding = true;
        SourceInitialized += (_, _) =>
        {
            // Layered transparent WPF windows can intermittently corrupt small vector paths on some GPU drivers.
            // The overlay is tiny, so per-window software composition is both stable and inexpensive.
            if (PresentationSource.FromVisual(this) is HwndSource source) source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (Expanded) Collapse(); else Close();
            returnFocus(); e.Handled = true;
        };
        Deactivated += (_, _) => { if (Expanded && settings.OverlayAutoCollapse && !IsMouseOver) Collapse(); };
        RefreshAppearance();
    }

    public void RefreshModules()
    {
        if (selected != null && !getDevices().Any(d => d.Id == selected.Id)) selected = null;
        Render();
    }

    public void RefreshAppearance()
    {
        Topmost = settings.OverlayAlwaysOnTop; Opacity = settings.OverlayOpacity;
        if (Expanded) dockAnchor = BadgePosition;
        Render();
        if (IsVisible) KeepOnScreen();
    }

    public void Collapse()
    {
        if (!Expanded) return;
        selected = null; Render(); Left = dockAnchor.X; Top = dockAnchor.Y; UpdateLayout(); KeepOnScreen();
    }

    internal void OpenFirstModuleForDiagnostics()
    {
        var first = getDevices().FirstOrDefault();
        if (first != null) Open(first);
    }

    public void KeepOnScreen()
    {
        var area = WorkArea();
        var currentSize = Expanded ? new Size(ActualWidth, ActualHeight) : CompactSize();
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - currentSize.Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - currentSize.Height));
        if (Expanded)
        {
            var compact = CompactSize();
            dockAnchor = new Point(Math.Clamp(dockAnchor.X, area.Left, area.Right - compact.Width), Math.Clamp(dockAnchor.Y, area.Top, area.Bottom - compact.Height));
        }
        savePosition();
    }

    private void Open(Device device)
    {
        if (selected?.Id == device.Id) { Collapse(); return; }
        if (!Expanded) dockAnchor = new Point(Left, Top);
        selected = device; Render();
        if (IsVisible)
        {
            if (App.PreviewDirectory == null) { KeepOnScreen(); Activate(); Focus(); }
        }
    }

    private void Render()
    {
        var devices = getDevices(); IconCount = devices.Count;
        var rail = CreateRail(devices);
        if (!Expanded)
        {
            var size = CompactSize(); Width = size.Width; Height = size.Height; SizeToContent = SizeToContent.Manual;
            Content = rail; return;
        }

        var area = WorkArea();
        var compactSize = CompactSize();
        var card = new Grid { Width = 374, MaxHeight = Math.Max(280, Math.Min(610, area.Height - 34)) };
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); card.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Margin = new Thickness(4, 2, 2, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(DeviceIcons.Create(selected!, 20));
        title.Children.Add(new TextBlock { Text = selected!.Name, FontWeight = FontWeights.SemiBold, FontSize = 14,
            Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 });
        header.Children.Add(title);
        var close = new Button { Content = "×", ToolTip = "Comprimi", Width = 32, Height = 30, Padding = new Thickness(0), Margin = new Thickness(0), FontSize = 18 };
        close.Click += (_, _) => { Collapse(); returnFocus(); }; Grid.SetColumn(close, 1); header.Children.Add(close); card.Children.Add(header);
        var scroll = new ScrollViewer { Content = createModule(selected), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(2, 0, 5, 3) };
        Grid.SetRow(scroll, 1); card.Children.Add(scroll);
        var surface = new Border { Background = Brush("#F20E1826"), BorderBrush = Brush("#52677F"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(13), Margin = new Thickness(8), Child = card };

        if (settings.OverlayOrientation == "Vertical")
        {
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            var right = dockAnchor.X > area.Left + area.Width / 2;
            Grid.SetColumn(rail, right ? 1 : 0); Grid.SetColumn(surface, right ? 0 : 1); grid.Children.Add(surface); grid.Children.Add(rail);
            grid.MinWidth = compactSize.Width + 406; grid.MaxHeight = Math.Min(620, area.Height - 20);
            ApplyExpandedLayout(grid, area, compactSize, right);
        }
        else
        {
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
            var bottom = dockAnchor.Y > area.Top + area.Height / 2;
            Grid.SetRow(rail, bottom ? 1 : 0); Grid.SetRow(surface, bottom ? 0 : 1); grid.Children.Add(surface); grid.Children.Add(rail);
            grid.MinWidth = Math.Max(compactSize.Width, 406); grid.MaxHeight = Math.Min(620, area.Height - 20);
            ApplyExpandedLayout(grid, area, compactSize, bottom);
        }
    }

    private void ApplyExpandedLayout(Grid content, Rect area, Size compact, bool opensBefore)
    {
        content.Measure(new Size(Math.Max(1, area.Width), Math.Max(1, area.Height - 20)));
        var width = Math.Ceiling(Math.Min(area.Width, Math.Max(1, content.DesiredSize.Width)));
        var height = Math.Ceiling(Math.Min(area.Height, Math.Max(1, content.DesiredSize.Height)));
        double left, top;
        if (settings.OverlayOrientation == "Vertical")
        {
            left = opensBefore ? dockAnchor.X + compact.Width - width : dockAnchor.X;
            top = Math.Clamp(dockAnchor.Y, area.Top, Math.Max(area.Top, area.Bottom - height));
        }
        else
        {
            left = Math.Clamp(dockAnchor.X, area.Left, Math.Max(area.Left, area.Right - width));
            top = opensBefore ? dockAnchor.Y + compact.Height - height : dockAnchor.Y;
        }
        left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - width));
        top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - height));

        // All final bounds are known before the visual tree is swapped. This prevents the one-frame
        // resize to the outside edge that SizeToContent caused on right/bottom docks.
        using (Dispatcher.DisableProcessing())
        {
            SizeToContent = SizeToContent.Manual;
            Left = left; Top = top; Width = width; Height = height; Content = content;
        }
    }

    private Border CreateRail(IReadOnlyList<Device> devices)
    {
        var horizontal = settings.OverlayOrientation == "Horizontal";
        var stack = new StackPanel { Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical };
        foreach (var device in devices)
        {
            var button = IconButton(DeviceIcons.Create(device, settings.OverlayIconSize * .48), device.Name, selected?.Id == device.Id);
            button.Click += (_, _) => Open(device); stack.Children.Add(button);
        }
        if (devices.Count == 0)
        {
            var empty = IconButton(DeviceIcons.CreateHub(22), "Apri Hub per scegliere i dispositivi", false);
            empty.Click += (_, _) => openSettings(); stack.Children.Add(empty);
        }
        var settingsButton = IconButton(DeviceIcons.CreateHub(settings.OverlayIconSize * .46), "Impostazioni overlay", false);
        settingsButton.Click += (_, _) => openSettings(); stack.Children.Add(settingsButton);
        var border = new Border { Background = Brush("#F20C1624"), BorderBrush = Brush("#52677F"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(5), Child = stack, ToolTip = "Trascina lo spazio intorno alle icone per spostare",
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Apri Hub e impostazioni overlay" }; open.Click += (_, _) => openSettings(); menu.Items.Add(open);
        var orientation = new MenuItem { Header = horizontal ? "Disponi in verticale" : "Disponi in orizzontale" };
        orientation.Click += (_, _) => { settings.OverlayOrientation = horizontal ? "Vertical" : "Horizontal"; settings.Save(); RefreshAppearance(); };
        menu.Items.Add(orientation); menu.Items.Add(new Separator());
        var hide = new MenuItem { Header = "Nascondi overlay" }; hide.Click += (_, _) => Close(); menu.Items.Add(hide); border.ContextMenu = menu;
        AddDrag(border); return border;
    }

    private Button IconButton(object content, string tooltip, bool active)
    {
        var size = settings.OverlayIconSize;
        if (content is FrameworkElement icon) icon.CacheMode = new BitmapCache { RenderAtScale = 2 };
        var button = new Button { Width = size, Height = size, Padding = new Thickness(0), Margin = new Thickness(3), Content = content,
            ToolTip = tooltip, Background = active ? Brush("#315D60") : Brushes.Transparent };
        button.SetResourceReference(StyleProperty, "OverlayIconButton");
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip); return button;
    }

    private Size CompactSize()
    {
        var count = Math.Max(1, getDevices().Count) + 1; var extent = count * (settings.OverlayIconSize + 6) + 12;
        return settings.OverlayOrientation == "Horizontal" ? new Size(extent, settings.OverlayIconSize + 12) : new Size(settings.OverlayIconSize + 12, extent);
    }

    private Rect WorkArea()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return SystemParameters.WorkArea;
        var area = Native.WorkArea(hwnd); var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(transform.Transform(new Point(area.Left, area.Top)), transform.Transform(new Point(area.Right, area.Bottom)));
    }

    private void AddDrag(UIElement target)
    {
        target.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (FindParent<Button>(e.OriginalSource as DependencyObject) != null) return;
            target.CaptureMouse(); dragging = false; pointerStart = PointToScreen(e.GetPosition(this)); windowStart = new Point(Left, Top); dragAnchor = BadgePosition;
        };
        target.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || !target.IsMouseCaptured) return;
            var point = PointToScreen(e.GetPosition(this)); var delta = point - pointerStart;
            if (!dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) < 5) return;
            dragging = true; var dpi = VisualTreeHelper.GetDpi(this);
            Left = windowStart.X + delta.X / dpi.DpiScaleX; Top = windowStart.Y + delta.Y / dpi.DpiScaleY;
            if (Expanded) dockAnchor = new Point(dragAnchor.X + delta.X / dpi.DpiScaleX, dragAnchor.Y + delta.Y / dpi.DpiScaleY);
        };
        target.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!target.IsMouseCaptured) return;
            target.ReleaseMouseCapture(); if (dragging) { KeepOnScreen(); e.Handled = true; } dragging = false;
        };
    }

    private static T? FindParent<T>(DependencyObject? value) where T : DependencyObject
    {
        while (value != null) { if (value is T result) return result; value = VisualTreeHelper.GetParent(value); }
        return null;
    }

    private static Brush Brush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
}
