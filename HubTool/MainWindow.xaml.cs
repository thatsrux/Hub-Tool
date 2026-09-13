using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
    private readonly System.Windows.Forms.NotifyIcon tray = new();
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly List<string> shortcutErrors = new();
    private Window? overlay;
    private StackPanel? overlayBody;
    private bool expanded = true;
    private bool exiting;
    private IntPtr handle;

    private sealed record ActionOption(string Label, string Action, string DeviceId = "", string Control = "");

    public MainWindow()
    {
        InitializeComponent();
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
                if (IsVisible) SelectionChanged(this, new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
                if (overlay?.IsVisible == true) RenderOverlay();
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
            HwndSource.FromHwnd(handle).AddHook(Hook);
            if (App.PreviewDirectory == null) RegisterShortcuts();
            else Shortcuts.ItemsSource = state.Shortcuts;
            events.RegisterEndpointNotificationCallback(notifications);
            await RunAsync(RefreshCore);
            if (state.OverlayEnabled) ToggleOverlay();
            await PreviewCapture.TryCaptureAsync(this);
        };
        tray.Icon = Environment.ProcessPath is string exe ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : System.Drawing.SystemIcons.Application;
        tray.Text = "Hub Tool";
        tray.Visible = App.PreviewDirectory == null;
        tray.DoubleClick += (_, _) => OpenHub();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Apri Hub Tool", null, (_, _) => OpenHub());
        menu.Items.Add("Overlay", null, (_, _) => ToggleOverlay());
        menu.Items.Add("Esci", null, (_, _) => { exiting = true; Close(); });
        tray.ContextMenuStrip = menu;
        Closing += (_, e) =>
        {
            if (!exiting) { e.Cancel = true; Hide(); return; }
            SaveOverlayPosition();
            state.Save();
            foreach (var id in registered.Keys) Native.UnregisterHotKey(handle, id);
            debounce.Stop(); audioDebounce.Stop();
            events.UnregisterEndpointNotificationCallback(notifications);
            events.Dispose(); service.Dispose(); tray.Dispose(); overlay?.Close();
        };
    }

    private void OpenHub() { Show(); WindowState = WindowState.Normal; Activate(); }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
    {
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
        Status.Text = $"{state.Devices.Count(d => d.Connected)} collegati · {state.Devices.Count(d => !d.Connected)} in memoria · chiudi per lasciare Hub nell’area notifiche";
        if (service.Errors.Count > 0) Status.Text += " · " + string.Join("; ", service.Errors);
        if (shortcutErrors.Count > 0) Status.Text += " · " + string.Join("; ", shortcutErrors);
        if (state.RecoveryNotice.Length > 0) Status.Text += " · " + state.RecoveryNotice;
        RenderOverlay();
    }

    private void RenderList()
    {
        var selected = (DeviceList.SelectedItem as Device)?.Id;
        var q = Search.Text.Trim();
        var list = state.Devices
            .Where(d => (q.Length == 0 || (d.Name + " " + d.Kind).Contains(q, StringComparison.OrdinalIgnoreCase))
                && (OnlyControllable.IsChecked != true || d.Volume.HasValue || d.Controls.Count > 0)
                && (ShowDisconnected.IsChecked == true || d.Connected))
            .OrderByDescending(d => d.Volume.HasValue || d.Controls.Count > 0)
            .ThenByDescending(d => d.Connected).ThenBy(d => d.Name).ToList();
        DeviceList.ItemsSource = list;
        DeviceList.SelectedItem = list.FirstOrDefault(d => d.Id == selected) ?? list.FirstOrDefault();
        DeviceCount.Text = list.Count + " dispositivi";
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) { if (service != null) RenderList(); }
    private void FilterChanged(object sender, RoutedEventArgs e) { if (service != null) RenderList(); }
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
        panel.Children.Add(Text(device.Name, compact ? 16 : 25));
        panel.Children.Add(Text(device.Kind + " · " + device.Status, 12));
        if (device.Error.Length > 0) panel.Children.Add(Text(device.Error, 12));
        if (device.Volume.HasValue)
        {
            AddSlider(panel, "Volume", 0, 100, 1, device.Volume.Value * 100, "%", async value =>
            {
                service.SetAudio(device, (float)value / 100, device.Muted);
                state.Save(); await Task.CompletedTask;
            });
            var mute = new CheckBox { Content = "Mute", IsChecked = device.Muted };
            mute.Click += async (_, _) => await RunAsync(() =>
            {
                service.SetAudio(device, device.Volume.Value, mute.IsChecked == true); state.Save();
                return Task.CompletedTask;
            });
            panel.Children.Add(mute);
        }
        if (device.Id.StartsWith("windows:"))
            panel.Children.Add(Text("Preferenze condivise da tutti i dispositivi di questa categoria in Windows.", 12));

        var controlPanel = new StackPanel();
        foreach (var control in device.Controls)
        {
            if (!device.Values.TryGetValue(control.Id, out var value)) continue;
            if (control.Toggle)
            {
                var check = new CheckBox { Content = control.Label, IsChecked = value != 0 };
                check.Click += async (_, _) => await RunAsync(async () =>
                {
                    await service.SetControlAsync(device, control.Id, check.IsChecked == true ? 1 : 0); state.Save();
                });
                controlPanel.Children.Add(check);
            }
            else AddSlider(controlPanel, control.Label, control.Min, control.Max, control.Step, value, control.Unit,
                async newValue => { await service.SetControlAsync(device, control.Id, newValue); state.Save(); });
        }
        if (device.Volume.HasValue)
            panel.Children.Add(new Expander { Header = "Canali e livello in dB", Content = controlPanel, Margin = new Thickness(0, 12, 0, 12), Foreground = Brushes.White });
        else panel.Children.Add(controlPanel);

        if (!compact)
        {
            var pin = new CheckBox { Content = "Mostra nell’overlay", IsChecked = device.Overlay };
            pin.Click += (_, _) => { device.Overlay = pin.IsChecked == true; state.Save(); RenderOverlay(); };
            panel.Children.Add(pin);
            if (device.Volume.HasValue || device.Controls.Count > 0)
            {
                var restore = new CheckBox { Content = "Ripristina impostazioni alla riconnessione e all’avvio", IsChecked = device.Restore };
                restore.Click += (_, _) => { device.Restore = restore.IsChecked == true; state.Save(); };
                panel.Children.Add(restore);
            }
            if (device.Volume.HasValue)
                panel.Children.Add(Text("I dB regolano il livello esposto dal driver Windows. Il gain analogico, il boost e gli effetti proprietari possono avere controlli separati nel software del produttore.", 12));
            if (device.Controls.Count == 0 && !device.Volume.HasValue)
                panel.Children.Add(Text("Windows rileva questo dispositivo. Nessun controllo diretto è ancora integrato per questo driver; puoi aprire il pannello della categoria.", 14));
            var open = new Button { Content = "Impostazioni di " + device.Kind, HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += async (_, _) => await RunAsync(() => { Launch(SettingsTarget(device)); return Task.CompletedTask; });
            panel.Children.Add(open);
            var metadata = new StackPanel();
            metadata.Children.Add(Text("ID persistente\n" + device.Id, 11));
            if (device.Manufacturer.Length > 0) metadata.Children.Add(Text("Produttore · " + device.Manufacturer, 12));
            if (device.Driver.Length > 0) metadata.Children.Add(Text("Driver · " + device.Driver, 11));
            panel.Children.Add(new Expander { Header = "Dettagli tecnici", Content = metadata, Foreground = Brushes.White, Margin = new Thickness(0, 16, 0, 8) });
        }
    }

    private void AddSlider(StackPanel panel, string label, double min, double max, double step, double value, string unit, Func<double, Task> apply)
    {
        var heading = Text($"{label}  ·  {value:0.##} {unit}");
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, SmallChange = step,
            LargeChange = Math.Max(step, (max - min) / 10), IsSnapToTickEnabled = true, Value = Math.Clamp(value, min, max),
            Margin = new Thickness(2, 8, 2, 16), ToolTip = label };
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        double committed = slider.Value;
        slider.ValueChanged += (_, _) => heading.Text = $"{label}  ·  {slider.Value:0.##} {unit}";
        async Task Commit()
        {
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
        foreach (var device in state.Devices)
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

    private void ToggleOverlay()
    {
        if (overlay == null)
        {
            overlayBody = new StackPanel { Margin = new Thickness(14) };
            overlay = new Window { Title = "Hub overlay", Width = 340, Height = 440, MinWidth = 270, Topmost = true,
                ShowInTaskbar = false, ResizeMode = ResizeMode.CanResizeWithGrip, Content = new ScrollViewer { Content = overlayBody },
                Left = Math.Clamp(state.OverlayLeft ?? SystemParameters.WorkArea.Right - 360, SystemParameters.VirtualScreenLeft,
                    Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 340)),
                Top = Math.Clamp(state.OverlayTop ?? SystemParameters.WorkArea.Top + 30, SystemParameters.VirtualScreenTop,
                    Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)) };
            overlay.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { overlay.Hide(); e.Handled = true; } };
            overlay.Closing += (_, _) => { SaveOverlayPosition(); state.OverlayEnabled = false; state.Save(); };
            overlay.Closed += (_, _) => { overlay = null; overlayBody = null; };
            RenderOverlay();
        }
        state.OverlayEnabled = true;
        if (!overlay.IsVisible) overlay.Show();
        overlay.Activate(); overlay.Focus(); state.Save();
    }

    private void SaveOverlayPosition() { if (overlay != null) { state.OverlayLeft = overlay.Left; state.OverlayTop = overlay.Top; } }
    private void RenderOverlay()
    {
        if (overlayBody == null) return;
        overlayBody.Children.Clear();
        var toggle = new Button { Content = expanded ? "HUB   −   Comprimi" : "HUB   +   Espandi" };
        toggle.Click += (_, _) => { expanded = !expanded; overlay!.Height = expanded ? 440 : 115; RenderOverlay(); };
        overlayBody.Children.Add(toggle);
        if (!expanded) return;
        var devices = state.Devices.Where(d => d.Overlay).ToList();
        if (devices.Count == 0) overlayBody.Children.Add(Text("Seleziona «Mostra nell’overlay» nei dispositivi da tenere a portata di mano."));
        foreach (var device in devices) RenderDevice(overlayBody, device, true);
        if (state.Profiles.Count > 0)
        {
            var profiles = new ComboBox { ItemsSource = state.Profiles, SelectedIndex = 0 };
            var apply = new Button { Content = "Applica profilo" };
            apply.Click += async (_, _) => { if (profiles.SelectedItem is Profile p) await RunAsync(() => ExecuteAsync(new Shortcut { Action = "profile:" + p.Name })); };
            overlayBody.Children.Add(profiles); overlayBody.Children.Add(apply);
        }
    }

    private void OverlayClick(object sender, RoutedEventArgs e) => ToggleOverlay();
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
