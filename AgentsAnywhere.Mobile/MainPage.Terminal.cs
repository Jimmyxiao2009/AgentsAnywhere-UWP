using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;
using AgentsAnywhere.Mobile.Models;
using AgentsAnywhere.Mobile.Services;
using AgentsAnywhere.Mobile.Ui;
using Newtonsoft.Json.Linq;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
namespace AgentsAnywhere.Mobile
{
 public sealed partial class MainPage
 {
  readonly TerminalBuffer terminalBuffer=new TerminalBuffer();
  readonly ObservableCollection<TerminalLineVm> terminalLines=new ObservableCollection<TerminalLineVm>();
  RealtimeSocket terminalSocket;
  string terminalId,terminalConnector,terminalRoot;long terminalSeq;
  bool terminalOpening,terminalAtBottom=true;
  // Consolas advances 0.55 em, so a 12 px font gives 6.6 px cells; the line template pins 16 px rows.
  const double TerminalCellWidth=6.6,TerminalLineHeight=16;
  async void OpenTerminal_Click(object sender,RoutedEventArgs e){await ExecuteAsync(()=>OpenTerminalAsync());}
  async void ReconnectTerminal_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=>{if(terminalId==null)await OpenTerminalAsync();else StartTerminalSocket();});}
  async Task OpenTerminalAsync() {
   RequireSession();if(terminalOpening)return;
   if(terminalId!=null){if(terminalSocket==null)StartTerminalSocket();return;}
   var id=selected.Id;var ct=sessionLife.Token;var connector=selected.ConnectorId;var root=FileRoot;
   if(String.IsNullOrWhiteSpace(root))throw new InvalidOperationException("当前会话没有工作目录。");
   terminalOpening=true;
   try {
    var reply=await api.PostAsync("/connectors/"+ApiClient.Escape(connector)+"/terminals-v2?root="+ApiClient.Escape(root),new JObject {
     ["cwd"]=".",["cols"]=terminalBuffer.Columns,["rows"]=terminalBuffer.Rows,["label"]="UWP Shell",["persistent"]=false
    },true,ct);ct.ThrowIfCancellationRequested();
    if(selected?.Id!=id)return;
    terminalId=reply["result"].Text("terminalId");if(terminalId.Length==0)throw new ApiException(400,"设备未返回终端。");
    terminalConnector=connector;terminalRoot=root;terminalSeq=0;terminalBuffer.Reset();terminalLines.Clear();StartTerminalSocket();UpdateTerminalChrome();
   }finally{terminalOpening=false;}
  }
  void StartTerminalSocket() {
   terminalSocket?.Dispose();var id=terminalId;var connector=terminalConnector;var ct=sessionLife.Token;
   terminalSocket=new RealtimeSocket(cancel=>Task.FromResult(api.SocketUrl("/connectors/"+ApiClient.Escape(connector)+"/terminals-v2/"+ApiClient.Escape(id)+"/stream?fromSeq="+terminalSeq+"&token="+ApiClient.Escape(api.Token))),msg=>UiAsync(()=> {
    if(ct.IsCancellationRequested||terminalId!=id)return Task.CompletedTask;
    var type=msg.Text("type");
    if(type=="output"||type=="replay") {
     var seq=msg.Number("seq");var data=msg.Text("data");
     if(seq>terminalSeq || type=="replay"&&terminalSeq==0 && data.Length>0) {
      terminalBuffer.Feed(Convert.FromBase64String(data));terminalSeq=Math.Max(terminalSeq,seq);
      RenderTerminal();
     }
    }else if(type=="exit"){TerminalStatus.Text="已退出 · "+msg.Number("exitCode");}
    else if(type=="error")StatusText.Text=msg.Text("message","终端错误");
    return Task.CompletedTask;
   }),()=>UiAsync(async()=>{if(terminalId==id)await terminalSocket.SendAsync(new JObject {["type"]="resize",["cols"]=terminalBuffer.Columns,["rows"]=terminalBuffer.Rows});}),status=> {
    var ignored=UiAsync(()=>{if(terminalId==id)TerminalStatus.Text=status;return Task.CompletedTask;});
   });terminalSocket.Start();
  }
  // Diff the buffer against the bound line collection: only lines whose stamp changed are rebuilt,
  // and the ListView virtualizes everything outside the visible window.
  void RenderTerminal() {
   while(terminalLines.Count>terminalBuffer.LineCount)terminalLines.RemoveAt(terminalLines.Count-1);
   while(terminalLines.Count<terminalBuffer.LineCount)terminalLines.Add(new TerminalLineVm());
   for(int i=0;i<terminalLines.Count;i++) {
    var line=terminalLines[i];var version=terminalBuffer.LineVersion(i);
    if(line.Built!=version)TerminalView.Build(line,terminalBuffer,i);
   }
   if(terminalAtBottom)ScrollTerminalToEnd();
  }
  ScrollViewer terminalViewer;
  void EnsureTerminalViewer() {
   if(terminalViewer!=null)return;
   terminalViewer=FindChild<ScrollViewer>(TerminalOutput);
   if(terminalViewer!=null)terminalViewer.ViewChanged+=TerminalScroll_ViewChanged;
  }
  void ScrollTerminalToEnd() {
   if(terminalLines.Count==0)return;
   EnsureTerminalViewer();
   var viewer=terminalViewer;
   if(viewer!=null && viewer.ScrollableHeight-viewer.VerticalOffset>4){terminalAtBottom=false;return;}
   terminalAtBottom=true;
   TerminalOutput.ScrollIntoView(terminalLines[terminalLines.Count-1]);
  }
  void TerminalScroll_ViewChanged(object sender,ScrollViewerViewChangedEventArgs e) {
   var viewer=sender as ScrollViewer;
   if(viewer==null)return;
   terminalAtBottom=viewer.ScrollableHeight-viewer.VerticalOffset<=24;
  }
  async Task DetachTerminalAsync(bool report) {
   var id=terminalId;var connector=terminalConnector;
   terminalId=terminalConnector=terminalRoot=null;terminalSocket?.Dispose();terminalSocket=null;
   TerminalStatus.Text="尚未连接";terminalBuffer.Reset();terminalLines.Clear();terminalSeq=0;UpdateTerminalChrome();
   if(id!=null && authenticated) {
    try {await api.RequestAsync("DELETE","/connectors/"+ApiClient.Escape(connector)+"/terminals-v2/"+ApiClient.Escape(id));}
    catch(Exception ex){if(report)throw;System.Diagnostics.Debug.WriteLine("Terminal close: "+ex.GetType().Name);}
   }
  }
  void UpdateTerminalChrome(){TerminalConnectPanel.Visibility=terminalId==null?Visibility.Visible:Visibility.Collapsed;BarCloseTerminal.IsEnabled=terminalId!=null;}
  async void CloseTerminal_Click(object sender,RoutedEventArgs e){
   if(terminalId==null)return;
   var confirm=new ContentDialog {Title="关闭终端？",Content="工作设备上的这个终端进程会被结束。",PrimaryButtonText="关闭",SecondaryButtonText="取消"};   
   if(await confirm.ShowAsync()!=ContentDialogResult.Primary)return;
   await ExecuteAsync(()=>DetachTerminalAsync(true),"终端已关闭");
  }
  async Task TerminalSendAsync(string text) {
   if(terminalSocket==null || !terminalSocket.IsConnected)throw new InvalidOperationException("请先连接终端。");
   await terminalSocket.SendAsync(new JObject {["type"]="input",["data"]=Convert.ToBase64String(Encoding.UTF8.GetBytes(text))});
  }
  async void TerminalSend_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=>{var text=TerminalInput.Text;await TerminalSendAsync(text+"\r");TerminalInput.Text="";});}
  void TerminalInput_KeyDown(object sender,KeyRoutedEventArgs e){if(e.Key==VirtualKey.Enter){e.Handled=true;TerminalSend_Click(sender,new RoutedEventArgs());}}
  async void TerminalControl_Click(object sender,RoutedEventArgs e){var key=(sender as Button)?.Tag as string;var text=key=="interrupt"?"\x03":key=="tab"?"\t":key=="escape"?"\x1b":key=="up"?"\x1b[A":"\x1b[B";await ExecuteAsync(()=>TerminalSendAsync(text));}
  // Tapping the output focuses it so a hardware keyboard types straight into the remote shell;
  // the input box below stays available for the phone soft keyboard.
  void TerminalOutput_Tapped(object sender,TappedRoutedEventArgs e){TerminalOutput.Focus(FocusState.Programmatic);}
  async void Terminal_KeyDown(object sender,KeyRoutedEventArgs e) {
   if(terminalSocket==null||!terminalSocket.IsConnected)return;
   var ctrl=(Window.Current.CoreWindow.GetKeyState(VirtualKey.Control)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0;
   var shift=(Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0;
   if(ctrl&&e.Key==VirtualKey.C)return; // Preserve copy on the selectable output surface.
   string text=null;
   switch(e.Key){
    case VirtualKey.Up:text="\x1b[A";break;case VirtualKey.Down:text="\x1b[B";break;
    case VirtualKey.Left:text="\x1b[D";break;case VirtualKey.Right:text="\x1b[C";break;
    case VirtualKey.Escape:text="\x1b";break;case VirtualKey.Enter:text="\r";break;
    case VirtualKey.Back:text="\x7f";break;case VirtualKey.Tab:text="\t";break;
    case VirtualKey.Home:text="\x1b[H";break;case VirtualKey.End:text="\x1b[F";break;
    case VirtualKey.Delete:text="\x1b[3~";break;case VirtualKey.PageUp:text="\x1b[5~";break;case VirtualKey.PageDown:text="\x1b[6~";break;
   }
   if(text==null&&!ctrl)text=KeyToText(e.Key,shift);
   if(ctrl&&e.Key>=VirtualKey.A&&e.Key<=VirtualKey.Z)text=""+(char)('\x01'+(e.Key-VirtualKey.A));
   if(text!=null){e.Handled=true;await ExecuteAsync(()=>TerminalSendAsync(text));}
  }
  // Printable keys become terminal input; shifted punctuation maps to its upper symbol.
  static string KeyToText(VirtualKey key,bool shift) {
   if(key>=VirtualKey.A&&key<=VirtualKey.Z){var letter=(char)('a'+(key-VirtualKey.A));return shift?letter.ToString().ToUpperInvariant():letter.ToString();}
   if(key>=VirtualKey.Number0&&key<=VirtualKey.Number9)return shift?")!@#$%^&*("[key-VirtualKey.Number0].ToString():(key-VirtualKey.Number0).ToString();
   if(key>=VirtualKey.NumberPad0&&key<=VirtualKey.NumberPad9)return (key-VirtualKey.NumberPad0).ToString();
   switch(key){
    case VirtualKey.Space:return " ";
    case VirtualKey.Subtract:return shift?"_":"-";
    case VirtualKey.Add:return "+";
    case VirtualKey.Multiply:return "*";
    case VirtualKey.Divide:return "/";
    case VirtualKey.Decimal:return ".";
   }
   return null;
  }
  async void Terminal_SizeChanged(object sender,SizeChangedEventArgs e) {
   if(!ready)return;
   var columns=(int)((e.NewSize.Width-16)/TerminalCellWidth);
   var rows=(int)((e.NewSize.Height-8)/TerminalLineHeight);
   if(columns==terminalBuffer.Columns&&rows==terminalBuffer.Rows)return;
   terminalBuffer.Resize(columns,rows);
   RenderTerminal();
   if(terminalSocket?.IsConnected==true)await ExecuteAsync(()=>terminalSocket.SendAsync(new JObject {["type"]="resize",["cols"]=terminalBuffer.Columns,["rows"]=terminalBuffer.Rows}));
  }
 }
}
