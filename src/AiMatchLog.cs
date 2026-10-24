using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("AI 대전 기록")]
[assembly: AssemblyProduct("AI 대전 기록")]
[assembly: AssemblyDescription("수동 경기 기록, 메모장 가져오기와 TXT 내보내기, 날짜별 전체 보기")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace AiMatchLog {
 public class Match {
  public string Id {get;set;}
  public string Date {get;set;}
  public string Opponent {get;set;}
  public int Seconds {get;set;}
  public string Result {get;set;}
  public string Memo {get;set;}
  public string CreatedAt {get;set;}
 }
 public class Store { public int Version {get;set;} public List<Match> Matches {get;set;} }
 public class MatchRow {
  public string Id {get;set;} public string Date {get;set;} public int Round {get;set;}
  public string Opponent {get;set;} public string Duration {get;set;} public string Result {get;set;} public string Memo {get;set;}
 }
 public class Summary {
  public string Name {get;set;} public int Count {get;set;} public int Wins {get;set;} public int Losses {get;set;} public int Draws {get;set;} public long Seconds {get;set;}
  public string Results {get {return Wins+"승 / "+Losses+"패 / "+Draws+"무";}}
  public string Rate {get {return Wins+Losses==0 ? "—" : (100.0*Wins/(Wins+Losses)).ToString("0.0",CultureInfo.InvariantCulture)+"%";}}
  public string Total {get {return Core.LongTime(Seconds);}}
  public string Average {get {return Core.ShortTime(Count==0?0:(long)Math.Round((double)Seconds/Count));}}
 }
 public static class Core {
  public static JavaScriptSerializer Json() {return new JavaScriptSerializer {MaxJsonLength=64*1024*1024, RecursionLimit=64};}
  public static string ShortTime(long s) {return s>=3600 ? (s/3600)+":"+((s%3600)/60).ToString("00")+":"+(s%60).ToString("00") : (s/60).ToString("00")+":"+(s%60).ToString("00");}
  public static string LongTime(long s) {return (s/3600)+"시간 "+((s%3600)/60)+"분 "+(s%60)+"초";}
  public static bool TryDuration(string text, out int seconds) {
   seconds=0; if(string.IsNullOrWhiteSpace(text)) return false;
   string[] p=text.Trim().Split(':'); if(p.Length!=2 && p.Length!=3) return false;
   long[] n=new long[p.Length]; for(int i=0;i<p.Length;i++) if(!long.TryParse(p[i],NumberStyles.None,CultureInfo.InvariantCulture,out n[i])) return false;
   if(n[n.Length-1]>59 || (p.Length==3 && n[1]>59)) return false;
   // Bound before multiplication to avoid overflow from malformed input.
   if(n[0]>1000000) return false;
   long v=p.Length==2 ? n[0]*60+n[1] : n[0]*3600+n[1]*60+n[2];
   if(v<=0 || v>604800) return false; seconds=(int)v; return true;
  }
  public static Summary Summarize(string name,IEnumerable<Match> matches) {
   List<Match> m=matches.ToList(); return new Summary {Name=name,Count=m.Count,Wins=m.Count(x=>x.Result=="승"),Losses=m.Count(x=>x.Result=="패"),Draws=m.Count(x=>x.Result=="무"),Seconds=m.Sum(x=>(long)x.Seconds)};
  }
  public static Store Parse(string json) {
   Store s=Json().Deserialize<Store>(json);
   if(s==null || s.Version!=1 || s.Matches==null) throw new InvalidDataException("지원하는 기록 백업 파일이 아닙니다.");
   if(s.Matches.Count>200000) throw new InvalidDataException("불러올 수 있는 기록 수를 초과했습니다.");
   HashSet<string> ids=new HashSet<string>();
   foreach(Match m in s.Matches) {
    DateTime date,created; Guid id;
    if(m==null || !Guid.TryParse(m.Id,out id) || !ids.Add(m.Id) || !DateTime.TryParseExact(m.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date)
     || string.IsNullOrWhiteSpace(m.Opponent) || m.Opponent.Length>80 || m.Seconds<=0 || m.Seconds>604800 || (m.Result!="승" && m.Result!="패" && m.Result!="무")
     || !DateTime.TryParse(m.CreatedAt,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out created) || (m.Memo!=null && m.Memo.Length>2000))
      throw new InvalidDataException("잘못된 경기 기록이 포함되어 있습니다. 기존 기록은 변경하지 않았습니다.");
    m.Opponent=m.Opponent.Trim(); m.Memo=m.Memo??"";
   }
   return s;
  }
  public static List<Match> Filter(IEnumerable<Match> records,int mode,DateTime date,string search) {
   string day=date.ToString("yyyy-MM-dd"),month=date.ToString("yyyy-MM"); string term=(search??"").Trim();
   return records.Where(m=>(mode==0 || (mode==1?m.Date.StartsWith(month,StringComparison.Ordinal):m.Date==day)) && (term.Length==0 || m.Opponent.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0 || (m.Memo??"").IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0)).ToList();
  }
  public static void AtomicWrite(string path,Store store) {
   string folder=Path.GetDirectoryName(path); Directory.CreateDirectory(folder); string tmp=path+".tmp";
   string json=Json().Serialize(store); File.WriteAllText(tmp,json,new UTF8Encoding(false));
   if(File.Exists(path)) File.Replace(tmp,path,path+".bak",true); else File.Move(tmp,path);
  }
 }
 public class Program {
  Window window; Store store; string dataPath; string editingId; string prefilledOpponent=""; bool initializing=true; bool readOnly=false;AllRecordsWindow overview;
  Stopwatch stopwatch=new Stopwatch(); int timerBase; DispatcherTimer ticker=new DispatcherTimer();
  TextBlock status,count,results,rate,total,filterLabel,empty,editorTitle,searchHint;
  TextBox duration,memo,search; ComboBox opponent,filterMode; DatePicker matchDate,filterDate;
  RadioButton win,loss,draw; Button save,timer,reset,cancel; DataGrid matches,days,opponents;
  T Find<T>(string name) where T:class {return window.FindName(name) as T;}
  [STAThread] public static void Main(string[] args) {
   if(args.Length>0 && args[0]=="--self-test") { SelfTests.Run(args.Length>1?args[1]:"self-test.txt"); return; }
   bool created; using(var mutex=new System.Threading.Mutex(true,"Local\\AiMatchLog_6D7F924A",out created)) {
    if(!created) {MessageBox.Show("AI 대전 기록이 이미 실행 중입니다. 작업 표시줄에서 열어주세요.","AI 대전 기록");return;}
    try {new Program().Run(args.Length==2 && args[0]=="--data-dir"?args[1]:null);} catch(Exception e) {MessageBox.Show("프로그램을 시작하지 못했습니다.\n\n"+e.Message,"AI 대전 기록",MessageBoxButton.OK,MessageBoxImage.Error);}
   }
  }
  void Run(string testDataDir) {
   var app=new Application();
   app.DispatcherUnhandledException+=(s,e)=> {MessageBox.Show("작업을 완료하지 못했습니다.\n\n"+e.Exception.Message,"AI 대전 기록",MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true;};
   using(Stream resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) window=(Window)XamlReader.Load(resource);
   status=Find<TextBlock>("StatusText");count=Find<TextBlock>("CountText");results=Find<TextBlock>("ResultsText");rate=Find<TextBlock>("RateText");total=Find<TextBlock>("TimeText");filterLabel=Find<TextBlock>("FilterLabel");empty=Find<TextBlock>("EmptyText");editorTitle=Find<TextBlock>("EditorTitle");searchHint=Find<TextBlock>("SearchHint");
   duration=Find<TextBox>("DurationBox");memo=Find<TextBox>("MemoBox");search=Find<TextBox>("SearchBox");opponent=Find<ComboBox>("OpponentBox");filterMode=Find<ComboBox>("FilterMode");matchDate=Find<DatePicker>("MatchDate");filterDate=Find<DatePicker>("FilterDate");
   win=Find<RadioButton>("WinRadio");loss=Find<RadioButton>("LossRadio");draw=Find<RadioButton>("DrawRadio");save=Find<Button>("SaveBtn");timer=Find<Button>("TimerBtn");reset=Find<Button>("ResetTimerBtn");cancel=Find<Button>("CancelEditBtn");matches=Find<DataGrid>("MatchesGrid");days=Find<DataGrid>("DaysGrid");opponents=Find<DataGrid>("OpponentsGrid");
   memo.MaxLength=2000;duration.MaxLength=14;search.MaxLength=200;memo.TextChanged+=(s,e)=>{Find<TextBlock>("MemoHint").Visibility=string.IsNullOrEmpty(memo.Text)?Visibility.Visible:Visibility.Collapsed;};
   dataPath=testDataDir==null?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AiMatchLog","records.json"):Path.Combine(Path.GetFullPath(testDataDir),"records.json");
   LoadData(); matchDate.SelectedDate=DateTime.Today;filterDate.SelectedDate=DateTime.Today;filterMode.SelectedIndex=0;
   save.Click+=(s,e)=>SaveMatch();timer.Click+=(s,e)=>ToggleTimer();reset.Click+=(s,e)=>ResetTimer();cancel.Click+=(s,e)=>ClearEditor();
   Find<Button>("EditBtn").Click+=(s,e)=>EditSelected();matches.MouseDoubleClick+=(s,e)=>EditSelected();Find<Button>("DeleteBtn").Click+=(s,e)=>DeleteSelected();
   filterMode.SelectionChanged+=(s,e)=>Refresh();filterDate.SelectedDateChanged+=(s,e)=>Refresh();search.TextChanged+=(s,e)=>Refresh();
   Find<Button>("TodayBtn").Click+=(s,e)=>{filterDate.SelectedDate=DateTime.Today;filterMode.SelectedIndex=2;Refresh();};
   Find<Button>("BackupBtn").Click+=(s,e)=>Backup();Find<Button>("RestoreBtn").Click+=(s,e)=>Restore();Find<Button>("HelpBtn").Click+=(s,e)=>Help();
   Find<Button>("TextImportBtn").Click+=(s,e)=>new TextImportWindow(window,store.Matches,additions=>{var next=new List<Match>(store.Matches);next.AddRange(additions);if(!Commit(next))return false;filterMode.SelectedIndex=0;search.Text="";Refresh();Status(additions.Count+"개 메모장 기록을 추가하고 자동 저장했습니다.");return true;}).Show();
   Find<Button>("OverviewBtn").Click+=(s,e)=>{if(overview!=null && overview.Window.IsVisible){overview.Window.Activate();return;}overview=new AllRecordsWindow(window,()=>store.Matches);overview.Show();};
   Button exportButton=Find<Button>("TextExportBtn");ContextMenu exportMenu=exportButton.ContextMenu;
   exportButton.Click+=(s,e)=>{((MenuItem)exportMenu.Items[0]).Header="전체 기록 내보내기 ("+store.Matches.Count+"판)";((MenuItem)exportMenu.Items[1]).Header="현재 조회·검색 결과 내보내기 ("+VisibleMatches().Count+"판)";exportMenu.PlacementTarget=exportButton;exportMenu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;exportMenu.IsOpen=true;};
   ((MenuItem)exportMenu.Items[0]).Click+=(s,e)=>ExportText(false);((MenuItem)exportMenu.Items[1]).Click+=(s,e)=>ExportText(true);
   ticker.Interval=TimeSpan.FromMilliseconds(200);ticker.Tick+=(s,e)=>{duration.Text=Core.ShortTime(timerBase+(long)stopwatch.Elapsed.TotalSeconds);};
   window.Closing+=(s,e)=>{if(HasDraft() && MessageBox.Show(window,"입력 중인 경기 기록은 아직 저장되지 않았습니다.\n기록하지 않고 종료할까요?","종료 확인",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)e.Cancel=true;};
   initializing=false;Refresh(); if(readOnly) Status("저장 파일을 읽지 못했습니다. 원본 보호 모드입니다. 백업 불러오기로 복구할 수 있습니다.");
   app.Run(window);
  }
  void LoadData() {
   store=new Store {Version=1,Matches=new List<Match>()}; if(!File.Exists(dataPath)) return;
   try {store=Core.Parse(File.ReadAllText(dataPath,Encoding.UTF8));}
   catch(Exception ex) {
    string backup=dataPath+".bak";
    if(File.Exists(backup)) {try {Store recovered=Core.Parse(File.ReadAllText(backup,Encoding.UTF8)); if(MessageBox.Show(window,"기록 파일을 읽지 못했지만 자동 백업이 있습니다.\n직전 저장 상태로 복구할까요?\n\n"+ex.Message,"기록 복구",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes){File.Copy(dataPath,dataPath+".damaged-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"),false);File.Copy(backup,dataPath,true);store=recovered;return;}} catch(Exception recovery) {MessageBox.Show(window,"자동 백업 복구를 완료하지 못했습니다.\n"+recovery.Message,"기록 복구");}}
    readOnly=true;MessageBox.Show(window,"기록 파일을 읽을 수 없어 원본을 보호했습니다.\n백업 불러오기로 복구해주세요.\n\n"+dataPath,"기록 파일 확인",MessageBoxButton.OK,MessageBoxImage.Warning);
   }
  }
  bool Commit(List<Match> records) {
   if(readOnly) {MessageBox.Show(window,"기존 기록 파일을 읽을 수 없는 상태입니다. 백업을 불러온 후 저장해주세요.","원본 보호 중");return false;}
   Store next=new Store {Version=1,Matches=records};
   try {Core.AtomicWrite(dataPath,next);store=next;return true;} catch(Exception ex){MessageBox.Show(window,"기록을 저장하지 못했습니다. 입력 내용을 유지했습니다.\n\n"+ex.Message,"저장 실패",MessageBoxButton.OK,MessageBoxImage.Error);return false;}
  }
  void Status(string text) {status.Text=text;}
  List<Match> VisibleMatches(){return Core.Filter(store.Matches,filterMode.SelectedIndex,filterDate.SelectedDate??DateTime.Today,search.Text);}
  void ExportText(bool filtered){if(readOnly){MessageBox.Show(window,"현재 기록을 읽지 못해 내보낼 수 없습니다. 먼저 백업 불러오기로 복구해주세요.");return;}string scope=filtered?filterLabel.Text:"전체 기록";TextExporter.Save(window,filtered?VisibleMatches():store.Matches,scope,dataPath,Status);}
  bool HasDraft() {return stopwatch.IsRunning || editingId!=null || opponent.Text.Trim()!=prefilledOpponent || !string.IsNullOrWhiteSpace(memo.Text) || duration.Text!="00:00" || win.IsChecked==true || loss.IsChecked==true || draw.IsChecked==true;}
  void Refresh() {
   if(initializing)return; int mode=filterMode.SelectedIndex;DateTime d=filterDate.SelectedDate??DateTime.Today;filterDate.IsEnabled=mode!=0;
   searchHint.Visibility=string.IsNullOrEmpty(search.Text)?Visibility.Visible:Visibility.Collapsed;
   List<Match> visible=Core.Filter(store.Matches,mode,d,search.Text);Summary sum=Core.Summarize("",visible);
   count.Text=sum.Count+"판";results.Text=sum.Wins+"승  "+sum.Losses+"패  "+sum.Draws+"무";rate.Text=sum.Rate;total.Text=sum.Total;
   filterLabel.Text=mode==0?"전체 기간":mode==1?d.ToString("yyyy년 M월"):d.ToString("yyyy년 M월 d일");
   if(!string.IsNullOrWhiteSpace(search.Text))filterLabel.Text+=" · 검색 결과";
   var rounds=new Dictionary<string,int>();foreach(var group in store.Matches.GroupBy(x=>x.Date)){int i=0;foreach(Match m in group.OrderBy(x=>x.CreatedAt,StringComparer.Ordinal).ThenBy(x=>x.Id,StringComparer.Ordinal))rounds[m.Id]=++i;}
   matches.ItemsSource=visible.OrderByDescending(x=>x.Date,StringComparer.Ordinal).ThenByDescending(x=>x.CreatedAt,StringComparer.Ordinal).ThenByDescending(x=>x.Id,StringComparer.Ordinal).Select(m=>new MatchRow {Id=m.Id,Date=m.Date,Round=rounds[m.Id],Opponent=m.Opponent,Duration=Core.ShortTime(m.Seconds),Result=m.Result=="무"?"무승부":m.Result,Memo=m.Memo}).ToList();
   days.ItemsSource=visible.GroupBy(x=>x.Date).OrderByDescending(x=>x.Key).Select(g=>Core.Summarize(g.Key,g)).ToList();
   opponents.ItemsSource=visible.GroupBy(x=>x.Opponent,StringComparer.OrdinalIgnoreCase).Select(g=>Core.Summarize(g.First().Opponent,g)).OrderByDescending(x=>x.Count).ThenBy(x=>x.Name).ToList();
   empty.Visibility=visible.Count==0?Visibility.Visible:Visibility.Collapsed;
   string current=opponent.Text;opponent.ItemsSource=store.Matches.Select(x=>x.Opponent).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToList();opponent.Text=current;
   if(overview!=null && overview.Window.IsVisible)overview.Refresh();
  }
  void SaveMatch() {
   if(stopwatch.IsRunning){MessageBox.Show(window,"타이머를 정지한 뒤 기록해주세요.","타이머 실행 중");return;}
   if(!matchDate.SelectedDate.HasValue){MessageBox.Show(window,"경기 날짜를 선택해주세요.");return;}
   string name=opponent.Text.Trim();if(name.Length==0 || name.Length>80){MessageBox.Show(window,"대전 상대를 1~80자로 입력해주세요.");opponent.Focus();return;}
   int seconds;if(!Core.TryDuration(duration.Text,out seconds)){MessageBox.Show(window,"시간은 분:초로 입력해주세요.\n예: 8:35 = 8분 35초\n1시간 이상: 1:02:30\n\n0초보다 크고 7일 이하의 시간을 입력할 수 있습니다.","경기 시간 확인");duration.Focus();return;}
   string result=win.IsChecked==true?"승":loss.IsChecked==true?"패":draw.IsChecked==true?"무":null;if(result==null){MessageBox.Show(window,"내 기준으로 승리, 패배, 무승부 중 하나를 선택해주세요.","승패 선택");return;}
   Match previous=editingId==null?null:store.Matches.Find(x=>x.Id==editingId);
   Match record=new Match {Id=previous==null?Guid.NewGuid().ToString():previous.Id,Date=matchDate.SelectedDate.Value.ToString("yyyy-MM-dd"),Opponent=name,Seconds=seconds,Result=result,Memo=memo.Text.Trim(),CreatedAt=previous==null?DateTime.UtcNow.ToString("o"):previous.CreatedAt};
   List<Match> next=store.Matches.Where(x=>x.Id!=record.Id).ToList();next.Add(record);bool edited=previous!=null;
   if(!Commit(next))return;
   ClearEditor();prefilledOpponent=name;opponent.Text=name;
   // Make a record saved outside the active filter visible immediately.
   if(!Core.Filter(new[]{record},filterMode.SelectedIndex,filterDate.SelectedDate??DateTime.Today,search.Text).Any()){search.Text="";filterDate.SelectedDate=DateTime.ParseExact(record.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture);filterMode.SelectedIndex=2;}
   Refresh();Status((edited?"기록 수정 완료":"경기 기록 추가 완료")+" · "+DateTime.Now.ToString("HH:mm:ss")+" 자동 저장");
  }
  void ClearEditor() {
   editingId=null;prefilledOpponent="";stopwatch.Reset();ticker.Stop();timerBase=0;duration.Text="00:00";duration.IsEnabled=true;timer.Content="▶ 타이머 시작";opponent.Text="";memo.Text="";win.IsChecked=false;loss.IsChecked=false;draw.IsChecked=false;editorTitle.Text="새 경기 기록";save.Content="＋ 기록 추가";cancel.Visibility=Visibility.Collapsed;save.IsEnabled=true;
  }
  void ToggleTimer() {
   if(stopwatch.IsRunning){stopwatch.Stop();ticker.Stop();duration.Text=Core.ShortTime(timerBase+(long)stopwatch.Elapsed.TotalSeconds);timer.Content="▶ 타이머 계속";duration.IsEnabled=true;save.IsEnabled=true;Status("타이머 정지 · 승패를 선택하고 기록 추가를 누르세요.");}
   else {int v=0;if(duration.Text.Trim()!="00:00" && duration.Text.Trim()!="0:00" && !Core.TryDuration(duration.Text,out v)){MessageBox.Show(window,"시간을 분:초로 입력하거나 초기화를 눌러주세요.");return;}timerBase=v;stopwatch.Restart();ticker.Start();duration.IsEnabled=false;timer.Content="■ 타이머 정지";save.IsEnabled=false;Status("타이머 측정 중 · 정지 후 기록을 저장할 수 있습니다.");}
  }
  void ResetTimer() {stopwatch.Reset();ticker.Stop();timerBase=0;duration.Text="00:00";duration.IsEnabled=true;timer.Content="▶ 타이머 시작";save.IsEnabled=true;Status("시간을 초기화했습니다.");}
  Match Selected() {MatchRow row=matches.SelectedItem as MatchRow;return row==null?null:store.Matches.Find(x=>x.Id==row.Id);}
  void EditSelected() {
   Match m=Selected();if(m==null){Status("경기 기록 탭에서 수정할 행을 먼저 선택해주세요.");return;}
   if(stopwatch.IsRunning){MessageBox.Show(window,"타이머를 먼저 정지해주세요.");return;}
   if(HasDraft())if(MessageBox.Show(window,"현재 입력 중인 내용을 바꾸고 선택한 기록을 수정할까요?","기록 수정",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
   ClearEditor();editingId=m.Id;matchDate.SelectedDate=DateTime.ParseExact(m.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture);opponent.Text=m.Opponent;duration.Text=Core.ShortTime(m.Seconds);memo.Text=m.Memo;win.IsChecked=m.Result=="승";loss.IsChecked=m.Result=="패";draw.IsChecked=m.Result=="무";editorTitle.Text="선택한 경기 수정";save.Content="수정 저장";cancel.Visibility=Visibility.Visible;Status("내용을 바꾼 뒤 수정 저장을 누르세요.");
  }
  void DeleteSelected() {
   Match m=Selected();if(m==null){Status("경기 기록 탭에서 삭제할 행을 먼저 선택해주세요.");return;}
   if(MessageBox.Show(window,m.Date+" / "+m.Opponent+" / "+Core.ShortTime(m.Seconds)+"\n이 기록을 삭제할까요?","기록 삭제",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   if(Commit(store.Matches.Where(x=>x.Id!=m.Id).ToList())){if(editingId==m.Id)ClearEditor();Refresh();Status("선택한 경기 기록을 삭제했습니다.");}
  }
  void Backup() {
   if(readOnly){MessageBox.Show(window,"현재 기록을 읽지 못해 백업할 수 없습니다. 먼저 백업 불러오기로 복구해주세요.");return;}
   SaveFileDialog dialog=new SaveFileDialog {Title="대전 기록 백업",Filter="대전 기록 백업 (*.json)|*.json",FileName="AI대전기록_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".json"};
   if(dialog.ShowDialog(window)!=true)return;
   if(Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(dataPath),StringComparison.OrdinalIgnoreCase)){MessageBox.Show(window,"자동 저장 파일과 다른 위치를 선택해주세요.");return;}
   try {File.WriteAllText(dialog.FileName,Core.Json().Serialize(store),new UTF8Encoding(false));Status("백업 저장 완료 · "+dialog.FileName);}catch(Exception ex){MessageBox.Show(window,"백업 저장 실패: "+ex.Message);}
  }
  void Restore() {
   OpenFileDialog dialog=new OpenFileDialog {Title="대전 기록 백업 불러오기",Filter="대전 기록 백업 (*.json)|*.json"};if(dialog.ShowDialog(window)!=true)return;
   try {
    Store imported=Core.Parse(File.ReadAllText(dialog.FileName,Encoding.UTF8));
    var existing=new HashSet<string>(store.Matches.Select(x=>x.Id));List<Match> additions=imported.Matches.Where(x=>!existing.Contains(x.Id)).ToList();int duplicate=imported.Matches.Count-additions.Count;
    if(additions.Count==0 && !readOnly){MessageBox.Show(window,"새로 추가할 기록이 없습니다.\n동일한 기록 "+duplicate+"개는 중복으로 가져오지 않습니다.","백업 불러오기");return;}
    if(MessageBox.Show(window,"백업에서 "+additions.Count+"개 기록을 추가합니다.\n동일한 ID의 기록 "+duplicate+"개는 현재 기록을 유지합니다.\n현재 기록과 합쳐서 불러올까요?","백업 불러오기",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
    bool wasReadOnly=readOnly;
    if(wasReadOnly && File.Exists(dataPath))File.Copy(dataPath,dataPath+".damaged-"+Guid.NewGuid().ToString("N"),false);
    readOnly=false;var next=new List<Match>(store.Matches);next.AddRange(additions);if(!Commit(next)){readOnly=wasReadOnly;return;}Refresh();Status(additions.Count+"개 기록을 불러왔습니다. · 중복 "+duplicate+"개 제외");
   }catch(Exception ex){MessageBox.Show(window,"백업을 불러오지 못했습니다.\n\n"+ex.Message,"백업 확인",MessageBoxButton.OK,MessageBoxImage.Error);}
  }
  void Help() {MessageBox.Show(window,"1. 날짜와 대전 상대를 입력하세요.\n2. 시간을 분:초로 입력하세요. (예: 8:35)\n   타이머를 쓰면 시작 → 정지 후 시간을 확인하세요.\n3. 내 기준으로 승패를 선택하고 기록 추가를 누르세요.\n\n• 경기 기록을 선택해 수정하거나 삭제할 수 있습니다.\n• 날짜별 합계 / 상대별 통계도 상단 조회 조건을 따릅니다.\n• 승률은 승 ÷ (승 + 패)로 계산하며 무승부는 제외합니다.\n• 누적 시간은 기록된 경기 시간의 합계입니다. 휴식은 포함하지 않습니다.\n• 백업 불러오기는 현재 기록에 합칩니다. 같은 ID는 중복 추가하지 않습니다.\n• 다른 PC로 옮길 때는 백업 파일도 함께 옮기세요.\n\n자동 저장 위치:\n"+dataPath,"AI 대전 기록 사용 안내",MessageBoxButton.OK,MessageBoxImage.Information);}
 }
 public static class SelfTests {
  public static void Run(string report) {
   List<string> passed=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);passed.Add("PASS "+label);};
   try {
    int seconds;check(Core.TryDuration("8:35",out seconds)&&seconds==515,"분:초 입력");check(Core.TryDuration("65:02",out seconds)&&seconds==3902,"60분 이상 입력");check(Core.TryDuration("1:02:30",out seconds)&&seconds==3750,"시간:분:초 입력");
    check(!Core.TryDuration("8:60",out seconds)&&!Core.TryDuration("-1:02",out seconds)&&!Core.TryDuration("0:00",out seconds)&&!Core.TryDuration("12",out seconds)&&!Core.TryDuration("99999999999999999:00",out seconds),"잘못된 시간 거부");
    Func<string,string,int,Match> create=(date,result,sec)=>new Match {Id=Guid.NewGuid().ToString(),Date=date,Result=result,Seconds=sec,Opponent="테스트 AI",Memo="한글, 줄바꿈\n메모",CreatedAt=DateTime.UtcNow.ToString("o")};
    List<Match> list=new List<Match>{create("2026-10-24","승",515),create("2026-10-24","패",3600),create("2026-10-23","무",60),create("2026-11-01","승",10)};
    Summary s=Core.Summarize("",list);check(s.Count==4&&s.Wins==2&&s.Losses==1&&s.Draws==1&&s.Seconds==4185&&s.Rate=="66.7%","총합 및 무승부 제외 승률");check(Core.Summarize("",new[]{list[2]}).Rate=="—","무승부만 있는 승률");
    check(Core.Filter(list,1,new DateTime(2026,10,24),"").Count==3,"월 필터");check(Core.Filter(list,2,new DateTime(2026,10,24),"").Count==2,"일 필터");check(Core.Filter(list,0,DateTime.Today,"없는 상대").Count==0,"검색 필터");
    Store store=new Store {Version=1,Matches=list};string json=Core.Json().Serialize(store);check(Core.Parse(json).Matches[0].Memo==list[0].Memo,"백업 한글과 줄바꿈 보존");
    bool invalid=false;try{Core.Parse("{\"Version\":2,\"Matches\":[]}");}catch{invalid=true;}check(invalid,"잘못된 백업 형식 거부");
    list.Add(list[0]);invalid=false;try{Core.Parse(Core.Json().Serialize(store));}catch{invalid=true;}check(invalid,"중복 ID 백업 거부");list.RemoveAt(list.Count-1);
    string testDir=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"test-data");string path=Path.Combine(testDir,"records.json");Directory.CreateDirectory(testDir);if(File.Exists(path))File.Delete(path);if(File.Exists(path+".bak"))File.Delete(path+".bak");
    Core.AtomicWrite(path,store);var prior=File.ReadAllText(path);Core.AtomicWrite(path,new Store {Version=1,Matches=new List<Match>()});check(Core.Parse(File.ReadAllText(path)).Matches.Count==0&&File.ReadAllText(path+".bak")==prior,"원자적 저장 및 직전 백업");
    check(Core.ShortTime(515)=="08:35"&&Core.ShortTime(3750)=="1:02:30"&&Core.LongTime(90061)=="25시간 1분 1초","시간 표시와 24시간 초과 누적");
    var imported=TextParser.Parse("2026-10-24\n플루토 8:35 승 # 테스트 메모\n2026-10-23, AI, 1:02:30, 패\n10/22\n1. 상대 7분 5초 무승부\n잘못된 줄",new DateTime(2026,10,24),new List<Match>());
    check(imported.Count==4&&imported[0].Date=="2026-10-24"&&imported[0].Opponent=="플루토"&&imported[0].Duration=="8:35"&&imported[0].Result=="승"&&imported[0].Memo=="테스트 메모","메모장 날짜 헤더와 메모 해석");
    check(imported[1].Date=="2026-10-23"&&imported[1].Result=="패"&&imported[1].Duration=="1:02:30"&&imported[2].Date=="2026-10-22"&&imported[2].Duration=="07:05"&&imported[2].Result=="무","메모장 쉼표, 한글 시간, 월일 해석");
    check(!imported[3].Include&&imported[3].Error.Length>0,"해석 불가 원문 보존 및 자동 제외");
    var duplicate=TextParser.Parse("플루토 8:35 승 # 테스트 메모",new DateTime(2026,10,24),new[]{imported[0].ToMatch()});check(duplicate[0].Duplicate&&!duplicate[0].Include,"메모장 동일 기록 중복 방지");
    var badDate=TextParser.Parse("2026-02-31 AI 8:35 패",new DateTime(2026,10,24),new List<Match>());check(!badDate[0].Include&&badDate[0].Error.Contains("날짜"),"존재하지 않는 날짜 자동 저장 방지");
    Window ui;using(Stream xaml=Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))ui=(Window)XamlReader.Load(xaml);
    check(ui.FindName("OverviewBtn")!=null&&ui.FindName("TextImportBtn")!=null&&ui.FindName("TextExportBtn")!=null&&((Button)ui.FindName("TextExportBtn")).ContextMenu.Items.Count==2,"메인 화면 XAML과 가져오기·내보내기 버튼 로드");
    var importUi=new TextImportWindow(null,new List<Match>(),r=>true);importUi.PreparePreview("2026-10-24\n플루토 8:35 승 # 테스트 메모\nAI 6:35 패\n해석이 안 된 줄");
    check(((System.Windows.Controls.DataGrid)importUi.PreviewWindow.FindName("PreviewGrid")).Items.Count==3,"가져오기 화면 로드와 미리보기 바인딩");
    var overviewUi=new AllRecordsWindow(null,()=>list);check(((System.Windows.Controls.DataGrid)overviewUi.Window.FindName("AllGrid")).Items.Count==4,"전체 목록 화면 로드와 바인딩");
    check(((System.Windows.Controls.ItemsControl)overviewUi.Window.FindName("DayCards")).Items.Count==3,"날짜별 카드 집계");
    RenderPreview(ui,1220,800,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"main-preview.png"));RenderPreview(importUi.PreviewWindow,1240,800,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"import-preview.png"));RenderPreview(overviewUi.Window,1500,900,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"overview-preview.png"));
    ((System.Windows.Controls.TextBox)overviewUi.Window.FindName("OverviewSearch")).Text="없는 상대";check(((System.Windows.Controls.DataGrid)overviewUi.Window.FindName("AllGrid")).Items.Count==0,"전체 기록 화면 검색 반영");
    string importPath=Path.Combine(testDir,"imported.json");var saveUi=new TextImportWindow(null,new List<Match>(),records=>{Core.AtomicWrite(importPath,new Store{Version=1,Matches=records});return true;});saveUi.PreparePreview("2026-10-24\n플루토 8:35 승\nAI 6:35 패\n잘못된 줄");((System.Windows.Controls.Button)saveUi.PreviewWindow.FindName("ImportBtn")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));var savedImport=Core.Parse(File.ReadAllText(importPath));check(savedImport.Matches.Count==2&&savedImport.Matches.Sum(m=>m.Seconds)==910,"가져오기 저장 버튼과 파일 재읽기 통합 검사");
    string exportedText=TextExporter.Build(list,"전체 기록");var exportedRows=TextParser.Parse(exportedText,DateTime.Today,new List<Match>());check(exportedRows.Count==list.Count&&exportedRows.All(r=>r.Include)&&new HashSet<string>(list.Select(TextParser.Fingerprint)).SetEquals(exportedRows.Select(r=>TextParser.Fingerprint(r.ToMatch()))),"TXT 내보내기 후 날짜·상대·시간·승패·메모 재가져오기");
    check(exportedText.Contains("# 총 4판")&&exportedText.Contains("66.7%")&&exportedText.Contains("1시간 9분 45초")&&exportedText.IndexOf("\r\n2026-11-01\r\n",StringComparison.Ordinal)<exportedText.IndexOf("\r\n2026-10-24\r\n",StringComparison.Ordinal),"TXT 전체 합계와 최신 날짜순 정렬");
    Match escaped=create("2026-10-24","무",3750);escaped.Opponent="승 # AI 09:30\t\\봇";escaped.Memo="첫 줄\r\n둘째\t# 메모 \\n \\ 끝";var escapedRows=TextParser.Parse(TextExporter.Build(new[]{escaped},"특수문자 검사"),DateTime.Today,new List<Match>());check(escapedRows.Count==1&&escapedRows[0].Include&&TextParser.Fingerprint(escapedRows[0].ToMatch())==TextParser.Fingerprint(escaped),"TXT 이름의 승패·시간 문자열과 메모 특수문자 보존");
    var filteredForExport=Core.Filter(list,2,new DateTime(2026,10,24),"");var filteredRows=TextParser.Parse(TextExporter.Build(filteredForExport,"2026년 10월 24일"),DateTime.Today,new List<Match>());check(filteredRows.Count==2&&filteredRows.All(r=>r.Date=="2026-10-24"),"현재 조회 결과만 TXT 내보내기");
    string exportPath=Path.Combine(testDir,"export.txt");TextExporter.Write(exportPath,exportedText);byte[] bytes=File.ReadAllBytes(exportPath);check(bytes.Length>3&&bytes[0]==239&&bytes[1]==187&&bytes[2]==191&&TextParser.ReadFile(exportPath)==exportedText,"메모장 호환 UTF-8 BOM 저장과 읽기");
    TextExporter.Write(exportPath,TextExporter.Build(new[]{escaped},"덮어쓰기 검사"));check(TextParser.Parse(TextParser.ReadFile(exportPath),DateTime.Today,new List<Match>()).Count==1&&!Directory.GetFiles(testDir,"export.txt.*.tmp").Any(),"TXT 덮어쓰기와 임시 파일 정리");
    check(TextParser.Parse(TextExporter.Build(new List<Match>(),"빈 목록"),DateTime.Today,new List<Match>()).Count==0,"빈 내보내기에서 가짜 기록 생성 방지");
    File.WriteAllLines(report,passed.ToArray(),Encoding.UTF8);
   }catch(Exception ex){passed.Add("FAIL "+ex.Message);File.WriteAllLines(report,passed.ToArray(),Encoding.UTF8);Environment.ExitCode=1;}
  }
  static void RenderPreview(Window window,int width,int height,string path){FrameworkElement root=(FrameworkElement)window.Content;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,System.Windows.Media.PixelFormats.Pbgra32);var background=new System.Windows.Media.DrawingVisual();using(var drawing=background.RenderOpen())drawing.DrawRectangle(window.Background,null,new Rect(0,0,width,height));bitmap.Render(background);bitmap.Render(root);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
 }
}
