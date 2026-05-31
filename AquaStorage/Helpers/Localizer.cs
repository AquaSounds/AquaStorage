using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace AquaStorage.Helpers;

public sealed class Localizer : INotifyPropertyChanged
{
    private static readonly Localizer _instance = new();
    public static Localizer Instance => _instance;

    private readonly ResourceManager _rm;

    private Localizer()
    {
        _rm = new ResourceManager("AquaStorage.Resources.Strings", typeof(Localizer).Assembly);
    }

    public static void SetCulture(CultureInfo culture)
    {
        CultureInfo.CurrentUICulture = culture;
        _instance.OnPropertyChanged(null);
    }

    public string this[string key] => _rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    // Static properties for AXAML {x:Static} binding
    public static string AppTitle => Instance["AppTitle"];
    public static string Settings => Instance["Settings"];
    public static string ThemeColor => Instance["ThemeColor"];
    public static string Confirm => Instance["Confirm"];
    public static string Cancel => Instance["Cancel"];
    public static string Close => Instance["Close"];
    public static string Minimize => Instance["Minimize"];
    public static string Maximize => Instance["Maximize"];

    public static string AddFolder => Instance["AddFolder"];
    public static string SelectFolder => Instance["SelectFolder"];

    public static string TabFolders => Instance["TabFolders"];
    public static string AppData => Instance["AppData"];
    public static string WatermarkAppDataPath => Instance["WatermarkAppDataPath"];
    public static string OpenFolder => Instance["OpenFolder"];
    public static string SetFolderLocation => Instance["SetFolderLocation"];
    public static string Delete => Instance["Delete"];
    public static string WatermarkSearchPaths => Instance["WatermarkSearchPaths"];
    public static string SelectConfigFolder => Instance["SelectConfigFolder"];

    public static string TabAppearance => Instance["TabAppearance"];
    public static string Theme => Instance["Theme"];
    public static string ThemeDark => Instance["ThemeDark"];
    public static string ThemeLight => Instance["ThemeLight"];
    public static string ThemeSystem => Instance["ThemeSystem"];
    public static string BackgroundImage => Instance["BackgroundImage"];
    public static string SelectImage => Instance["SelectImage"];
    public static string FillMode => Instance["FillMode"];
    public static string FillStretch => Instance["FillStretch"];
    public static string FillUniformToFill => Instance["FillUniformToFill"];
    public static string FillUniform => Instance["FillUniform"];
    public static string FillTile => Instance["FillTile"];
    public static string BackgroundMask => Instance["BackgroundMask"];
    public static string BackgroundBlur => Instance["BackgroundBlur"];
    public static string ControlTheme => Instance["ControlTheme"];
    public static string RemoveBackground => Instance["RemoveBackground"];
    public static string FontSize => Instance["FontSize"];
    public static string Language => Instance["Language"];
    public static string RestartRequired => Instance["RestartRequired"];
    public static string RestartNow => Instance["RestartNow"];
    public static string RestartLater => Instance["RestartLater"];

    public static string TabStorage => Instance["TabStorage"];
    public static string MaxCache => Instance["MaxCache"];
    public static string WatermarkCacheSize => Instance["WatermarkCacheSize"];
    public static string ClearCache => Instance["ClearCache"];
    public static string CacheCleared => Instance["CacheCleared"];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
