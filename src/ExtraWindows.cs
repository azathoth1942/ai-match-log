using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

namespace AiMatchLog {
 public class ImportRow : INotifyPropertyChanged {
  bool include;string date="",opponent="",duration="",result="",memo="";
  public string Source {get;set;} public bool Duplicate {get;set;}
  public bool Include {get{return include;}set{include=value;Changed("Include");}}
  public string Date {get{return date;}set{date=value;Changed("Date");}}
  public string Opponent {get{return opponent;}set{opponent=value;Changed("Opponent");}}
  public string Duration {get{return duration;}set{duration=value;Changed("Duration");}}
  public string Result {get{return result;}set{result=value;Changed("Result");}}
  public string Memo {get{return memo;}set{memo=value;Changed("Memo");}}
  public string Error {
   get {DateTime dt;int sec;var errors=new List<string>();if(!DateTime.TryParseExact(Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out dt))errors.Add("날짜 확인");if(string.IsNullOrWhiteSpace(Opponent)||Opponent.Length>80)errors.Add("상대 확인");if(!Core.TryDuration(Duration,out sec))errors.Add("시간 확인");if(Result!="승"&&Result!="패"&&Result!="무")errors.Add("승패 확인");if((Memo??"").Length>2000)errors.Add("메모 2000자 이하");return string.Join(" · ",errors.ToArray());}
  }
  public string Issue {get{return Error.Length>0?Error:Duplicate?"기존 동일 기록 있음":"확인됨";}}
  public Match ToMatch() {int sec;Core.TryDuration(Duration,out sec);return new Match {Id=Guid.NewGuid().ToString(),Date=Date,Opponent=Opponent.Trim(),Seconds=sec,Result=Result,Memo=(Memo??"").Trim(),CreatedAt=DateTime.UtcNow.ToString("o")};}
  public event PropertyChangedEventHandler PropertyChanged;
  void Changed(string property) {if(PropertyChanged!=null){PropertyChanged(this,new PropertyChangedEventArgs(property));PropertyChanged(this,new PropertyChangedEventArgs("Issue"));}}
 }
 public static class TextParser {
  public const string ExportHeader="# AI 대전 기록 TXT v1";
  static readonly Regex FullDate=new Regex(@"^\s*\[?(\d{4})[-./년]\s*(\d{1,2})[-./월]\s*(\d{1,2})일?\]?",RegexOptions.Compiled);
  static readonly Regex ShortDate=new Regex(@"^\s*\[?(\d{1,2})[/월]\s*(\d{1,2})일?\]?",RegexOptions.Compiled);
  static readonly Regex Time=new Regex(@"(?<![\d:])(?:\d{1,4}:)?\d{1,4}:[0-5]\d(?![\d:])",RegexOptions.Compiled);
  static readonly Regex Result=new Regex(@"(?<![가-힣A-Za-z0-9])(무승부|승리|패배|승|패|무|win|loss|lose|draw|w|l|d)(?![가-힣A-Za-z0-9])",RegexOptions.Compiled|RegexOptions.IgnoreCase);
  static string TrimSeparators(string s) {return Regex.Replace(s,@"^[\s,;|\t\-:]+|[\s,;|\t\-:]+$","").Trim();}
  public static string Fingerprint(Match m) {return Core.Json().Serialize(new[]{m.Date,m.Opponent.Trim().ToUpperInvariant(),m.Seconds.ToString(CultureInfo.InvariantCulture),m.Result,(m.Memo??"").Trim()});}
  public static List<ImportRow> Parse(string text,DateTime defaultDate,IEnumerable<Match> existing) {
   if(text.Length>2000000)throw new InvalidDataException("텍스트는 200만 자 이하로 나누어 가져와주세요.");
   string[] lines=text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');bool exported=lines.Length>0 && lines[0].TrimStart('\uFEFF').Trim()==ExportHeader;
   var rows=new List<ImportRow>();var known=new HashSet<string>(existing.Select(Fingerprint));string currentDate=defaultDate.ToString("yyyy-MM-dd");
   foreach(string source in lines) {
    string s=source.TrimStart('\uFEFF').Trim();if(s.Length==0 || (exported && s.StartsWith("#",StringComparison.Ordinal)))continue;if(rows.Count>=20000)throw new InvalidDataException("한 번에 2만 경기 이하로 나누어 가져와주세요.");
    if(exported && source.Contains('\t')){string[] fields=source.Split('\t');if(fields.Length==4){var exact=new ImportRow{Date=currentDate,Opponent=TextExporter.Unescape(fields[0]),Duration=fields[1],Result=fields[2]=="무승부"?"무":fields[2],Memo=TextExporter.Unescape(fields[3]),Source=source};if(exact.Error.Length==0)exact.Duplicate=known.Contains(Fingerprint(exact.ToMatch()));exact.Include=exact.Error.Length==0&&!exact.Duplicate;rows.Add(exact);continue;}}
    DateTime dt;bool hadDate=false;string invalidDate=null;var full=FullDate.Match(s);
    if(full.Success) {
     string dateText=full.Groups[1].Value+"-"+full.Groups[2].Value.PadLeft(2,'0')+"-"+full.Groups[3].Value.PadLeft(2,'0');
     if(DateTime.TryParseExact(dateText,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out dt)){currentDate=dateText;s=s.Substring(full.Length);hadDate=true;}else{invalidDate=dateText;s=s.Substring(full.Length);}
    } else {var shortDate=ShortDate.Match(s);if(shortDate.Success){string dateText=defaultDate.Year+"-"+shortDate.Groups[1].Value.PadLeft(2,'0')+"-"+shortDate.Groups[2].Value.PadLeft(2,'0');if(DateTime.TryParseExact(dateText,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out dt)){currentDate=dateText;s=s.Substring(shortDate.Length);hadDate=true;}else{invalidDate=dateText;s=s.Substring(shortDate.Length);}}}
    s=TrimSeparators(s);if(hadDate && s.Length==0)continue;
    s=Regex.Replace(s,@"^\d{1,5}[.)]\s*","");string note="";int hash=s.IndexOf('#');if(hash>=0){note=s.Substring(hash+1).Trim();s=s.Substring(0,hash).Trim();}
    var row=new ImportRow {Date=invalidDate??currentDate,Source=source,Memo=note};
    var tm=Time.Match(s);if(tm.Success){row.Duration=tm.Value;s=s.Remove(tm.Index,tm.Length);}
    else {var korean=Regex.Match(s,@"(\d+)\s*분(?:\s*(\d+)\s*초)?");if(korean.Success){string formatted=korean.Groups[1].Value+":"+(korean.Groups[2].Success?korean.Groups[2].Value.PadLeft(2,'0'):"00");int n;if(Core.TryDuration(formatted,out n))row.Duration=Core.ShortTime(n);s=s.Remove(korean.Index,korean.Length);}}
    var result=Result.Match(s);if(result.Success){string val=result.Value.ToLowerInvariant();row.Result=(val=="승"||val=="승리"||val=="win"||val=="w")?"승":(val=="패"||val=="패배"||val=="loss"||val=="lose"||val=="l")?"패":"무";s=s.Remove(result.Index,result.Length);}
    row.Opponent=TrimSeparators(s);if(row.Error.Length==0)row.Duplicate=known.Contains(Fingerprint(row.ToMatch()));row.Include=row.Error.Length==0 && !row.Duplicate;rows.Add(row);
   }
   return rows;
  }
  public static string ReadFile(string path) {
   if(new FileInfo(path).Length>8*1024*1024)throw new InvalidDataException("8MB 이하 TXT 파일을 선택해주세요.");
   try {using(var reader=new StreamReader(path,new UTF8Encoding(false,true),true))return reader.ReadToEnd();}
   catch(DecoderFallbackException){return File.ReadAllText(path,Encoding.GetEncoding(949));}
  }
 }
 public static class TextExporter {
  public static string Escape(string value){return (value??"").Replace("\\","\\\\").Replace("\t","\\t").Replace("\r","\\r").Replace("\n","\\n");}
  public static string Unescape(string value){var output=new StringBuilder();for(int i=0;i<value.Length;i++){char c=value[i];if(c=='\\'&&i+1<value.Length){char next=value[++i];if(next=='n')output.Append('\n');else if(next=='r')output.Append('\r');else if(next=='t')output.Append('\t');else if(next=='\\')output.Append('\\');else{output.Append('\\');output.Append(next);}}else output.Append(c);}return output.ToString();}
  public static string Build(IEnumerable<Match> records,string scope){List<Match> matches=records.ToList();Summary total=Core.Summarize("",matches);var text=new StringBuilder();text.AppendLine(TextParser.ExportHeader);text.AppendLine("# 범위: "+Escape(scope));text.AppendLine("# 총 "+total.Count+"판 · "+total.Results+" · 승률 "+total.Rate+" · 누적 "+total.Total);text.AppendLine("# 상대 / 경기 시간 / 승패 / 메모 (탭 구분)");text.AppendLine("# 필드 안의 줄바꿈·탭·역슬래시는 \\n, \\r, \\t, \\\\로 표시됩니다.");
   foreach(var day in matches.GroupBy(m=>m.Date).OrderByDescending(g=>g.Key,StringComparer.Ordinal)){Summary sum=Core.Summarize("",day);text.AppendLine();text.AppendLine(day.Key);text.AppendLine("# "+sum.Count+"판 · "+sum.Results+" · 승률 "+sum.Rate+" · 누적 "+sum.Total);foreach(Match match in day.OrderBy(m=>m.CreatedAt,StringComparer.Ordinal).ThenBy(m=>m.Id,StringComparer.Ordinal))text.AppendLine(Escape(match.Opponent)+"\t"+Core.ShortTime(match.Seconds)+"\t"+(match.Result=="무"?"무승부":match.Result)+"\t"+Escape(match.Memo));}
   return text.ToString();
  }
  public static void Write(string path,string text){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,text,new UTF8Encoding(true));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
  public static void Save(Window owner,IEnumerable<Match> records,string scope,string databasePath,Action<string> status){List<Match> list=records.ToList();if(list.Count==0){MessageBox.Show(owner,"내보낼 경기 기록이 없습니다.","메모장 내보내기");return;}SaveFileDialog dialog=new SaveFileDialog{Title="메모장으로 내보내기 · "+list.Count+"판",Filter="메모장 텍스트 (*.txt)|*.txt",DefaultExt=".txt",AddExtension=true,OverwritePrompt=true,FileName="AI대전기록_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt"};if(dialog.ShowDialog(owner)!=true)return;string target=Path.GetFullPath(dialog.FileName);if(new[]{databasePath,databasePath+".bak",databasePath+".tmp"}.Any(p=>target.Equals(Path.GetFullPath(p),StringComparison.OrdinalIgnoreCase))){MessageBox.Show(owner,"자동 저장 파일과 다른 위치를 선택해주세요.","저장 위치 확인");return;}try{Write(target,Build(list,scope));status("TXT 내보내기 완료 · "+list.Count+"판 · "+target);MessageBox.Show(owner,list.Count+"판의 기록을 TXT로 저장했습니다.\n메모장에서 열어 확인할 수 있습니다.\n\n"+target,"메모장 내보내기 완료",MessageBoxButton.OK,MessageBoxImage.Information);}catch(Exception ex){MessageBox.Show(owner,"TXT 파일을 저장하지 못했습니다.\n\n"+ex.Message,"내보내기 실패",MessageBoxButton.OK,MessageBoxImage.Error);}}
 }
 public class TextImportWindow {
  Window w;TextBox raw;DataGrid grid;TextBlock status;DatePicker defaultDate;List<ImportRow> rows=new List<ImportRow>();List<Match> existing;Func<List<Match>,bool> commit;
  T Find<T>(string name)where T:class{return w.FindName(name) as T;}
  public TextImportWindow(Window owner,IEnumerable<Match> records,Func<List<Match>,bool> saver) {
   existing=records.ToList();commit=saver;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("TextImport.xaml"))w=(Window)XamlReader.Load(stream);if(owner!=null)w.Owner=owner;
   raw=Find<TextBox>("RawText");grid=Find<DataGrid>("PreviewGrid");status=Find<TextBlock>("ParseStatus");defaultDate=Find<DatePicker>("DefaultDate");defaultDate.SelectedDate=DateTime.Today;
   Find<DataGridComboBoxColumn>("ImportResultColumn").ItemsSource=new[]{"승","패","무"};
   raw.MaxLength=2000000;Find<Button>("ParseBtn").Click+=(s,e)=>Parse();Find<Button>("OpenFile").Click+=(s,e)=>Open();Find<Button>("CancelBtn").Click+=(s,e)=>w.Close();Find<Button>("ImportBtn").Click+=(s,e)=>Save();
   raw.TextChanged+=(s,e)=>{if(rows.Count>0){grid.ItemsSource=null;rows.Clear();status.Text="내용이 바뀌었습니다. 아래 표로 변환을 다시 눌러주세요.";}};
   defaultDate.SelectedDateChanged+=(s,e)=>{if(rows.Count>0){grid.ItemsSource=null;rows.Clear();status.Text="기본 날짜가 바뀌었습니다. 아래 표로 변환을 다시 눌러주세요.";}};
  }
  public void Show(){w.ShowDialog();}
  public Window PreviewWindow {get{return w;}}
  public void PreparePreview(string text){raw.Text=text;Parse();}
  void Open(){OpenFileDialog d=new OpenFileDialog {Filter="메모장 텍스트 (*.txt)|*.txt|모든 파일 (*.*)|*.*",Title="경기 기록 TXT 선택"};if(d.ShowDialog(w)!=true)return;try{raw.Text=TextParser.ReadFile(d.FileName);Find<TextBlock>("ImportHint").Text="읽은 파일: "+d.FileName+" · 원본 파일은 변경하지 않습니다.";Parse();}catch(Exception ex){MessageBox.Show(w,ex.Message,"TXT 가져오기 실패");}}
  void Parse(){try{rows=TextParser.Parse(raw.Text,defaultDate.SelectedDate??DateTime.Today,existing);grid.ItemsSource=new ObservableCollection<ImportRow>(rows);int invalid=rows.Count(r=>r.Error.Length>0);int dup=rows.Count(r=>r.Duplicate);status.Text=rows.Count+"줄 · 선택 "+rows.Count(r=>r.Include)+"개 · 확인 필요 "+invalid+"개 · 기존 동일 기록 "+dup+"개";}catch(Exception ex){MessageBox.Show(w,ex.Message,"기록 변환");}}
  void Save(){grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);var selected=rows.Where(r=>r.Include).ToList();if(selected.Count==0){MessageBox.Show(w,"저장할 줄의 '추가'를 체크해주세요. 먼저 아래 표로 변환을 눌러 기록을 확인하세요.");return;}ImportRow invalid=selected.FirstOrDefault(r=>r.Error.Length>0);if(invalid!=null){grid.SelectedItem=invalid;grid.ScrollIntoView(invalid);MessageBox.Show(w,"선택한 기록을 확인해주세요: "+invalid.Error+"\n\n원문: "+invalid.Source,"입력 확인");return;}var records=selected.Select(r=>r.ToMatch()).ToList();if(commit(records))w.Close();}
 }
 public class DayCard {public string Date{get;set;}public string Label{get;set;}public string Total{get;set;}public List<MatchRow> Rows{get;set;}}
 public class OverviewLayout : INotifyPropertyChanged {
  double width=280;
  public double CardWidth {get{return width;}set{if(double.IsNaN(value)||double.IsInfinity(value))return;double next=Math.Max(200,Math.Min(520,Math.Round(value/10)*10));if(next==width)return;width=next;if(PropertyChanged!=null)PropertyChanged(this,new PropertyChangedEventArgs("CardWidth"));}}
  public event PropertyChangedEventHandler PropertyChanged;
  public static OverviewLayout Load(string path){var layout=new OverviewLayout();if(path==null||!File.Exists(path))return layout;try{double value;if(double.TryParse(File.ReadAllText(path),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!double.IsNaN(value)&&!double.IsInfinity(value)&&value>=200&&value<=520)layout.CardWidth=value;}catch(IOException){}catch(UnauthorizedAccessException){}return layout;}
  public void Save(string path){if(path==null)return;Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path,CardWidth.ToString(CultureInfo.InvariantCulture),new UTF8Encoding(false));}
 }
 public class AllRecordsWindow {
  public Window Window{get;private set;}public OverviewLayout Layout{get;private set;}Func<List<Match>> source;TextBox search;
  T Find<T>(string name)where T:class{return Window.FindName(name)as T;}
  public AllRecordsWindow(Window owner,Func<List<Match>> getRecords,string settingsPath=null){source=getRecords;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AllRecords.xaml"))Window=(Window)XamlReader.Load(stream);if(owner!=null)Window.Owner=owner;Layout=OverviewLayout.Load(settingsPath);Window.DataContext=Layout;search=Find<TextBox>("OverviewSearch");search.TextChanged+=(s,e)=>Refresh();Find<Button>("ClearSearch").Click+=(s,e)=>search.Text="";Find<Button>("CloseOverview").Click+=(s,e)=>Window.Close();Find<Button>("DefaultWidthBtn").Click+=(s,e)=>Layout.CardWidth=280;Window.Closed+=(s,e)=>{try{Layout.Save(settingsPath);}catch(Exception ex){MessageBox.Show("표 너비 설정을 저장하지 못했습니다.\n"+ex.Message,"화면 설정 저장");}};Refresh();}
  public void Refresh(){List<Match> all=source();List<Match> visible=Core.Filter(all,0,DateTime.Today,search.Text);Summary sum=Core.Summarize("",visible);Find<TextBlock>("OverviewStats").Text=sum.Count+"판    "+sum.Results+"    승률 "+sum.Rate+"    "+sum.Total;Find<TextBlock>("OverviewRange").Text=string.IsNullOrWhiteSpace(search.Text)?"전체 기간 · 모든 경기 기록":"전체 기간 · 검색 결과";
   var rounds=new Dictionary<string,int>();foreach(var group in all.GroupBy(m=>m.Date)){int n=0;foreach(Match m in group.OrderBy(x=>x.CreatedAt,StringComparer.Ordinal).ThenBy(x=>x.Id,StringComparer.Ordinal))rounds[m.Id]=++n;}
   List<MatchRow> records=visible.OrderByDescending(x=>x.Date).ThenBy(x=>rounds[x.Id]).Select(m=>new MatchRow{Id=m.Id,Date=m.Date,Round=rounds[m.Id],Opponent=m.Opponent,Duration=Core.ShortTime(m.Seconds),Result=m.Result=="무"?"무승부":m.Result,Memo=m.Memo}).ToList();Find<DataGrid>("AllGrid").ItemsSource=records;
   Find<ItemsControl>("DayCards").ItemsSource=visible.GroupBy(m=>m.Date).OrderByDescending(g=>g.Key).Select(g=>{Summary summary=Core.Summarize("",g);return new DayCard{Date=g.Key,Label=summary.Count+"판 · "+summary.Results+" · "+summary.Rate,Total=summary.Total,Rows=records.Where(r=>r.Date==g.Key).ToList()};}).ToList();Find<TextBlock>("OverviewEmpty").Visibility=records.Count==0?Visibility.Visible:Visibility.Collapsed;
  }
  public void Show(){Window.Show();}
 }
}
