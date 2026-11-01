using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Text;
using Windows.UI.Xaml.Media;
using AgentsAnywhere.Mobile.Services;
namespace AgentsAnywhere.Mobile.Ui
{
 public sealed class TerminalRunVm {
  public string Text { get; set; }
  public Brush Foreground { get; set; }
  public Brush Background { get; set; }
  public FontWeight Weight { get; set; }
  public FontStyle Style { get; set; }
 }
 public sealed class TerminalLineVm {
  public int Index { get; set; }
  public long Built { get; set; }
  public ObservableCollection<TerminalRunVm> Runs { get; } = new ObservableCollection<TerminalRunVm>();
 }
 /// <summary>ANSI/256/truecolour to brush mapping with the app's dark terminal palette.</summary>
 public static class TerminalPalette {
  public static readonly Brush DefaultForeground = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC));
  public static readonly Brush DefaultBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x0C, 0x0C, 0x0C));
  public static readonly Brush Cursor = new SolidColorBrush(Color.FromArgb(0xFF, 0x76, 0xB9, 0xED));
  static readonly Dictionary<int, Brush> cache = new Dictionary<int, Brush>();
  static readonly Color[] Ansi = {
   Color.FromArgb(0xFF,0x1E,0x1E,0x1E), Color.FromArgb(0xFF,0xCD,0x31,0x31), Color.FromArgb(0xFF,0x0D,0xBC,0x79), Color.FromArgb(0xFF,0xE5,0xE5,0x10),
   Color.FromArgb(0xFF,0x24,0x72,0xC8), Color.FromArgb(0xFF,0xBC,0x3F,0xBC), Color.FromArgb(0xFF,0x11,0xA8,0xCD), Color.FromArgb(0xFF,0xE5,0xE5,0xE5),
   Color.FromArgb(0xFF,0x66,0x66,0x66), Color.FromArgb(0xFF,0xF1,0x4C,0x4C), Color.FromArgb(0xFF,0x23,0xD1,0x8B), Color.FromArgb(0xFF,0xF5,0xF5,0x43),
   Color.FromArgb(0xFF,0x3B,0x8E,0xEA), Color.FromArgb(0xFF,0xD6,0x70,0xD6), Color.FromArgb(0xFF,0x29,0xB8,0xDB), Color.FromArgb(0xFF,0xFF,0xFF,0xFF)
  };
  public static Brush Brush(int code) {
   Brush brush;
   if (cache.TryGetValue(code, out brush)) return brush;
   Color colour;
   if (code < 16) colour = Ansi[Math.Max(0, Math.Min(15, code))];
   else if (code < 232) {
    var level = code - 16;
    var r = level / 36; var g = (level / 6) % 6; var b = level % 6;
    colour = Color.FromArgb(0xFF, Channel(r), Channel(g), Channel(b));
   } else if (code < 256) {
    var grey = (byte)Math.Max(0, Math.Min(255, 8 + (code - 232) * 10));
    colour = Color.FromArgb(0xFF, grey, grey, grey);
   } else colour = Color.FromArgb(0xFF, (byte)((code >> 16) & 0xFF), (byte)((code >> 8) & 0xFF), (byte)(code & 0xFF));
   brush = new SolidColorBrush(colour);
   cache[code] = brush;
   return brush;
  }
  static byte Channel(int level) { return (byte)(level == 0 ? 0 : 55 + level * 40); }
  public static Brush Foreground(int code, byte flags) {
   if (code < 0) return DefaultForeground;
   if ((flags & TerminalRun.Bold) != 0 && code < 8) code += 8; // Bold brightens the first eight colours.
   return Brush(code);
  }
  public static Brush Background(int code) { return code < 0 ? null : Brush(code); }
 }
 /// <summary>Projects buffer lines into bindable run view-models, including the block caret.</summary>
 public static class TerminalView {
  public static void Build(TerminalLineVm line, TerminalBuffer buffer, int index) {
   line.Index = index;
   line.Runs.Clear();
   var runs = buffer.LineRuns(index);
   var cursor = buffer.CursorVisible && buffer.CursorRow == index ? buffer.CursorCol : -1;
   var position = 0;
   foreach (var run in runs) {
    var start = position;
    var end = position + run.Text.Length;
    position = end;
    if (cursor < start || cursor >= end) { Add(line, run.Text, run.Foreground, run.Background, run.Flags, false); continue; }
    var at = cursor - start;
    if (at > 0) Add(line, run.Text.Substring(0, at), run.Foreground, run.Background, run.Flags, false);
    Add(line, "" + run.Text[at], run.Foreground, run.Background, run.Flags, true);
    if (run.Text.Length > at + 1) Add(line, run.Text.Substring(at + 1), run.Foreground, run.Background, run.Flags, false);
   }
   if (cursor >= position) Add(line, " ", -1, -1, 0, true); // Caret past the end of the line.
   line.Built = buffer.LineVersion(index);
  }
  static void Add(TerminalLineVm line, string text, int fg, int bg, byte flags, bool cursor) {
   if (text.Length == 0) return;
   if ((flags & TerminalRun.Inverse) != 0) { var swap = fg; fg = bg; bg = swap; }
   Brush foreground, background;
   if (cursor) {
    foreground = bg < 0 ? TerminalPalette.DefaultBackground : TerminalPalette.Brush(bg);
    background = fg < 0 ? TerminalPalette.Cursor : TerminalPalette.Brush(fg);
   } else {
    foreground = TerminalPalette.Foreground(fg, flags);
    background = TerminalPalette.Background(bg);
   }
   line.Runs.Add(new TerminalRunVm {
    Text = text,
    Foreground = foreground,
    Background = background,
    Weight = (flags & TerminalRun.Bold) != 0 ? FontWeights.SemiBold : FontWeights.Normal,
    Style = (flags & TerminalRun.Italic) != 0 ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal
   });
  }
 }
}