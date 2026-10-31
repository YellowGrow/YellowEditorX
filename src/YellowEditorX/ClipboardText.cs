using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace YellowEditorX;

/// <summary>
/// Text cut / copy / paste for the editor's text boxes without WPF's clipboard code.
/// Clipboard sync tools (for example the MuMu emulator) open the clipboard right after every change;
/// WPF then fails to finish a cut and waits a long time on paste. Here the text box changes at once,
/// the clipboard is written in the background as soon as it is free, and text copied in this program
/// is pasted from memory while nobody else has put anything on the clipboard.
/// </summary>
static class ClipboardText
{
    const uint CF_UNICODETEXT = 13;
    const uint GMEM_MOVEABLE = 0x2;

    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr hMem);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr h);

    static string? copied;
    static uint copiedSequence;
    static bool writePending;
    static DispatcherTimer? retry;
    static DateTime retryUntil;
    static IntPtr owner;

    /// <summary>Self test only: replaces the real clipboard (true = the write worked, false = clipboard busy).</summary>
    public static Func<bool>? TestWrite;
    public static int TestWrites;

    /// <summary>Called when the clipboard could not be written at all.</summary>
    public static Action<string>? Failed;

    /// <summary>Routes Cut / Copy / Paste of every TextBox inside the window through this class.</summary>
    public static void Attach(Window w)
    {
        owner = new WindowInteropHelper(w).EnsureHandle();
        CommandManager.AddPreviewExecutedHandler(w, OnExecuted);
        CommandManager.AddPreviewCanExecuteHandler(w, OnCanExecute);
    }

    static void OnCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste || e.OriginalSource is not TextBox tb) return;
        // WPF's own check opens the clipboard; this one doesn't.
        e.CanExecute = !tb.IsReadOnly && (writePending || TestWrite != null && copied != null || IsClipboardFormatAvailable(CF_UNICODETEXT));
        e.Handled = true;
    }

    static void OnExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.OriginalSource is not TextBox tb) return;
        if (e.Command == ApplicationCommands.Copy || e.Command == ApplicationCommands.Cut)
        {
            e.Handled = true;
            string text = tb.SelectedText;
            if (text.Length == 0) return;
            SetText(text);
            if (e.Command == ApplicationCommands.Cut && !tb.IsReadOnly) tb.SelectedText = "";
        }
        else if (e.Command == ApplicationCommands.Paste)
        {
            e.Handled = true;
            if (tb.IsReadOnly) return;
            string? text = GetText();
            if (string.IsNullOrEmpty(text)) return;
            text = tb.AcceptsReturn
                ? text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n")
                : text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            int start = tb.SelectionStart;
            tb.SelectedText = text;
            tb.Select(start + text.Length, 0);
        }
    }

    public static void SetText(string text)
    {
        copied = text;
        writePending = true;
        if (TryWrite()) return;
        // Someone holds the clipboard: keep trying in the background for a few seconds.
        retryUntil = DateTime.Now.AddSeconds(5);
        if (retry == null)
        {
            retry = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(15) };
            retry.Tick += (_, _) =>
            {
                if (TryWrite()) retry!.Stop();
                else if (DateTime.Now > retryUntil)
                {
                    retry!.Stop();
                    writePending = false;
                    Failed?.Invoke("다른 프로그램이 클립보드를 쓰고 있어서 복사한 내용을 클립보드에 넣지 못했어요. (이 프로그램 안에서는 붙여넣을 수 있어요)");
                }
            };
        }
        retry.Start();
    }

    static bool TryWrite()
    {
        if (copied == null) return false;
        if (TestWrite != null)
        {
            if (!TestWrite()) return false;
            TestWrites++;
            writePending = false;
            copiedSequence = GetClipboardSequenceNumber();
            return true;
        }
        if (!OpenClipboard(owner)) return false;
        try
        {
            EmptyClipboard();
            int bytes = (copied.Length + 1) * 2;
            IntPtr h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (h == IntPtr.Zero) return false;
            IntPtr p = GlobalLock(h);
            Marshal.Copy(copied.ToCharArray(), 0, p, copied.Length);
            Marshal.WriteInt16(p, copied.Length * 2, 0);
            GlobalUnlock(h);
            if (SetClipboardData(CF_UNICODETEXT, h) == IntPtr.Zero)
            {
                GlobalFree(h);
                return false;
            }
        }
        finally
        {
            CloseClipboard();
        }
        copiedSequence = GetClipboardSequenceNumber();
        writePending = false;
        return true;
    }

    public static string? GetText()
    {
        // Our own copy is still the newest clipboard content (or not written yet): no need to open the clipboard.
        if (writePending || copied != null && GetClipboardSequenceNumber() == copiedSequence || TestWrite != null) return copied;
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
        var until = DateTime.Now.AddSeconds(2);
        while (!OpenClipboard(owner))
        {
            if (DateTime.Now > until) return null;
            Thread.Sleep(5);
        }
        try
        {
            IntPtr h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) return null;
            IntPtr p = GlobalLock(h);
            try { return Marshal.PtrToStringUni(p); }
            finally { GlobalUnlock(h); }
        }
        finally
        {
            CloseClipboard();
        }
    }
}
