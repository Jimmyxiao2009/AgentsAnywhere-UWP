using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.ViewManagement;
using Windows.Foundation;
namespace AgentsAnywhere.Mobile
{
 public sealed partial class App : Application
 {
  public App() {
   try { InitializeComponent(); ApplyAccent(); } catch (Exception ex) { Crash("app.ctor", ex); throw; }
   Suspending += OnSuspending; Resuming += OnResuming; UnhandledException += OnUnhandled;
  }
  // Synchronous crash capture for startup failures that outrun the asynchronous diagnostic log.
  internal static void Crash(string phase, Exception ex) {
   try { System.IO.File.AppendAllText(System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path,"crash.log"),DateTime.UtcNow.ToString("o")+" "+phase+"\r\n"+ex+"\r\n\r\n"); } catch { }
  }
  internal void ApplyAccent() {
   // Application.Resources can throw E_UNEXPECTED this early in activation, so retry after Window.Activate.
   try {
    var accent = new UISettings().GetColorValue(UIColorType.Accent);
    ((Windows.UI.Xaml.Media.SolidColorBrush)Resources["AccentBrush"]).Color = accent;
    ((Windows.UI.Xaml.Media.SolidColorBrush)Resources["AccentTextBrush"]).Color = Lighten(accent, 0.55);
    ((Windows.UI.Xaml.Media.SolidColorBrush)Resources["AccentSoftBrush"]).Color = Windows.UI.Color.FromArgb(0x59, accent.R, accent.G, accent.B);
   } catch (Exception e) { Services.DiagnosticLog.Exception("app.accent", e); }
  }
  // AccentLight1/2/3 are unsupported on Windows 10 Mobile (E_UNEXPECTED), so derive the light tint.
  private static Windows.UI.Color Lighten(Windows.UI.Color c, double amount) {
   Func<byte, byte> mix = v => (byte)Math.Min(255, v + (255 - v) * amount);
   return Windows.UI.Color.FromArgb(255, mix(c.R), mix(c.G), mix(c.B));
  }
  protected override void OnLaunched(LaunchActivatedEventArgs e) { Launch(e.Arguments); }
  protected override void OnActivated(IActivatedEventArgs args) {
   var protocol = args as ProtocolActivatedEventArgs;
   Launch(protocol == null ? "" : protocol.Uri.ToString());
  }
  private void Launch(string args) {
   try { LaunchCore(args); } catch (Exception ex) { Crash("app.launch", ex); throw; }
  }
  private void LaunchCore(string args) {
   Services.DiagnosticLog.Write("app.launch version="+Package.Current.Id.Version.Major+"."+Package.Current.Id.Version.Minor+"."+Package.Current.Id.Version.Build+"."+Package.Current.Id.Version.Revision);
   var frame = Window.Current.Content as Frame;
   if (frame == null) { frame = new Frame(); Window.Current.Content = frame; }
   if (frame.Content == null) frame.Navigate(typeof(MainPage), args);
   // The UI is designed for phones; a phone-sized desktop window keeps desktop verification representative.
   ApplicationView.PreferredLaunchViewSize = new Size(400, 760);
   ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;
   Window.Current.Activate();
   ApplyAccent();
  }
  private void OnSuspending(object sender, SuspendingEventArgs e) { (Window.Current.Content as Frame)?.Content.AsPage()?.Pause(); }
  private async void OnResuming(object sender, object e) {
   ApplyAccent();
   var page = (Window.Current.Content as Frame)?.Content as MainPage;
   if (page != null) await page.ResumeAsync();
  }
  private void OnUnhandled(object sender, UnhandledExceptionEventArgs e) {
   // Expected network and action failures are caught by the page. Never hide programming errors.
   Services.DiagnosticLog.Exception("app.unhandled",e.Exception);
   Services.DiagnosticLog.Flush();
   Crash("app.unhandled",e.Exception);
  }
 }
 internal static class PageExtensions { internal static MainPage AsPage(this object value) { return value as MainPage; } }
}
