using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Data;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace HubTool;

public partial class MainWindow : Window
{
    private readonly Settings state = Settings.Load();
    private readonly DeviceService service;
    private readonly Dictionary<int, Shortcut> registered = new();
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer audioDebounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly HashSet<string> pendingAudio = new();
    private readonly MMDeviceEnumerator events = new();
    private readonly Notifications notifications;
    private TrayIcon? tray;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly List<string> shortcutErrors = new();
    private OverlayWindow? overlay;
    private bool exiting;
    private IntPtr handle;
    private IntPtr previousForeground;

    private sealed record ActionOption(string Label, string Action, string DeviceId = "", string Control = "");

    public MainWindow()
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetName().Version;
        AppVersion.Text = version == null ? "" : $"{version.Major}.{version.Minor}.{version.Build}";
        service = new DeviceService(state);
        service.AudioChanged += id => Dispatcher.BeginInvoke(() =>
        {
            if (exiting) return;
            pendingAudio.Add(id);
            if (!audioDebounce.IsEnabled) audioDebounce.Start();
        });
        audioDebounce.Tick += async (_, _) =>
        {
            if (Mouse.Captured != null || operations.CurrentCount == 0) return;
            audioDebounce.Stop();
            await RunAsync(() =>
            {
                foreach (var id in pendingAudio)
                {
                    var device = state.Devices.FirstOrDefault(d => d.Id == id);
                    if (device != null) service.ReadAudio(device);
                }
                pendingAudio.Clear(); state.Save();
                return Task.CompletedTask;
            });
        };
        notifications = new Notifications(() => Dispatcher.BeginInvoke(() =>
        {
            if (exiting) return;
            debounce.Stop(); debounce.Start();
        }));
        debounce.Tick += async (_, _) => { debounce.Stop(); await RunAsync(RefreshCore); };
        Loaded += async (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.DarkCaption(handle);
            HwndSource.FromHwnd(handle).AddHook(Hook);
            if (App.PreviewDirectory == null) tray = new TrayIcon(handle, OpenHub, () => ToggleOverlay(false), () => { exiting = true; Close(); });
            if (App.PreviewDirectory == null) RegisterShortcuts();
            else Shortcuts.ItemsSource = state.Shortcuts;
            events.RegisterEndpointNotificationCallback(notifications);
            await RunAsync(RefreshCore);
            if (state.OverlayEnabled) ToggleOverlay(false);
            await PreviewCapture.TryCaptureAsync(this);
        };
        Closing += (_, e) =>
        {
            if (!exiting) { e.Cancel = true; Hide(); return; }
            SaveOverlayPosition();
            state.Save();
            foreach (var id in registered.Keys) Native.UnregisterHotKey(handle, id);
            debounce.Stop(); audioDebounce.Stop();
            events.UnregisterEndpointNotificationCallback(notifications);
            events.Dispose(); service.Dispose(); tray?.Dispose(); overlay?.Close();
        };
    }

    private void OpenHub() { Show(); WindowState = WindowState.Normal; Activate(); }
    private async void ExitHub(object sender, RoutedEventArgs e) => await RunAsync(() => { exiting = true; Close(); return Task.CompletedTask; });
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (tray?.Handle(message, l) == true) { handled = true; return IntPtr.Zero; }
        if (message == 0x312 && registered.TryGetValue(w.ToInt32(), out var shortcut))
        {
            _ = RunAsync(() => ExecuteAsync(shortcut)); handled = true;
        }
        if (message is 0x219 or 0x7E) { debounce.Stop(); debounce.Start(); }
        if (message == 0x8001) { OpenHub(); handled = true; }
        return IntPtr.Zero;
    }

    private async Task RunAsync(Func<Task> action)
    {
        await operations.WaitAsync();
        try { if (!exiting) await action(); }
        catch (Exception ex) { Status.Text = "Operazione non riuscita: " + ex.Message; }
        finally { operations.Release(); }
    }

    private async Task RefreshCore()
    {
        Status.Text = "Rilevamento dispositivi…";
        await service.RefreshAsync();
        RenderList();
        var selected = Profiles.SelectedItem;
        Profiles.ItemsSource = null; Profiles.ItemsSource = state.Profiles;
        Profiles.SelectedItem = selected ?? state.Profiles.FirstOrDefault();
        PopulateActions();
        Status.Text = $"{state.Devices.Count(d => PeripheralCatalog.IsVisible(d) && d.Connected)} collegati · {state.Devices.Count(d => PeripheralCatalog.IsVisible(d) && !d.Connected)} scollegati";
        if (service.Errors.Count > 0) Status.Text += " · " + string.Join("; ", service.Errors);
        if (shortcutErrors.Count > 0) Status.Text += " · " + string.Join("; ", shortcutErrors);
        if (state.RecoveryNotice.Length > 0) Status.Text += " · " + state.RecoveryNotice;
        RenderOverlay();
    }

    private void RenderList()
    {
        var selected = (DeviceList.SelectedItem as Device)?.Id;
        var q = Search.Text.Trim();
        var category = (CategoryFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var list = state.Devices.Where(PeripheralCatalog.IsVisible)
            .Where(d => (q.Length == 0 || (d.Name + " " + d.Kind).Contains(q, StringComparison.OrdinalIgnoreCase))
                && (category == null || category == "Tutti" || d.Category == category)
                && (OnlyControllable.IsChecked != true || d.Volume.HasValue || d.Controls.Count > 0)
                && (ShowDisconnected.IsChecked == true || d.Connected))
            .OrderByDescending(d => d.Volume.HasValue || d.Controls.Count > 0)
            .ThenByDescending(d => d.Connected).ThenBy(d => d.Name).ToList();
        DeviceList.ItemsSource = list;
        DeviceList.SelectedItem = list.FirstOrDefault(d => d.Id == selected) ?? list.FirstOrDefault();
        DeviceCount.Text = list.Count + " dispositivi";
        ConnectedCount.Text = state.Devices.Count(d => PeripheralCatalog.IsVisible(d) && d.Connected).ToString();
        OverlayCount.Text = state.Devices.Count(d => PeripheralCatalog.IsVisible(d) && d.Overlay).ToString();
        ProfileCount.Text = state.Profiles.Count.ToString();
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) { if (service != null) RenderList(); }
    private void FilterChanged(object sender, RoutedEventArgs e) { if (service != null) RenderList(); }
    private void CategoryChanged(object sender, SelectionChangedEventArgs e) { if (service != null) RenderList(); }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Details.Children.Clear();
        if (DeviceList.SelectedItem is Device device) RenderDevice(Details, device, false);
        else Details.Children.Add(Text("Nessun dispositivo corrisponde alla ricerca.", 20));
    }

    private static TextBlock Text(string value, int size = 14) => new()
    {
        Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 6), Foreground = Brushes.White
    };

    private void RenderDevice(StackPanel panel, Device device, bool compact)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, compact ? 8 : 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 40 : 56) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new Border { Width = compact ? 32 : 44, Height = compact ? 32 : 44, CornerRadius = new CornerRadius(10),
            Background = (Brush)new BrushConverter().ConvertFromString("#223044")!, Child = DeviceIcons.Create(device, compact ? 19 : 25), VerticalAlignment = VerticalAlignment.Top };
        header.Children.Add(icon);
        var titles = new StackPanel(); titles.Children.Add(Text(device.Name, compact ? 14 : 21));
        titles.Children.Add(new TextBlock { Text = device.TypeLabel + " · " + device.Status, FontSize = 11,
            Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(titles, 1); header.Children.Add(titles); panel.Children.Add(header);
        if (device.Error.Length > 0) panel.Children.Add(Text(device.Error, 12));
        if (!compact && device.Id.StartsWith("camera:", StringComparison.Ordinal) && device.Controls.Count > 0)
        {
            var resetCamera = new Button { Content = "Ripristina impostazioni predefinite", HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12) };
            resetCamera.Click += async (_, _) => await RunAsync(async () =>
            {
                var result = await service.ResetCameraAsync(device);
                Status.Text = result.Errors.Count == 0
                    ? $"Videocamera ripristinata · {result.Applied} impostazioni"
                    : $"Ripristinate {result.Applied} impostazioni · " + string.Join("; ", result.Errors);
            });
            panel.Children.Add(resetCamera);
        }
        if (device.Volume.HasValue)
        {
            AddSlider(panel, "Volume", 0, 100, 1, device.Volume.Value * 100, "%", async value =>
            {
                service.SetAudio(device, (float)value / 100, device.Muted);
                state.Save(); await Task.CompletedTask;
            }, device, "");
            var mute = new CheckBox { Content = "Mute", IsChecked = device.Muted };
            mute.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Binding(nameof(Device.Muted)) { Source = device, Mode = BindingMode.OneWay });
            mute.Click += async (_, _) => await RunAsync(() =>
            {
                service.SetAudio(device, device.Volume.Value, mute.IsChecked == true); state.Save();
                return Task.CompletedTask;
            });
            panel.Children.Add(mute);
        }
        if (device.Kind is "Keyboard" or "Mouse")
            panel.Children.Add(Text("Preferenze Windows condivise con le altre " + (device.Kind == "Keyboard" ? "tastiere." : "periferiche mouse."), 11));

        if (device.Id.StartsWith("light:", StringComparison.Ordinal))
        {
            panel.Children.Add(Text(LightControls.IsDxLightRunning()
                ? "DX Light è aperto. Hub ha memorizzato le impostazioni e prenderà il controllo automaticamente appena lo chiudi, riaccendendo le luci."
                : "Hub controlla direttamente il controller USB. Puoi chiudere DX Light: l’ultimo stato resta attivo.", 11));
            RenderLightControls(panel, device, compact);
        }

        var sidetone = device.Controls.Where(c => AudioTopologyControls.IsSidetone(c.Id)).ToList();
        if (sidetone.Count > 0)
        {
            var sidetonePanel = new StackPanel();
            sidetonePanel.Children.Add(Text("Eco microfono nelle cuffie", compact ? 13 : 16));
            if (!compact) sidetonePanel.Children.Add(Text("Ascolto diretto del microfono gestito dal driver delle cuffie.", 11));
            RenderControls(sidetonePanel, device, sidetone);
            panel.Children.Add(new Border { Child = sidetonePanel, Background = (Brush)new BrushConverter().ConvertFromString("#1D2C3E")!,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#35485D")!, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(compact ? 9 : 12), Margin = new Thickness(0, 10, 0, 10) });
        }

        var ordinaryControls = device.Controls.Where(c => !AudioTopologyControls.IsSidetone(c.Id) && !c.Id.StartsWith("light:")).ToList();
        var controlPanel = new StackPanel();
        RenderControls(controlPanel, device, ordinaryControls);
        if (ordinaryControls.Count > 0 && device.Volume.HasValue)
            panel.Children.Add(new Expander { Header = "Canali e livello in dB", Content = controlPanel, Margin = new Thickness(0, 12, 0, 12), Foreground = Brushes.White });
        else if (ordinaryControls.Count > 0) panel.Children.Add(controlPanel);

        if (!compact)
        {
            var pin = new CheckBox { Content = "Mostra nell’overlay", IsChecked = device.Overlay };
            pin.Click += (_, _) => { device.Overlay = pin.IsChecked == true; state.Save(); OverlayCount.Text = state.Devices.Count(d => PeripheralCatalog.IsVisible(d) && d.Overlay).ToString(); RenderOverlay(); };
            panel.Children.Add(pin);
            if (device.Volume.HasValue || device.Controls.Count > 0)
            {
                var restore = new CheckBox { Content = "Ripristina impostazioni alla riconnessione e all’avvio", IsChecked = device.Restore };
                restore.Click += (_, _) => { device.Restore = restore.IsChecked == true; state.Save(); };
                panel.Children.Add(restore);
            }
            if (device.Volume.HasValue)
                panel.Children.Add(Text("dB e canali: controlli del driver audio Windows.", 11));
            if (device.Controls.Count == 0 && !device.Volume.HasValue)
                panel.Children.Add(Text("Impostazioni gestite da Windows o dal software del produttore.", 12));
            var open = new Button { Content = "Impostazioni Windows", HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += async (_, _) => await RunAsync(() => { Launch(SettingsTarget(device)); return Task.CompletedTask; });
            panel.Children.Add(open);
            var metadata = new StackPanel();
            metadata.Children.Add(Text("ID persistente\n" + device.Id, 11));
            if (device.Manufacturer.Length > 0) metadata.Children.Add(Text("Produttore · " + device.Manufacturer, 12));
            if (device.Driver.Length > 0) metadata.Children.Add(Text("Driver · " + device.Driver, 11));
            panel.Children.Add(new Expander { Header = "Dettagli tecnici", Content = metadata, Foreground = Brushes.White, Margin = new Thickness(0, 16, 0, 8) });
        }
    }

    private void RenderLightControls(StackPanel panel, Device device, bool compact)
    {
        var basic = device.Controls.Where(c => c.Id is "light:enabled" or "light:sync" or "light:brightness").ToList();
        RenderControls(panel, device, basic);

        var colorPanel = new StackPanel();
        colorPanel.Children.Add(Text("Colore fisso", compact ? 13 : 16));
        var swatches = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var hex in new[] { "#FF3B30", "#FF9500", "#FFD60A", "#34C759", "#00C7BE", "#0A84FF", "#AF52DE", "#FFFFFF" })
        {
            var button = new Button { Width = 31, Height = 31, Margin = new Thickness(2), Padding = new Thickness(0), Tag = hex,
                Background = (Brush)new BrushConverter().ConvertFromString(hex)!, ToolTip = hex };
            button.Click += async (_, _) => await RunAsync(() => SetLightColor(device, (string)button.Tag));
            swatches.Children.Add(button);
        }
        colorPanel.Children.Add(swatches);
        var hexRow = new DockPanel();
        var apply = new Button { Content = "Applica", Padding = new Thickness(12, 5, 12, 5), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(apply, Dock.Right); hexRow.Children.Add(apply);
        var hexBox = new TextBox { Text = $"#{(int)device.Values.GetValueOrDefault("light:red", 0):X2}{(int)device.Values.GetValueOrDefault("light:green", 0):X2}{(int)device.Values.GetValueOrDefault("light:blue", 0):X2}",
            MaxLength = 7, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        apply.Click += async (_, _) => await RunAsync(() => SetLightColor(device, hexBox.Text));
        hexRow.Children.Add(hexBox); colorPanel.Children.Add(hexRow);
        RenderControls(colorPanel, device, device.Controls.Where(c => c.Id is "light:red" or "light:green" or "light:blue"));
        panel.Children.Add(new Border { Child = colorPanel, Background = (Brush)new BrushConverter().ConvertFromString("#1D2C3E")!,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#35485D")!, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(compact ? 9 : 12), Margin = new Thickness(0, 10, 0, 10) });

        var advanced = new StackPanel();
        RenderControls(advanced, device, device.Controls.Where(c => c.Id is "light:fps" or "light:saturation" or "light:smoothing"));
        panel.Children.Add(new Expander { Header = "Qualità sincronizzazione schermo", Content = advanced, Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 10) });
    }

    private async Task SetLightColor(Device device, string hex)
    {
        hex = hex.Trim();
        if (hex.Length != 7 || hex[0] != '#' || !int.TryParse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new InvalidOperationException("Inserisci un colore nel formato #RRGGBB");
        await service.SetControlAsync(device, "light:red", rgb >> 16 & 255);
        await service.SetControlAsync(device, "light:green", rgb >> 8 & 255);
        await service.SetControlAsync(device, "light:blue", rgb & 255);
        state.Save(); RenderList();
    }

    private void RenderControls(StackPanel panel, Device device, IEnumerable<DeviceControl> controls)
    {
        foreach (var control in controls)
        {
            if (!device.Values.TryGetValue(control.Id, out var value)) continue;
            if (control.Toggle)
            {
                var check = new CheckBox { Content = control.Label, IsChecked = value != 0 };
                check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                    new Binding("Values[" + control.Id + "]") { Source = device, Mode = BindingMode.OneWay, Converter = new ToggleConverter() });
                check.Click += async (_, _) => await RunAsync(async () =>
                {
                    await service.SetControlAsync(device, control.Id, check.IsChecked == true ? 1 : 0); state.Save();
                });
                panel.Children.Add(check);
            }
            else AddSlider(panel, control.Label, control.Min, control.Max, control.Step, value, control.Unit,
                async newValue => { await service.SetControlAsync(device, control.Id, newValue); state.Save(); }, device, control.Id);
        }
    }

    private void AddSlider(StackPanel panel, string label, double min, double max, double step, double value, string unit, Func<double, Task> apply, Device? device = null, string key = "")
    {
        var heading = Text($"{label}  ·  {value:0.##} {unit}");
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, SmallChange = step,
            LargeChange = Math.Max(step, (max - min) / 10), IsSnapToTickEnabled = true, Value = Math.Clamp(value, min, max),
            Margin = new Thickness(2, 8, 2, 16), ToolTip = label };
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        double committed = slider.Value;
        bool edited = false;
        slider.PreviewMouseLeftButtonDown += (_, _) => edited = true;
        slider.PreviewKeyDown += (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) edited = true; };
        slider.ValueChanged += (_, _) => heading.Text = $"{label}  ·  {slider.Value:0.##} {unit}";
        if (device != null) slider.SetBinding(Slider.ValueProperty, new Binding(key.Length == 0 ? nameof(Device.Volume) : "Values[" + key + "]")
        { Source = device, Mode = BindingMode.OneWay, Converter = key.Length == 0 ? new PercentConverter() : null });
        async Task Commit()
        {
            if (!edited) return;
            edited = false;
            if (Math.Abs(committed - slider.Value) < .0001) return;
            await RunAsync(async () => { await apply(slider.Value); committed = slider.Value; });
        }
        slider.PreviewMouseLeftButtonUp += async (_, _) => await Commit();
        slider.PreviewKeyUp += async (_, _) => await Commit();
        slider.LostKeyboardFocus += async (_, _) => await Commit();
        panel.Children.Add(heading); panel.Children.Add(slider);
    }

    private void RegisterShortcuts()
    {
        foreach (var id in registered.Keys) Native.UnregisterHotKey(handle, id);
        registered.Clear(); shortcutErrors.Clear();
        int idCounter = 1;
        foreach (var shortcut in state.Shortcuts)
        {
            try { RegisterShortcut(idCounter, shortcut); registered[idCounter++] = shortcut; }
            catch (Exception ex) { shortcutErrors.Add(ex.Message); }
        }
        Shortcuts.ItemsSource = null; Shortcuts.ItemsSource = state.Shortcuts;
    }

    private void RegisterShortcut(int id, Shortcut shortcut)
    {
        var gesture = (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(shortcut.Gesture)!;
        uint mods = 0x4000;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) mods |= 2;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= 1;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= 4;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= 8;
        if (!Native.RegisterHotKey(handle, id, mods, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key)))
            throw new InvalidOperationException("Shortcut non disponibile: " + shortcut.Gesture);
    }

    private void PopulateActions()
    {
        var selected = ActionChoice.SelectedItem as ActionOption;
        var choices = new List<ActionOption>
        {
            new("Mostra e focalizza overlay", "overlay"), new("Mute di tutti i microfoni", "mute-input"),
            new("Mute di tutte le uscite", "mute-output"), new("Alza volume uscite del 5%", "volume-up"),
            new("Abbassa volume uscite del 5%", "volume-down"), new("Apri un programma o file", "launch")
        };
        choices.AddRange(state.Profiles.Select(p => new ActionOption("Profilo · " + p.Name, "profile:" + p.Name)));
        foreach (var device in state.Devices.Where(PeripheralCatalog.IsVisible))
        {
            if (device.Volume.HasValue)
            {
                choices.Add(new(device.Name + " · volume %", "set-volume", device.Id));
                choices.Add(new(device.Name + " · mute", "toggle-device", device.Id));
            }
            choices.AddRange(device.Controls.Select(c => new ActionOption(device.Name + " · " + c.Label, "set-control", device.Id, c.Id)));
        }
        ActionChoice.ItemsSource = choices;
        ActionChoice.SelectedItem = choices.FirstOrDefault(c => c == selected) ?? choices.First();
    }

    private async Task ExecuteAsync(Shortcut shortcut)
    {
        var action = shortcut.Action;
        if (action == "overlay") { ToggleOverlay(); return; }
        if (action == "launch") { Process.Start(new ProcessStartInfo(shortcut.Target) { Arguments = shortcut.Arguments, UseShellExecute = true }); return; }
        if (action.StartsWith("profile:"))
        {
            var profile = state.Profiles.FirstOrDefault(p => p.Name == action[8..]) ?? throw new InvalidOperationException("Profilo non trovato");
            var failures = await service.ApplyAsync(profile);
            Status.Text = failures.Count == 0 ? "Profilo applicato: " + profile.Name : string.Join("; ", failures);
        }
        else if (action is "set-volume" or "toggle-device" or "set-control")
        {
            var device = state.Devices.FirstOrDefault(d => d.Id == shortcut.DeviceId) ?? throw new InvalidOperationException("Dispositivo non trovato");
            if (action == "set-control") await service.SetControlAsync(device, shortcut.Control, shortcut.Value);
            else
            {
                service.ReadAudio(device);
                service.SetAudio(device, action == "set-volume" ? (float)shortcut.Value / 100 : device.Volume!.Value,
                    action == "toggle-device" ? !device.Muted : device.Muted);
            }
        }
        else
        {
            var devices = state.Devices.Where(d => d.Connected && d.Volume.HasValue &&
                (action == "mute-input" ? d.Kind == "Microfono" : d.Kind == "Uscita audio")).ToList();
            foreach (var device in devices) service.ReadAudio(device);
            bool mute = !devices.All(d => d.Muted);
            foreach (var device in devices)
                service.SetAudio(device, device.Volume!.Value + (action == "volume-up" ? .05f : action == "volume-down" ? -.05f : 0),
                    action is "mute-input" or "mute-output" ? mute : device.Muted);
        }
        state.Save(); RenderList(); RenderOverlay();
    }

    private void ToggleOverlay(bool focus = true)
    {
        var foreground = Native.GetForegroundWindow();
        if (focus && (overlay == null || foreground != new WindowInteropHelper(overlay).Handle)) previousForeground = foreground;
        if (overlay == null)
        {
            overlay = new OverlayWindow(CreateOverlayModules, SaveOverlayPosition, ReturnFocus)
            { Left = state.OverlayLeft ?? SystemParameters.WorkArea.Right - 74, Top = state.OverlayTop ?? SystemParameters.WorkArea.Bottom - 90 };
            overlay.Closing += (_, _) => { SaveOverlayPosition(); if (!exiting) state.OverlayEnabled = false; state.Save(); };
            overlay.Closed += (_, _) => { overlay = null; OverlayToggle.Content = "Overlay"; };
        }
        state.OverlayEnabled = true;
        OverlayToggle.Content = "Disattiva overlay";
        if (!overlay.IsVisible) overlay.Show();
        overlay.KeepOnScreen();
        if (focus) { overlay.SetExpanded(true); overlay.Activate(); overlay.Focus(); }
        state.Save();
    }

    private void ReturnFocus()
    {
        if (previousForeground != IntPtr.Zero && Native.IsWindow(previousForeground)) Native.SetForegroundWindow(previousForeground);
        previousForeground = IntPtr.Zero;
    }

    private void SaveOverlayPosition() { if (overlay != null) { state.OverlayLeft = overlay.BadgePosition.X; state.OverlayTop = overlay.BadgePosition.Y; state.Save(); } }
    private void RenderOverlay() { overlay?.RefreshModules(); if (overlay?.IsVisible == true) overlay.KeepOnScreen(); }
    internal OverlayWindow CreateDiagnosticOverlay()
    {
        if (App.PreviewDirectory == null) throw new InvalidOperationException("Richiede preferenze isolate");
        foreach (var device in state.Devices.Where(PeripheralCatalog.IsVisible).Where(d => d.Volume.HasValue || d.Kind == "Monitor")
            .OrderByDescending(d => d.Volume.HasValue).GroupBy(d => d.Category).Take(3).Select(g => g.First())) device.Overlay = true;
        return new OverlayWindow(CreateOverlayModules, () => { }, () => { })
        { Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 100, Top = SystemParameters.VirtualScreenTop + 100 };
    }
    private StackPanel CreateOverlayModules()
    {
        var body = new StackPanel();
        var devices = state.Devices.Where(d => PeripheralCatalog.IsVisible(d) && d.Overlay).ToList();
        if (devices.Count == 0)
        {
            body.Children.Add(Text("Nessun dispositivo selezionato.", 12));
            var open = new Button { Content = "Scegli dispositivi" }; open.Click += (_, _) => OpenHub(); body.Children.Add(open);
        }
        foreach (var device in devices)
        {
            var module = new StackPanel(); RenderDevice(module, device, true);
            body.Children.Add(new Border { Background = (Brush)FindResource("Panel"), BorderBrush = (Brush)new BrushConverter().ConvertFromString("#293B50")!,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Child = module });
        }
        if (state.Profiles.Count > 0)
        {
            var profiles = new ComboBox { ItemsSource = state.Profiles, SelectedIndex = 0 };
            var apply = new Button { Content = "Applica profilo" };
            apply.Click += async (_, _) => { if (profiles.SelectedItem is Profile p) await RunAsync(() => ExecuteAsync(new Shortcut { Action = "profile:" + p.Name })); };
            body.Children.Add(profiles); body.Children.Add(apply);
        }
        return body;
    }

    private void OverlayClick(object sender, RoutedEventArgs e) { if (overlay != null) overlay.Close(); else ToggleOverlay(false); }
    private async void SaveProfile(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var name = ProfileName.Text.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Inserisci un nome");
        if (state.Profiles.Any(p => p.Name == name)) throw new InvalidOperationException("Nome già presente: scegli un nuovo nome");
        await service.RefreshAsync(); state.Profiles.Add(service.Capture(name)); state.Save();
        Profiles.ItemsSource = null; Profiles.ItemsSource = state.Profiles; Profiles.SelectedIndex = state.Profiles.Count - 1;
        PopulateActions(); Status.Text = "Profilo salvato: " + name;
    });
    private async void ApplyProfile(object sender, RoutedEventArgs e)
    {
        if (Profiles.SelectedItem is Profile profile) await RunAsync(() => ExecuteAsync(new Shortcut { Action = "profile:" + profile.Name }));
    }
    private async void ExportProfile(object sender, RoutedEventArgs e)
    {
        if (Profiles.SelectedItem is not Profile profile) { Status.Text = "Seleziona un profilo da esportare"; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Esporta profilo Hub", Filter = "Profilo Hub (*.hubprofile)|*.hubprofile", FileName = "profilo.hubprofile" };
        if (dialog.ShowDialog(this) == true) await RunAsync(() =>
        {
            ProfileTransfer.Export(state, profile, dialog.FileName); Status.Text = "Profilo esportato"; return Task.CompletedTask;
        });
    }
    private void ProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileSummary == null) return;
        ProfileCount.Text = state.Profiles.Count.ToString();
        ProfileSummary.Children.Clear();
        if (Profiles.SelectedItem is not Profile profile) { ProfileSummary.Children.Add(Text("Nessun profilo selezionato.", 12)); return; }
        foreach (var id in profile.Audio.Keys.Union(profile.Controls.Keys))
        {
            var device = state.Devices.FirstOrDefault(d => d.Id == id);
            if (device?.Id.StartsWith("windows:") == true) continue;
            var row = new StackPanel();
            row.Children.Add(Text(device?.Name ?? id, 14));
            if (profile.Audio.TryGetValue(id, out var audio)) row.Children.Add(Text($"Volume {audio.Volume * 100:0}% · {(audio.Muted ? "Mute" : "Audio attivo")}", 12));
            if (profile.Controls.TryGetValue(id, out var values))
                foreach (var (key, value) in values)
                    row.Children.Add(Text((device?.Controls.FirstOrDefault(c => c.Id == key)?.Label ?? key) + $" · {value:0.##}", 11));
            ProfileSummary.Children.Add(new Border { Child = row, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Background = (Brush)new BrushConverter().ConvertFromString("#1D2C3E")! });
        }
    }
    private async void ImportProfile(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Importa profilo Hub", Filter = "Profilo Hub (*.hubprofile)|*.hubprofile" };
        if (dialog.ShowDialog(this) == true) await RunAsync(() =>
        {
            var profile = ProfileTransfer.Import(state, dialog.FileName); state.Save();
            Profiles.ItemsSource = null; Profiles.ItemsSource = state.Profiles; Profiles.SelectedItem = profile;
            PopulateActions(); RenderList(); Status.Text = "Importato: " + profile.Name + ". Premi Applica profilo per usarlo.";
            return Task.CompletedTask;
        });
    }
    private void DeleteProfile(object sender, RoutedEventArgs e)
    {
        if (Profiles.SelectedItem is not Profile profile) return;
        state.Profiles.Remove(profile); state.Shortcuts.RemoveAll(s => s.Action == "profile:" + profile.Name);
        state.Save(); RegisterShortcuts(); PopulateActions(); Profiles.ItemsSource = null; Profiles.ItemsSource = state.Profiles;
    }
    private async void AddShortcut(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (ActionChoice.SelectedItem is not ActionOption option) return Task.CompletedTask;
        double value = 0;
        if (option.Action is "set-volume" or "set-control")
        {
            if (!double.TryParse(ActionValue.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) || !double.IsFinite(value))
                throw new InvalidOperationException("Inserisci un valore numerico valido");
            if (option.Action == "set-volume" && (value < 0 || value > 100)) throw new InvalidOperationException("Volume tra 0 e 100");
            if (option.Action == "set-control")
            {
                var control = state.Devices.Single(d => d.Id == option.DeviceId).Controls.Single(c => c.Id == option.Control);
                if (value < control.Min || value > control.Max) throw new InvalidOperationException($"Valore tra {control.Min} e {control.Max}");
            }
        }
        if (option.Action == "launch" && !File.Exists(LaunchTarget.Text)) throw new InvalidOperationException("Scegli un file o programma esistente");
        var shortcut = new Shortcut { Gesture = Gesture.Text, Action = option.Action, DeviceId = option.DeviceId,
            Control = option.Control, Value = value, Target = LaunchTarget.Text, Arguments = LaunchArguments.Text, Label = option.Label };
        int id = registered.Keys.DefaultIfEmpty(0).Max() + 1;
        RegisterShortcut(id, shortcut); registered[id] = shortcut; state.Shortcuts.Add(shortcut); state.Save();
        Shortcuts.ItemsSource = null; Shortcuts.ItemsSource = state.Shortcuts; Status.Text = "Shortcut registrata: " + shortcut.Gesture;
        return Task.CompletedTask;
    });
    private void RemoveShortcut(object sender, RoutedEventArgs e)
    {
        if (Shortcuts.SelectedItem is Shortcut shortcut) { state.Shortcuts.Remove(shortcut); RegisterShortcuts(); state.Save(); }
    }
    private void BrowseProgram(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Scegli cosa aprire con la shortcut" };
        if (dialog.ShowDialog(this) == true) LaunchTarget.Text = dialog.FileName;
    }
    private void ActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LaunchPanel == null || ValuePanel == null) return;
        var action = (ActionChoice.SelectedItem as ActionOption)?.Action;
        LaunchPanel.Visibility = action == "launch" ? Visibility.Visible : Visibility.Collapsed;
        ValuePanel.Visibility = action is "set-volume" or "set-control" ? Visibility.Visible : Visibility.Collapsed;
    }
    private static string SettingsTarget(Device device) => device.Kind.ToLowerInvariant() switch
    {
        "keyboard" => "ms-settings:easeofaccess-keyboard", "mouse" => "ms-settings:mousetouchpad",
        "monitor" or "display" => "ms-settings:display", "bluetooth" => "ms-settings:bluetooth",
        "printer" or "printqueue" => "ms-settings:printers", "camera" or "image" => "ms-settings:camera",
        "net" => "ms-settings:network-status", "microfono" or "uscita audio" or "media" or "audioendpoint" => "mmsys.cpl",
        _ => "ms-settings:connecteddevices"
    };
    private static void Launch(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    private async void OpenSettings(object sender, RoutedEventArgs e) => await RunAsync(() => { Launch((string)((Button)sender).Tag); return Task.CompletedTask; });
    private async void OpenData(object sender, RoutedEventArgs e) => await RunAsync(() => { Launch(Settings.Folder); return Task.CompletedTask; });
    private async void RefreshClick(object sender, RoutedEventArgs e) => await RunAsync(RefreshCore);

    private sealed class Notifications(Action changed) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string id, DeviceState state) => changed();
        public void OnDeviceAdded(string id) => changed();
        public void OnDeviceRemoved(string id) => changed();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id) => changed();
        public void OnPropertyValueChanged(string id, PropertyKey key) => changed();
    }
}
