using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
namespace AgentsAnywhere.Mobile.Services
{
 public sealed class RealtimeSocket : IDisposable {
  readonly Func<CancellationToken,Task<Uri>> address;
  readonly Func<JObject,Task> receive;
  readonly Func<Task> connected;
  readonly Action<string> status;
  readonly CancellationTokenSource lifetime=new CancellationTokenSource();
  readonly SemaphoreSlim sendGate=new SemaphoreSlim(1,1),receiveGate=new SemaphoreSlim(1,1);
  MessageWebSocket socket; Task runner;
  public bool IsConnected {get;private set;}
  public RealtimeSocket(Func<CancellationToken,Task<Uri>> url,Func<JObject,Task> handler,Func<Task> onConnected,Action<string> onStatus){address=url;receive=handler;connected=onConnected;status=onStatus;}
  public void Start(){if(runner==null)runner=RunAsync();}
  async Task RunAsync(){
   int attempts=0;var ct=lifetime.Token;
   while(!ct.IsCancellationRequested){
    MessageWebSocket current=null;var closed=new TaskCompletionSource<bool>();var opened=DateTime.MinValue;string route="pending";
    try{
     status?.Invoke(attempts==0?"正在连接…":"正在重新连接…");
     var uri=await address(ct);route=uri.AbsolutePath;current=new MessageWebSocket();current.Control.MessageType=SocketMessageType.Utf8;
     current.MessageReceived+=async(s,e)=>{
      try{
       string raw;using(var reader=e.GetDataReader()){reader.UnicodeEncoding=UnicodeEncoding.Utf8;DiagnosticLog.Write("socket.message "+route+" bytes="+reader.UnconsumedBufferLength);if(reader.UnconsumedBufferLength>5*1024*1024)throw new Exception("Frame too large.");raw=reader.ReadString(reader.UnconsumedBufferLength);}
       if(ct.IsCancellationRequested)return;var obj=JObject.Parse(raw);
       await receiveGate.WaitAsync(ct);
       try{if(!ct.IsCancellationRequested&&ReferenceEquals(socket,s))await receive(obj);}finally{receiveGate.Release();}
      }catch(OperationCanceledException){}catch(Exception error){DiagnosticLog.Exception("socket.receive "+route,error);closed.TrySetResult(false);}
     };
     current.Closed+=(s,e)=>{DiagnosticLog.Write("socket.closed "+route+" code="+e.Code);closed.TrySetResult(true);};socket=current;
     await current.ConnectAsync(uri).AsTask(ct);IsConnected=true;opened=DateTime.UtcNow;
     DiagnosticLog.Write("socket.connected "+route);
     if(connected!=null)await connected();
     status?.Invoke("实时已连接");
     await closed.Task.WithCancellation(ct);
    }catch(OperationCanceledException){break;}catch(Exception e){DiagnosticLog.Exception("socket.connect",e);status?.Invoke("连接暂不可用，自动重试");}
    finally{IsConnected=false;if(ReferenceEquals(socket,current))socket=null;current?.Dispose();}
    if(opened!=DateTime.MinValue&&(DateTime.UtcNow-opened).TotalSeconds>=30)attempts=0;
    attempts=Math.Min(attempts+1,5);
    try{await Task.Delay(TimeSpan.FromSeconds(Math.Min(30,Math.Pow(2,attempts))),ct);}catch(OperationCanceledException){break;}
   }
  }
  public async Task SendAsync(JObject message){
   var ct=lifetime.Token;await sendGate.WaitAsync(ct);
   try{
    var active=socket;if(active==null||!IsConnected)throw new ApiException(409,"终端尚未连接。");
    using(var writer=new DataWriter(active.OutputStream)){writer.UnicodeEncoding=UnicodeEncoding.Utf8;writer.WriteString(message.ToString(Newtonsoft.Json.Formatting.None));await writer.StoreAsync().AsTask(ct);writer.DetachStream();}
   }finally{sendGate.Release();}
  }
  public void Dispose(){if(!lifetime.IsCancellationRequested)lifetime.Cancel();socket?.Dispose();IsConnected=false;}
 }
 internal static class CancellationTasks{
  public static async Task<T> WithCancellation<T>(this Task<T> task,CancellationToken ct){
   var canceled=new TaskCompletionSource<bool>();
   using(ct.Register(()=>canceled.TrySetResult(true))){if(task!=await Task.WhenAny(task,canceled.Task))throw new OperationCanceledException(ct);return await task;}
  }
 }
}
