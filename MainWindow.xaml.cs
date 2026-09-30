using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Controls;
using WBrushes = System.Windows.Media.Brushes;
namespace NightBrightness;
public partial class MainWindow : Window
{
    readonly Scheduler scheduler;
    readonly Dictionary<uint, (System.Windows.Controls.Slider Day, System.Windows.Controls.Slider Night)> monitorSliders = new();
    bool ready, loading, manualCoordinates;
    DateTime? pendingLocationUpdatedUtc;
    string pendingLocationSource="";
    string? locationMessage;
    public bool AllowClose { get; set; }
    internal MainWindow(Scheduler scheduler)
    {
        this.scheduler=scheduler;
        InitializeComponent();
        ready=true; LoadSettings(); RefreshStatus();
        scheduler.Changed += RefreshStatus;
    }
    void LoadSettings()
    {
        loading=true;
        var s=scheduler.Config;
        DaySlider.Value=s.DayBrightness; NightSlider.Value=s.NightBrightness;
        MonitorBrightnessToggle.IsChecked=s.PerMonitorBrightness;
        BuildMonitorControls(s);
        MorningBox.Text=s.Morning; NightBox.Text=s.Night; FadeBox.Text=s.FadeMinutes.ToString();
        ThemeToggle.IsChecked=s.ThemeEnabled; StartupToggle.IsChecked=s.StartAtSignIn;
        WallpaperToggle.IsChecked=s.WallpaperEnabled;
        DayWallpaperBox.Text=s.DayWallpaper; NightWallpaperBox.Text=s.NightWallpaper;
        UpdateWallpaperPreviews();
        SolarToggle.IsChecked=s.SolarEnabled;
        LatitudeBox.Text=s.Latitude?.ToString("F4",CultureInfo.InvariantCulture) ?? "";
        LongitudeBox.Text=s.Longitude?.ToString("F4",CultureInfo.InvariantCulture) ?? "";
        manualCoordinates=false; pendingLocationSource=s.LocationSource; pendingLocationUpdatedUtc=s.LocationUpdatedUtc; locationMessage=null;
        CityBox.Text=s.LocationSource is "Windows" or "Manual" ? "" : s.LocationSource; CityResults.Children.Clear(); CityResults.Visibility=Visibility.Collapsed;
        loading=false; UpdateDraft(false);
    }
    static double? Coordinate(System.Windows.Controls.TextBox field,string name)
    {
        if(string.IsNullOrWhiteSpace(field.Text)) return null;
        if(!double.TryParse(field.Text.Trim().Replace(',', '.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var value))
            throw new ArgumentException($"Enter a valid {name} in degrees.");
        return value;
    }
    Settings Draft()
    {
        if(!int.TryParse(FadeBox.Text.Trim(),out int fade)) throw new ArgumentException("Enter the fade duration in whole minutes.");
        var profiles = new Dictionary<uint, MonitorBrightness>(scheduler.Config.MonitorBrightness);
        foreach(var row in monitorSliders) profiles[row.Key] = new((int)row.Value.Day.Value, (int)row.Value.Night.Value);
        var s=scheduler.Config with {
            PerMonitorBrightness=MonitorBrightnessToggle.IsChecked==true, MonitorBrightness=profiles,
            DayBrightness=(int)DaySlider.Value, NightBrightness=(int)NightSlider.Value,
            Morning=MorningBox.Text.Trim(), Night=NightBox.Text.Trim(), FadeMinutes=fade,
            ThemeEnabled=ThemeToggle.IsChecked==true, StartAtSignIn=StartupToggle.IsChecked==true,
            WallpaperEnabled=WallpaperToggle.IsChecked==true,
            DayWallpaper=DayWallpaperBox.Text, NightWallpaper=NightWallpaperBox.Text,
            SolarEnabled=SolarToggle.IsChecked==true,
            Latitude=Coordinate(LatitudeBox,"latitude"), Longitude=Coordinate(LongitudeBox,"longitude"),
            LocationSource=pendingLocationSource,
            LocationUpdatedUtc=manualCoordinates ? null : pendingLocationUpdatedUtc };
        s.Validate();
        if(s.WallpaperEnabled && (!File.Exists(s.DayWallpaper) || !File.Exists(s.NightWallpaper)))
            throw new ArgumentException("Choose two image files that are still available on this PC.");
        if(s.WallpaperEnabled && (DayWallpaperPreview.Source==null || NightWallpaperPreview.Source==null))
            throw new ArgumentException("Choose two readable JPG, PNG, or BMP images.");
        return s;
    }
    void UpdateDraft(bool edited)
    {
        if(!ready || loading) return;
        DayValue.Text=$"{DaySlider.Value:0}%"; NightValue.Text=$"{NightSlider.Value:0}%";
        MonitorBrightnessPanel.Visibility=MonitorBrightnessToggle.IsChecked==true ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            var s=Draft(); var times=s.EffectiveTimes(DateTime.Today);
            string morning=Settings.Format(times.Morning), night=Settings.Format(times.Night);
            FadeSummary.Text=$"Starts dimming at {s.FadeStart}. Night brightness by {night}.";
            ThemeSummary.Text=$"Light mode  {morning}     ·     Dark mode  {night}";
            WallpaperSummary.Text=s.WallpaperEnabled
                ? $"Day background at {morning} · Night background at {night}."
                : "Desktop background switching is off.";
            MorningBox.IsEnabled=!s.SolarEnabled; NightBox.IsEnabled=!s.SolarEnabled;
            if(locationMessage != null) SolarStatus.Text=locationMessage;
            else if(s.Latitude.HasValue)
            {
                var solar=SolarTimes.ForDate(DateTime.Today,s.Latitude.Value,s.Longitude!.Value);
                SolarStatus.Text=solar.HasValue ? $"Sunrise {solar.Value.Sunrise:HH:mm} · Sunset {solar.Value.Sunset:HH:mm}"
                    : "No sunrise or sunset today; using fixed times.";
            }
            else SolarStatus.Text="Search a city, use Windows location, or enter coordinates.";
            SaveButton.IsEnabled=true;
            SaveStatus.Text=edited ? "Unsaved changes · preview updates as you edit." : "Your schedule is up to date.";
            SaveStatus.Foreground=(SolidColorBrush)new BrushConverter().ConvertFromString("#A6A6A6")!;
            DrawTimeline(s);
        }
        catch(ArgumentException e)
        {
            SaveButton.IsEnabled=false; SaveStatus.Text=e.Message;
            SaveStatus.Foreground=(SolidColorBrush)new BrushConverter().ConvertFromString("#E5AD91")!;
            if(SolarToggle.IsChecked==true) SolarStatus.Text=locationMessage ?? "Search a city, use Windows location, or enter coordinates.";
            if(WallpaperToggle.IsChecked==true) WallpaperSummary.Text=e.Message;
        }
    }
    void DrawTimeline(Settings s)
    {
        double width=Timeline.ActualWidth; if(width<=0) return;
        Timeline.Children.Clear();
        Timeline.Children.Add(new Line { X1=0,X2=width,Y1=57,Y2=57,Stroke=(SolidColorBrush)new BrushConverter().ConvertFromString("#444444")!,StrokeThickness=1 });
        var series=s.PerMonitorBrightness && scheduler.Monitors.Count>0
            ? scheduler.Monitors.Select(m => (uint?)m.Luid).ToArray() : new uint?[] { null };
        var colors=new[] { "#B9DAD0", "#DAC7F2", "#E6C08F" };
        for(int index=0;index<series.Length;index++)
        {
            var points=new PointCollection();
            for(int m=0;m<=1440;m++)
            {
                double level=series[index] is uint id ? s.Target(DateTime.Today.AddMinutes(m),id) : s.Target(DateTime.Today.AddMinutes(m));
                points.Add(new System.Windows.Point(width*m/1440,55-level*.46));
            }
            Timeline.Children.Add(new Polyline { Points=points,Stroke=(SolidColorBrush)new BrushConverter().ConvertFromString(colors[index%colors.Length])!,StrokeThickness=2,StrokeLineJoin=PenLineJoin.Round });
        }
        double now=DateTime.Now.TimeOfDay.TotalMinutes;
        var marker=new Ellipse { Width=7,Height=7,Fill=WBrushes.White };
        Canvas.SetLeft(marker,width*now/1440-3.5); Canvas.SetTop(marker,55-s.Target(DateTime.Now)*.46-3.5);
        Timeline.Children.Add(marker);
    }
    void RefreshStatus()
    {
        if(!ready) return;
        LiveBrightness.Text=scheduler.BrightnessSummary;
        LiveBrightness.FontSize=scheduler.BrightnessSummary.Length>6 ? 30 : 40;
        MonitorLabel.Text=$"on {scheduler.MonitorCount} monitor{(scheduler.MonitorCount==1 ? "" : "s")}";
        LiveStatus.Text=scheduler.ErrorMessage ?? scheduler.Status;
        LiveStatus.TextWrapping=TextWrapping.Wrap;
        PauseButton.Content=!scheduler.Config.Enabled || scheduler.IsOverride ? "Resume" : "Pause";
        SidebarStatus.Text=scheduler.Config.Enabled ? "●  Running in background" : "○  Schedule paused";
        var s=scheduler.Config; var times=s.EffectiveTimes(DateTime.Today);
        string day=s.PerMonitorBrightness ? "Day brightness" : $"{s.DayBrightness}%";
        string night=s.PerMonitorBrightness ? "night brightness" : $"{s.NightBrightness}%";
        NextEvent.Text=s.IsDark(DateTime.Now) ? $"{day} at {Settings.Format(times.Morning)}"+(s.ThemeEnabled ? " · light mode" : "")
            : $"Fade at {s.FadeStart} · {night} by {Settings.Format(times.Night)}";
        MonitorLiveValues.Text=string.Join("  ·  ", scheduler.Monitors.Select(m => $"{m.Name}: {scheduler.BrightnessFor(m.Luid):0.#}%"));
        if(scheduler.LocationStatus!=null && s.SolarEnabled) SolarStatus.Text=scheduler.LocationStatus;
        if(scheduler.WallpaperError!=null && s.WallpaperEnabled) WallpaperSummary.Text=scheduler.WallpaperError;
    }
    void Edited(object sender,RoutedPropertyChangedEventArgs<double> e)=>UpdateDraft(true);
    void BuildMonitorControls(Settings settings)
    {
        monitorSliders.Clear(); MonitorBrightnessPanel.Children.Clear();
        foreach(var monitor in scheduler.Monitors)
        {
            var levels=settings.MonitorBrightness.TryGetValue(monitor.Luid, out var saved) ? saved
                : new MonitorBrightness(settings.DayBrightness, settings.NightBrightness);
            var panel=new StackPanel { Margin=new Thickness(0,18,0,0) };
            panel.Children.Add(new TextBlock { Text=monitor.Name, FontWeight=FontWeights.SemiBold });
            var grid=new Grid { Margin=new Thickness(0,12,0,0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            System.Windows.Controls.Slider AddSlider(string name, int initial, int column)
            {
                var stack=new StackPanel(); Grid.SetColumn(stack,column);
                var value=new TextBlock { Text=$"{name} · {initial}%", Foreground=WBrushes.LightGray };
                var slider=new System.Windows.Controls.Slider { Minimum=0,Maximum=100,TickFrequency=1,IsSnapToTickEnabled=true,Value=initial,Margin=new Thickness(0,8,0,0) };
                System.Windows.Automation.AutomationProperties.SetName(slider, $"{monitor.Name} {name.ToLowerInvariant()} brightness");
                slider.ValueChanged+=(_,_) => { value.Text=$"{name} · {slider.Value:0}%"; UpdateDraft(true); };
                stack.Children.Add(value); stack.Children.Add(slider); grid.Children.Add(stack); return slider;
            }
            var day=AddSlider("Day",levels.Day,0); var night=AddSlider("Night",levels.Night,2);
            monitorSliders.Add(monitor.Luid,(day,night));
            panel.Children.Add(grid); MonitorBrightnessPanel.Children.Add(panel);
        }
        if(monitorSliders.Count==0) MonitorBrightnessPanel.Children.Add(new TextBlock { Text="Connect an NVIDIA monitor, then reopen settings.", TextWrapping=TextWrapping.Wrap });
    }
    void RefreshMonitorsClick(object sender,RoutedEventArgs e)
    {
        try
        {
            var draft=Draft(); scheduler.RefreshMonitors();
            loading=true; BuildMonitorControls(draft); loading=false; UpdateDraft(true);
        }
        catch(Exception error) { loading=false; SaveStatus.Text=error.Message; }
    }
    void TextEdited(object sender,TextChangedEventArgs e)=>UpdateDraft(true);
    void OptionEdited(object sender,RoutedEventArgs e)=>UpdateDraft(true);
    void CoordinateEdited(object sender,TextChangedEventArgs e)
    {
        if(!loading) { manualCoordinates=true; pendingLocationSource="Manual"; locationMessage=null; UpdateDraft(true); }
    }
    async void UseLocationClick(object sender,RoutedEventArgs e)
    {
        LocateButton.IsEnabled=false; SolarStatus.Text="Asking Windows for your location…";
        try
        {
            var position=await LocationProbe.RequestAsync();
            if(!position.HasValue)
            {
                locationMessage="Windows location is off or access was denied. Search a city instead.";
                UpdateDraft(true); return;
            }
            loading=true;
            LatitudeBox.Text=position.Value.Latitude.ToString("F4",CultureInfo.InvariantCulture);
            LongitudeBox.Text=position.Value.Longitude.ToString("F4",CultureInfo.InvariantCulture);
            SolarToggle.IsChecked=true;
            loading=false; manualCoordinates=false; pendingLocationSource="Windows"; pendingLocationUpdatedUtc=DateTime.UtcNow;
            locationMessage=null; UpdateDraft(true);
            SolarStatus.Text=$"Windows location found · {position.Value.Latitude:F2}°, {position.Value.Longitude:F2}°. Save to activate.";
        }
        catch(Exception ex)
        {
            loading=false; Program.Log("Windows location: " + ex); locationMessage="Windows location is unavailable. Search a city or enter coordinates."; UpdateDraft(true);
        }
        finally { LocateButton.IsEnabled=true; }
    }
    async void SearchCityClick(object sender,RoutedEventArgs e)
    {
        SearchCityButton.IsEnabled=false;
        CityResults.Children.Clear(); CityResults.Visibility=Visibility.Collapsed;
        SolarStatus.Text="Searching cities…";
        try
        {
            var results=await CityLookup.SearchAsync(CityBox.Text);
            if(results.Count==0)
            {
                locationMessage="No city found. Try adding the country name.";
                UpdateDraft(true); return;
            }
            foreach(var place in results)
            {
                var button=new System.Windows.Controls.Button {
                    Content=place.Label, HorizontalContentAlignment=System.Windows.HorizontalAlignment.Left,
                    Margin=new Thickness(0,0,0,5), Padding=new Thickness(10,7,10,7),
                    Tag=place, ToolTip=$"{place.Latitude:F4}°, {place.Longitude:F4}°"
                };
                button.Click+=ChooseCityClick;
                CityResults.Children.Add(button);
            }
            CityResults.Visibility=Visibility.Visible;
            SolarStatus.Text="Choose the matching place below.";
        }
        catch(ArgumentException ex) { locationMessage=ex.Message; UpdateDraft(true); }
        catch(Exception ex)
        {
            Program.Log("City search: " + ex);
            locationMessage="City search is unavailable. Check your connection or enter coordinates.";
            UpdateDraft(true);
        }
        finally { SearchCityButton.IsEnabled=true; }
    }
    void ChooseCityClick(object sender,RoutedEventArgs e)
    {
        var place=(CityResult)((System.Windows.Controls.Button)sender).Tag;
        loading=true;
        LatitudeBox.Text=place.Latitude.ToString("F5",CultureInfo.InvariantCulture);
        LongitudeBox.Text=place.Longitude.ToString("F5",CultureInfo.InvariantCulture);
        SolarToggle.IsChecked=true;
        loading=false;
        manualCoordinates=false; pendingLocationSource=place.Label; pendingLocationUpdatedUtc=DateTime.UtcNow;
        locationMessage=null; CityResults.Visibility=Visibility.Collapsed;
        UpdateDraft(true);
        SolarStatus.Text=$"{place.Label} · Save to activate.";
    }
    static void ShowWallpaperPreview(System.Windows.Controls.Image image, TextBlock placeholder, string path)
    {
        image.Source=null;
        if(File.Exists(path))
        {
            try
            {
                var bitmap=new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption=BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth=360;
                bitmap.UriSource=new Uri(System.IO.Path.GetFullPath(path));
                bitmap.EndInit();
                bitmap.Freeze();
                image.Source=bitmap;
            }
            catch(Exception error) { Program.Log("Wallpaper preview: " + error.Message); }
        }
        placeholder.Visibility=image.Source==null ? Visibility.Visible : Visibility.Collapsed;
    }
    void UpdateWallpaperPreviews()
    {
        ShowWallpaperPreview(DayWallpaperPreview,DayWallpaperPlaceholder,DayWallpaperBox.Text);
        ShowWallpaperPreview(NightWallpaperPreview,NightWallpaperPlaceholder,NightWallpaperBox.Text);
        DayWallpaperBox.ToolTip=DayWallpaperBox.Text;
        NightWallpaperBox.ToolTip=NightWallpaperBox.Text;
    }
    void ChooseWallpaper(System.Windows.Controls.TextBox field,string title)
    {
        var dialog=new Microsoft.Win32.OpenFileDialog {
            Title=title,
            Filter="Image files|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*",
            CheckFileExists=true,
            Multiselect=false
        };
        if(File.Exists(field.Text)) dialog.InitialDirectory=System.IO.Path.GetDirectoryName(field.Text);
        else dialog.InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if(dialog.ShowDialog(this)!=true) return;
        field.Text=dialog.FileName;
        UpdateWallpaperPreviews();
        UpdateDraft(true);
    }
    void ChooseDayWallpaperClick(object sender,RoutedEventArgs e)=>ChooseWallpaper(DayWallpaperBox,"Choose a day background");
    void ChooseNightWallpaperClick(object sender,RoutedEventArgs e)=>ChooseWallpaper(NightWallpaperBox,"Choose a night background");
    void TimelineSized(object sender,SizeChangedEventArgs e) { if(ready) { try { DrawTimeline(Draft()); } catch(ArgumentException) {} } }
    void SaveClick(object sender,RoutedEventArgs e)
    {
        try { scheduler.Save(Draft()); LoadSettings(); SaveStatus.Text="Saved. Your updated schedule is active."; }
        catch(Exception ex) { SaveStatus.Text=ex.Message; }
    }
    void ResetClick(object sender,RoutedEventArgs e)=>LoadSettings();
    void PauseClick(object sender,RoutedEventArgs e)
    {
        try { if(!scheduler.Config.Enabled || scheduler.IsOverride) scheduler.Resume(); else scheduler.Pause(); }
        catch(Exception ex) { SaveStatus.Text=ex.Message; }
    }
    void PreviewClick(object sender,RoutedEventArgs e)
    {
        try { scheduler.Preview(Draft()); SaveStatus.Text="Previewing each monitor for 10 seconds. Your saved schedule returns automatically."; }
        catch(Exception error) { SaveStatus.Text=error.Message; }
    }
    void RestoreClick(object sender,RoutedEventArgs e) { scheduler.Restore(); SaveStatus.Text="Day brightness restored until the next fade. Resume from Schedule to cancel."; }
    void OpenFolder(object sender,RoutedEventArgs e)=>Process.Start(new ProcessStartInfo(Program.Data) { UseShellExecute=true });
    void Navigate(object sender,RoutedEventArgs e)
    {
        if(!ready) return;
        var page=(string)((System.Windows.Controls.RadioButton)sender).Tag;
        SchedulePage.Visibility=page=="Schedule" ? Visibility.Visible : Visibility.Collapsed;
        PreferencesPage.Visibility=page=="Preferences" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility=page=="About" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text=page=="Schedule" ? "Your daily rhythm" : page=="Preferences" ? "Make it yours" : "A quieter kind of utility";
        PageSubtitle.Text=page=="Schedule" ? "Comfortable screens, from morning to night." : page=="Preferences" ? "Small details that fit the way you use your PC." : "Simple controls. A little more comfort.";
    }
    void WindowClosing(object? sender,CancelEventArgs e)
    {
        if(!AllowClose) { e.Cancel=true; Hide(); }
        else scheduler.Changed-=RefreshStatus;
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    void WindowReady(object? sender,EventArgs e)
    {
        var hwnd=new WindowInteropHelper(this).Handle;
        int dark=1,caption=0x00212121,text=0x00ECECEC;
        DwmSetWindowAttribute(hwnd,20,ref dark,4);
        DwmSetWindowAttribute(hwnd,35,ref caption,4);
        DwmSetWindowAttribute(hwnd,36,ref text,4);
    }
}
