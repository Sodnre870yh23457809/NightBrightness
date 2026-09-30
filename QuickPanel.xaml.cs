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
        debounce.Tick += (_,_) => { debounce.Stop(); if(changedByUser) ApplyBrightness(); };
        scheduler.Changed += UpdateState;
    }
    public void ShowNearTray()
    {
        loading=true;
        MonitorPicker.Items.Clear();
        MonitorPicker.Items.Add(new System.Windows.Controls.ComboBoxItem { Content="All monitors", Tag=null });
        foreach(var monitor in scheduler.Monitors)
            MonitorPicker.Items.Add(new System.Windows.Controls.ComboBoxItem { Content=monitor.Name, Tag=monitor.Luid });
        MonitorPicker.SelectedIndex=0;
        BrightnessSlider.Value=SelectedBrightness();
        loading=false; changedByUser=false;
        Show(); PlaceNearTray(); UpdateState(); Activate();
    }
    void UpdateState()
    {
        if (!IsVisible) return;
        if(!changedByUser)
        {
            loading=true;
            BrightnessSlider.Value=SelectedBrightness();
            loading=false;
        }
        ValueText.Text=$"{BrightnessSlider.Value:0}%";
        MonitorsText.Text=SelectedMonitor.HasValue ? "1 monitor" : $"{scheduler.MonitorCount} monitors";
        if(!SelectedMonitor.HasValue && !changedByUser) ValueText.Text=scheduler.BrightnessSummary;
        HintText.Text=scheduler.IsOverride ? $"Temporary until {scheduler.OverrideUntil:HH:mm}." : "Adjust until the next schedule change.";
    }
    void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ValueText==null || loading) return;
        changedByUser=true;
        ValueText.Text=$"{BrightnessSlider.Value:0}%";
        debounce.Stop(); debounce.Start();
    }
    uint? SelectedMonitor => (MonitorPicker.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as uint?;
    double SelectedBrightness() => SelectedMonitor is uint id ? scheduler.BrightnessFor(id)
        : scheduler.CurrentBrightness<0 ? scheduler.Config.Target(DateTime.Now) : scheduler.CurrentBrightness;
    void ApplyBrightness() => scheduler.OverrideBrightness(BrightnessSlider.Value, SelectedMonitor);
    void MonitorSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if(loading || BrightnessSlider==null) return;
        debounce.Stop(); changedByUser=false;
        loading=true; BrightnessSlider.Value=SelectedBrightness(); loading=false; UpdateState();
    }
    void ReturnClick(object sender, RoutedEventArgs e) { debounce.Stop(); scheduler.ReturnToSchedule(); Hide(); }
    void SettingsClick(object sender, RoutedEventArgs e) { debounce.Stop(); Hide(); scheduler.ShowWindow(); }
    void CloseClick(object sender, RoutedEventArgs e) { debounce.Stop(); Hide(); }
    void PanelDeactivated(object sender, EventArgs e) { if(changedByUser && debounce.IsEnabled) ApplyBrightness(); debounce.Stop(); Hide(); }
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
