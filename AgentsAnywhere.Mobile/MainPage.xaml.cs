using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentsAnywhere.Mobile.Models;
using AgentsAnywhere.Mobile.Services;
using Newtonsoft.Json.Linq;
using Windows.ApplicationModel;
using Windows.Foundation.Metadata;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
namespace AgentsAnywhere.Mobile
{
 enum AppView { Login, Home, NewSession, Session, Settings, File }

 public sealed partial class MainPage : Page
 {
  readonly ApiClient api = new ApiClient();
  readonly AuthService auth;
  readonly TimelineState timeline = new TimelineState();
  readonly TimelineProjector projector = new TimelineProjector();
  readonly ObservableCollection<TimelineRow> timelineRows = new ObservableCollection<TimelineRow>();
  List<Entity> devices = new List<Entity>(), projects = new List<Entity>(), sessions = new List<Entity>();
  Entity selected;
  // Draft state: the new-session page fills this in, the message page shows it, and the
  // first send turns it into a real session through /sessions/create-and-start.
  Entity draft;
  JObject draftBody;
  string draftMessageId,draftMessageText;
  bool IsDraft { get { return draft!=null && ReferenceEquals(selected,draft); } }
  RealtimeSocket dashboardSocket, sessionSocket;
  CancellationTokenSource workspaceLife = new CancellationTokenSource(), sessionLife = new CancellationTokenSource();
  readonly SemaphoreSlim dashboardGate = new SemaphoreSlim(1,1), recoveryGate = new SemaphoreSlim(1,1);
  string clientId = "uwp-" + Guid.NewGuid().ToString("N");
  string catalogsSignature="", realtimeText="";
  bool ready, filtering, authenticated, paused, testMode;
  int busy, workspaceVersion;
  bool turnJustStarted;
  AppView view = AppView.Login;
  readonly DispatcherTimer fallbackTimer = new DispatcherTimer {Interval=TimeSpan.FromSeconds(25)};
  readonly DispatcherTimer bannerTimer = new DispatcherTimer {Interval=TimeSpan.FromSeconds(4)};
  public MainPage() {
   try { InitializeComponent(); } catch (Exception ex) { App.Crash("page.init", ex); throw; }
    auth=new AuthService(api); TimelineList.ItemsSource=timelineRows; TerminalOutput.ItemsSource=terminalLines;
   api.Trace+=DiagnosticLog.Write;
   ready=true;
   var v=Package.Current.Id.Version;VersionLabel.Text="社区原生客户端 "+v.Major+"."+v.Minor+"."+v.Build+" · MIT";
   api.Unauthorized+=()=> {var ignored=UiAsync(()=> {ExpireLogin();return Task.CompletedTask;});};
   fallbackTimer.Tick+=async(s,e)=> {
    if(authenticated && !paused && busy==0) await ExecuteAsync(async()=> {
     if(dashboardSocket==null || !dashboardSocket.IsConnected) await LoadDashboardAsync();
     if(selected!=null && !IsDraft && (sessionSocket==null || !sessionSocket.IsConnected)) await RecoverAsync(selected.Id,sessionLife.Token);
    });
   };
   bannerTimer.Tick+=(s,e)=> {bannerTimer.Stop();StatusBanner.Visibility=Visibility.Collapsed;};
   StatusText.RegisterPropertyChangedCallback(TextBlock.TextProperty,(s,p)=>ShowBanner());
   SystemNavigationManager.GetForCurrentView().BackRequested+=(s,e)=> {if(!e.Handled)e.Handled=NavigateBack();};
  }
  protected override async void OnNavigatedTo(NavigationEventArgs e) {
   base.OnNavigatedTo(e);
#if DEBUG
   var args=e.Parameter as string ?? "";
   if(args.Contains("--self-test") || args.Contains("--test-ui")) {
    testMode=true;
    await ExecuteAsync(async()=> {await StartTestWorkspaceAsync(args.Contains("--self-test"),args.Contains("--loop"));});
    return;
   }
#endif
   OriginBox.Text=auth.SavedOrigin;
   if(OriginBox.Text!=ApiClient.OfficialOrigin)OriginBox.Visibility=Visibility.Visible;
   LoginOverlay.Visibility=Visibility.Collapsed;
   await ExecuteAsync(async()=> {if(await auth.RestoreAsync()) await EnterWorkspaceAsync();});
   if(!authenticated)ShowView(AppView.Login);
  }

  // ---------- views and back navigation ----------
  void ShowView(AppView next) {
   view=next;
   LoginOverlay.Visibility=next==AppView.Login?Visibility.Visible:Visibility.Collapsed;
   HomeView.Visibility=next==AppView.Home?Visibility.Visible:Visibility.Collapsed;
   SessionView.Visibility=next==AppView.Session?Visibility.Visible:Visibility.Collapsed;
   SettingsView.Visibility=next==AppView.Settings?Visibility.Visible:Visibility.Collapsed;
   FileView.Visibility=next==AppView.File?Visibility.Visible:Visibility.Collapsed;
   NewSessionView.Visibility=next==AppView.NewSession?Visibility.Visible:Visibility.Collapsed;
   SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility=next==AppView.Login||next==AppView.Home?AppViewBackButtonVisibility.Collapsed:AppViewBackButtonVisibility.Visible;
   if(next==AppView.Session)UpdateSessionBar();
  }
  bool NavigateBack() {
   switch(view) {
    case AppView.NewSession: ShowView(AppView.Home);FilterSessions();return true;
    case AppView.File: ShowView(AppView.Session);Tabs.SelectedIndex=1;return true;
    case AppView.Settings: ShowView(AppView.Session);return true;
    case AppView.Session:
     if(Tabs.SelectedIndex==1 && !AtFileRoot){ParentFiles_Click(this,new RoutedEventArgs());return true;}
     ShowView(AppView.Home);FilterSessions();return true;
    case AppView.Home:
     if(SearchBox.Visibility==Visibility.Visible){SearchBox.Text="";SearchBox.Visibility=Visibility.Collapsed;return true;}
     return false;
    default: return false;
   }
  }
  void HomePivot_Changed(object sender,SelectionChangedEventArgs e) {
   if(!ready)return;var sessionsTab=HomePivot.SelectedIndex==0;
   NewSessionButton.Visibility=SearchButton.Visibility=sessionsTab?Visibility.Visible:Visibility.Collapsed;
   HomeBar.Visibility=HomePivot.SelectedIndex==2?Visibility.Collapsed:Visibility.Visible;
  }
  void UpdateSessionBar() {
   int tab=Tabs.SelectedIndex;
   SessionBar.ClosedDisplayMode=tab==0?AppBarClosedDisplayMode.Minimal:AppBarClosedDisplayMode.Compact;
   BarAttach.Visibility=tab==0?Visibility.Visible:Visibility.Collapsed;
   BarUp.Visibility=BarUpload.Visibility=BarGoTo.Visibility=tab==1?Visibility.Visible:Visibility.Collapsed;
   BarReconnect.Visibility=BarCloseTerminal.Visibility=tab==2?Visibility.Visible:Visibility.Collapsed;
   BarRefresh.Visibility=tab==2?Visibility.Collapsed:Visibility.Visible;
   BarCloseTerminal.IsEnabled=terminalId!=null;
  }
  void OpenSettings_Click(object sender,RoutedEventArgs e){if(selected!=null)ShowView(AppView.Settings);}
  void Search_Click(object sender,RoutedEventArgs e) {
   var show=SearchBox.Visibility!=Visibility.Visible;SearchBox.Visibility=show?Visibility.Visible:Visibility.Collapsed;
   if(show)SearchBox.Focus(FocusState.Programmatic);else SearchBox.Text="";
  }
  void ServerToggle_Click(object sender,RoutedEventArgs e){OriginBox.Visibility=OriginBox.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;}

  // ---------- status, progress, errors ----------
  void ShowBanner() {
   bannerTimer.Stop();
   if(String.IsNullOrEmpty(StatusText.Text)){StatusBanner.Visibility=Visibility.Collapsed;return;}
   StatusBanner.Visibility=view==AppView.Login?Visibility.Collapsed:Visibility.Visible;
   if(!testMode)bannerTimer.Start();
  }
  void StatusBanner_Tapped(object sender,TappedRoutedEventArgs e){bannerTimer.Stop();StatusBanner.Visibility=Visibility.Collapsed;}
  void SetRealtime(string text){RealtimeLabel.Text=text;realtimeText=text;UpdateProgress();}
  bool Reconnecting{get{return authenticated && !paused && (realtimeText=="正在重新连接…"||realtimeText=="连接暂不可用，自动重试");}}
  async void UpdateProgress() {
   bool active=busy>0, reconnect=Reconnecting;
   if(ApiInformation.IsTypePresent("Windows.UI.ViewManagement.StatusBar")) {
    try {
     var indicator=StatusBar.GetForCurrentView().ProgressIndicator;
     if(active||reconnect){indicator.Text=reconnect?"正在重新连接…":"";indicator.ProgressValue=null;await indicator.ShowAsync();}
     else await indicator.HideAsync();
    } catch(Exception e){DiagnosticLog.Exception("ui.statusbar",e);}
   } else BusyBar.Visibility=active||reconnect?Visibility.Visible:Visibility.Collapsed;
  }
  async Task ExecuteAsync(Func<Task> action,string success=null) {
   busy++;UpdateProgress();
   try {await action();if(success!=null)StatusText.Text=success;}
   catch(OperationCanceledException e) {
    DiagnosticLog.Exception("action.cancelled",e);
    if(!workspaceLife.IsCancellationRequested&&!sessionLife.IsCancellationRequested) {
     StatusText.Text="请求已取消或超时，请重试。";if(!authenticated)LoginError.Text=StatusText.Text;
    }
   }
   catch(Exception e) {
    DiagnosticLog.Exception("action.failed",e);
    var message=FriendlyError(e);StatusText.Text=message;
    if(!authenticated)LoginError.Text=message;
   } finally {busy=Math.Max(0,busy-1);UpdateProgress();}
  }
  static string FriendlyError(Exception e) {
   var a=e as ApiException;
   if(a!=null) {
    if(a.Status==401)return a.Operation=="/api/v2/auth/login" || a.Operation=="/api/v2/auth/password-salt" ? "登录失败：邮箱或密码不正确，或账号尚未完成验证。" : "服务器拒绝登录凭证，请重新登录。";
    if(a.Status==412)return "文件已在工作设备上修改，请重新打开后再保存。";
    if(ApiClient.IsMissingFileError(a))return "工作设备上找不到这个文件或目录。";
    if(a.Status==502 || a.Status==503)return "工作设备暂不可用，请检查 Connector 并重试。";
    if(ApiClient.IsSessionLockedError(a))return "这个会话正在执行任务，暂时不能发送新消息。请等当前回合结束，或用「追加」把指令并入当前任务。";
    return a.Message;
   }
   if(e is System.Net.Http.HttpRequestException)return "无法连接服务器，请检查网络、服务器地址和工作设备状态。";
   if(e is TaskCanceledException)return "请求超时，请重试。";
   return e.Message;
  }
  async Task UiAsync(Func<Task> action) {
   if(Dispatcher.HasThreadAccess) {await action();return;}
   var done=new TaskCompletionSource<bool>();
   await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,async()=> {
    try{await action();done.TrySetResult(true);}catch(Exception e){done.TrySetException(e);}
   });
   await done.Task;
  }
  async void Diagnostics_Click(object sender,RoutedEventArgs e) {
   await ExecuteAsync(async()=> {
    var text=await DiagnosticLog.ReadAsync();
    var viewer=new TextBox {Text=text,AcceptsReturn=true,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=320,FontSize=11,FontFamily=new FontFamily("Consolas"),HorizontalAlignment=HorizontalAlignment.Stretch};
    ScrollViewer.SetVerticalScrollBarVisibility(viewer,ScrollBarVisibility.Auto);
    var dialog=new ContentDialog {Title="调试日志",Content=viewer,PrimaryButtonText="复制",SecondaryButtonText="关闭"};
    if(await dialog.ShowAsync()==ContentDialogResult.Primary) {
     var data=new Windows.ApplicationModel.DataTransfer.DataPackage();data.SetText(text);Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
    }
   });
  }

  // ---------- login ----------
  async void Login_Click(object sender,RoutedEventArgs e) {
   if(!LoginButton.IsEnabled)return;
   LoginButton.IsEnabled=false;BrowserLoginButton.IsEnabled=false;LoginError.Text="";
   var password=PasswordBox.Password;
   await ExecuteAsync(async()=> {await auth.LoginAsync(OriginBox.Text,EmailBox.Text,password);PasswordBox.Password="";await EnterWorkspaceAsync();});
   password=null;LoginButton.IsEnabled=true;BrowserLoginButton.IsEnabled=true;
  }
  async void BrowserLogin_Click(object sender,RoutedEventArgs e) {
   LoginButton.IsEnabled=false;BrowserLoginButton.IsEnabled=false;LoginError.Text="";
   await ExecuteAsync(async()=> {await auth.LoginWithBrowserAsync(OriginBox.Text);await EnterWorkspaceAsync();});
   LoginButton.IsEnabled=true;BrowserLoginButton.IsEnabled=true;
  }
  void Password_KeyDown(object sender,KeyRoutedEventArgs e){if(e.Key==VirtualKey.Enter){e.Handled=true;Login_Click(sender,new RoutedEventArgs());}}
  async Task EnterWorkspaceAsync() {
   workspaceLife.Dispose();workspaceLife=new CancellationTokenSource();workspaceVersion++;
   authenticated=true;paused=false;ShowView(AppView.Home);HomePivot.SelectedIndex=0;
   AccountLabel.Text=auth.User?.Text("displayName",auth.User?.Text("email","你的工作区")) ?? "桌面测试";
   ServerLabel.Text=api.Origin==ApiClient.OfficialOrigin?"官方云 · "+new Uri(api.Origin).Host:"自托管 · "+api.Origin;LoginError.Text="";
   await LoadDashboardAsync();StartDashboardSocket();fallbackTimer.Start();
  }
  void ExpireLogin() {
   if(!authenticated)return;
   Pause();workspaceLife.Cancel();sessionLife.Cancel();workspaceVersion++;
   authenticated=false;auth.SignOut();selected=null;turnJustStarted=false;timelineRows.Clear();
   ((CollectionViewSource)Resources["SessionSource"]).Source=null;
   ShowView(AppView.Login);LoginError.Text="登录已过期，请重新登录。";UpdateCapabilities();UpdateProgress();
  }
  async void SignOut_Click(object sender,RoutedEventArgs e) {
   var confirm=new ContentDialog {Title="退出登录？",Content="保存在这台设备上的登录凭证会被删除。",PrimaryButtonText="退出",SecondaryButtonText="取消"};
   if(await confirm.ShowAsync()!=ContentDialogResult.Primary)return;
   ExpireLogin();auth.SignOut();PasswordBox.Password="";LoginError.Text="已退出登录。";SetRealtime("");
  }
  public void Pause() {
   paused=true;fallbackTimer.Stop();dashboardSocket?.Dispose();sessionSocket?.Dispose();terminalSocket?.Dispose();
   dashboardSocket=sessionSocket=terminalSocket=null;
  }
  public async Task ResumeAsync() {
   if(!authenticated)return;paused=false;
   await ExecuteAsync(async()=> {
    await LoadDashboardAsync();
    if(selected!=null) {await RecoverAsync(selected.Id,sessionLife.Token);StartSessionSocket(selected.Id);}
    StartDashboardSocket();if(terminalId!=null)StartTerminalSocket();fallbackTimer.Start();
   });
  }

  // ---------- workspace ----------
  async Task LoadDashboardAsync() {
   var ct=workspaceLife.Token;var version=workspaceVersion;
   await dashboardGate.WaitAsync(ct);
   try {
    var d=api.GetAsync("/connectors",ct);var p=api.GetAsync("/projects",ct);var s=api.GetAsync("/sessions/list",ct);
    await Task.WhenAll(d,p,s);ct.ThrowIfCancellationRequested();
    if(version!=workspaceVersion)return;
    ApplyDashboard(await d,await p,await s);
   } finally {dashboardGate.Release();}
  }
  void ApplyDashboard(JObject d,JObject p,JObject s) {
   devices=d.Objects("connectors").Select(x=>new Entity(x)).ToList();
   projects=p.Objects("projects").Select(x=>new Entity(x)).ToList();
   sessions=s.Objects("sessions").Select(x=>new Entity(x)).ToList();
   var deviceId=(DeviceBox.SelectedItem as Entity)?.Id;var projectId=(ProjectBox.SelectedItem as Entity)?.Id;
   filtering=true;
   DeviceBox.ItemsSource=new[]{AllEntity("全部设备")}.Concat(devices).ToList();
   DeviceBox.SelectedItem=((IEnumerable<Entity>)DeviceBox.ItemsSource).FirstOrDefault(x=>x.Id==deviceId) ?? ((IEnumerable<Entity>)DeviceBox.ItemsSource).First();
   UpdateProjectFilter(projectId);filtering=false;FilterSessions();
   DeviceList.ItemsSource=devices.OrderByDescending(x=>x.Online).Select(x=>DeviceCard.From(x,sessions.Count(y=>y.ConnectorId==x.Id&&!y.Archived))).ToList();
   ProjectList.ItemsSource=projects;
  }
  static Entity AllEntity(string name){return new Entity(new JObject {["id"]="",["name"]=name});}
  void UpdateProjectFilter(string preferred=null) {
   var id=(DeviceBox.SelectedItem as Entity)?.Id;
   var list=new[]{AllEntity("全部项目")}.Concat(projects.Where(x=>String.IsNullOrEmpty(id)||x.ConnectorId==id)).ToList();
   ProjectBox.ItemsSource=list;ProjectBox.SelectedItem=list.FirstOrDefault(x=>x.Id==preferred) ?? list.First();
  }
  void Filter_Changed(object sender,SelectionChangedEventArgs e) {
   if(!ready||filtering)return;
   if(sender==DeviceBox){filtering=true;UpdateProjectFilter((ProjectBox.SelectedItem as Entity)?.Id);filtering=false;}
   FilterSessions();
  }
  void Search_Changed(object sender,TextChangedEventArgs e){if(ready)FilterSessions();}
  void Archive_Changed(object sender,RoutedEventArgs e){if(ready)FilterSessions();}
  void FilterSessions() {
   if(!ready || filtering || !authenticated)return;
   var d=(DeviceBox.SelectedItem as Entity)?.Id;var p=(ProjectBox.SelectedItem as Entity)?.Id;
   var q=(SearchBox.Text??"").Trim();var archive=ArchiveCheck.IsChecked==true;
   var list=sessions.Where(x=>x.Archived==archive && (String.IsNullOrEmpty(d)||x.ConnectorId==d) && (String.IsNullOrEmpty(p)||x.ProjectId==p) && (q.Length==0||x.Name.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0||x.Workspace.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0))
    .OrderByDescending(x=>x.Data.Flag("pinned")).ThenByDescending(x=>x.Data.Text("sortAt",x.Data.Text("updatedAt"))).ToList();
   ((CollectionViewSource)Resources["SessionSource"]).Source=SessionCards.Group(list,devices);
   EmptySessions.Text=archive?"没有已归档的会话。":q.Length>0?"没有找到匹配的会话。":"还没有会话。先在工作设备上安装官方桌面客户端并登录同一个账号，再点下方的“新建”创建会话。";
   EmptySessions.Visibility=list.Count==0?Visibility.Visible:Visibility.Collapsed;
  }
  void Device_Click(object sender,RoutedEventArgs e) {
   var card=(sender as FrameworkElement)?.Tag as DeviceCard;if(card==null)return;
   DeviceBox.SelectedItem=((IEnumerable<Entity>)DeviceBox.ItemsSource).FirstOrDefault(x=>x.Id==card.Entity.Id);HomePivot.SelectedIndex=0;
  }
  void Entity_Click(object sender,RoutedEventArgs e) {
   var project=(sender as FrameworkElement)?.Tag as Entity;if(project==null)return;
   filtering=true;DeviceBox.SelectedIndex=0;UpdateProjectFilter(project.Id);filtering=false;FilterSessions();HomePivot.SelectedIndex=0;
  }
  void StartDashboardSocket() {
   dashboardSocket?.Dispose();var ct=workspaceLife.Token;var version=workspaceVersion;
   dashboardSocket=new RealtimeSocket(async cancel=> {
    var t=await api.PostAsync("/ws-ticket",new JObject {["clientId"]=clientId,["scope"]=new JObject {["dashboard"]=true}},true,cancel);
    return api.SocketUrl("/dashboard/ws?ticket="+ApiClient.Escape(t.Text("ticket")));
   }, msg=>UiAsync(()=> {
    if(!ct.IsCancellationRequested && version==workspaceVersion && msg.Text("type")=="dashboard.snapshot")ApplyDashboard(msg,msg,msg);
    return Task.CompletedTask;
   }),()=>UiAsync(async()=> {if(!ct.IsCancellationRequested)await LoadDashboardAsync();}),text=> {
    var ignored=UiAsync(()=> {if(!ct.IsCancellationRequested && selected==null)SetRealtime(text);return Task.CompletedTask;});
   });dashboardSocket.Start();
  }

  // ---------- session ----------
  async void Session_Click(object sender,ItemClickEventArgs e) {
   var card=e.ClickedItem as SessionCard;if(card==null)return;
   if(card.Entity.Id==selected?.Id){ShowView(AppView.Session);return;}
   await ExecuteAsync(()=>SelectSessionAsync(card.Entity));
  }
  async Task SelectSessionAsync(Entity entity,bool startingTurn=false) {
   // create-and-start starts a turn, but the first snapshot can still carry the pre-turn
   // capabilities; hold 发送 until the event stream proves the turn finished.
   turnJustStarted=startingTurn;
   await DetachTerminalAsync(false);
   sessionSocket?.Dispose();sessionSocket=null;sessionLife.Cancel();sessionLife.Dispose();sessionLife=new CancellationTokenSource();
   var ct=sessionLife.Token;selected=entity;timelineRows.Clear();projector.ClearToggles();catalogsSignature="";
   attachments.Clear();UpdateAttachments();
   MessageBox.Text="";ResetFiles();UpdateCapabilities(false);
   SessionTitle.Text=entity.Name;SessionSubtitle.Text=DeviceStatus(entity);TimelineEmpty.Text="正在加载会话…";TimelineEmpty.Visibility=Visibility.Visible;
   RunBar.Visibility=Visibility.Collapsed;RunProgress.Visibility=Visibility.Collapsed;
   Tabs.SelectedIndex=0;ShowView(AppView.Session);
   var snapshot=await api.GetAsync(SessionPath(entity.Id)+"/snapshot",ct);ct.ThrowIfCancellationRequested();
   if(selected?.Id!=entity.Id)return;
   timeline.Snapshot(snapshot);RenderSession(true);StartSessionSocket(entity.Id);
   await LoadCommandsAsync(entity.Id,ct);
  }
  static string SessionPath(string id){return "/sessions/"+ApiClient.Escape(id);}
  void RequireSession(){if(selected==null)throw new InvalidOperationException("请先选择一个会话。");if(IsDraft)throw new InvalidOperationException("请先发送第一条消息创建会话。");}
  string DeviceStatus(Entity session) {
   var device=devices.FirstOrDefault(x=>x.Id==session.ConnectorId);
   if(device==null)return session.Data.Text("connectorStatus")=="online"?"在线":"";
   return device.Name+" · "+(device.Online?"在线":"离线");
  }
  async Task RecoverAsync(string id,CancellationToken ct) {
   await recoveryGate.WaitAsync(ct);
   try {
    if(selected?.Id!=id)return;
    bool recovered=false;
    for(int page=0;page<20;page++) {
     var before=timeline.Cursor;
     var r=await api.GetAsync(SessionPath(id)+"/events?after="+ApiClient.Escape(before),ct);ct.ThrowIfCancellationRequested();
     if(selected?.Id!=id)return;
     if(r.Flag("snapshotRequired")){var snap=await api.GetAsync(SessionPath(id)+"/snapshot",ct);ct.ThrowIfCancellationRequested();if(selected?.Id==id)timeline.Snapshot(snap);break;}
     foreach(var evt in r.Objects("events"))if(timeline.Apply(evt,true))recovered=true;
     timeline.AdvanceCursor(r.Text("nextCursor",timeline.Cursor));
     if(r.Text("nextCursor",timeline.Cursor)==timeline.Cursor || r.Text("nextCursor")==before)break;
    }
    if(recovered)turnJustStarted=false;
    RenderSession();
   } finally {recoveryGate.Release();}
  }
  void StartSessionSocket(string id) {
   sessionSocket?.Dispose();var ct=sessionLife.Token;
   sessionSocket=new RealtimeSocket(async cancel=> {
    var t=await api.PostAsync("/ws-ticket",new JObject {["clientId"]=clientId,["scope"]=new JObject {["sessionId"]=id}},true,cancel);
    return api.SocketUrl(SessionPath(id)+"/ws?ticket="+ApiClient.Escape(t.Text("ticket")));
   }, evt=>UiAsync(async()=> {
    if(ct.IsCancellationRequested || selected?.Id!=id || evt.Text("type")=="keepalive")return;
    if(evt.Text("type")=="timeline.reset"){await ReloadSessionAsync(id);return;}
    if(evt.Number("sequence")>timeline.Sequence+1)await RecoverAsync(id,ct);
    if(timeline.Apply(evt)){turnJustStarted=false;RenderSession();}
   }),()=>UiAsync(async()=>{
    await RecoverAsync(id,ct);
    var snapshot=await api.GetAsync(SessionPath(id)+"/snapshot",ct);ct.ThrowIfCancellationRequested();
    if(selected?.Id==id){timeline.RefreshProjection(snapshot);RenderSession();}
   }),text=> {
    var ignored=UiAsync(()=> {if(!ct.IsCancellationRequested && selected?.Id==id)SetRealtime(text);return Task.CompletedTask;});
   });sessionSocket.Start();
  }
  string RunState{get{return timeline.State.Text("status",selected?.Status??"");}}
  bool SessionBusy{get{var s=RunState;return turnJustStarted||s=="waiting"||s=="pending"||s=="running"||s=="stopping"||s=="waiting_approval"||s=="blocked";}}
  void RenderSession(bool scroll=false) {
   if(selected==null)return;
   if(IsDraft){RenderDraft();return;}
   selected=new Entity(timeline.Session);SessionTitle.Text=selected.Name;SessionSubtitle.Text=DeviceStatus(selected);
   projector.AgentName=selected.Data.Text("runtimeName",selected.Data.Text("runtime","Agent"));
   RenderTimeline(scroll,false);
   RenderCatalogs();UpdateCapabilities();FileRootLabel.Text=FileRoot;
   SettingsSessionName.Text=selected.Name;SettingsWorkspace.Text=FileRoot;
   SettingsDevice.Text=DeviceStatus(selected);SettingsRuntime.Text=String.Join(" · ",new[]{selected.Data.Text("runtimeName",selected.Data.Text("runtime")),RunState}.Where(x=>x.Length>0));
  }
  void RenderDraft() {
   SessionTitle.Text=draft.Data.Text("title","新对话");
   SessionSubtitle.Text=DeviceStatus(draft);
   TimelineEmpty.Text="发送第一条消息后才会真正创建会话。";
   TimelineEmpty.Visibility=timelineRows.Count==0?Visibility.Visible:Visibility.Collapsed;
   HistoryButton.Visibility=Visibility.Collapsed;
   RunBar.Visibility=RunProgress.Visibility=Visibility.Collapsed;RunRing.IsActive=false;
   FileRootLabel.Text=FileRoot;
   UpdateCapabilities();
  }
  void RenderTimeline(bool scroll,bool keepPosition) {
   var viewer=FindChild<ScrollViewer>(TimelineList);var atEnd=viewer==null || viewer.ScrollableHeight-viewer.VerticalOffset<=80;
   var running=SessionBusy;
   var rows=projector.Project(timeline.Items,timeline.Notices,running);
   var keys=new HashSet<string>(rows.Select(x=>x.Key));
   int before=timelineRows.Count;
   for(int i=timelineRows.Count-1;i>=0;i--)if(!keys.Contains(timelineRows[i].Key))timelineRows.RemoveAt(i);
   var previousTail=timelineRows.Count>0?timelineRows[timelineRows.Count-1].Signature:null;
   for(int i=0;i<rows.Count;i++) {
    if(i<timelineRows.Count && timelineRows[i].Key==rows[i].Key) {
     if(timelineRows[i].Signature!=rows[i].Signature) {
      // Swap the item only when its template changes; otherwise mutate it in place so the
      // virtualized list keeps its containers instead of rebuilding them on every delta.
      if(timelineRows[i].Kind!=rows[i].Kind)timelineRows[i]=rows[i];else timelineRows[i].CopyFrom(rows[i]);
     }
    }
    else {var old=timelineRows.FirstOrDefault(x=>x.Key==rows[i].Key);if(old!=null)timelineRows.Remove(old);timelineRows.Insert(i,rows[i]);}
   }
   var tailChanged=timelineRows.Count>0 && timelineRows[timelineRows.Count-1].Signature!=previousTail;
   if(rows.Count>0 && !keepPosition && (scroll || atEnd && (timelineRows.Count>before || tailChanged)))ScrollToEnd();
   TimelineEmpty.Text="这个会话还没有消息。发送任务即可开始。";TimelineEmpty.Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;
   HistoryButton.Visibility=timeline.HasOlder?Visibility.Visible:Visibility.Collapsed;
   RunProgress.Visibility=RunBar.Visibility=running?Visibility.Visible:Visibility.Collapsed;RunRing.IsActive=running;
   RunText.Text=RunDescription(rows);
   HookTimelineScroll();UpdateBottomButton();
  }
  ScrollViewer timelineViewer;bool scrollHooked;
  void HookTimelineScroll() {
   if(scrollHooked)return;
   var v=FindChild<ScrollViewer>(TimelineList);if(v==null)return;
   scrollHooked=true;timelineViewer=v;
   v.ViewChanged+=(s,e)=>UpdateBottomButton();
  }
  void UpdateBottomButton() {
   var v=timelineViewer??FindChild<ScrollViewer>(TimelineList);
   BottomButton.Visibility=v!=null&&v.ScrollableHeight-v.VerticalOffset>240?Visibility.Visible:Visibility.Collapsed;
  }
  void Bottom_Click(object sender,RoutedEventArgs e) {
   ScrollToEnd();
   var v=timelineViewer??FindChild<ScrollViewer>(TimelineList);if(v!=null)v.ChangeView(null,v.ScrollableHeight,null,true);
  }
  // Rows have very different heights, so the virtualized ListView only knows the real extent
  // after the last rows are measured; settle the offset over a few layout passes.
  bool scrollingToEnd;
  async void ScrollToEnd() {
   if(timelineRows.Count==0||scrollingToEnd)return;
   scrollingToEnd=true;
   try {
    for(int i=0;i<3;i++) {
     var v=timelineViewer??FindChild<ScrollViewer>(TimelineList);if(v==null)return;
     if(v.ScrollableHeight-v.VerticalOffset<=1)return;
     v.ChangeView(null,v.ScrollableHeight,null,true);
     await Task.Delay(90);
    }
   } finally {scrollingToEnd=false;}
  }
  string RunDescription(List<TimelineRow> rows) {
   if(RunState=="waiting_approval")return "等待你批准";
   if(RunState=="stopping")return "正在停止…";
   var process=rows.LastOrDefault(x=>x.Kind==RowKind.Process && x.Running);
   var step=process?.Steps.LastOrDefault();
   if(step==null)return "运行中";
   var prefix="第 "+process.Summary.Split(' ')[1]+" 步 · ";
   if(step.Kind==StepKind.Reasoning)return prefix+"正在思考";
   if(step.State==StepState.Running)return prefix+"正在执行 "+step.Title;
   return prefix+"运行中";
  }
  void ProcessHeader_Tapped(object sender,TappedRoutedEventArgs e) {
   var row=(sender as FrameworkElement)?.DataContext as TimelineRow;if(row==null)return;
   projector.Toggle(row.Key);RenderTimeline(false,true);
  }
  void Step_Tapped(object sender,TappedRoutedEventArgs e) {
   var step=(sender as FrameworkElement)?.DataContext as ProcessStep;if(step==null||!step.CanExpand)return;
   e.Handled=true;projector.Toggle(step.Id);RenderTimeline(false,true);
  }
  static T FindChild<T>(DependencyObject parent) where T:DependencyObject {
   for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var c=VisualTreeHelper.GetChild(parent,i);if(c is T)return (T)c;var match=FindChild<T>(c);if(match!=null)return match;}return null;
  }
  void UpdateCapabilities(bool loaded=true) {
   bool active=loaded&&authenticated&&selected!=null;
   bool steerMode=active&&SessionBusy&&timeline.Can("session.steer");
   SendButton.Visibility=steerMode?Visibility.Collapsed:Visibility.Visible;
   SteerButton.Visibility=steerMode?Visibility.Visible:Visibility.Collapsed;
   MessageBox.PlaceholderText=steerMode?"追加到当前任务":"输入任务或回复";
   SendButton.IsEnabled=active&&!sending&&(IsDraft||(!SessionBusy&&timeline.Can("session.send_message")));
   SteerButton.IsEnabled=active&&!sending&&!IsDraft&&timeline.Can("session.steer");
   InterruptButton.IsEnabled=active&&!IsDraft&&timeline.Can("session.interrupt");
   AttachButton.IsEnabled=BarAttach.IsEnabled=active&&(IsDraft||timeline.Can("runtime.attachment"));
   TakeoverButton.IsEnabled=BarTakeover.IsEnabled=active&&!IsDraft;
   BarCommands.IsEnabled=active&&!IsDraft&&timeline.Can("session.commands");
   BarSettings.IsEnabled=!IsDraft;
   var takeover=selected?.Data.Flag("takeover")==true;
   TakeoverButton.Content=BarTakeover.Label=takeover?"释放控制":"接管控制";
   ArchiveButton.Content=BarArchive.Label=selected?.Archived==true?"恢复":"归档";
  }
  async void History_Click(object sender,RoutedEventArgs e) {
   await ExecuteAsync(async()=> {
    RequireSession();var id=selected.Id;var ct=sessionLife.Token;var first=timeline.Items.FirstOrDefault();if(first==null)return;
    var r=await api.GetAsync(SessionPath(id)+"/timeline?mode=history&limit=100&beforeOrderSeq="+first.Order,ct);ct.ThrowIfCancellationRequested();
    if(selected?.Id!=id)return;foreach(var item in r.Objects("items"))timeline.Upsert(item);timeline.HasOlder=r.Flag("hasMore");
    RenderTimeline(false,true);
   });
  }
  async void Refresh_Click(object sender,RoutedEventArgs e) {await ExecuteAsync(async()=> {if(!authenticated){api.Configure(OriginBox.Text);await api.GetAsync("/auth/config");StatusText.Text="服务器可用";return;}await LoadDashboardAsync();if(selected!=null)await RecoverAsync(selected.Id,sessionLife.Token);},"已刷新");}
  async void SessionRefresh_Click(object sender,RoutedEventArgs e) {
   if(Tabs.SelectedIndex==1){await ExecuteAsync(()=>BrowseFilesAsync());return;}
   await ExecuteAsync(async()=> {RequireSession();await RecoverAsync(selected.Id,sessionLife.Token);await ReloadSessionAsync(selected.Id);},"已刷新");
  }
  async void Register_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {var origin=ApiClient.NormalizeOrigin(OriginBox.Text);await Launcher.LaunchUriAsync(new Uri(origin+"/#/login"));});}
  async void Help_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {await Launcher.LaunchUriAsync(new Uri("https://agents-anywhere.com"));});}
  async void License_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {var f=await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///THIRD-PARTY-NOTICES.txt"));await ShowInfoAsync("关于与许可证","Agents Anywhere UWP · "+VersionLabel.Text+"\n协议来自 anywhere-labs/Agents-Anywhere（MIT）。\n\n"+await Windows.Storage.FileIO.ReadTextAsync(f));});}
  async Task ShowInfoAsync(string title,string text){await new ContentDialog {Title=title,Content=new ScrollViewer {MaxHeight=420,Content=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true}},PrimaryButtonText="确定"}.ShowAsync();}
 }
}
