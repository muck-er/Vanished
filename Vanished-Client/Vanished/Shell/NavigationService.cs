using Avalonia.Controls;
using System.Collections.Generic;
using Vanished.UI;

namespace Vanished.Shell;

public interface INavigationTarget<in TParameters>
{
    void OnNavigatedTo(TParameters parameters);
}

public static class NavigationService
{
    private static ContentControl? _host;
    private static readonly Stack<Control> _history = new();
    private static Control? _current;

    public static bool CanGoBack => _history.Count > 0;

    public static void Initialize(ContentControl host) => _host = host;

    public static void Navigate(Control view, bool addToHistory = true)
    {
        if (_host == null)
            return;

        if (addToHistory && _current != null)
            _history.Push(_current);

        _current = view;
        view.Opacity = 0;
        _host.Content = view;
        Ui.SoftFadeIn(view);
    }

    public static void Navigate<TParameters>(Control view, TParameters parameters, bool addToHistory = true)
    {
        if (view is INavigationTarget<TParameters> target)
            target.OnNavigatedTo(parameters);
        Navigate(view, addToHistory);
    }

    public static void GoBack()
    {
        if (_host == null)
            return;

        if (_history.Count == 0)
        {
            Navigate(new Vanished.Pages.ChatPage(), false);
            return;
        }

        var view = _history.Pop();
        _current = view;
        view.Opacity = 0;
        _host.Content = view;
        Ui.SoftFadeIn(view);
    }

    public static void Reset(Control view)
    {
        _history.Clear();
        Navigate(view, false);
    }
}
