using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace NightBrightness;
public partial class QuickPanel : Window
{
    readonly Scheduler scheduler;
    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(100) };
    bool loading, changedByUser;
    internal QuickPanel(Scheduler scheduler)
    {
        this.scheduler=scheduler;
        InitializeComponent();
        debounce.Tick += (_,_) => { debounce.Stop(); if(changedByUser) scheduler.OverrideBrightness(BrightnessSlider.Value); };
        scheduler.Changed += UpdateState;
    }
    public void ShowNearTray()
    {
        loading=true;
        BrightnessSlider.Value=scheduler.CurrentBrightness<0 ? scheduler.Config.Target(DateTime.Now) : scheduler.CurrentBrightness;
        loading=false; changedByUser=false;
        Show(); PlaceNearTray(); UpdateState(); Activate();
    }
    void UpdateState()
    {
        if (!IsVisible) return;
        if(!changedByUser)
        {
            loading=true;
            BrightnessSlider.Value=scheduler.CurrentBrightness<0 ? scheduler.Config.Target(DateTime.Now) : scheduler.CurrentBrightness;
            loading=false;
        }
        ValueText.Text=$"{BrightnessSlider.Value:0}%";
        MonitorsText.Text=$"{scheduler.MonitorCount} monitor{(scheduler.MonitorCount==1 ? "" : "s")}";
        HintText.Text=scheduler.IsOverride ? $"Temporary until {scheduler.OverrideUntil:HH:mm}." : "Adjust until the next schedule change.";
    }
    void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ValueText==null || loading) return;
        changedByUser=true;
        ValueText.Text=$"{BrightnessSlider.Value:0}%";
        debounce.Stop(); debounce.Start();
    }
    void ReturnClick(object sender, RoutedEventArgs e) { debounce.Stop(); scheduler.ReturnToSchedule(); Hide(); }
    void SettingsClick(object sender, RoutedEventArgs e) { debounce.Stop(); Hide(); scheduler.ShowWindow(); }
    void CloseClick(object sender, RoutedEventArgs e) { debounce.Stop(); Hide(); }
    void PanelDeactivated(object sender, EventArgs e) { if(changedByUser && debounce.IsEnabled) scheduler.OverrideBrightness(BrightnessSlider.Value); debounce.Stop(); Hide(); }
    void PanelLoaded(object sender, RoutedEventArgs e) => PlaceNearTray();
    void PlaceNearTray()
    {
        var scale=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var area=Screen.FromPoint(System.Windows.Forms.Control.MousePosition).WorkingArea;
        var point=scale.Transform(new System.Windows.Point(area.Right,area.Bottom));
        Left=point.X-Width-16; Top=point.Y-Height-16;
    }
    internal void Shutdown()
    {
        debounce.Stop(); scheduler.Changed-=UpdateState; Close();
    }
}
