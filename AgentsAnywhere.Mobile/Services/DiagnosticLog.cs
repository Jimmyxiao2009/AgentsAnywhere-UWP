using System;
using System.Threading.Tasks;
using Windows.Storage;
namespace AgentsAnywhere.Mobile.Services
{
 // Only callers' fixed diagnostic labels are accepted. Never pass payloads or exception messages.
 public static class DiagnosticLog
 {
  static readonly object gate=new object();
  static Task pending=Task.CompletedTask;
  public static void Write(string label) {
   var line=DateTime.UtcNow.ToString("o")+" "+label+"\r\n";
   lock(gate) pending=AppendAsync(pending,line);
  }
  static async Task AppendAsync(Task previous,string line) {
   try {
    await previous.ConfigureAwait(false);
    var file=await ApplicationData.Current.LocalFolder.CreateFileAsync("diagnostics.log",CreationCollisionOption.OpenIfExists);
    var properties=await file.GetBasicPropertiesAsync();
    if(properties.Size>131072) {
     var old=await FileIO.ReadTextAsync(file);var start=Math.Max(0,old.Length-32768);var newline=old.IndexOf('\n',start);
     await FileIO.WriteTextAsync(file,newline<0 ? "" : old.Substring(newline+1));
    }
    await FileIO.AppendTextAsync(file,line);
   } catch { /* Diagnostics must never break an application action. */ }
  }
  // Blocks until queued writes finish; only for crash paths where the process is about to die.
  public static void Flush() { Task task;lock(gate)task=pending;try{task.Wait();}catch{}}
  public static void Exception(string phase,Exception error) {
   Write(phase+" exception="+error.GetType().Name+" hresult=0x"+error.HResult.ToString("X8")+
    (error.InnerException==null ? "" : " inner="+error.InnerException.GetType().Name+" hresult=0x"+error.InnerException.HResult.ToString("X8")));
  }
  public static async Task<string> ReadAsync() {
   Task task;lock(gate)task=pending;await task;
   try {return await FileIO.ReadTextAsync(await ApplicationData.Current.LocalFolder.GetFileAsync("diagnostics.log"));}
   catch {return "尚无调试日志。";}
  }
 }
}
