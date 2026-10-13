using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rahbar.Core.Storage;

namespace Rahbar.App.Windows;

/// <summary>Last 50 questions and answers (text only) with search and copy.</summary>
internal sealed class HistoryWindow : Window
{
    private readonly HistoryStore _store;
    private readonly TextBox _search = new() { Padding = new Thickness(6, 4, 6, 4), FontSize = 14 };
    private readonly ListBox _list = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };

    public HistoryWindow(HistoryStore store)
    {
        _store = store;
        Title = "Rahbar – History";
        Width = 640;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI, Nirmala UI, Urdu Typesetting");
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/rahbar.ico"));

        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _search.TextChanged += (_, _) => Refresh();

        var copy = new Button { Content = "Copy" };
        copy.Click += (_, _) => CopySelected();
        var clear = new Button { Content = "Clear history" };
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Delete all saved questions and answers?", "Rahbar", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                _store.Clear();
        };

        var top = new DockPanel { Margin = new Thickness(12, 12, 12, 8) };
        top.Children.Add(new TextBlock { Text = "Search", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        top.Children.Add(_search);
        DockPanel.SetDock(top, Dock.Top);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 8, 4, 12) };
        bottom.Children.Add(copy);
        bottom.Children.Add(clear);
        DockPanel.SetDock(bottom, Dock.Bottom);

        var root = new DockPanel();
        root.Children.Add(top);
        root.Children.Add(bottom);
        _list.Margin = new Thickness(12, 0, 12, 0);
        root.Children.Add(_list);
        Content = root;

        _store.Changed += OnChanged;
        Closed += (_, _) => _store.Changed -= OnChanged;
        Refresh();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        _list.Items.Clear();
        foreach (var e in _store.Search(_search.Text))
        {
            var panel = new StackPanel { Margin = new Thickness(4, 6, 4, 6), Tag = e };
            panel.Children.Add(new TextBlock
            {
                Text = $"{e.Time:ddd d MMM, HH:mm}  ·  {e.App}",
                Foreground = Brushes.Gray, FontSize = 11,
            });
            panel.Children.Add(new TextBlock { Text = e.Question, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = e.Answer, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            _list.Items.Add(panel);
        }
        if (_list.Items.Count == 0)
            _list.Items.Add(new TextBlock { Text = "Nothing here yet.", Foreground = Brushes.Gray, Margin = new Thickness(6) });
    }

    private void CopySelected()
    {
        if (_list.SelectedItem is not StackPanel { Tag: HistoryEntry e })
        {
            MessageBox.Show(this, "Click a question first, then press Copy.", "Rahbar");
            return;
        }
        Clipboard.SetText($"Q: {e.Question}\r\nA: {e.Answer}");
    }
}
