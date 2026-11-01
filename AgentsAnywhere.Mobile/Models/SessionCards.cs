using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace AgentsAnywhere.Mobile.Models
{
 public enum SessionTone { Idle, Running, Attention, Failed }

 public sealed class SessionCard {
  public Entity Entity { get; set; }
  public string Name { get { return Entity.Name; } }
  public string Detail { get; set; } = "";
  public string Source { get; set; } = "";
  public string Time { get; set; } = "";
  public SessionTone Tone { get; set; }
  public bool IsRunning { get { return Tone == SessionTone.Running; } }
 }

 public sealed class SessionGroup : List<SessionCard> {
  public string Key { get; set; }
  public bool Attention { get; set; }
  public SessionTone Tone { get { return Attention ? SessionTone.Attention : SessionTone.Idle; } }
  public SessionGroup(string key, IEnumerable<SessionCard> cards) : base(cards) { Key = key; }
 }

 public sealed class DeviceCard {
  public Entity Entity { get; set; }
  public string Name { get { return Entity.Name; } }
  public string Detail { get; set; } = "";
  public double Opacity { get { return Entity.Online ? 1 : 0.45; } }
  public static DeviceCard From(Entity device, int sessions) {
   var parts = new[] { device.Data.Text("deviceOs"), device.Online ? "在线" : "离线", sessions > 0 ? sessions + " 个会话" : "" };
   return new DeviceCard { Entity = device, Detail = String.Join(" · ", parts.Where(x => x.Length > 0)) };
  }
 }

 public sealed class AttachmentChip {
  public Newtonsoft.Json.Linq.JObject Data { get; set; }
  public string Name { get { return Data.Text("name", "附件"); } }
 }

 public static class SessionCards {
  public static SessionTone Tone(Entity s) {
   var d = s.Data;
   if (s.Status == "waiting_approval" || s.Status == "blocked" || d.Number("openNoticeCount") > 0 || d.Number("pendingNoticeCount") > 0 || d.Flag("needsAttention")) return SessionTone.Attention;
   if (s.Status == "running" || s.Status == "pending" || s.Status == "stopping") return SessionTone.Running;
   if (s.Status == "error" || s.Status == "failed") return SessionTone.Failed;
   return SessionTone.Idle;
  }
  static string ToneText(SessionTone tone, Entity s) {
   switch (tone) {
    case SessionTone.Attention: return "等待你处理";
    case SessionTone.Running: return "运行中";
    case SessionTone.Failed: return "出错" + (s.Data.Text("statusReason").Length > 0 ? "：" + s.Data.Text("statusReason") : "");
    default: return "";
   }
  }
  static DateTimeOffset? When(Entity s) {
   DateTimeOffset value;
   return DateTimeOffset.TryParse(s.Data.Text("sortAt", s.Data.Text("updatedAt", s.Data.Text("createdAt"))), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value) ? value.ToLocalTime() : (DateTimeOffset?)null;
  }
  public static SessionCard Card(Entity s, IEnumerable<Entity> devices) {
   var tone = Tone(s);
   var device = devices.FirstOrDefault(x => x.Id == s.ConnectorId);
   var deviceText = device == null ? "" : device.Name + (device.Online ? "" : "（离线）");
   var when = When(s);
   var now = DateTimeOffset.Now;
   var time = when == null ? "" : when.Value.Date == now.Date ? when.Value.ToString("HH:mm") : when.Value.Date == now.Date.AddDays(-1) ? "昨天" : when.Value.ToString("M月d日");
   var preview = s.Data.Text("lastMessagePreview", s.Data.Text("preview", s.Data.Text("summary")));
   var status = ToneText(tone, s);
   return new SessionCard {
    Entity = s, Tone = tone, Time = time,
    Detail = preview.Length > 0 ? preview : s.Workspace,
    Source = String.Join(" · ", new[] { status, s.Data.Text("runtimeName", s.Data.Text("runtime")), deviceText }.Where(x => x.Length > 0))
   };
  }
  public static List<SessionGroup> Group(IEnumerable<Entity> sessions, IEnumerable<Entity> devices) {
   var deviceList = devices.ToList();
   var cards = sessions.Select(x => new { Card = Card(x, deviceList), When = When(x) }).ToList();
   var today = DateTimeOffset.Now.Date;
   var groups = new List<SessionGroup>();
   var attention = cards.Where(x => x.Card.Tone == SessionTone.Attention).Select(x => x.Card).ToList();
   if (attention.Count > 0) groups.Add(new SessionGroup("需要你处理", attention) { Attention = true });
   var rest = cards.Where(x => x.Card.Tone != SessionTone.Attention).ToList();
   var recent = rest.Where(x => x.When != null && x.When.Value.Date == today).Select(x => x.Card).ToList();
   if (recent.Count > 0) groups.Add(new SessionGroup("今天", recent));
   var older = rest.Where(x => x.When == null || x.When.Value.Date != today).Select(x => x.Card).ToList();
   if (older.Count > 0) groups.Add(new SessionGroup("更早", older));
   return groups;
  }
 }
}
