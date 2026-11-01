using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AgentsAnywhere.Mobile.Models;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
namespace AgentsAnywhere.Mobile.Ui
{
 static class Res {
  public static Brush Brush(string key) { return (Brush)Application.Current.Resources[key]; }
  public static FontFamily Font(string key) { return (FontFamily)Application.Current.Resources[key]; }
 }

 /// <summary>bool / non-empty string / non-empty collection / non-null to Visible. Parameter "not" inverts.</summary>
 public sealed class VisibleIf : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, string language) {
   bool on;
   if (value is bool) on = (bool)value;
   else if (value is string) on = ((string)value).Length > 0;
   else if (value is ICollection) on = ((ICollection)value).Count > 0;
   else on = value != null;
   if ((parameter as string) == "not") on = !on;
   return on ? Visibility.Visible : Visibility.Collapsed;
  }
  public object ConvertBack(object value, Type targetType, object parameter, string language) { throw new NotSupportedException(); }
 }

 /// <summary>Visual mapping for one process-rail step. Shape carries the type: circle = reasoning, square = tool.</summary>
 public sealed class StepVisual : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, string language) {
   var s = value as ProcessStep; if (s == null) return null;
   switch (parameter as string) {
    case "bg":
     if (s.Kind == StepKind.Reasoning || s.Kind == StepKind.More) return new SolidColorBrush(Windows.UI.Colors.Transparent);
     if (s.Kind == StepKind.Note) return new SolidColorBrush(Windows.UI.Colors.Transparent);
     if (s.State == StepState.Failed || s.Kind == StepKind.Error) return Res.Brush("ErrBrush");
     if (s.Kind == StepKind.Approval) return Res.Brush("WarnBrush");
     if (s.State == StepState.Running) return Res.Brush("AccentBrush");
     return Res.Brush("LowBrush");
    case "border":
     if (s.Kind == StepKind.Reasoning) return Res.Brush(s.State == StepState.Running ? "ThinkBrush" : "ThinkBorderBrush");
     if (s.Kind == StepKind.More) return Res.Brush("FaintBrush");
     return new SolidColorBrush(Windows.UI.Colors.Transparent);
    case "corner": return new CornerRadius(s.Kind == StepKind.Reasoning ? 10 : 0);
    case "fg":
     if (s.Kind == StepKind.Reasoning) return Res.Brush("ThinkBrush");
     if (s.Kind == StepKind.Approval) return new SolidColorBrush(Windows.UI.Colors.Black);
     if (s.Kind == StepKind.More) return Res.Brush("MidBrush");
     return new SolidColorBrush(Windows.UI.Colors.White);
    case "glyph": return s.Kind == StepKind.Note ? "" : s.Glyph;
    case "dot": return s.Kind == StepKind.Note ? Visibility.Visible : Visibility.Collapsed;
    case "titlefg":
     if (s.Kind == StepKind.Reasoning) return Res.Brush(s.State == StepState.Running ? "ThinkBrush" : "MidBrush");
     if (s.Kind == StepKind.More) return Res.Brush("AccentTextBrush");
     return new SolidColorBrush(Windows.UI.Colors.White);
    case "titlefont": return s.Kind == StepKind.Tool && !s.Title.Contains(" / ") ? Res.Font("MonoFont") : new FontFamily("Segoe UI");
    case "titlesize": return s.Kind == StepKind.Reasoning ? 12.0 : 13.0;
    case "status":
     if (s.State == StepState.Failed) return "\uE711";
     if (s.State == StepState.Done && (s.Kind == StepKind.Tool || s.Kind == StepKind.Approval)) return "\uE73E";
     return "";
    case "statusfg": return Res.Brush(s.State == StepState.Failed ? "ErrTextBrush" : "OkBrush");
    case "running": return s.State == StepState.Running && s.Kind != StepKind.Note ? Visibility.Visible : Visibility.Collapsed;
    case "lines": return s.Expanded ? 0 : 2;
    case "think": return s.Kind == StepKind.Reasoning && s.Body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    case "note": return s.Kind == StepKind.Note ? Visibility.Visible : Visibility.Collapsed;
    case "head": return s.Kind == StepKind.Note ? Visibility.Collapsed : Visibility.Visible;
    case "sub": return s.Subtitle.Length > 0 && s.Kind != StepKind.Reasoning && s.Kind != StepKind.Note ? Visibility.Visible : Visibility.Collapsed;
    case "line": return s.IsLast ? Visibility.Collapsed : Visibility.Visible;
   }
   return null;
  }
  public object ConvertBack(object value, Type targetType, object parameter, string language) { throw new NotSupportedException(); }
 }

 public sealed class RowVisual : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, string language) {
   var r = value as TimelineRow; if (r == null) return null;
   switch (parameter as string) {
    case "glyph": return r.Running ? "" : r.Stopped ? "\uE7BA" : r.Failed ? "\uE783" : "\uE73E";
    case "glyphfg": return Res.Brush(r.Stopped ? "WarnBrush" : r.Failed ? "ErrTextBrush" : "OkBrush");
    case "chevron": return r.Expanded ? "\uE70E" : "\uE70D";
    case "steps": return r.Expanded ? Visibility.Visible : Visibility.Collapsed;
   }
   return null;
  }
  public object ConvertBack(object value, Type targetType, object parameter, string language) { throw new NotSupportedException(); }
 }

 public sealed class ToneVisual : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, string language) {
   var tone = value is SessionTone ? (SessionTone)value : SessionTone.Idle;
   var bar = (parameter as string) == "bar";
   if ((parameter as string) == "head") return tone == SessionTone.Attention ? Res.Brush("WarnBrush") : new SolidColorBrush(Windows.UI.Colors.White);
   switch (tone) {
    case SessionTone.Attention: return Res.Brush("WarnBrush");
    case SessionTone.Running: return Res.Brush(bar ? "AccentBrush" : "AccentTextBrush");
    case SessionTone.Failed: return Res.Brush(bar ? "ErrBrush" : "ErrTextBrush");
    default: return bar ? (Brush)new SolidColorBrush(Windows.UI.Colors.Transparent) : Res.Brush("MidBrush");
   }
  }
  public object ConvertBack(object value, Type targetType, object parameter, string language) { throw new NotSupportedException(); }
 }

 public sealed class TimelineTemplateSelector : DataTemplateSelector {
  public DataTemplate User { get; set; }
  public DataTemplate Process { get; set; }
  public DataTemplate Output { get; set; }
  public DataTemplate Marker { get; set; }
  public DataTemplate Error { get; set; }
  public DataTemplate Notice { get; set; }
  protected override DataTemplate SelectTemplateCore(object item) {
   var row = item as TimelineRow; if (row == null) return Marker;
   switch (row.Kind) {
    case RowKind.User: return User;
    case RowKind.Process: return Process;
    case RowKind.Output: return Output;
    case RowKind.Error: return Error;
    case RowKind.Notice: return Notice;
    default: return Marker;
   }
  }
  protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) { return SelectTemplateCore(item); }
 }

 /// <summary>Small Markdown renderer for agent output: paragraphs, headings, quotes, lists, tasks, tables, rules, fenced code, links, bold, italic and inline code.</summary>
 public static class Markdown {
  public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Markdown), new PropertyMetadata(null, (d, e) => Render(d as StackPanel, e.NewValue as string)));
  public static string GetText(DependencyObject d) { return (string)d.GetValue(TextProperty); }
  public static void SetText(DependencyObject d, string value) { d.SetValue(TextProperty, value); }

  static readonly Regex ListItem = new Regex(@"^\s*([-*+]|\d+[.)])\s+(.*)$");
  static readonly Regex TaskItem = new Regex(@"^\[([ xX])\]\s+(.*)$");
  static readonly Regex Inline = new Regex(@"(`[^`\n]+`|\*\*[^*\n]+\*\*|\*[^*\n]+\*|\[[^\]\n]+\]\([^)\n]+\))");

  static void Render(StackPanel panel, string text) {
   if (panel == null) return;
   panel.Children.Clear();
   if (String.IsNullOrEmpty(text)) return;
   var lines = text.Replace("\r\n", "\n").Split('\n');
   var paragraph = new List<string>();
   Action flush = () => { if (paragraph.Count > 0) { panel.Children.Add(Para(String.Join("\n", paragraph), 15, FontWeights.Normal, null)); paragraph.Clear(); } };
   for (int i = 0; i < lines.Length; i++) {
    var line = lines[i];
    var trimmed = line.Trim();
    if (trimmed.StartsWith("```")) {
     flush();
     var lang = trimmed.Substring(3).Trim(); var code = new List<string>();
     for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```"); i++) code.Add(lines[i]);
     panel.Children.Add(Code(lang, String.Join("\n", code)));
     continue;
    }
    if (trimmed.Length == 0) { flush(); continue; }
    if (line.Contains("|") && i + 1 < lines.Length && IsTableDivider(lines[i + 1])) {
     flush();
     var rows = new List<string[]> { SplitRow(line) };
     for (i += 2; i < lines.Length && lines[i].Trim().StartsWith("|"); i++) rows.Add(SplitRow(lines[i]));
     i--;
     panel.Children.Add(Table(rows));
     continue;
    }
    if (IsRule(trimmed)) { flush(); panel.Children.Add(new Border { Height = 1, Background = Res.Brush("ChromeLowBrush"), Margin = new Thickness(0, 6, 0, 2) }); continue; }
    if (trimmed.StartsWith(">")) {
     flush();
     var quote = new List<string>();
     while (i < lines.Length && lines[i].TrimStart().StartsWith(">")) { var q = lines[i].TrimStart(); quote.Add(q.Substring(1).TrimStart()); i++; }
     i--;
     panel.Children.Add(Quote(String.Join("\n", quote)));
     continue;
    }
    if (line.StartsWith("#")) {
     int level = 0; while (level < line.Length && line[level] == '#') level++;
     if (level <= 6 && level < line.Length && line[level] == ' ') {
      flush();
      panel.Children.Add(Para(line.Substring(level + 1).Trim(), level <= 1 ? 20 : level == 2 ? 17 : 15, FontWeights.SemiBold, null));
      continue;
     }
    }
    var m = ListItem.Match(line);
    if (m.Success) {
     flush();
     var body = m.Groups[2].Value;
     var task = TaskItem.Match(body);
     if (task.Success) panel.Children.Add(Bullet(task.Groups[1].Value == " " ? "☐" : "☑", task.Groups[2].Value));
     else panel.Children.Add(Bullet(Char.IsDigit(m.Groups[1].Value[0]) ? m.Groups[1].Value : "•", body));
     continue;
    }
    paragraph.Add(line);
   }
   flush();
   for (int i = 1; i < panel.Children.Count; i++) ((FrameworkElement)panel.Children[i]).Margin = new Thickness(0, panel.Children[i] is Grid && panel.Children[i - 1] is Grid ? 2 : 8, 0, 0);
  }
  static bool IsRule(string trimmed) {
   if (trimmed.Length < 3) return false;
   foreach (var c in trimmed) if (c != '-' && c != '*' && c != '_') return false;
   return true;
  }
  static bool IsTableDivider(string line) {
   var t = line.Trim();
   if (!t.Contains("-")) return false;
   foreach (var c in t) if (c != '|' && c != '-' && c != ':' && c != ' ') return false;
   return true;
  }
  static string[] SplitRow(string line) {
   var t = line.Trim();
   if (t.StartsWith("|")) t = t.Substring(1);
   if (t.EndsWith("|")) t = t.Substring(0, t.Length - 1);
   return t.Split('|').Select(x => x.Trim()).ToArray();
  }
  static FrameworkElement Table(List<string[]> rows) {
   int cols = rows.Max(r => r.Length);
   var grid = new Grid();
   for (int c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 72 });
   for (int r = 0; r < rows.Count; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
   for (int r = 0; r < rows.Count; r++) {
    for (int c = 0; c < cols; c++) {
     var cell = Para(c < rows[r].Length ? rows[r][c] : "", 13, r == 0 ? FontWeights.SemiBold : FontWeights.Normal, null);
     cell.Margin = new Thickness(8, 5, 8, 5);
     Grid.SetRow(cell, r); Grid.SetColumn(cell, c); grid.Children.Add(cell);
    }
    if (r == 0) {
     var line = new Border { Height = 1, Background = Res.Brush("ChromeLowBrush"), VerticalAlignment = VerticalAlignment.Bottom };
     Grid.SetRow(line, 0); Grid.SetColumnSpan(line, cols); grid.Children.Add(line);
    }
   }
   var border = new Border { Child = grid, BorderBrush = Res.Brush("ChromeLowBrush"), BorderThickness = new Thickness(1) };
   return new ScrollViewer { Content = border, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Auto, VerticalScrollMode = ScrollMode.Disabled };
  }
  static Border Quote(string text) {
   return new Border { BorderBrush = Res.Brush("AccentBrush"), BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2), Child = Para(text, 14, FontWeights.Normal, Res.Brush("MidBrush")) };
  }
  static TextBlock Para(string text, double size, FontWeight weight, Brush foreground) {
   var block = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = size, FontWeight = weight, LineHeight = size * 1.55 };
   if (foreground != null) block.Foreground = foreground;
   AddInlines(block.Inlines, text);
   return block;
  }
  static Grid Bullet(string marker, string text) {
   var grid = new Grid();
   grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
   grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
   grid.Children.Add(new TextBlock { Text = marker, FontSize = 15, Foreground = Res.Brush("MidBrush") });
   var body = Para(text, 15, FontWeights.Normal, null); Grid.SetColumn(body, 1); grid.Children.Add(body);
   return grid;
  }
  static void AddInlines(InlineCollection inlines, string text) {
   foreach (var part in Inline.Split(text)) {
    if (part.Length == 0) continue;
    if (part.Length > 2 && part.StartsWith("`") && part.EndsWith("`")) inlines.Add(new Run { Text = part.Substring(1, part.Length - 2), FontFamily = Res.Font("MonoFont"), Foreground = Res.Brush("AccentTextBrush") });
    else if (part.Length > 4 && part.StartsWith("**") && part.EndsWith("**")) inlines.Add(new Run { Text = part.Substring(2, part.Length - 4), FontWeight = FontWeights.SemiBold });
    else if (part.Length > 2 && part.StartsWith("*") && part.EndsWith("*")) inlines.Add(new Run { Text = part.Substring(1, part.Length - 2), FontStyle = FontStyle.Italic });
    else if (part.StartsWith("[") && part.EndsWith(")")) {
     var close = part.IndexOf("](", StringComparison.Ordinal);
     Uri uri;
     if (close > 1 && Uri.TryCreate(part.Substring(close + 2, part.Length - close - 3), UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https")) {
      var link = new Hyperlink { NavigateUri = uri, Foreground = Res.Brush("AccentTextBrush") };
      link.Inlines.Add(new Run { Text = part.Substring(1, close - 1) });
      inlines.Add(link);
     } else inlines.Add(new Run { Text = part });
    }
    else inlines.Add(new Run { Text = part });
   }
  }
  static Border Code(string lang, string code) {
   var grid = new Grid();
   grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
   grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
   grid.Children.Add(new TextBlock { Text = lang.Length > 0 ? lang : "代码", Style = (Style)Application.Current.Resources["Caption"], Margin = new Thickness(10, 0, 48, 0), VerticalAlignment = VerticalAlignment.Center });
   var copy = new Button { Content = "\uE8C8", Style = (Style)Application.Current.Resources["IconButton"], FontSize = 13, Height = 32, Width = 40, HorizontalAlignment = HorizontalAlignment.Right };
   Windows.UI.Xaml.Automation.AutomationProperties.SetName(copy, "复制代码");
   copy.Click += (s, e) => { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(code); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); };
   grid.Children.Add(copy);
   var scroller = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Auto, VerticalScrollMode = ScrollMode.Disabled, Padding = new Thickness(10, 0, 10, 8) };
   scroller.Content = new TextBlock { Text = code, FontFamily = Res.Font("MonoFont"), FontSize = 12.5, IsTextSelectionEnabled = true, Foreground = Res.Brush("SoftTextBrush") };
   Grid.SetRow(scroller, 1); grid.Children.Add(scroller);
   return new Border { Background = Res.Brush("ChromeLowBrush"), BorderBrush = Res.Brush("AccentBrush"), BorderThickness = new Thickness(2, 0, 0, 0), Child = grid };
  }
 }
}
