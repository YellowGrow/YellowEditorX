using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using YellowEditorX.Core;

namespace YellowEditorX;

public partial class MainWindow : Window
{
    readonly Settings settings;
    /// <summary>Set only in self test mode: scripted answers for Ask() and the questions that were asked.</summary>
    Queue<MessageBoxResult>? testAnswers;
    readonly List<string> testPrompts = new();
    int testImageDialogs;
    readonly History history = new();
    CardInfo info = new();
    EditorModel model = null!;

    List<DbEntry> dbs = new();
    DbEntry? currentDb;
    List<CardRow> allRows = new();
    List<CardRow> shownRows = new();
    CardRow? currentRow;
    string sortKey = "Code";
    bool sortAscending = true;
    bool suppressListSelection, suppressDbSelection;
    /// <summary>Search text and chosen cards of each database, kept while the window is open.</summary>
    readonly Dictionary<string, DbView> dbViews = new(StringComparer.OrdinalIgnoreCase);
    record DbView(string Filter, long? CurrentId, long[] SelectedIds);
    /// <summary>Code of the card a duplicate was made from, so its picture can be copied on first save.</summary>
    long duplicateSource;

    /// <summary>False when the card info file could not be read; the window must not be shown then.</summary>
    public bool Ready { get; }

    public MainWindow() : this(null) { }

    public MainWindow(Settings? testSettings)
    {
        settings = testSettings ?? Settings.Load();
        if (testSettings != null) testAnswers = new();
        InitializeComponent();
        if (!LoadCardInfo()) return;
        ApplyRemote(ReadRemoteCache(), live: false);
        Ready = true;
        model = new EditorModel(info);
        DataContext = model;
        model.PropertyChanged += Model_PropertyChanged;
        ApplyZoom(settings.Zoom);
        ListColumn.Width = new GridLength(Math.Max(240, settings.ListWidth));
        if (settings.StringsWidth > 0) StringsColumn.Width = new GridLength(settings.StringsWidth, GridUnitType.Star);
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewMouseWheel += Window_PreviewMouseWheel;
        PreviewMouseLeftButtonDown += (_, _) => imagePressed = false; // the image box sets it again after this
        Closing += Window_Closing;
        Loaded += Window_Loaded;
        ClipboardText.Attach(this);
        ClipboardText.Failed = ShowStatus;
        UpdateTitle();
        UpdateUndo();
    }

    #region startup and settings
    bool LoadCardInfo()
    {
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(settings.CardInfo)) candidates.Add(settings.CardInfo);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "data", "cardinfo_korean.txt"));
        foreach (var p in candidates)
        {
            if (!File.Exists(p)) continue;
            try
            {
                info = CardInfo.Load(p);
                return true;
            }
            catch (Exception ex)
            {
                Ask("카드 정보 파일을 읽지 못했어요.\n" + p + "\n\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        Ask("카드 정보 파일(data\\cardinfo_korean.txt)이 없어요.", Title, MessageBoxButton.OK, MessageBoxImage.Error);
        return false;
    }

    #region card groups, types, races and attributes from GitHub
    // The last files read from GitHub, so the lists are the same at the next start even without internet.
    static string RemoteCacheDir => Path.Combine(AppContext.BaseDirectory, "data", "github");
    const string StringsCacheName = "strings.conf", ConstantsCacheName = "ocgapi_constants.h", DataManagerCacheName = "data_manager.cpp";

    (string? Strings, string? Constants, string? DataManager) ReadRemoteCache()
    {
        string? Read(string name)
        {
            try { return File.ReadAllText(Path.Combine(RemoteCacheDir, name)); } catch { return null; }
        }
        return settings.GitHubSync ? (Read(StringsCacheName), Read(ConstantsCacheName), Read(DataManagerCacheName)) : (null, null, null);
    }

    /// <summary>
    /// Makes the lists the same as CyanPro's. Before the model exists it changes the card info, afterwards the
    /// model's lists. The type check boxes are made once, so a changed type list shows from the next start.
    /// </summary>
    int ApplyRemote((string? Strings, string? Constants, string? DataManager) remote, bool live) =>
        ApplyRemote(remote, live, out _);

    int ApplyRemote((string? Strings, string? Constants, string? DataManager) remote, bool live, out bool typesChanged)
    {
        IList<InfoItem> setnames = live ? model.SetnameItems : info.Setnames;
        IList<InfoItem> races = live ? model.Races : info.Races;
        IList<InfoItem> attributes = live ? model.Attributes : info.Attributes;
        long[] keepRace = live ? new[] { model.Race } : Array.Empty<long>();
        long[] keepAttribute = live ? new[] { model.Attribute } : Array.Empty<long>();
        int n = 0;
        typesChanged = false;
        if (remote.Strings != null)
            n += RemoteInfo.Sync(setnames, RemoteInfo.ParseSetnames(remote.Strings), live ? model.Setcodes.Select(s => s.Value).ToArray() : Array.Empty<long>());
        if (remote.Constants != null)
        {
            n += RemoteInfo.Sync(races, RemoteInfo.ParseBits(remote.Constants, "RACE", remote.Strings, null, info.Races), keepRace);
            n += RemoteInfo.Sync(attributes, RemoteInfo.ParseBits(remote.Constants, "ATTRIBUTE", remote.Strings, null, info.Attributes), keepAttribute);
            var types = RemoteInfo.ParseBits(remote.Constants, "TYPE", remote.Strings, remote.DataManager, info.Types);
            if (live)
            {
                // Only compared here: the check boxes are built from info.Types when the window opens.
                var copy = info.Types.ToList();
                typesChanged = RemoteInfo.Sync(copy, types) > 0;
            }
            else n += RemoteInfo.Sync(info.Types, types);
        }
        if (live && n > 0)
        {
            model.RefreshSetnames();
            model.RefreshChoices();
        }
        return n;
    }

    static readonly System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Reads the lists from GitHub in the background and adds anything new.</summary>
    async Task SyncRemote(bool manual)
    {
        if (!settings.GitHubSync && !manual) return;
        if (manual) ShowStatus("GitHub에서 카드군·종류·종족·속성 목록을 받는 중…");
        string strings, constants, dataManager;
        try
        {
            var s = http.GetStringAsync(RemoteInfo.StringsUrl);
            var c = http.GetStringAsync(RemoteInfo.ConstantsUrl);
            var d = http.GetStringAsync(RemoteInfo.DataManagerUrl);
            strings = await s;
            constants = await c;
            dataManager = await d;
        }
        catch (Exception ex)
        {
            if (manual) ShowStatus("GitHub에 연결하지 못했어요. 저장해 둔 목록을 그대로 써요. (" + ex.Message + ")");
            return;
        }
        if (!settings.ReadOnly)
        {
            try
            {
                Directory.CreateDirectory(RemoteCacheDir);
                File.WriteAllText(Path.Combine(RemoteCacheDir, StringsCacheName), strings);
                File.WriteAllText(Path.Combine(RemoteCacheDir, ConstantsCacheName), constants);
                File.WriteAllText(Path.Combine(RemoteCacheDir, DataManagerCacheName), dataManager);
            }
            catch { }
        }
        int n = ApplyRemote((strings, constants, dataManager), live: true, out bool typesChanged);
        string types = typesChanged ? " 몬스터·마법·함정 종류도 바뀌었어요. 에디터를 다시 켜면 보여요." : "";
        if (n > 0) ShowStatus($"GitHub(CyanPro) 목록에 맞췄어요 — 추가·변경·삭제된 항목 {n}개.{types}");
        else if (typesChanged) ShowStatus("GitHub(CyanPro) 목록을 받았어요." + types);
        else if (manual) ShowStatus("GitHub 목록과 같아요. 바뀐 게 없어요.");
    }

    async void SyncRemote_Click(object sender, RoutedEventArgs e) => await SyncRemote(manual: true);
    #endregion

    #region updates
    // A new version is a GitHub release; the download and the updater script go to a temp folder.
    static string UpdateDir => Path.Combine(Path.GetTempPath(), "YellowEditorX_update");
    static string UpdateLog => Path.Combine(UpdateDir, "update.log");
    static Version CurrentVersion => Updates.Normalize(typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0));
    static string Short(Version v) => v.Revision > 0 ? v.ToString() : v.ToString(3);
    static readonly System.Net.Http.HttpClient download = new() { Timeout = TimeSpan.FromMinutes(5) };
    bool closingForUpdate;

    /// <summary>After a restart by the updater: says whether the files were replaced.</summary>
    void ReportUpdate()
    {
        if (settings.UpdatingTo == null) return;
        string? log = null;
        try { log = File.ReadAllText(UpdateLog).Trim(); } catch { }
        if (log == "ok") ShowStatus($"YellowEditorX {Short(CurrentVersion)}(으)로 업데이트했어요.");
        else Ask("업데이트하지 못해서 이전 버전 그대로예요.\n" + (log ?? "") + "\n\n에디터 폴더에 쓸 수 없는 곳(Program Files 등)이면 다른 폴더로 옮겨 주세요.",
                 Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        settings.UpdatingTo = null;
        settings.Save();
    }

    /// <summary>Looks for a newer release in the background and offers to install it.</summary>
    async Task CheckForUpdate()
    {
        if (!settings.UpdateCheck) return;
        UpdateInfo? found;
        try
        {
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, Updates.LatestUrl);
            req.Headers.UserAgent.ParseAdd("YellowEditorX/" + Short(CurrentVersion));
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return;
            found = Updates.Parse(await res.Content.ReadAsStringAsync(), CurrentVersion);
        }
        catch { return; }
        if (found == null || Short(found.Version) == settings.SkipVersion) return;
        string notes = found.Notes.Length > 800 ? found.Notes[..800] + "…" : found.Notes;
        var answer = Ask($"새 버전 {Short(found.Version)}이(가) 나왔어요. (지금 {Short(CurrentVersion)})\n\n"
                         + (notes.Length > 0 ? notes + "\n\n" : "")
                         + "지금 업데이트할까요? 에디터가 잠깐 닫혔다가 다시 켜져요.\n\n"
                         + "[예] 업데이트   [아니요] 나중에   [취소] 이 버전은 건너뛰기",
                         "업데이트", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Cancel)
        {
            settings.SkipVersion = Short(found.Version);
            settings.Save();
        }
        else if (answer == MessageBoxResult.Yes) await InstallUpdate(found);
    }

    async Task InstallUpdate(UpdateInfo update)
    {
        if (!ConfirmLeave()) return;
        ShowStatus($"새 버전 {Short(update.Version)}을(를) 받는 중…");
        try
        {
            if (Directory.Exists(UpdateDir)) Directory.Delete(UpdateDir, true);
            Directory.CreateDirectory(UpdateDir);
            string zip = Path.Combine(UpdateDir, "update.zip");
            using (var s = await download.GetStreamAsync(update.ZipUrl))
            using (var f = File.Create(zip))
                await s.CopyToAsync(f);
            string extracted = Path.Combine(UpdateDir, "new");
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, extracted);
            string folder = Updates.ProgramFolder(extracted);
            string exe = Path.GetFileName(Environment.ProcessPath ?? "YellowEditorX.exe");
            if (!File.Exists(Path.Combine(folder, exe))) throw new InvalidDataException("받은 파일 안에 " + exe + "가 없어요.");
            string script = Path.Combine(UpdateDir, "update.ps1");
            File.WriteAllText(script, Updates.UpdaterScript(Environment.ProcessId, folder, AppContext.BaseDirectory.TrimEnd('\\'), exe, UpdateLog),
                              new System.Text.UTF8Encoding(true));
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            Ask("업데이트하지 못했어요.\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            ShowStatus("업데이트하지 못했어요.");
            return;
        }
        settings.UpdatingTo = Short(update.Version);
        settings.Save();
        closingForUpdate = true;
        Close();
    }
    #endregion

    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _ = SyncRemote(manual: false);
        // No dialog at startup: the list just stays empty until a file is opened.
        if (settings.OpenDbs.Count == 0) ShowStatus("왼쪽 위 [열기]로 cdb 파일을 여세요. 여러 개를 한 번에 고를 수 있어요.");
        RefreshDbList(settings.LastDb);
        if (currentDb == null) LoadEmpty();
        ReportUpdate();
        _ = CheckForUpdate();
    }

    void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (model != null && !closingForUpdate && !ConfirmLeave())
        {
            e.Cancel = true;
            return;
        }
        settings.ListWidth = ListColumn.ActualWidth;
        settings.Save();
    }

    void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        settings.ListWidth = ListColumn.ActualWidth;
        settings.Save();
    }

    void ApplyZoom(double z)
    {
        z = Math.Round(Math.Clamp(z, 0.7, 2.0), 2);
        settings.Zoom = z;
        Root.LayoutTransform = Math.Abs(z - 1) < 0.001 ? Transform.Identity : new ScaleTransform(z, z);
        ZoomText.Text = $"{z * 100:0}%";
    }
    #endregion

    #region databases
    void RefreshDbList(string? select)
    {
        // A file that isn't there right now (a drive not plugged in) is hidden but kept in the settings.
        dbs = settings.OpenDbs.Where(File.Exists).Select(p => new DbEntry(p))
            .OrderBy(d => d.Title, StringComparer.CurrentCulture).ThenBy(d => d.Detail, StringComparer.CurrentCulture).ToList();
        suppressDbSelection = true;
        DbList.ItemsSource = dbs;
        suppressDbSelection = false;
        var target = dbs.FirstOrDefault(d => select != null && SamePath(d.Path, select))
                     ?? (currentDb != null ? dbs.FirstOrDefault(d => SamePath(d.Path, currentDb.Path)) : null)
                     ?? dbs.FirstOrDefault();
        if (target != null)
        {
            DbList.SelectedItem = target;
            DbList.ScrollIntoView(target);
        }
    }

    /// <summary>Where file dialogs start: the folder that holds the current archetype folder.</summary>
    string DialogFolder()
    {
        if (currentDb == null) return "";
        string dir = Path.GetDirectoryName(currentDb.Path)!;
        return Path.GetDirectoryName(dir) ?? dir;
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    void DbList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressDbSelection) return;
        if (DbList.SelectedItem is not DbEntry db || (currentDb != null && SamePath(db.Path, currentDb.Path))) return;
        if (!ConfirmLeave())
        {
            suppressDbSelection = true;
            DbList.SelectedItem = currentDb == null ? null : dbs.FirstOrDefault(d => SamePath(d.Path, currentDb.Path));
            suppressDbSelection = false;
            return;
        }
        OpenDb(db);
    }

    void OpenDb(DbEntry db)
    {
        RememberDbView();
        currentDb = db;
        settings.LastDb = db.Path;
        settings.Save();
        DbPathText.Text = db.Path;
        dbViews.TryGetValue(Path.GetFullPath(db.Path), out var view);
        currentRow = null;
        FilterBox.Text = view?.Filter ?? "";
        if (!ReloadCards(null)) return;
        var row = shownRows.FirstOrDefault(r => r.Card.Id == view?.CurrentId) ?? shownRows.FirstOrDefault();
        if (row != null)
        {
            SelectRow(row);
            if (view != null)
            {
                // Put back the other cards that were chosen together with it.
                suppressListSelection = true;
                foreach (var r in shownRows.Where(r => r != row && view.SelectedIds.Contains(r.Card.Id)))
                    CardList.SelectedItems.Add(r);
                suppressListSelection = false;
            }
        }
        else LoadEmpty();
        UpdateTitle();
        ShowStatus($"{db.Title} 열었어요 — 카드 {allRows.Count}장");
    }

    /// <summary>Keeps the search text and chosen cards of the open database for when it is opened again.</summary>
    void RememberDbView()
    {
        if (currentDb == null) return;
        dbViews[Path.GetFullPath(currentDb.Path)] = new DbView(FilterBox.Text, currentRow?.Card.Id,
            CardList.SelectedItems.Cast<CardRow>().Select(r => r.Card.Id).ToArray());
    }

    /// <summary>Reads the current database again and selects the card with the given code if it is there.</summary>
    bool ReloadCards(long? selectId)
    {
        if (currentDb == null) return false;
        try
        {
            allRows = CardDb.ReadAll(currentDb.Path).Select(c => new CardRow(c, info)).ToList();
        }
        catch (Exception ex)
        {
            Ask("cdb 파일을 읽지 못했어요.\n" + currentDb.Path + "\n\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            allRows = new();
            ApplyFilter();
            return false;
        }
        ApplyFilter();
        if (selectId != null)
        {
            var row = shownRows.FirstOrDefault(r => r.Card.Id == selectId);
            MarkCurrent(row);
            suppressListSelection = true;
            CardList.SelectedItem = row;
            suppressListSelection = false;
            if (row != null) CardList.ScrollIntoView(row);
        }
        return true;
    }

    void NewDb_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "새 cdb 만들기 — 카드군 폴더 안에 저장하면 pics, script 폴더도 같이 만들어요",
            Filter = "카드 DB (*.cdb)|*.cdb",
            FileName = "NewArchetype.cdb",
            InitialDirectory = DialogFolder(),
        };
        if (dlg.ShowDialog(this) != true) return;
        if (!ConfirmLeave()) return;
        try
        {
            if (File.Exists(dlg.FileName))
            {
                Ask("이미 있는 파일이에요. 다른 이름을 골라 주세요.", Title);
                return;
            }
            CardDb.Create(dlg.FileName);
            string dir = Path.GetDirectoryName(dlg.FileName)!;
            Directory.CreateDirectory(Path.Combine(dir, "pics", "thumbnail"));
            Directory.CreateDirectory(Path.Combine(dir, "script"));
        }
        catch (Exception ex)
        {
            Ask("만들지 못했어요.\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        AddDb(dlg.FileName);
        RememberDbView();
        currentDb = null;
        RefreshDbList(dlg.FileName);
    }

    void OpenDb_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "cdb 열기 — Ctrl이나 Shift를 누른 채 여러 개를 고를 수 있어요",
            Filter = "카드 DB (*.cdb)|*.cdb|모든 파일|*.*",
            Multiselect = true,
            InitialDirectory = DialogFolder(),
        };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in dlg.FileNames) AddDb(f);
        RefreshDbList(dlg.FileNames[0]);
    }

    void AddDb(string path)
    {
        settings.Add(path);
        settings.Save();
    }

    void CloseDb_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DbEntry db) CloseDb(db);
    }

    /// <summary>Takes a database off the list. The file itself stays where it is.</summary>
    void CloseDb(DbEntry db)
    {
        if (currentDb != null && SamePath(currentDb.Path, db.Path))
        {
            if (!ConfirmLeave()) return;
            currentDb = null;
            allRows = new();
            ApplyFilter();
            LoadEmpty();
            DbPathText.Text = "";
            UpdateTitle();
        }
        settings.Remove(db.Path);
        dbViews.Remove(Path.GetFullPath(db.Path));
        if (settings.LastDb != null && SamePath(settings.LastDb, db.Path)) settings.LastDb = null;
        settings.Save();
        RefreshDbList(null);
        ShowStatus($"{db.Title} 닫았어요. 파일은 그대로 있어요: {db.Path}");
    }
    #endregion

    #region card list
    void ApplyFilter()
    {
        var words = FilterBox.Text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<CardRow> rows = allRows.Where(r => words.All(w => r.SearchText.Contains(w)));
        Func<CardRow, object> key = sortKey switch
        {
            "Name" => r => r.Name,
            "Kind" => r => r.Kind,
            "LevelText" => r => r.Card.Level & 0xff,
            "Stats" => r => r.Card.Atk,
            _ => r => r.Card.Id,
        };
        rows = sortAscending ? rows.OrderBy(key).ThenBy(r => r.Card.Id) : rows.OrderByDescending(key).ThenBy(r => r.Card.Id);
        shownRows = rows.ToList();
        suppressListSelection = true;
        CardList.ItemsSource = shownRows;
        if (currentRow != null)
        {
            var same = shownRows.FirstOrDefault(r => r.Card.Id == currentRow.Card.Id);
            CardList.SelectedItem = same;
        }
        suppressListSelection = false;
        CountText.Text = words.Length == 0 ? $"카드 {allRows.Count}장" : $"{allRows.Count}장 중 {shownRows.Count}장";
    }

    void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    void FilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { FilterBox.Text = ""; e.Handled = true; }
        else if ((e.Key == Key.Down || e.Key == Key.Enter) && shownRows.Count > 0)
        {
            var row = CardList.SelectedItem as CardRow ?? shownRows[0];
            if (e.Key == Key.Enter) SelectRow(row);
            CardList.UpdateLayout();
            (CardList.ItemContainerGenerator.ContainerFromItem(row) as ListViewItem)?.Focus();
            e.Handled = true;
        }
    }

    void CardList_HeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader h || h.Column?.DisplayMemberBinding is not System.Windows.Data.Binding b) return;
        string key = b.Path.Path;
        if (key == sortKey) sortAscending = !sortAscending;
        else { sortKey = key; sortAscending = true; }
        ApplyFilter();
    }

    void CardList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressListSelection) return;
        var row = e.AddedItems.Count > 0 ? e.AddedItems[^1] as CardRow : null;
        if (row == null || row == currentRow) return;
        long targetId = row.Card.Id;
        if (!ConfirmLeave())
        {
            suppressListSelection = true;
            CardList.SelectedItem = currentRow;
            suppressListSelection = false;
            return;
        }
        // Saving in ConfirmLeave reloads the list, so the clicked row may have been replaced.
        var fresh = shownRows.FirstOrDefault(r => r.Card.Id == targetId);
        if (fresh != null && fresh != row) SelectRow(fresh);
        else ShowCard(row);
    }

    void SelectRow(CardRow row)
    {
        suppressListSelection = true;
        CardList.SelectedItem = row;
        suppressListSelection = false;
        CardList.ScrollIntoView(row);
        ShowCard(row);
    }

    void CardList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
    }

    void MarkCurrent(CardRow? row)
    {
        if (currentRow != null) currentRow.IsCurrent = false;
        currentRow = row;
        if (row != null) row.IsCurrent = true;
    }

    List<Card> SelectedCards()
    {
        var list = CardList.SelectedItems.Cast<CardRow>().Select(r => r.Card).ToList();
        if (list.Count == 0 && currentRow != null) list.Add(currentRow.Card);
        return list;
    }

    void CardMenu_Opened(object sender, RoutedEventArgs e)
    {
        CopyToMenu.Items.Clear();
        MoveToMenu.Items.Clear();
        foreach (var db in dbs.Where(d => currentDb == null || !SamePath(d.Path, currentDb.Path)))
        {
            var target = db;
            var c = new MenuItem { Header = db.Title + "  (" + db.Detail + ")" };
            c.Click += (_, _) => CopySelectedTo(target, move: false);
            CopyToMenu.Items.Add(c);
            var m = new MenuItem { Header = db.Title + "  (" + db.Detail + ")" };
            m.Click += (_, _) => CopySelectedTo(target, move: true);
            MoveToMenu.Items.Add(m);
        }
        CopyToMenu.IsEnabled = MoveToMenu.IsEnabled = CopyToMenu.Items.Count > 0;
    }

    void CopySelectedTo(DbEntry target, bool move)
    {
        if (currentDb == null) return;
        var cards = SelectedCards();
        if (cards.Count == 0) return;
        if (move && !ConfirmLeave()) return;
        try
        {
            var existing = CardDb.ReadIds(target.Path);
            int clash = cards.Count(c => existing.Contains(c.Id));
            bool overwrite = false;
            if (clash > 0)
            {
                var r = Ask($"{target.Title}에 같은 코드의 카드가 {clash}장 있어요.\n\n예: 덮어쓰기\n아니요: 그 카드는 건너뛰기",
                    Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) return;
                overwrite = r == MessageBoxResult.Yes;
            }
            var entry = CardOps.CopyTo(currentDb.Path, cards, target.Path, overwrite, move, out int done);
            if (done == 0) { ShowStatus("옮길 카드가 없어요 (모두 건너뜀)."); return; }
            history.Push(entry);
            UpdateUndo();
            if (move)
            {
                ReloadCards(null);
                AfterCurrentRemoved();
            }
            ShowStatus($"{done}장을 {target.Title}(으)로 {(move ? "옮겼어요" : "복사했어요")} — 이미지·스크립트 포함. Ctrl+Z로 되돌릴 수 있어요.");
        }
        catch (Exception ex)
        {
            Ask("복사하지 못했어요.\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    #endregion

    #region editing
    void ShowCard(CardRow row)
    {
        duplicateSource = 0;
        MarkCurrent(row);
        model.Load(row.Card);
        UpdateImage();
    }

    void LoadEmpty()
    {
        duplicateSource = 0;
        MarkCurrent(null);
        model.Load(null);
        UpdateImage();
    }

    void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorModel.IsDirty) || e.PropertyName == nameof(EditorModel.IsNew)) UpdateTitle();
        else if (e.PropertyName == nameof(EditorModel.Code)) UpdateImage();
    }

    void UpdateTitle()
    {
        string t = "YellowEditorX";
        if (currentDb != null) t = currentDb.Title + " — " + t;
        if (model != null && model.IsDirty) t = "● " + t;
        Title = t;
    }

    /// <summary>Asks what to do with unsaved changes. False means the user wants to stay on this card.</summary>
    bool ConfirmLeave()
    {
        if (!model.HasUnsavedWork) return true;
        string name = model.Name.Trim().Length > 0 ? model.Name.Trim() : "이 카드";
        var r = Ask($"'{name}'에 저장하지 않은 내용이 있어요. 저장할까요?\n\n예: 저장\n아니요: 버리기\n취소: 계속 편집",
            Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.No) return true;
        return SaveCurrent();
    }

    long[] AllOpenIds()
    {
        var ids = new HashSet<long>();
        foreach (var db in dbs)
        {
            try { ids.UnionWith(CardDb.ReadIds(db.Path)); } catch { }
        }
        foreach (var r in allRows) ids.Add(r.Card.Id);
        return ids.ToArray();
    }

    long NextCode(long after)
    {
        var current = allRows.Select(r => r.Card.Id).ToList();
        if (after <= 0) after = current.Count > 0 ? current.Max() : 0;
        return Workspace.NextFreeCode(AllOpenIds().ToHashSet(), after);
    }

    bool SaveCurrent()
    {
        if (currentDb == null)
        {
            Ask("먼저 왼쪽에서 카드 DB를 골라 주세요.", Title);
            return false;
        }
        string? err = model.Validate();
        if (err != null)
        {
            Ask(err, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var c = model.ToSaveCard(DateTime.Today);
        var orig = model.Original;
        string db = currentDb.Path;
        try
        {
            HistoryEntry entry;
            var ids = CardDb.ReadIds(db);
            if (orig != null && orig.Id == c.Id)
            {
                entry = CardOps.Save(db, c);
            }
            else
            {
                if (ids.Contains(c.Id))
                {
                    var other = allRows.FirstOrDefault(r => r.Card.Id == c.Id)?.Name ?? "";
                    if (Ask($"코드 {c.Id}는 이 DB의 '{other}'이(가) 이미 쓰고 있어요.\n덮어쓸까요?", Title,
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return false;
                }
                var elsewhere = Workspace.FindIdElsewhere(dbs, db, c.Id);
                if (elsewhere.Count > 0 &&
                    Ask($"코드 {c.Id}는 다른 DB({string.Join(", ", elsewhere)})에서도 쓰고 있어요.\n게임에서 겹칠 수 있는데, 그래도 저장할까요?",
                        Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return false;
                if (orig != null)
                {
                    var r = Ask($"코드를 {orig.Id} → {c.Id}(으)로 바꿨어요.\n\n예: 카드를 새 코드로 옮기기 (이미지·스크립트 이름도 바꿈)\n아니요: 원래 카드는 두고 새 코드로 복사본 저장\n취소: 저장 안 함",
                        Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (r == MessageBoxResult.Cancel) return false;
                    entry = CardOps.ChangeCode(db, orig.Id, c, move: r == MessageBoxResult.Yes);
                }
                else
                {
                    entry = CardOps.Save(db, c);
                    CopyDuplicatePicture(entry, c.Id);
                }
            }
            history.Push(entry);
            UpdateUndo();
            duplicateSource = 0;
            model.MarkSaved(c);
            if (model.Maker.Trim().Length > 0 && model.Maker.Trim() != settings.LastMaker)
            {
                settings.LastMaker = model.Maker.Trim();
                settings.Save();
            }
            ReloadCards(c.Id);
            UpdateImage();
            ShowStatus($"저장했어요: {c.Name} ({c.Id})");
            return true;
        }
        catch (Exception ex)
        {
            Ask("저장하지 못했어요.\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>A duplicated card starts with the same picture (not the script, which is named after its code).</summary>
    void CopyDuplicatePicture(HistoryEntry entry, long id)
    {
        if (duplicateSource <= 0 || currentDb == null) return;
        var f = new CardFiles(currentDb.Path);
        foreach (var (from, to) in new[] { (f.Picture(duplicateSource), f.Picture(id)), (f.Thumbnail(duplicateSource), f.Thumbnail(id)) })
        {
            if (!File.Exists(from) || File.Exists(to)) continue;
            File.Copy(from, to);
            entry.CreatedFiles.Add(to);
        }
    }

    void Save_Click(object sender, RoutedEventArgs e) => SaveCurrent();

    void NewCard_Click(object sender, RoutedEventArgs e) => NewCard();

    void NewCard()
    {
        if (currentDb == null) { Ask("먼저 왼쪽에서 카드 DB를 골라 주세요.", Title); return; }
        if (!ConfirmLeave()) return;
        var c = new Card
        {
            Id = NextCode(0), Type = CardTypes.Monster | CardTypes.Effect, Ot = Card.RuleCustom,
            // The maker is filled in from the last saved card.
            Desc = CardFooter.Join("", new CardFooter { Maker = settings.LastMaker ?? "" }),
        };
        ClearListSelection();
        duplicateSource = 0;
        model.Load(c, fromDb: false);
        UpdateImage();
        NameBox.Focus();
        ShowStatus($"새 카드 — 코드 {c.Id}. 다 쓰면 Ctrl+S로 저장하세요.");
    }

    void Duplicate_Click(object sender, RoutedEventArgs e) => Duplicate();

    void Duplicate()
    {
        if (currentDb == null) return;
        if (model.Original != null && model.IsDirty)
        {
            if (!ConfirmLeave()) return;
            // "Discard" means the copy starts from the saved card.
            if (model.IsDirty) model.Load(model.Original);
        }
        var c = model.ToCard();
        long source = c.Id;
        c.Id = NextCode(c.Id);
        c.Ot = Card.RuleCustom | (c.Ot & Card.ScopeHidden);
        // A copy is a new card: it gets its own dates when saved.
        c.Created = c.Modified = "";
        ClearListSelection();
        model.Load(c, fromDb: false);
        duplicateSource = source;
        UpdateImage();
        NameBox.Focus();
        NameBox.SelectAll();
        ShowStatus($"복제했어요 — 새 코드 {c.Id}. 저장(Ctrl+S)하면 이미지도 같이 복사돼요.");
    }

    void ClearListSelection()
    {
        suppressListSelection = true;
        CardList.SelectedItem = null;
        suppressListSelection = false;
        MarkCurrent(null);
    }

    void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    void DeleteSelected()
    {
        if (currentDb == null) return;
        var cards = SelectedCards();
        if (cards.Count == 0) return;
        string what = cards.Count == 1 ? $"'{cards[0].Name}'" : $"{cards.Count}장";
        if (Ask($"{what}을(를) 삭제할까요?\n이미지와 스크립트 파일도 같이 지워요. (Ctrl+Z로 되돌릴 수 있어요)",
                Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        int index = currentRow != null ? shownRows.IndexOf(currentRow) : 0;
        try
        {
            history.Push(CardOps.Delete(currentDb.Path, cards, settings.DeleteWithFiles));
            UpdateUndo();
        }
        catch (Exception ex)
        {
            Ask("삭제하지 못했어요.\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        bool currentGone = model.Original != null && cards.Any(c => c.Id == model.Original.Id);
        ReloadCards(currentGone ? null : model.Original?.Id);
        if (currentGone)
        {
            if (shownRows.Count > 0) SelectRow(shownRows[Math.Clamp(index, 0, shownRows.Count - 1)]);
            else LoadEmpty();
        }
        ShowStatus($"{what} 삭제했어요.");
    }

    /// <summary>After cards were moved away, show a card that still exists.</summary>
    void AfterCurrentRemoved()
    {
        if (model.Original != null && allRows.Any(r => r.Card.Id == model.Original.Id)) return;
        if (shownRows.Count > 0) SelectRow(shownRows[0]);
        else LoadEmpty();
    }

    void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    void Undo()
    {
        if (history.Count == 0) { ShowStatus("되돌릴 작업이 없어요."); return; }
        if (model.IsDirty &&
            Ask("편집 중인 내용은 버리고 되돌릴까요?", Title, MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            return;
        try
        {
            var e = history.Undo();
            UpdateUndo();
            long? id = model.Original?.Id ?? (long.TryParse(model.Code, out long typed) ? typed : null);
            ReloadCards(id);
            var row = id != null ? allRows.FirstOrDefault(r => r.Card.Id == id) : null;
            if (row != null && shownRows.Contains(row)) SelectRow(row);
            else if (row != null) ShowCard(row);
            else if (shownRows.Count > 0) SelectRow(shownRows[0]);
            else LoadEmpty();
            ShowStatus("되돌렸어요: " + e!.Description);
        }
        catch (Exception ex)
        {
            Ask("되돌리지 못했어요.\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void UpdateUndo()
    {
        UndoButton.IsEnabled = history.Count > 0;
        UndoButton.ToolTip = history.Next != null ? $"되돌리기: {history.Next} (Ctrl+Z)" : "되돌릴 작업 없음";
    }

    void NextCode_Click(object sender, RoutedEventArgs e) => FillNextCode();

    void FillNextCode()
    {
        if (currentDb == null) return;
        long.TryParse(model.Code, out long now);
        model.Code = NextCode(now).ToString();
        ShowStatus($"다음 빈 코드: {model.Code}");
    }

    void Number_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Up && e.Key != Key.Down || sender is not TextBox tb) return;
        long.TryParse(tb.Text, out long v);
        v = Math.Clamp(v + (e.Key == Key.Up ? 1 : -1), 0, 255);
        tb.Text = v.ToString();
        tb.CaretIndex = tb.Text.Length;
        e.Handled = true;
    }

    void StringBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter is a line break and Tab goes to the next string; ↑ / ↓ leave a string from its first / last line.
        var tb = (TextBox)sender;
        int line = tb.GetLineIndexFromCharacterIndex(tb.CaretIndex);
        if (e.Key == Key.Down && (line < 0 || line >= tb.LineCount - 1))
        {
            ((UIElement)sender).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Up && line <= 0)
        {
            ((UIElement)sender).MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
            e.Handled = true;
        }
    }

    void DescBox_TextChanged(object sender, TextChangedEventArgs e) =>
        DescCount.Text = DescBox.Text.Length == 0 ? "" : $"{DescBox.Text.Replace("\r\n", "\n").Length}자";
    #endregion

    #region script and image
    CardFiles? Files => currentDb == null ? null : new CardFiles(currentDb.Path);

    void Script_Click(object sender, RoutedEventArgs e) => OpenScript();

    void OpenScript()
    {
        var files = Files;
        if (files == null) return;
        var c = model.ToCard();
        if (c.Id <= 0) { ShowStatus("카드 코드를 먼저 입력해 주세요."); return; }
        try
        {
            string lua = files.EnsureScript(c, out bool created);
            OpenWithShell(lua);
            ShowStatus(created ? $"스크립트를 새로 만들어서 열었어요: {Path.GetFileName(lua)}" : $"스크립트 열었어요: {Path.GetFileName(lua)}");
        }
        catch (Exception ex)
        {
            Ask("스크립트를 열지 못했어요.\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    static void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No program is set for .lua files yet.
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (currentDb != null) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + currentDb.Path + "\""));
    }

    void OpenPics_Click(object sender, RoutedEventArgs e)
    {
        var f = Files;
        if (f == null) return;
        string dir = Path.Combine(f.Root, "pics");
        Directory.CreateDirectory(dir);
        long.TryParse(model.Code, out long id);
        if (id > 0 && File.Exists(f.Picture(id))) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + f.Picture(id) + "\""));
        else Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\""));
    }

    void UpdateImage()
    {
        var f = Files;
        BitmapSource? img = null;
        if (f != null && long.TryParse(model.Code, out long id) && id > 0) img = ImageTools.Load(f.Picture(id));
        CardImage.Source = img;
        NoImageText.Visibility = img == null ? Visibility.Visible : Visibility.Collapsed;
    }

    // Only a click that also started on the image box opens the dialog. Double-clicking a file in the dialog
    // releases the mouse button over the window after the dialog has closed; that must not open it again.
    bool imagePressed;
    void Image_MouseDown(object sender, MouseButtonEventArgs e) => imagePressed = true;

    void Image_Click(object sender, MouseButtonEventArgs e)
    {
        if (!imagePressed) return;
        imagePressed = false;
        ImportImageFromDialog();
    }
    void ImportImage_Click(object sender, RoutedEventArgs e) => ImportImageFromDialog();

    void ImportImageFromDialog()
    {
        if (Files == null) return;
        if (testAnswers != null) { testImageDialogs++; return; } // self test: count instead of showing the dialog
        var dlg = new OpenFileDialog { Title = "카드 이미지 고르기 — " + model.Name, Filter = "이미지|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|모든 파일|*.*" };
        if (dlg.ShowDialog(this) == true) ImportImage(ImageTools.Load(dlg.FileName), Path.GetFileName(dlg.FileName));
    }

    void Image_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Image_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            ImportImage(ImageTools.Load(files[0]), Path.GetFileName(files[0]));
        else if (e.Data.GetData(DataFormats.Bitmap) is BitmapSource bmp)
            ImportImage(bmp, "끌어다 놓은 이미지");
    }

    static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
    static bool IsCdb(string f) => Path.GetExtension(f).Equals(".cdb", StringComparison.OrdinalIgnoreCase);
    static bool IsImage(string f) => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant());

    /// <summary>Files dropped anywhere on the window: .cdb files are opened, an image becomes the current card's picture.</summary>
    void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || !files.Any(f => IsCdb(f) || IsImage(f))) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        e.Handled = DropFiles(files);
    }

    bool DropFiles(string[] files)
    {
        var cdbs = files.Where(IsCdb).ToList();
        if (cdbs.Count > 0)
        {
            foreach (var f in cdbs) AddDb(f);
            RefreshDbList(cdbs[0]);
            ShowStatus(cdbs.Count == 1 ? $"{Path.GetFileName(cdbs[0])} 열었어요" : $"cdb {cdbs.Count}개를 목록에 넣었어요");
            return true;
        }
        var img = files.FirstOrDefault(IsImage);
        if (img == null) return false;
        ImportImage(ImageTools.Load(img), Path.GetFileName(img));
        return true;
    }

    void PasteImage_Click(object sender, RoutedEventArgs e) => PasteImage();

    void PasteImage()
    {
        if (Clipboard.ContainsImage()) ImportImage(Clipboard.GetImage(), "클립보드 이미지");
        else if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList() is { Count: > 0 } list)
            ImportImage(ImageTools.Load(list[0]!), Path.GetFileName(list[0]) ?? "");
        else ShowStatus("클립보드에 이미지가 없어요.");
    }

    void ImportImage(BitmapSource? src, string from)
    {
        if (currentDb == null) return;
        if (src == null) { ShowStatus("이미지 파일을 읽지 못했어요: " + from); return; }
        if (!long.TryParse(model.Code, out long id) || id <= 0) { ShowStatus("카드 코드를 먼저 입력해 주세요."); return; }
        try
        {
            history.Push(CardOps.ReplaceImage(currentDb.Path, id, (pic, thumb) => ImageTools.WriteCardImage(src, pic, thumb)));
            UpdateUndo();
            UpdateImage();
            ShowStatus($"이미지를 넣었어요 ({from} → pics\\{id}.jpg, {ImageTools.Width}×{ImageTools.Height})");
        }
        catch (Exception ex)
        {
            Ask("이미지를 저장하지 못했어요.\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    #endregion

    #region keys, zoom, status
    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift);
        bool inText = Keyboard.FocusedElement is TextBox;
        bool handled = true;
        if (ctrl && key == Key.S) SaveCurrent();
        else if (ctrl && key == Key.Enter) SaveCurrent();
        else if (ctrl && shift && key == Key.N) FillNextCode();
        else if (ctrl && key == Key.N) NewCard();
        else if (ctrl && key == Key.D) Duplicate();
        else if (ctrl && key == Key.F) { FilterBox.Focus(); FilterBox.SelectAll(); }
        else if (ctrl && key == Key.L) OpenScript();
        else if (ctrl && key == Key.I) ImportImageFromDialog();
        else if (ctrl && key == Key.Z && !inText) Undo();
        else if (ctrl && !inText && key == Key.V && Clipboard.ContainsImage()) PasteImage();
        else if (ctrl && (key == Key.OemPlus || key == Key.Add)) ApplyZoom(settings.Zoom + 0.1);
        else if (ctrl && (key == Key.OemMinus || key == Key.Subtract)) ApplyZoom(settings.Zoom - 0.1);
        else if (ctrl && (key == Key.D0 || key == Key.NumPad0)) ApplyZoom(1.0);
        else if (key == Key.F5 && mods == ModifierKeys.None) { if (ConfirmLeave()) RefreshDbList(currentDb?.Path); }
        else handled = false;
        if (handled) e.Handled = true;
    }

    void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        ApplyZoom(settings.Zoom + (e.Delta > 0 ? 0.1 : -0.1));
        e.Handled = true;
    }

    void ZoomIn_Click(object sender, RoutedEventArgs e) => ApplyZoom(settings.Zoom + 0.1);
    void ZoomOut_Click(object sender, RoutedEventArgs e) => ApplyZoom(settings.Zoom - 0.1);

    void Help_Click(object sender, RoutedEventArgs e) => Ask(
        "Ctrl+S / Ctrl+Enter\t저장\n" +
        "Ctrl+N\t\t새 카드\n" +
        "Ctrl+D\t\t복제 (다음 빈 코드로)\n" +
        "Ctrl+Shift+N\t다음 빈 코드 넣기\n" +
        "Ctrl+Z\t\t되돌리기 (글 입력칸 밖에서)\n" +
        "Ctrl+F\t\t검색\n" +
        "Ctrl+L\t\t스크립트 열기\n" +
        "Ctrl+I\t\t이미지 가져오기\n" +
        "Ctrl+V\t\t이미지 붙여넣기 (글 입력칸 밖에서)\n" +
        "Delete\t\t목록에서 선택한 카드 삭제\n" +
        "F5\t\tDB 목록 새로 읽기\n" +
        "Ctrl+휠, Ctrl+= / - / 0\t화면 크기\n\n" +
        "스트링 칸: Enter 줄바꿈, Tab 다음 칸, 첫·끝 줄에서 ↑ / ↓ 로 칸 이동\n" +
        "레벨·스케일 칸: ↑ / ↓ 로 1씩 변경\n" +
        "목록 우클릭: 다른 DB로 복사 / 이동",
        "단축키", MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>Every question and message goes through here, so the self test can answer them.</summary>
    MessageBoxResult Ask(string text, string caption, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        if (testAnswers != null)
        {
            testPrompts.Add(text);
            return testAnswers.Count > 0 ? testAnswers.Dequeue() : MessageBoxResult.Cancel;
        }
        return MessageBox.Show(this, text, caption, buttons, image);
    }

    void ShowStatus(string msg) => StatusText.Text = DateTime.Now.ToString("HH:mm:ss") + "   " + msg;
    #endregion
}
