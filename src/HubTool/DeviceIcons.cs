using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HubTool;

public static class DeviceIcons
{
    private static readonly Brush Teal = FrozenTeal();
    private static Brush FrozenTeal() { var brush = new SolidColorBrush(Color.FromRgb(104,227,198)); brush.Freeze(); return brush; }
    private static readonly Dictionary<string, Geometry> Shapes = new()
    {
        ["headphones"] = Shape("M4,14 V11 A8,8 0 0 1 20,11 V14 M4,12 H7 V21 H4 Q2,21 2,19 V14 Q2,12 4,12 M20,12 H17 V21 H20 Q22,21 22,19 V14 Q22,12 20,12"),
        ["microphone"] = Shape("M8,5 A4,4 0 0 1 16,5 V12 A4,4 0 0 1 8,12 Z M5,11 V12 A7,7 0 0 0 19,12 V11 M12,19 V23 M8,23 H16"),
        ["speaker"] = Shape("M3,9 H7 L12,5 V21 L7,17 H3 Z M16,9 Q21,13 16,17 M19,5 Q27,13 19,21"),
        ["keyboard"] = Shape("M3,5 H21 Q23,5 23,7 V19 Q23,21 21,21 H3 Q1,21 1,19 V7 Q1,5 3,5 Z M5,10 H6 M10,10 H11 M15,10 H16 M20,10 H20.5 M5,14 H6 M10,14 H11 M15,14 H16 M20,14 H20.5 M7,18 H17"),
        ["mouse"] = Shape("M5,9 A7,7 0 0 1 19,9 V17 A7,7 0 0 1 5,17 Z M12,3 V10 M11,7 H13"),
        ["monitor"] = Shape("M2,3 H22 V18 H2 Z M12,18 V23 M7,23 H17"),
        ["camera"] = Shape("M3,6 H16 Q18,6 18,8 V18 Q18,20 16,20 H3 Q1,20 1,18 V8 Q1,6 3,6 Z M18,11 L24,7 V19 L18,15 Z M6,10 H8"),
        ["usb"] = Shape("M3,10 H21 V20 H3 Z M7,14 V17 M12,14 V17 M17,14 V17 M12,10 V3 M9,3 H15"),
        ["printer"] = Shape("M6,9 V2 H18 V9 M6,19 H2 V9 H22 V19 H18 M6,15 H18 V24 H6 Z M17,12 H19"),
        ["scanner"] = Shape("M2,14 H22 V22 H2 Z M3,14 L7,3 L22,10 M6,18 H15 M18,18 H19"),
        ["drive"] = Shape("M5,3 H19 L23,18 V23 H1 V18 Z M1,18 H23 M17,21 H19"),
        ["gamepad"] = Shape("M7,7 H17 Q21,7 23,17 Q24,23 19,21 L15,17 H9 L5,21 Q0,23 1,17 Q3,7 7,7 Z M5,12 H11 M8,9 V15 M17,11 H18 M19,14 H20")
    };
    private static Geometry Shape(string path) { var geometry = Geometry.Parse(path); geometry.Freeze(); return geometry; }
    public static Geometry For(string kind) => Shapes.GetValueOrDefault(kind, Shapes["usb"]);
    public static Brush Accent(string kind) => kind switch
    {
        "microphone" => Brushes.LightCoral, "keyboard" or "mouse" => Brushes.LightSkyBlue,
        "monitor" => Brushes.CornflowerBlue, "camera" => Brushes.Plum, "usb" or "drive" => Brushes.Khaki,
        _ => Teal
    };
    public static FrameworkElement Create(Device device, double size = 22) => new Viewbox
    {
        Width = size, Height = size, Child = new System.Windows.Shapes.Path { Data = For(PeripheralCatalog.Icon(device)), Stroke = Accent(PeripheralCatalog.Icon(device)),
            StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stretch = Stretch.Uniform, Width = 25, Height = 25 }
    };
}
