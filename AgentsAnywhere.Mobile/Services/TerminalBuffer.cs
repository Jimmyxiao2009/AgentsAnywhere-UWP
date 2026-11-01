using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace AgentsAnywhere.Mobile.Services
{
 /// <summary>One styled run of a terminal line. Colour codes: -1 default, 0..255 ANSI, >=0x1000000 packed RGB.</summary>
 public sealed class TerminalRun {
  public string Text = "";
  public int Foreground = -1;
  public int Background = -1;
  public byte Flags;
  public const byte Bold = 1, Dim = 2, Italic = 4, Underline = 8, Inverse = 16;
 }
 public sealed class TerminalBuffer{
  const int MaxLines = 700;
  const int MaxColumns = 250;
  struct Cell { public char Ch; public int Fg; public int Bg; public byte Flags; }
  readonly List<List<Cell>> lines = new List<List<Cell>>();
  List<List<Cell>> alternate;
  readonly Decoder decoder = new UTF8Encoding(false, false).GetDecoder();
  readonly StringBuilder escape = new StringBuilder();
  readonly List<long> stamps = new List<long>();
  int row, col, savedRow, savedCol, mode;
  bool oscEsc, wrapPending;
  int fg = -1, bg = -1;
  byte flags;
  bool cursorVisible = true;
  long structure, cursorVersion;
  public int Columns { get; private set; } = 100;
  public int Rows { get; private set; } = 28;
  public int CursorRow { get { return row; } }
  public int CursorCol { get { return col; } }
  public bool CursorVisible { get { return cursorVisible; } }
  public bool UsingAlternateScreen { get { return alternate != null; } }
  public int LineCount { get { return Screen.Count; } }
  public string Text {
   get {
    var parts = Screen.Select(LineText).ToList();
    while (parts.Count > 0 && parts[parts.Count - 1].Length == 0) parts.RemoveAt(parts.Count - 1);
    return String.Join("\n", parts);
   }
  }
  static string LineText(List<Cell> line) {
   var builder = new StringBuilder(line.Count);
   foreach (var cell in line) builder.Append(cell.Ch);
   return builder.ToString().TrimEnd();
  }
  public void Resize(int columns, int rows) { Columns = Math.Max(20, Math.Min(MaxColumns, columns)); Rows = Math.Max(10, Math.Min(100, rows)); structure++; }
  public void Reset() {
   lines.Clear(); lines.Add(NewLine());
   alternate = null;
   row = col = savedRow = savedCol = mode = 0;
   fg = -1; bg = -1; flags = 0; wrapPending = false; oscEsc = false; cursorVisible = true;
   decoder.Reset(); escape.Clear(); stamps.Clear();
   structure++;
  }
  static List<Cell> NewLine() { return new List<Cell>(); }
  List<List<Cell>> Screen { get { return alternate ?? lines; } }
  int Top { get { return Math.Max(0, Screen.Count - Rows); } }
  void EnsureLine() {
   var screen = Screen;
   while (screen.Count <= row) { screen.Add(NewLine()); structure++; }
  }
  void Touch(int index) {
   // Alternate-screen lines are rebuilt together, so only the global stamp matters there.
   if (alternate != null) { structure++; return; }
   while (stamps.Count <= index) stamps.Add(0);
   stamps[index] = ++structure;
  }
  public void Feed(byte[] data) {
   var chars = new char[Encoding.UTF8.GetMaxCharCount(data.Length)];
   var count = decoder.GetChars(data, 0, data.Length, chars, 0, false);
   for (int i = 0; i < count; i++) Consume(chars[i]);
   if (alternate == null && lines.Count > MaxLines) {
    var cut = lines.Count - MaxLines;
    lines.RemoveRange(0, cut);
    stamps.RemoveRange(0, cut);
    row = Math.Max(0, row - cut);
    structure++;
   }
   cursorVersion++;
  }
  void Consume(char ch) {
   if (mode == 3) { if (ch == '\a' || oscEsc && ch == '\\') { mode = 0; oscEsc = false; } else oscEsc = ch == '\x1b'; return; }
   if (mode == 1) {
    if (ch == '[') { mode = 2; escape.Clear(); return; }
    if (ch == ']') { mode = 3; oscEsc = false; return; }
    if (ch == '(' || ch == ')' || ch == '*' || ch == '+') { mode = 4; return; }
    if (ch == '7') { savedRow = row; savedCol = col; }
    if (ch == '8') { row = savedRow; col = savedCol; EnsureLine(); }
    if (ch == 'M') ReverseIndex();
    if (ch == 'D') Index();
    if (ch == 'E') { Index(); col = 0; }
    if (ch == 'c') Reset();
    mode = 0; return;
   }
   if (mode == 4) { mode = 0; return; } // Charset designation: this buffer is UTF-8 only.
   if (mode == 2) {
    if (ch >= '@' && ch <= '~') { Csi(ch, escape.ToString()); mode = 0; }
    else if (escape.Length < 100) escape.Append(ch); else { mode = 0; escape.Clear(); }
    return;
   }
   if (ch == '\x1b') { mode = 1; return; }
   if (ch == '\r') { col = 0; wrapPending = false; return; }
   if (ch == '\n') { Index(); wrapPending = false; return; }
   if (ch == '\b') { if (wrapPending) wrapPending = false; else col = Math.Max(0, col - 1); return; }
   if (ch == '\t') { col = Math.Min(Columns - 1, (col / 8 + 1) * 8); wrapPending = false; return; }
   if (ch < ' ' || ch == '\x7f') return;
   if (wrapPending || col >= Columns) { Index(); col = 0; wrapPending = false; }
   EnsureLine();
   var line = Screen[row];
   while (line.Count <= col) line.Add(new Cell { Ch = ' ', Fg = -1, Bg = bg, Flags = 0 });
   line[col] = new Cell { Ch = ch, Fg = fg, Bg = bg, Flags = flags };
   Touch(row);
   col++;
   if (col >= Columns) { col = Columns - 1; wrapPending = true; }
  }
  Cell Blank() { return new Cell { Ch = ' ', Fg = -1, Bg = bg, Flags = 0 }; }
  void Index() {
   var top = Top;
   if (row >= top + Rows - 1) {
    EnsureLine();
    Screen.RemoveAt(top);
    Screen.Add(NewLine());
    structure++;
   } else { row++; EnsureLine(); }
  }
  void ReverseIndex() {
   var top = Top;
   EnsureLine();
   if (row <= top) {
    Screen.RemoveAt(Screen.Count - 1);
    Screen.Insert(top, NewLine());
    structure++;
   } else row--;
   EnsureLine();
  }
  void Csi(char command, string args) {
   if (args.StartsWith("?")) { PrivateMode(args, command); return; }
   var p = Parse(args);
   var n = p.Length > 0 ? p[0] : 0;
   var step = n == 0 ? 1 : n;
   var top = Top;
   EnsureLine();
   switch (command) {
    case 'A': row = Math.Max(top, row - step); break;
    case 'B': row = Math.Min(top + Rows - 1, row + step); break;
    case 'C': col = Math.Min(Columns - 1, col + step); break;
    case 'D': col = Math.Max(0, col - step); break;
    case 'E': row = Math.Min(top + Rows - 1, row + step); col = 0; break;
    case 'F': row = Math.Max(top, row - step); col = 0; break;
    case 'G': col = Math.Min(Columns - 1, Math.Max(0, step - 1)); break;
    case 'd': row = Math.Min(top + Rows - 1, top + Math.Max(0, step - 1)); break;
    case 'H': case 'f': row = top + Math.Min(Rows - 1, Math.Max(0, step - 1)); col = p.Length > 1 ? Math.Min(Columns - 1, Math.Max(0, p[1] - 1)) : 0; break;
    case 'J': EraseDisplay(n); break;
    case 'K': EraseLine(n); break;
    case 'L': InsertLines(step); break;
    case 'M': DeleteLines(step); break;
    case '@': InsertChars(step); break;
    case 'P': DeleteChars(step); break;
    case 'X': EraseChars(step); break;
    case 'S': ScrollUp(step); break;
    case 'T': ScrollDown(step); break;
    case 'm': Sgr(p); break;
    case 's': savedRow = row; savedCol = col; break;
    case 'u': row = savedRow; col = savedCol; EnsureLine(); break;
   }
   wrapPending = false;
   EnsureLine();
   cursorVersion++;
  }
  void PrivateMode(string args, char command) {
   if (command != 'h' && command != 'l') return;
   var enable = command == 'h';
   foreach (var raw in args.Substring(1).Split(';')) {
    int value;
    if (!int.TryParse(raw, out value)) continue;
    if (value == 25) { cursorVisible = enable; cursorVersion++; }
    if (value == 1049 || value == 1047 || value == 47) {
     if (enable) {
      if (alternate == null) {
       savedRow = row; savedCol = col;
       alternate = new List<List<Cell>>();
       for (int i = 0; i < Rows; i++) alternate.Add(NewLine());
       row = col = 0;
       structure++;
      }
     } else if (alternate != null) {
      alternate = null;
      row = savedRow; col = savedCol;
      structure++;
      EnsureLine();
     }
    }
   }
  }
  void ScrollUp(int count) {
   var top = Top;
   EnsureLine();
   for (int i = 0; i < count; i++) {
    if (Screen.Count > top + 1) Screen.RemoveAt(top); else Screen.RemoveAt(Screen.Count - 1);
    Screen.Add(NewLine());
   }
   structure++;
   EnsureLine();
  }
  void ScrollDown(int count) {
   var top = Top;
   EnsureLine();
   for (int i = 0; i < count; i++) {
    Screen.RemoveAt(Screen.Count - 1);
    Screen.Insert(top, NewLine());
   }
   structure++;
   EnsureLine();
  }
  void InsertLines(int count) {
   var top = Top;
   if (row < top) return;
   EnsureLine();
   for (int i = 0; i < count; i++) {
    if (Screen.Count > top + Rows) Screen.RemoveAt(Screen.Count - 1);
    Screen.Insert(row, NewLine());
   }
   col = 0;
   structure++;
   EnsureLine();
  }
  void DeleteLines(int count) {
   var top = Top;
   if (row < top) return;
   EnsureLine();
   for (int i = 0; i < count && Screen.Count > row; i++) {
    Screen.RemoveAt(row);
    Screen.Add(NewLine());
   }
   col = 0;
   structure++;
   EnsureLine();
  }
  void InsertChars(int count) {
   var line = Screen[row];
   while (line.Count < col) line.Add(Blank());
   for (int i = 0; i < count && line.Count < Columns; i++) line.Insert(Math.Min(col, line.Count), Blank());
   while (line.Count > Columns) line.RemoveAt(line.Count - 1);
   Touch(row);
  }
  void DeleteChars(int count) {
   var line = Screen[row];
   for (int i = 0; i < count && col < line.Count; i++) line.RemoveAt(col);
   while (line.Count < Columns) line.Add(Blank());
   Touch(row);
  }
  void EraseChars(int count) {
   var line = Screen[row];
   for (int i = col; i < Math.Min(Columns, col + count); i++) { while (line.Count <= i) line.Add(Blank()); line[i] = Blank(); }
   Touch(row);
  }
  void EraseDisplay(int n) {
   if (n == 2 || n == 3) {
    Screen.Clear();
    for (int i = 0; i < Rows; i++) Screen.Add(NewLine());
    row = col = 0;
    structure++;
    return;
   }
   EnsureLine();
   if (n == 0) {
    EraseLine(0);
    var screen = Screen;
    if (screen.Count > row + 1) { screen.RemoveRange(row + 1, screen.Count - row - 1); structure++; }
   } else if (n == 1) {
    EraseLine(1);
    var screen = Screen;
    for (int i = 0; i < row && i < screen.Count; i++) { screen[i] = NewLine(); Touch(i); }
   }
  }
  void EraseLine(int n) {
   EnsureLine();
   var line = Screen[row];
   if (n == 2) { line.Clear(); Touch(row); return; }
   if (n == 0) { if (line.Count > col) { line.RemoveRange(col, line.Count - col); Touch(row); } return; }
   if (n == 1) { for (int i = 0; i <= col && i < line.Count; i++) line[i] = Blank(); Touch(row); }
  }
  static int[] Parse(string args) {
   if (String.IsNullOrEmpty(args)) return new int[0];
   var parts = args.Split(';');
   var values = new int[parts.Length];
   for (int i = 0; i < parts.Length; i++) {
    int parsed;
    values[i] = int.TryParse(parts[i], out parsed) ? Math.Max(0, Math.Min(10000, parsed)) : 0;
   }
   return values;
  }
  void Sgr(int[] p) {
   if (p.Length == 0) { fg = -1; bg = -1; flags = 0; return; }
   for (int i = 0; i < p.Length; i++) {
    var code = p[i];
    if (code == 0) { fg = -1; bg = -1; flags = 0; }
    else if (code == 1) flags |= TerminalRun.Bold;
    else if (code == 2) flags |= TerminalRun.Dim;
    else if (code == 3) flags |= TerminalRun.Italic;
    else if (code == 4) flags |= TerminalRun.Underline;
    else if (code == 7) flags |= TerminalRun.Inverse;
    else if (code == 22) flags = (byte)(flags & ~(TerminalRun.Bold | TerminalRun.Dim));
    else if (code == 23) flags = (byte)(flags & ~TerminalRun.Italic);
    else if (code == 24) flags = (byte)(flags & ~TerminalRun.Underline);
    else if (code == 27) flags = (byte)(flags & ~TerminalRun.Inverse);
    else if (code >= 30 && code <= 37) fg = code - 30;
    else if (code >= 40 && code <= 47) bg = code - 40;
    else if (code >= 90 && code <= 97) fg = code - 90 + 8;
    else if (code >= 100 && code <= 107) bg = code - 100 + 8;
    else if (code == 39) fg = -1;
    else if (code == 49) bg = -1;
    else if (code == 38 || code == 48) {
     int consumed;
     var colour = Extended(p, i, out consumed);
     if (colour >= 0) { if (code == 38) fg = colour; else bg = colour; }
     i += consumed;
    }
   }
  }
  // Handles 5;n (ANSI 256) and 2;r;g;b (truecolour) after a 38/48 introducer. Returns -1 when unsupported.
  static int Extended(int[] p, int index, out int consumed) {
   consumed = 0;
   if (index + 1 >= p.Length) return -1;
   if (p[index + 1] == 5) {
    if (index + 2 >= p.Length) return -1;
    consumed = 2;
    return Math.Max(0, Math.Min(255, p[index + 2]));
   }
   if (p[index + 1] == 2) {
    if (index + 4 >= p.Length) return -1;
    consumed = 4;
    var r = Math.Max(0, Math.Min(255, p[index + 2]));
    var g = Math.Max(0, Math.Min(255, p[index + 3]));
    var b = Math.Max(0, Math.Min(255, p[index + 4]));
    return 0x1000000 | (r << 16) | (g << 8) | b;
   }
   return -1;
  }
  /// <summary>Effective version of one line: its content stamp plus the caret stamp on the caret line.</summary>
  public long LineVersion(int index) {
   if (alternate != null) return structure + (index == row ? cursorVersion : 0);
   var content = index < stamps.Count ? stamps[index] : 0;
   return index == row ? content + cursorVersion : content;
  }
  /// <summary>Plain styled runs for one active-screen line; the caret is added by the view.</summary>
  public List<TerminalRun> LineRuns(int index) {
   var cells = Screen[index];
   var runs = new List<TerminalRun>();
   TerminalRun run = null;
   foreach (var cell in cells) {
    if (run == null || run.Foreground != cell.Fg || run.Background != cell.Bg || run.Flags != cell.Flags) {
     run = new TerminalRun { Foreground = cell.Fg, Background = cell.Bg, Flags = cell.Flags };
     runs.Add(run);
    }
    run.Text += cell.Ch;
   }
   return runs;
  }
 }
}