using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YellowEditorX.Core;

namespace YellowEditorX;

/// <summary>
/// A scripted run through the real window: rows are selected, text is typed into the actual text boxes,
/// buttons are clicked and questions are answered from a queue. Only ever run on copied data (--selftest).
/// </summary>
public partial class MainWindow
{
    readonly StringBuilder testLog = new();
    int testFailures;

    void T(bool ok, string what)
    {
        if (!ok) testFailures++;
        testLog.AppendLine((ok ? "ok   " : "FAIL ") + what);
    }

    static Task Idle() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    static IEnumerable<T2> Children<T2>(DependencyObject root) where T2 : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T2 t) yield return t;
            foreach (var d in Children<T2>(c)) yield return d;
        }
    }

    TextBox StrBox(int i) => Children<TextBox>(StringsList).ElementAt(i);
    ToggleButton Chip(string text) => Children<ToggleButton>(Editor).First(b => b is not RadioButton && (b.Content as string) == text);
    RadioButton MainChip(string text) => Children<RadioButton>(Editor).First(b => (b.Content as string) == text);
    static void Press(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    async Task Select(int index)
    {
        CardList.SelectedItem = shownRows[index];
        await Idle();
    }

    void Answer(params MessageBoxResult[] answers)
    {
        testAnswers!.Clear();
        foreach (var a in answers) testAnswers.Enqueue(a);
        testPrompts.Clear();
    }

    void Snapshot(string dir, string name)
    {
        UpdateLayout();
        var rtb = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(this);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    public async Task<string> RunSelfTest(string outDir)
    {
        WindowState = WindowState.Normal;
        Width = 1920; Height = 1050;
        await Idle();
        var gelz = dbs.First(d => d.Title == "겔지");
        DbList.SelectedItem = gelz;
        await Idle();
        string db = gelz.Path;
        var files = new CardFiles(db);
        Card Row(long id) => CardDb.Read(db, id)!;

        T(allRows.Count == 14, $"14 cards listed ({allRows.Count})");
        T(NameBox.Text == "폐지 줍는 할아버지", "first card shown: " + NameBox.Text);
        T(CardImage.Source != null, "card picture shown");
        Snapshot(outDir, "self_1_start.png");

        // 1. a typed string, then moving to another card and discarding: nothing is written anywhere
        await Select(1);
        long id1 = shownRows[1].Card.Id;
        var before1 = Row(id1);
        StrBox(3).Text = "임시 스트링";
        await Idle();
        T(Title.StartsWith("●"), "title shows unsaved mark");
        Answer(MessageBoxResult.No);
        await Select(2);
        T(testPrompts.Count == 1 && testPrompts[0].Contains("저장하지 않은"), "asked about unsaved changes");
        T(Row(id1).SameAs(before1), "discard left the db unchanged");
        T(StrBox(3).Text == Row(shownRows[2].Card.Id).Str[3], "next card shows its own string 3");
        await Select(1);
        T(StrBox(3).Text == before1.Str[3], "string 3 back to saved value");

        // 2. edit the name, save, undo
        NameBox.Text = "할아버지 수정본";
        Press(SaveButton);
        await Idle();
        T(Row(id1).Name == "할아버지 수정본" && Row(id1).Ot == 0x20, "saved to db with rule 0x20");
        T(!Title.StartsWith("●"), "title clean after save");
        T(shownRows.First(r => r.Card.Id == id1).Name == "할아버지 수정본", "list shows new name");
        Press(UndoButton);
        await Idle();
        T(Row(id1).SameAs(before1), "undo restored the row exactly");
        T(NameBox.Text == before1.Name, "editor shows restored name");

        // 3. typing a string and switching with save: it lands on its own card only
        StrBox(3).Text = "1번 카드 전용";
        await Idle();
        Answer(MessageBoxResult.Yes);
        await Select(3);
        long id3 = shownRows[3].Card.Id;
        T(Row(id1).Str[3] == "1번 카드 전용", "string saved on its own card");
        T(Row(id3).Str[3] == "" && StrBox(3).Text == "", "next card did not get the string");
        T(currentRow != null && currentRow.Card.Id == id3 && CardList.SelectedItem == currentRow && shownRows.Contains(currentRow), "list selection follows to the next card after saving");

        // 3b. a long string wraps onto more lines instead of scrolling sideways
        double oneLine = StrBox(5).ActualHeight;
        string saved5 = StrBox(5).Text;
        StrBox(5).Text = string.Concat(Enumerable.Repeat("길게 이어지는 스트링 문장 ", 14));
        await Idle();
        T(StrBox(5).ActualHeight > oneLine * 1.8 && StrBox(5).LineCount >= 2, $"long string wraps ({oneLine:0} -> {StrBox(5).ActualHeight:0}, {StrBox(5).LineCount} lines)");
        Snapshot(outDir, "self_1b_wrap.png");
        StrBox(5).Text = saved5;
        await Idle();
        T(!model.IsDirty, "back to saved string is clean");

        // 3c. strings take line breaks and grow; cut / paste work while the clipboard is busy (MuMu)
        StrBox(6).Text = "첫 줄\r\n둘째 줄";
        await Idle();
        T(StrBox(6).AcceptsReturn && StrBox(6).LineCount == 2 && StrBox(6).ActualHeight > oneLine * 1.6, "line break in a string makes the box taller");
        T(model.ToCard().Str[6] == "첫 줄\r\n둘째 줄", "line break kept in the string");
        int busy = 3;
        ClipboardText.TestWrite = () => busy-- <= 0;
        ClipboardText.TestWrites = 0;
        var box = StrBox(6);
        box.Focus();
        box.SelectAll();
        ApplicationCommands.Cut.Execute(null, box);
        T(box.Text == "", "cut removes the text at once even though the clipboard is busy");
        ApplicationCommands.Paste.Execute(null, box);
        T(box.Text == "첫 줄\r\n둘째 줄" && box.CaretIndex == box.Text.Length, "paste right after cut is immediate");
        await Task.Delay(200);
        T(ClipboardText.TestWrites == 1, "clipboard written in the background once it was free");
        NameBox.Focus();
        NameBox.SelectAll();
        ApplicationCommands.Paste.Execute(null, NameBox);
        T(NameBox.Text == "첫 줄 둘째 줄", "multi-line paste into the name becomes one line: " + NameBox.Text);
        ClipboardText.TestWrite = null;
        Answer(MessageBoxResult.No);
        await Select(0);
        await Select(3);

        // 4. link: markers appear, arrows set the rating; pendulum shows scales; spell hides monster fields
        await Select(0);
        Chip("링크").IsChecked = true;
        await Idle();
        T(MarkerGrid.IsVisible, "link markers visible");
        var markers = Children<ToggleButton>(MarkerGrid).ToList();
        markers[1].IsChecked = true; markers[8].IsChecked = true;
        await Idle();
        T(model.Level == "2" && model.ToCard().Def == (0x80 | 0x4), $"link rating {model.Level}, def {model.ToCard().Def:x}");
        var markerFace = (System.Windows.Controls.Border)markers[1].Template.FindName("b", markers[1]);
        T(markerFace.Background is SolidColorBrush mb && mb.Color == ((SolidColorBrush)FindResource("Accent")).Color, "checked link marker is blue");
        Snapshot(outDir, "self_2_link.png");
        Chip("링크").IsChecked = false;
        Chip("펜듈럼").IsChecked = true;
        await Idle();
        T(ScaleLabel.IsVisible && !MarkerGrid.IsVisible, "pendulum scales visible");
        T(Chip("기프트").IsVisible && Chip("속공").IsVisible, "every sub type is offered for monsters too");
        string atkBefore = model.Atk, levelBefore = model.Level;
        long attrBefore = model.Attribute;
        MainChip("마법").IsChecked = true;
        await Idle();
        T(AttributeLabel.IsVisible && !ScaleLabel.IsVisible, "spell keeps attribute / race / level / stats, hides scales");
        T(model.Atk == atkBefore && model.Level == levelBefore && model.Attribute == attrBefore && model.ToCard().Atk == CardDb.Read(db, model.Original!.Id)!.Atk,
            "stats kept when switching to spell");
        T(Chip("속공").IsVisible && Chip("기프트").IsVisible && Chip("튜너").IsVisible && Chip("토큰").IsVisible, "spell offers every sub type");
        T(Chip("펜듈럼").IsChecked == true, "switching to spell keeps the sub types");
        Chip("펜듈럼").IsChecked = false;
        Chip("기프트").IsChecked = true;
        T((model.ToCard().Type & (CardTypes.Spell | CardTypes.Gift)) == (CardTypes.Spell | CardTypes.Gift), "기프트 spell type bits");
        MainChip("함정").IsChecked = true;
        await Idle();
        T(AttributeLabel.IsVisible && Chip("기프트").IsVisible && (model.ToCard().Type & CardTypes.Gift) != 0, "trap keeps stats and 기프트");
        Chip("기프트").IsChecked = false;
        MainChip("마법").IsChecked = true;
        await Idle();
        Snapshot(outDir, "self_3_spell.png");
        // 키-해금됨 brings 키 along; taking 키-해금됨 off leaves 키 on.
        MainChip("몬스터").IsChecked = true;
        await Idle();
        Chip("키").IsChecked = false;
        Chip("키-해금됨").IsChecked = true;
        await Idle();
        T(Chip("키").IsChecked == true && (model.ToCard().Type & (CardTypes.Key | CardTypes.Unlocked)) == (CardTypes.Key | CardTypes.Unlocked),
          "키-해금됨 turns 키 on too");
        Chip("키-해금됨").IsChecked = false;
        await Idle();
        T(Chip("키").IsChecked == true && (model.ToCard().Type & CardTypes.Unlocked) == 0, "키-해금됨 off leaves 키 on");
        Chip("키").IsChecked = false;
        Chip("키-해금됨").IsChecked = false;

        // 5. duplicate (discarding the edits above), save, picture copied
        Answer(MessageBoxResult.No);
        Press(DuplicateButton);
        await Idle();
        T(model.Code == "101232014" && model.IsNew, "duplicate got next free code " + model.Code);
        T(model.Name == "폐지 줍는 할아버지" && model.IsMonster, "duplicate keeps the saved card, not the discarded edits");
        NameBox.Text = "복제 테스트";
        Answer();
        Press(SaveButton);
        await Idle();
        T(CardDb.Read(db, 101232014)?.Name == "복제 테스트", "duplicate saved");
        T(File.Exists(files.Picture(101232014)) && File.Exists(files.Thumbnail(101232014)), "duplicate picture copied");
        T(!File.Exists(files.Script(101232014)), "duplicate does not copy the script");
        T(allRows.Count == 15 && currentRow?.Card.Id == 101232014, "list has 15 cards, new one selected");
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        T(Row(101232014).Created == today && Row(101232014).Modified == today && CreatedBox.Text == today, "new card saved with today's dates");

        // 6. code change: move the card with its files
        CodeBox.Text = "101232099";
        Answer(MessageBoxResult.Yes);
        Press(SaveButton);
        await Idle();
        T(CardDb.Read(db, 101232014) == null && CardDb.Read(db, 101232099) != null, "card moved to new code");
        T(File.Exists(files.Picture(101232099)) && !File.Exists(files.Picture(101232014)), "picture renamed");

        // 7. delete with files, undo
        Answer(MessageBoxResult.OK);
        Press(DeleteButton);
        await Idle();
        T(CardDb.Read(db, 101232099) == null && !File.Exists(files.Picture(101232099)) && allRows.Count == 14, "deleted with picture");
        Press(UndoButton);
        await Idle();
        T(CardDb.Read(db, 101232099) != null && File.Exists(files.Picture(101232099)) && allRows.Count == 15, "undo delete");

        // 8. image import replaces the picture at 177x254, undo puts the old one back
        var oldPic = File.ReadAllBytes(files.Picture(101232099));
        var red = BitmapSource.Create(400, 600, 96, 96, PixelFormats.Bgr32, null, Enumerable.Repeat((byte)0x80, 400 * 600 * 4).ToArray(), 400 * 4);
        ImportImage(red, "test");
        await Idle();
        var pic = ImageTools.Load(files.Picture(101232099))!;
        var thumb = ImageTools.Load(files.Thumbnail(101232099))!;
        T(pic.PixelWidth == 177 && pic.PixelHeight == 254 && thumb.PixelWidth == 44 && thumb.PixelHeight == 64, "imported picture and thumbnail sizes");
        Press(UndoButton);
        await Idle();
        T(File.ReadAllBytes(files.Picture(101232099)).SequenceEqual(oldPic), "undo image import");

        // 8b. the image dialog opens only for a click that started on the image box
        //     (double-clicking a file in the dialog lets the button go over the box afterwards)
        MouseButtonEventArgs MouseArgs(RoutedEvent ev) => new(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = ev };
        testImageDialogs = 0;
        ImageBox.RaiseEvent(MouseArgs(UIElement.MouseLeftButtonUpEvent));
        T(testImageDialogs == 0, "button released over the image box without a press there: no dialog");
        ImageBox.RaiseEvent(MouseArgs(UIElement.MouseLeftButtonDownEvent));
        ImageBox.RaiseEvent(MouseArgs(UIElement.MouseLeftButtonUpEvent));
        T(testImageDialogs == 1, "click on the image box opens the dialog");
        ImageBox.RaiseEvent(MouseArgs(UIElement.MouseLeftButtonDownEvent));
        RaiseEvent(MouseArgs(UIElement.PreviewMouseLeftButtonDownEvent)); // a later press somewhere else
        ImageBox.RaiseEvent(MouseArgs(UIElement.MouseLeftButtonUpEvent));
        T(testImageDialogs == 1, "a press elsewhere cancels a pending image click");

        // 8c. dropping an image file anywhere on the window makes it the card picture
        string dropPng = Path.Combine(outDir, "drop_test.png");
        using (var fs = File.Create(dropPng)) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(red)); enc.Save(fs); }
        long shownId = long.Parse(model.Code);
        byte[]? shownPic = File.Exists(files.Picture(shownId)) ? File.ReadAllBytes(files.Picture(shownId)) : null;
        T(DropFiles(new[] { dropPng }) && File.Exists(files.Picture(shownId)) && ImageTools.Load(files.Picture(shownId))!.PixelWidth == 177
          && (shownPic == null || !File.ReadAllBytes(files.Picture(shownId)).SequenceEqual(shownPic)), "dropped image file imported for the shown card " + shownId);
        Press(UndoButton);
        await Idle();
        T(shownPic == null ? !File.Exists(files.Picture(shownId)) : File.ReadAllBytes(files.Picture(shownId)).SequenceEqual(shownPic), "undo dropped image");
        T(!DropFiles(new[] { Path.Combine(outDir, "notes.txt") }), "other files are not taken");

        // 9. copy to another database with files, then undo
        var other = dbs.First(d => d.Title == "결연희");
        CardList.SelectedItems.Clear();
        CardList.SelectedItem = shownRows.First(r => r.Card.Id == 101232099);
        await Idle();
        Answer();
        CopySelectedTo(other, move: false);
        var otherFiles = new CardFiles(other.Path);
        T(CardDb.Read(other.Path, 101232099)?.Name == "복제 테스트" && File.Exists(otherFiles.Picture(101232099)), "copied to 결연희 with picture");
        Press(UndoButton);
        await Idle();
        T(CardDb.Read(other.Path, 101232099) == null && !File.Exists(otherFiles.Picture(101232099)), "undo copy");

        // 10. filter, sort
        FilterBox.Text = "미래가 함정";
        await Idle();
        T(shownRows.Count == 3, $"filter '미래가 함정' (kind counts too) -> {shownRows.Count}");
        FilterBox.Text = "";
        sortKey = "Name"; sortAscending = true; ApplyFilter();
        T(shownRows.Select(r => r.Name).SequenceEqual(shownRows.Select(r => r.Name).OrderBy(n => n)), "sort by name");
        sortKey = "Code"; ApplyFilter();

        // 11. new card asks nothing when clean and gets the next code
        await Select(0);
        Answer();
        Press(NewButton);
        await Idle();
        T(testPrompts.Count == 0 && model.IsNew && model.Code == "101232100", "new card code " + model.Code);
        T(!model.HasUnsavedWork, "blank new card is not unsaved work");
        Snapshot(outDir, "self_4_new.png");

        // 12. switching database with unsaved work asks first; cancel stays
        NameBox.Text = "작성 중";
        Answer(MessageBoxResult.Cancel);
        DbList.SelectedItem = other;
        await Idle();
        T(testPrompts.Count == 1 && currentDb == gelz && DbList.SelectedItem == gelz && model.Name == "작성 중", "cancel keeps the database and the text");
        Answer(MessageBoxResult.No);
        DbList.SelectedItem = other;
        await Idle();
        T(currentDb == other && allRows.Count == 25, "switched to 결연희 (" + allRows.Count + ")");

        // 13. 제작 정보: footer lines shown as fields, saved back without [카드군], dates only in the table
        await Select(0);
        var lp = CardDb.Read(other.Path, currentRow!.Card.Id)!;
        T(MakerBox.Text == "이피 / YiPi" && TagsBox.Text.Length > 0 && !DescBox.Text.Contains("[이피") && !model.IsDirty,
            $"footer shown in fields: '{MakerBox.Text}' / '{TagsBox.Text}'");
        T(FooterFields.IsVisible && MakerBox.ActualWidth > 80, "footer fields visible");
        Snapshot(outDir, "self_5_footer.png");
        TagsBox.Text += ", 추가 태그";
        Answer();
        Press(SaveButton);
        await Idle();
        var ls = CardDb.Read(other.Path, lp.Id)!;
        T(ls.Desc.EndsWith("[이피 / YiPi]\r\n[태그: 연옥의 결연희, 추가 태그]") && !ls.Desc.Contains("카드군") && !ls.Desc.Contains("수정일"),
            "footer saved, comma in the tag kept as typed: " + ls.Desc[^60..].Replace("\r\n", "⏎"));
        // A card number the site can't read is caught before saving.
        NumberBox.Text = "AB-1";
        Answer(MessageBoxResult.OK);
        Press(SaveButton);
        await Idle();
        T(testPrompts.Count == 1 && testPrompts[0].Contains("카드 번호") && model.IsDirty, "loose card number stops the save");
        NumberBox.Text = "";
        T(!model.IsDirty, "clearing the number again is clean");
        T(ls.Modified == today && ls.Created == "" && ModifiedBox.Text == today, "edited card: 수정일 today, 제작일 untouched");
        T(ls.Ot == 0x20, $"rule 0x20 (was 0x{lp.Ot:x})");
        ls.Desc = lp.Desc; ls.Modified = ""; ls.Ot = lp.Ot;
        T(ls.SameAs(lp), "nothing else changed");
        // 숨김 adds SCOPE_HIDDEN (0x1000) to the saved rule and takes it off again.
        T(HiddenBox.IsVisible && HiddenBox.IsChecked == false, "숨김 box shown, off");
        HiddenBox.IsChecked = true;
        T(model.IsDirty, "숨김 makes the card unsaved");
        Answer();
        Press(SaveButton);
        await Idle();
        T(CardDb.Read(other.Path, lp.Id)!.Ot == (Card.RuleCustom | Card.ScopeHidden) && HiddenBox.IsChecked == true && !model.IsDirty, "숨김 saved as ot 0x1020");
        HiddenBox.IsChecked = false;
        Answer();
        Press(SaveButton);
        await Idle();
        T(CardDb.Read(other.Path, lp.Id)!.Ot == Card.RuleCustom, "숨김 off saved as ot 0x20");
        Answer();
        Press(NewButton);
        await Idle();
        T(MakerBox.Text == "이피 / YiPi" && !model.HasUnsavedWork, "new card gets the last maker");

        // 14. opening a cdb adds it to the list; CDB 닫기 takes it off again and leaves the file
        string outside = Path.Combine(outDir, "outside_test", "Outside.cdb");
        if (File.Exists(outside)) File.Delete(outside);
        CardDb.Create(outside);
        CardDb.Write(outside, new[] { new Card { Id = 5, Name = "외부 카드", Type = CardTypes.Monster } });
        AddDb(outside);
        RefreshDbList(outside);
        await Idle();
        var ext = dbs.FirstOrDefault(d => SamePath(d.Path, outside));
        T(ext != null && ext.Title == "outside_test" && currentDb == ext && allRows.Count == 1, "opened cdb added and shown");
        Snapshot(outDir, "self_6_open.png");
        // Right-click must work on the empty right part of a row too, not only on the text.
        var item = (ListBoxItem)DbList.ItemContainerGenerator.ContainerFromItem(ext!);
        var hit = item.InputHitTest(new Point(item.ActualWidth - 10, item.ActualHeight / 2)) as DependencyObject;
        while (hit != null && !(hit is FrameworkElement { ContextMenu: not null })) hit = VisualTreeHelper.GetParent(hit);
        T(hit != null, "right edge of a cdb row has the CDB 닫기 menu");
        NameBox.Text = "고치는 중";
        Answer(MessageBoxResult.Cancel);
        CloseDb(ext!);
        await Idle();
        T(currentDb == ext && dbs.Contains(ext!) && model.Name == "고치는 중", "cancel keeps the cdb open");
        Answer(MessageBoxResult.No);
        CloseDb(ext!);
        await Idle();
        T(!dbs.Any(d => SamePath(d.Path, outside)) && !settings.OpenDbs.Any(p => SamePath(p, outside)) && File.Exists(outside), "closed: off the list, file kept");
        T(currentDb != null && currentDb != ext && allRows.Count > 0, "another cdb shown after closing the current one: " + currentDb?.Title);
        var shown = currentDb!;
        int before = dbs.Count;
        Answer();
        CloseDb(dbs.First(d => d != shown));
        T(dbs.Count == before - 1 && currentDb == shown && testPrompts.Count == 0, "closing another cdb asks nothing and keeps the current one");

        // 14b. dropping a .cdb file opens it; search finds cards by a piece of their text
        Answer();
        T(DropFiles(new[] { outside }) && currentDb != null && SamePath(currentDb.Path, outside) && dbs.Count == before, "dropped cdb opened");
        Answer();
        DbList.SelectedItem = dbs.First(d => SamePath(d.Path, shown.Path)); // the list was read again, so a new entry
        await Idle();
        var withText = allRows.First(r => r.Card.Desc.Replace("\r\n", "").Length > 30).Card;
        string piece = withText.Desc.Replace("\r\n", " ").Substring(10, 8).Trim();
        FilterBox.Text = piece;
        await Idle();
        T(shownRows.Any(r => r.Card.Id == withText.Id) && shownRows.Count < allRows.Count, $"search by card text '{piece}' → {shownRows.Count}");
        FilterBox.Text = withText.Name;
        await Idle();
        T(shownRows.Any(r => r.Card.Id == withText.Id), "search by name");
        // Each cdb keeps its own search and chosen card while going to another one and back.
        FilterBox.Text = piece;
        await Idle();
        var keep = shownRows[^1];
        CardList.SelectedItem = keep;
        await Idle();
        Answer();
        DbList.SelectedItem = dbs.First(d => SamePath(d.Path, outside));
        await Idle();
        T(FilterBox.Text == "" && shownRows.Count == allRows.Count, "other cdb starts with an empty search");
        FilterBox.Text = "외부";
        await Idle();
        DbList.SelectedItem = dbs.First(d => SamePath(d.Path, shown.Path));
        await Idle();
        T(FilterBox.Text == piece && currentRow?.Card.Id == keep.Card.Id && keep.Card.Id != shownRows[0].Card.Id && CardList.SelectedItem == currentRow,
            $"back: search '{FilterBox.Text}' and card {currentRow?.Card.Id} kept");
        DbList.SelectedItem = dbs.First(d => SamePath(d.Path, outside));
        await Idle();
        T(FilterBox.Text == "외부" && currentRow?.Card.Id == 5, "the other cdb kept its own search");
        DbList.SelectedItem = dbs.First(d => SamePath(d.Path, shown.Path));
        await Idle();
        FilterBox.Text = "";

        // 14c. card groups, races and attributes follow CyanPro while running; what the card on screen uses stays
        long shownRace = model.Race, shownAttribute = model.Attribute;
        bool dirtyBefore = model.IsDirty;
        string fakeStrings = "#setnames\n!setname 0x9f01\t테스트 카드군\n!setname 0x300\t마칭 파이어\n";
        string fakeHeader = "#define RACE_WARRIOR 0x1\n#define RACE_NEWKIND 0x100000000\n#define RACE_ALL (RACE_WARRIOR)\n#define ATTRIBUTE_EARTH 0x01\n"
                          + "#define TYPE_MONSTER 0x1\n#define TYPE_NEWKIND 0x80000000\n";
        int changed = ApplyRemote((fakeStrings, fakeHeader, null), live: true, out bool typesChanged);
        await Idle();
        T(model.IsDirty == dirtyBefore, "syncing doesn't make the card unsaved");
        model.Setcodes[3].Hex = "9f01";
        T(changed >= 2 && model.SetnameItems.Any(s => s.Text == "테스트 카드군") && model.Setcodes[3].Selected?.Text == "테스트 카드군"
          && model.Races.Any(r => r.Value == 0x100000000 && r.Text == "RACE_NEWKIND"), $"GitHub lists synced live ({changed})");
        T(model.SetnameItems.All(s => s.Value <= 0 || s.Value is 0x9f01 or 0x300 || model.Setcodes.Any(c => c.Value == s.Value))
          && model.Races.All(r => r.Value is 0 or 0x1 or 0x100000000 || r.Value == shownRace)
          && model.Attributes.All(a => a.Value is 0 or 0x1 || a.Value == shownAttribute), "values CyanPro doesn't have are taken out");
        T(model.Race == shownRace && model.Attribute == shownAttribute
          && model.Races.Any(r => r.Value == shownRace), "the card on screen keeps its race and attribute");
        T(typesChanged, "a changed type list is noticed");
        T(ApplyRemote((fakeStrings, fakeHeader, null), live: true) == 0, "same list again changes nothing");

        // 14d. after a restart by the updater: a good update refreshes the Windows icons, a failed one says so
        string? oldLog = File.Exists(UpdateLog) ? File.ReadAllText(UpdateLog) : null;
        Directory.CreateDirectory(UpdateDir);
        File.WriteAllText(UpdateLog, "ok");
        settings.UpdatingTo = "9.9.9";
        Answer();
        ReportUpdate();
        T(iconRefreshes == 1 && settings.UpdatingTo == null && testPrompts.Count == 0 && StatusText.Text.Contains("업데이트했어요"),
          "successful update: status shown, icons refreshed");
        File.WriteAllText(UpdateLog, "failed: test");
        settings.UpdatingTo = "9.9.9";
        Answer(MessageBoxResult.OK);
        ReportUpdate();
        T(iconRefreshes == 1 && testPrompts.Count == 1 && testPrompts[0].Contains("failed: test"), "failed update: told, no icon refresh");
        ReportUpdate();
        T(iconRefreshes == 1, "nothing to report on a normal start");
        if (oldLog != null) File.WriteAllText(UpdateLog, oldLog); else File.Delete(UpdateLog);
        model.Setcodes[3].Hex = "";

        // 15. settings from the workspace version become the open list
        string wsRoot = Path.GetDirectoryName(Path.GetDirectoryName(gelz.Path))!;
        var old = new Settings { Workspace = wsRoot, ExtraDbs = new() { outside }, ReadOnly = true };
        old.MigrateWorkspace();
        T(old.OpenDbs.Count == 16 && old.Workspace == null && old.ExtraDbs == null, $"workspace settings migrated ({old.OpenDbs.Count} cdbs)");

        model.Load(null);
        testLog.AppendLine(testFailures == 0 ? "SELFTEST ALL PASS" : $"{testFailures} SELFTEST FAILURES");
        return testLog.ToString();
    }
}
