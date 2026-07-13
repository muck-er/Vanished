using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanished.API.Helpers;
using Vanished.API.Models;
using Vanished.API.Services;
using Vanished.Shell;
using Vanished.UI;

namespace Vanished.Pages;

public sealed class ChatPage : UserControl
{
    private readonly ObservableCollection<ConversationVm> _threads = new();
    private readonly ObservableCollection<MessageVm> _messages = new();
    private readonly ObservableCollection<PublicUser> _searchResults = new();
    private readonly ObservableCollection<ConversationVm> _allThreads = new();
    private readonly Dictionary<int, List<MessageVm>> _localSystemMessages = new();
    private readonly Dictionary<int, long> _lastMarkedReadByPeer = new();
    private readonly HashSet<int> _typingPeers = new();
    private readonly HashSet<int> _onlinePeers = new();
    private readonly Dictionary<int, DispatcherTimer> _typingExpiryTimers = new();
    private CancellationTokenSource? _conversationLoadCts;
    private Border? _connectionBanner;
    private TextBlock? _connectionBannerText;
    private ContentControl? _connectionBannerSpinnerHost;
    private DispatcherTimer? _connectionBannerHideTimer;

    private readonly ItemsControl _threadList = new();
    private readonly ItemsControl _messageList = new();
    private readonly ItemsControl _searchList = new();
    private ItemsControl? _newSearchList;

    private readonly ScrollViewer _messagesScroll = new();
    private readonly Grid _messagesArea = new();
    private readonly StackPanel _messagesScrollContent = Ui.V(0);
    private readonly Button _scrollToBottomButton = Ui.IconButtonName("chevron_down");
    private int _unreadWhileScrolled;
    private readonly ContentControl _messageHost = new();
    private readonly ContentControl _sidePanel = new();
    private readonly Border _dialogOverlay = new();

    private readonly TextBox _messageInput = Ui.TextBox("Mensagem");
    private readonly TextBox _search = Ui.TextBox("Pesquisar conversas...");
    private TextBox? _newSearch;
    private ContentControl? _newSearchIconHost;
    private readonly TextBlock _title = Ui.TextBlock("Vanished", 20, Ui.Text, FontWeight.SemiBold);
    private readonly TextBlock _subtitle = Ui.TextBlock("Seleciona uma conversa", 12, Ui.Muted);
    private readonly TextBlock _profileName = Ui.TextBlock("", 15, Ui.Text, FontWeight.SemiBold);
    private readonly TextBlock _profileEmail = Ui.TextBlock("", 12, Ui.Muted);
    private readonly TextBlock _profileAvatarInitial = new()
    {
        Text = "V",
        Foreground = Ui.Text,
        FontSize = 16.5,
        FontWeight = FontWeight.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center
    };
    private readonly ContentControl _profileAvatarImageHost = new();
    private double _profileAvatarSize = 44;
    private readonly TextBlock _status = Ui.StatusBlock();
    private readonly TextBlock _searchState = Ui.TextBlock("", 12, Ui.Muted);
    private TextBlock? _newSearchState;
    private readonly ContentControl _typingIndicator = new() { IsVisible = false };
    private readonly StackPanel _actions = Ui.H(8);
    private readonly Button _send = Ui.PrimaryButton("Enviar");
    private readonly Button _closeChatButton = Ui.IconButtonName("close");
    private readonly Button _inboxButton = Ui.GhostButton("Conversas");
    private readonly Button _requestsButton = Ui.IconButtonName("request");
    private readonly TextBlock _requestBadgeText = Ui.TextBlock("", 10, Brushes.White, FontWeight.Bold);
    private readonly Border _tabIndicator = new();
    private readonly Canvas _tabCanvas = new();
    private readonly TranslateTransform _tabIndicatorTransform = new();
    private readonly ContentControl _tabContentHost = new();
    private readonly Border _composer = new();

    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _newSearchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _typingStopTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private DispatcherTimer? _tabAnimationTimer;

    private PublicUser? _selectedPeer;
    private string _selectedStatus = "accepted";
    private bool _selectedFromRequests;
    private bool _showingRequests;
    private bool _loading;
    private bool _sending;
    private bool _searching;
    private bool _lastTypingSent;
    private DateTime _lastTypingSentAtUtc = DateTime.MinValue;
    private bool _profileEventsSubscribed;
    private bool _wsEventsSubscribed;
    private bool _closingConversation;
    private double _tabIndicatorX;
    private double _tabIndicatorWidth = 100;

    public ChatPage()
    {
        _threadList.ItemsSource = _threads;
        _threadList.ItemTemplate = new FuncDataTemplate<ConversationVm>((vm, _) => BuildThreadRow(vm), true);

        _searchList.ItemsSource = _searchResults;
        _searchList.ItemTemplate = new FuncDataTemplate<PublicUser>((u, _) => BuildSearchRow(u), true);

        _messageList.ItemsSource = _messages;
        _messageList.ItemTemplate = new FuncDataTemplate<MessageVm>((vm, _) => BuildMessageBubble(vm), true);
        _status.IsVisible = false;
        _typingIndicator.IsVisible = false;
        _messagesScrollContent.Children.Add(_messageList);
        _messagesScrollContent.Children.Add(_typingIndicator);
        _messagesScroll.Content = _messagesScrollContent;
        _messagesScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _messagesScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        ConfigureMessagesArea();

        _search.TextChanged += (_, _) => RestartConversationFilterDebounce();
        _search.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    e.Handled = true;
                    _searchDebounceTimer.Stop();
                    ApplyConversationFilter();
                }
            },
            routes: RoutingStrategies.Tunnel,
            handledEventsToo: false);

        _send.Click += async (_, _) => await SendAsync();
        _messageInput.AcceptsReturn = true;
        _messageInput.TextWrapping = TextWrapping.Wrap;
        _messageInput.MinHeight = 36;
        _messageInput.MaxHeight = 120;
        _messageInput.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _messageInput.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        _messageInput.VerticalContentAlignment = VerticalAlignment.Top;
        _messageInput.ClipToBounds = true;
        _messageInput.TextChanged += (_, _) => HandleLocalTypingChanged();
        _messageInput.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (e.Key != Key.Enter) return;

                e.Handled = true;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    var pos = _messageInput.CaretIndex;
                    var current = _messageInput.Text ?? string.Empty;
                    _messageInput.Text = current.Insert(pos, Environment.NewLine);
                    _messageInput.CaretIndex = pos + Environment.NewLine.Length;
                    return;
                }

                var content = (_messageInput.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(content) || _sending) return;
                _ = SendAsync(content);
            },
            routes: RoutingStrategies.Tunnel,
            handledEventsToo: false);

        _inboxButton.Click += async (_, _) => { _showingRequests = false; UpdateTabsVisual(); Ui.SoftFadeIn(_tabContentHost); await RefreshAsync(true); };

        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            ApplyConversationFilter();
        };

        _newSearchDebounceTimer.Tick += async (_, _) =>
        {
            _newSearchDebounceTimer.Stop();
            await SearchUsersAsync();
        };

        _typingStopTimer.Tick += async (_, _) =>
        {
            _typingStopTimer.Stop();
            await SendTypingAsync(false);
        };

        UpdateProfileHeader();
        Content = BuildRoot();
        UpdateProfileHeader();
        SubscribeProfileEvents();
        Ui.ThemeChanged += OnThemeChanged;
        UpdateChatState();

        AttachedToVisualTree += async (_, _) =>
        {
            SubscribeProfileEvents();
            SubscribeWebSocketEvents();
            MergeCachedPresenceSnapshot();
            Ui.ThemeChanged -= OnThemeChanged;
            Ui.ThemeChanged += OnThemeChanged;
            UpdateProfileHeader();
            await ApiService.Connection.ConnectAsync();
            ApplyCachedPresenceSnapshot();
            await RefreshAsync(true);
            ApplyCachedPresenceSnapshot();
        };
        DetachedFromVisualTree += async (_, _) =>
        {
            _searchDebounceTimer.Stop();
            _newSearchDebounceTimer.Stop();
            _typingStopTimer.Stop();
            _conversationLoadCts?.Cancel();
            await SendTypingAsync(false);
            UnsubscribeWebSocketEvents();
            ApiService.Connection.StateChanged -= OnConnectionStateChanged;
            UnsubscribeProfileEvents();
            Ui.ThemeChanged -= OnThemeChanged;
        };
    }

    private void ConfigureMessagesArea()
    {
        _scrollToBottomButton.Width = 42;
        _scrollToBottomButton.Height = 42;
        _scrollToBottomButton.MinWidth = 42;
        _scrollToBottomButton.MinHeight = 42;
        _scrollToBottomButton.CornerRadius = new CornerRadius(21);
        _scrollToBottomButton.Background = Ui.AccentSoft;
        _scrollToBottomButton.BorderBrush = Ui.Accent;
        _scrollToBottomButton.BorderThickness = new Thickness(1);
        _scrollToBottomButton.Foreground = Ui.Text;
        _scrollToBottomButton.Padding = new Thickness(0);
        _scrollToBottomButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _scrollToBottomButton.VerticalContentAlignment = VerticalAlignment.Center;
        _scrollToBottomButton.HorizontalAlignment = HorizontalAlignment.Right;
        _scrollToBottomButton.VerticalAlignment = VerticalAlignment.Bottom;
        _scrollToBottomButton.Margin = new Thickness(0, 0, 18, 18);
        _scrollToBottomButton.IsVisible = false;
        _scrollToBottomButton.Opacity = 0;
        _scrollToBottomButton.PointerEntered += (_, _) =>
        {
            _scrollToBottomButton.Background = Ui.Accent;
            _scrollToBottomButton.BorderBrush = Ui.AccentHover;
            _scrollToBottomButton.Opacity = 1;
        };
        _scrollToBottomButton.PointerExited += (_, _) =>
        {
            _scrollToBottomButton.Background = Ui.AccentSoft;
            _scrollToBottomButton.BorderBrush = Ui.Accent;
            _scrollToBottomButton.Opacity = _scrollToBottomButton.IsVisible ? 1 : 0;
        };
        ToolTip.SetTip(_scrollToBottomButton, "Ir para mensagens recentes");
        _scrollToBottomButton.Click += (_, e) =>
        {
            e.Handled = true;
            _unreadWhileScrolled = 0;
            ScrollToEnd();
            UpdateScrollToBottomButton();
        };

        _messagesArea.Children.Add(_messagesScroll);
        _messagesArea.Children.Add(_scrollToBottomButton);
        _messagesScroll.ScrollChanged += (_, _) => UpdateScrollToBottomButton();
    }

    private bool IsNearMessagesBottom(double threshold = 150)
    {
        var distance = _messagesScroll.Extent.Height - (_messagesScroll.Offset.Y + _messagesScroll.Viewport.Height);
        return distance <= threshold || _messagesScroll.Extent.Height <= 0;
    }

    private void UpdateScrollToBottomButton()
    {
        var shouldShow = !IsNearMessagesBottom(150);
        _scrollToBottomButton.IsVisible = shouldShow;
        _scrollToBottomButton.Opacity = shouldShow ? 1 : 0;
        if (!shouldShow) _unreadWhileScrolled = 0;
        _scrollToBottomButton.Content = _unreadWhileScrolled > 0
            ? BuildScrollToBottomBadge(_unreadWhileScrolled)
            : Ui.Icon("chevron_down", 18, Ui.Text);
    }

    private static Control BuildScrollToBottomBadge(int count)
    {
        var label = new Border
        {
            MinWidth = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = Ui.Accent,
            Padding = new Thickness(4, 0),
            Child = new TextBlock
            {
                Text = count > 99 ? "99+" : count.ToString(),
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };

        var host = Ui.H(3, Ui.Icon("chevron_down", 14, Ui.Text), label);
        host.HorizontalAlignment = HorizontalAlignment.Center;
        host.VerticalAlignment = VerticalAlignment.Center;
        return host;
    }

    private Control BuildRoot()
    {
        _sidePanel.IsVisible = false;
        _dialogOverlay.IsVisible = false;
        _dialogOverlay.Background = Brush.Parse("#99000000");
        _dialogOverlay.Focusable = true;
        _dialogOverlay.KeyDown += (_, e) => { if (e.Key == Key.Escape) HideDialog(); };
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,*,Auto"),
            Background = Ui.Bg,
            Focusable = true,
            Children = { BuildSidebar(), BuildChatPanel(), _sidePanel, _dialogOverlay }
        };
        root.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (e.Key != Key.Escape) return;

                if (_dialogOverlay.IsVisible)
                {
                    e.Handled = true;
                    HideDialog();
                    return;
                }

                if (_sidePanel.IsVisible)
                {
                    e.Handled = true;
                    HideSidePanel();
                    return;
                }

                if (_showingRequests)
                {
                    e.Handled = true;
                    CloseRequestsView();
                    return;
                }

                if (_selectedPeer != null)
                {
                    e.Handled = true;
                    CloseActiveConversation();
                }
            },
            routes: RoutingStrategies.Tunnel,
            handledEventsToo: false);
        Grid.SetColumn(root.Children[1], 1);
        Grid.SetColumn(_sidePanel, 2);
        Grid.SetColumnSpan(_dialogOverlay, 3);
        return root;
    }

    private void HideSidePanel()
    {
        _sidePanel.Content = null;
        _sidePanel.IsVisible = false;
    }

    private Control BuildSidebar()
    {
        var profileButton = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = BuildProfileSummary()
        };

        _tabContentHost.Content = BuildThreadScroller();

        var newConversation = Ui.IconButtonName("add");
        newConversation.Width = 38;
        newConversation.Height = 38;
        newConversation.Background = Ui.Surface2;
        newConversation.BorderBrush = Ui.Border;
        newConversation.BorderThickness = new Thickness(1);
        ToolTip.SetTip(newConversation, "Nova conversa");
        newConversation.Click += (_, e) => { e.Handled = true; ShowNewConversationPanel(); };

        _requestsButton.Width = 38;
        _requestsButton.Height = 38;
        _requestsButton.Background = Ui.Surface2;
        _requestsButton.BorderBrush = Ui.Border;
        _requestsButton.BorderThickness = new Thickness(1);
        ToolTip.SetTip(_requestsButton, "Pedidos de mensagem");
        _requestsButton.Click += async (_, e) => { e.Handled = true; await ShowRequestsMainAsync(); };
        var requestButtonHost = BuildRequestButtonHost();

        var searchGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            Children = { _search, newConversation, requestButtonHost }
        };
        Grid.SetColumn(newConversation, 1);
        Grid.SetColumn(requestButtonHost, 2);

        var header = Ui.V(14, profileButton, searchGrid);

        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(14),
            RowSpacing = 12,
            Children = { header, _tabContentHost }
        };
        Grid.SetRow(_tabContentHost, 1);

        return new Border
        {
            Background = Ui.Surface,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = panel
        };
    }

    private Control BuildRequestButtonHost()
    {
        var grid = new Grid { Width = 38, Height = 38, Children = { _requestsButton } };
        grid.Children.Add(new Border
        {
            IsVisible = false,
            Name = "RequestBadge",
            IsHitTestVisible = false,
            MinWidth = 18,
            Height = 18,
            Background = Ui.Danger,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(4,0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = _requestBadgeText
        });
        return grid;
    }

    private Control BuildThreadScroller()
        => new ScrollViewer
        {
            Content = _threadList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

    private Control BuildTabs()
    {
        _inboxButton.BorderThickness = new Thickness(0);
        _requestsButton.BorderThickness = new Thickness(0);
        _inboxButton.MinWidth = 80;
        _requestsButton.MinWidth = 80;
        _inboxButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _requestsButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _inboxButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _requestsButton.HorizontalAlignment = HorizontalAlignment.Stretch;

        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 4,
            Children = { _inboxButton, _requestsButton }
        };
        Grid.SetColumn(_requestsButton, 1);

        _tabIndicator.Height = 2;
        _tabIndicator.Width = _tabIndicatorWidth;
        _tabIndicator.CornerRadius = new CornerRadius(1);
        _tabIndicator.Background = Ui.Accent;
        _tabIndicator.RenderTransform = _tabIndicatorTransform;

        _tabCanvas.Height = 2;
        _tabCanvas.HorizontalAlignment = HorizontalAlignment.Stretch;
        _tabCanvas.Children.Clear();
        _tabCanvas.Children.Add(_tabIndicator);

        var host = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 8),
            Background = Brushes.Transparent,
            Child = Ui.V(5, buttons, _tabCanvas)
        };
        UpdateTabsVisual(false);
        return host;
    }

    private void UpdateTabsVisual(bool animate = true)
    {
        _inboxButton.Foreground = _showingRequests ? Ui.Muted : Ui.Text;
        _requestsButton.Foreground = _showingRequests ? Ui.Text : Ui.Muted;
        _inboxButton.Opacity = _showingRequests ? 0.55 : 1.0;
        _requestsButton.Opacity = _showingRequests ? 1.0 : 0.55;
        _inboxButton.Background = Brushes.Transparent;
        _requestsButton.Background = Brushes.Transparent;

        Dispatcher.UIThread.Post(() =>
        {
            var totalWidth = _tabCanvas.Bounds.Width;
            if (totalWidth <= 0)
                totalWidth = 220;
            var targetWidth = Math.Max(80, (totalWidth - 4) / 2);
            var targetX = _showingRequests ? targetWidth + 4 : 0;
            if (animate)
                AnimateTabIndicator(targetX, targetWidth);
            else
            {
                _tabAnimationTimer?.Stop();
                _tabIndicatorX = targetX;
                _tabIndicatorWidth = targetWidth;
                _tabIndicator.Width = targetWidth;
                _tabIndicatorTransform.X = targetX;
            }
        }, DispatcherPriority.Render);
    }

    private void AnimateTabIndicator(double targetX, double targetWidth)
    {
        _tabAnimationTimer?.Stop();
        var startX = _tabIndicatorX;
        var startW = _tabIndicatorWidth;
        var steps = 14;
        var step = 0;
        _tabAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _tabAnimationTimer.Tick += (_, _) =>
        {
            step++;
            var t = step / (double)steps;
            t = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            _tabIndicatorX = startX + (targetX - startX) * t;
            _tabIndicatorWidth = startW + (targetWidth - startW) * t;
            _tabIndicatorTransform.X = _tabIndicatorX;
            _tabIndicator.Width = _tabIndicatorWidth;
            if (step >= steps)
            {
                _tabIndicatorX = targetX;
                _tabIndicatorWidth = targetWidth;
                _tabIndicatorTransform.X = targetX;
                _tabIndicator.Width = targetWidth;
                _tabAnimationTimer.Stop();
            }
        };
        _tabAnimationTimer.Start();
    }


    private Control BuildProfileSummary()
    {
        var name = CurrentUserDisplayName();
        var menu = Ui.IconButtonName("more");
        menu.Width = 32;
        menu.Height = 32;
        menu.MinWidth = 32;
        menu.MinHeight = 32;
        menu.Padding = new Thickness(0);
        menu.Margin = new Thickness(8, 0, 0, 0);
        menu.VerticalAlignment = VerticalAlignment.Center;
        menu.HorizontalAlignment = HorizontalAlignment.Right;
        menu.HorizontalContentAlignment = HorizontalAlignment.Center;
        menu.VerticalContentAlignment = VerticalAlignment.Center;
        menu.Click += (_, e) => { e.Handled = true; ShowProfilePanel(); };

        _profileName.Text = name;
        _profileEmail.Text = SessionContext.Email;
        var identity = Ui.H(12,
            CurrentUserAvatarWithStatus(44),
            Ui.V(1, _profileName, _profileEmail));
        identity.VerticalAlignment = VerticalAlignment.Center;

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { identity, menu }
        };
        Grid.SetColumn(menu, 1);
        return row;
    }

    private Control BuildChatPanel()
    {
        _closeChatButton.Width = 36;
        _closeChatButton.Height = 36;
        _closeChatButton.MinWidth = 36;
        _closeChatButton.MinHeight = 36;
        _closeChatButton.Padding = new Thickness(0);
        _closeChatButton.HorizontalAlignment = HorizontalAlignment.Right;
        _closeChatButton.VerticalAlignment = VerticalAlignment.Center;
        _closeChatButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _closeChatButton.VerticalContentAlignment = VerticalAlignment.Center;
        _closeChatButton.IsVisible = false;
        _closeChatButton.Click += (_, e) =>
        {
            e.Handled = true;
            if (_showingRequests)
                CloseRequestsView();
            else
                CloseActiveConversation();
        };
        ToolTip.SetTip(_closeChatButton, "Fechar conversa");
        var titleStack = Ui.V(3, _title, _subtitle);
        titleStack.Cursor = new Cursor(StandardCursorType.Hand);
        titleStack.PointerPressed += (_, _) => { if (_selectedPeer != null) NavigationService.Navigate(new ProfilePage(_selectedPeer, false)); };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(20, 18, 20, 12),
            MinHeight = 44,
            ColumnSpacing = 10,
            Children = { titleStack, _closeChatButton }
        };
        Grid.SetColumn(_closeChatButton, 1);

        _messageHost.Content = BuildEmptyState();
        var messageHostBorder = new Border
        {
            Background = Ui.Bg,
            Padding = new Thickness(16, 4, 16, 4),
            Child = _messageHost
        };

        _send.MinWidth = 92;
        _send.Height = 36;
        _send.Padding = new Thickness(20, 0);
        _send.CornerRadius = new CornerRadius(8);
        _send.FontSize = 14;
        _send.FontWeight = FontWeight.Medium;
        _send.HorizontalContentAlignment = HorizontalAlignment.Center;
        _send.VerticalContentAlignment = VerticalAlignment.Center;
        var composerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            Children = { _messageInput, _send }
        };
        Grid.SetColumn(_send, 1);

        _composer.BorderBrush = Ui.Border;
        _composer.BorderThickness = new Thickness(0, 1, 0, 0);
        _composer.Background = Ui.Bg;
        _composer.Padding = new Thickness(16, 6, 16, 8);
        _composer.Child = Ui.V(4, _actions, _status, composerGrid);

        var connectionBanner = BuildConnectionBanner();
        var chat = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Background = Ui.Bg,
            Children = { header, connectionBanner, messageHostBorder, _composer }
        };
        Grid.SetRow(connectionBanner, 1);
        Grid.SetRow(messageHostBorder, 2);
        Grid.SetRow(_composer, 3);
        return chat;
    }

    private Control BuildConnectionBanner()
    {
        _connectionBannerText = Ui.TextBlock("", 12, Ui.Text, FontWeight.SemiBold);
        _connectionBannerSpinnerHost = new ContentControl
        {
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Content = Ui.LoadingSpinner(16, 2, Ui.Text)
        };
        _connectionBanner = new Border
        {
            IsVisible = false,
            Height = 36,
            Padding = new Thickness(16, 0),
            Background = Ui.Surface2,
            Child = Ui.H(8, _connectionBannerSpinnerHost, _connectionBannerText)
        };

        ApiService.Connection.StateChanged -= OnConnectionStateChanged;
        ApiService.Connection.StateChanged += OnConnectionStateChanged;
        OnConnectionStateChanged(ApiService.Connection.State);
        return _connectionBanner;
    }

    private void OnConnectionStateChanged(ConnectionState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_connectionBanner == null || _connectionBannerText == null || _connectionBannerSpinnerHost == null) return;
            _connectionBannerHideTimer?.Stop();
            _connectionBanner.IsVisible = true;
            _connectionBannerSpinnerHost.IsVisible = true;

            switch (state)
            {
                case ConnectionState.Connected:
                    _connectionBanner.Background = new SolidColorBrush(Color.FromArgb(34, Ui.Success.Color.R, Ui.Success.Color.G, Ui.Success.Color.B));
                    _connectionBannerText.Text = "Ligado!";
                    _connectionBannerSpinnerHost.IsVisible = false;
                    _connectionBannerHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    _connectionBannerHideTimer.Tick += (_, _) =>
                    {
                        _connectionBannerHideTimer?.Stop();
                        if (_connectionBanner != null) Ui.SoftSlideOut(_connectionBanner, () => _connectionBanner.IsVisible = false, -12);
                    };
                    _connectionBannerHideTimer.Start();
                    break;
                case ConnectionState.Connecting:
                    _connectionBanner.Background = new SolidColorBrush(Color.FromArgb(34, Ui.Warning.Color.R, Ui.Warning.Color.G, Ui.Warning.Color.B));
                    _connectionBannerText.Text = "A ligar ao servidor...";
                    break;
                case ConnectionState.Reconnecting:
                    _connectionBanner.Background = new SolidColorBrush(Color.FromArgb(34, Ui.Danger.Color.R, Ui.Danger.Color.G, Ui.Danger.Color.B));
                    _connectionBannerText.Text = "Conexão perdida. A tentar reconectar...";
                    break;
                case ConnectionState.Disconnected:
                    _connectionBanner.Background = new SolidColorBrush(Color.FromArgb(34, Ui.Danger.Color.R, Ui.Danger.Color.G, Ui.Danger.Color.B));
                    _connectionBannerText.Text = "Sem ligação ao servidor.";
                    _connectionBannerSpinnerHost.IsVisible = false;
                    break;
            }
            Ui.SoftSlideIn(_connectionBanner, -12);
        });
    }

    private Control BuildThreadRow(ConversationVm? vm)
    {
        if (vm == null) return new Border();

        var preview = Ui.TextBlock(vm.IsTyping ? $"{vm.Title} está a escrever" : vm.Preview, 12, vm.IsTyping ? Ui.Accent : Ui.Muted);
        preview.MaxLines = 1;
        preview.TextTrimming = TextTrimming.CharacterEllipsis;

        Control previewControl = vm.IsTyping
            ? Ui.H(6, preview, BuildTypingDots())
            : preview;

        Control right;
        if (_showingRequests && vm.Status == "pending")
        {
            var accept = Ui.CircleButton("✓", Ui.Success, Brushes.Transparent);
            var reject = Ui.CircleButton("×", Ui.Danger, Brushes.Transparent);
            ToolTip.SetTip(accept, "Aceitar pedido");
            ToolTip.SetTip(reject, "Recusar pedido");
            accept.Click += async (_, e) => { e.Handled = true; await AcceptRequestAsync(vm.Peer); };
            reject.Click += async (_, e) => { e.Handled = true; await RejectRequestAsync(vm.Peer); };
            right = Ui.H(6, accept, reject);
        }
        else
        {
            var unread = vm.Unread > 0
                ? new Border
                {
                    MinWidth = 22,
                    Height = 22,
                    Background = Ui.Accent,
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(6, 1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Child = new TextBlock
                    {
                        Text = vm.Unread > 99 ? "99+" : vm.Unread.ToString(),
                        Foreground = Brushes.White,
                        FontWeight = FontWeight.Bold,
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
                : new Border();

            right = Ui.V(5, Ui.TextBlock(vm.Time, 11, Ui.Muted2), unread);
            right.HorizontalAlignment = HorizontalAlignment.Right;
        }

        var titleRow = Ui.H(6, Ui.TextBlock(vm.Title, 14, Ui.Text, FontWeight.SemiBold));
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 12,
            Children =
            {
                AvatarWithStatus(vm.Peer, 42),
                Ui.V(2, titleRow, previewControl),
                right
            }
        };
        Grid.SetColumn(row.Children[1], 1);
        Grid.SetColumn(right, 2);

        var content = new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(14),
            Background = vm.IsSelected ? Ui.AccentSoft : Brushes.Transparent,
            BorderBrush = vm.IsSelected ? Ui.Accent : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = row
        };

        content.PointerEntered += (_, _) =>
        {
            if (!vm.IsSelected)
            {
                content.Background = Ui.Surface2;
                content.BorderBrush = Ui.BorderSoft;
            }
        };
        content.PointerExited += (_, _) =>
        {
            if (!vm.IsSelected)
            {
                content.Background = Brushes.Transparent;
                content.BorderBrush = Brushes.Transparent;
            }
        };
        content.PointerPressed += async (_, e) =>
        {
            e.Handled = true;
            if (_showingRequests && vm.Status == "pending")
                await ShowRequestPreviewPanelAsync(vm);
            else
                await SelectThreadAsync(vm);
        };

        return new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 4),
            Child = content
        };
    }

    private Control BuildSearchRow(PublicUser? user)
    {
        if (user == null) return new Border();

        var messageStatus = (user.message_status ?? string.Empty).Trim().ToLowerInvariant();
        var requestDirection = (user.request_direction ?? string.Empty).Trim().ToLowerInvariant();
        var canSendRequest = string.IsNullOrWhiteSpace(messageStatus) || messageStatus == "none" || messageStatus == "rejected";

        Control actionControl;
        if (canSendRequest)
        {
            var sendButton = Ui.IconButtonName("send");
            sendButton.Width = 34;
            sendButton.Height = 34;
            sendButton.Opacity = 0.78;
            sendButton.Background = Brushes.Transparent;
            sendButton.BorderThickness = new Thickness(0);
            ToolTip.SetTip(sendButton, "Enviar pedido de mensagem");
            sendButton.Click += async (_, e) =>
            {
                e.Handled = true;
                sendButton.IsEnabled = false;
                await CreateMessageRequestAsync(user);
                sendButton.IsEnabled = true;
            };
            actionControl = sendButton;
        }
        else
        {
            var label = messageStatus == "accepted"
                ? "Conversa"
                : requestDirection == "outgoing"
                    ? "Pendente"
                    : "Pedido recebido";
            actionControl = Ui.Pill(Ui.TextBlock(label, 11, Ui.Muted, FontWeight.SemiBold), Ui.Surface2, Ui.BorderSoft);
        }

        var profileArea = Ui.H(12,
            AvatarWithStatus(user, 36),
            Ui.V(1,
                Ui.TextBlock(user.DisplayName, 14, Ui.Text, FontWeight.SemiBold),
                Ui.TextBlock("@" + user.username, 12, Ui.Muted)));
        profileArea.Cursor = new Cursor(StandardCursorType.Hand);
        profileArea.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            NavigationService.Navigate(new ProfilePage(user, false));
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Children = { profileArea, actionControl }
        };
        Grid.SetColumn(actionControl, 1);

        var border = new Border
        {
            Height = 52,
            Padding = new Thickness(10, 7),
            Margin = new Thickness(0, 2),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Child = row
        };
        border.PointerEntered += (_, _) => border.Background = Ui.Surface2;
        border.PointerExited += (_, _) => border.Background = Brushes.Transparent;
        return border;
    }

    private static Control BuildTypingDots(double dotSize = 5, IBrush? brush = null)
    {
        var dotBrush = brush ?? Ui.Accent;
        var dot1 = new Border { Width = dotSize, Height = dotSize, CornerRadius = new CornerRadius(dotSize / 2), Background = dotBrush, Opacity = 0.45 };
        var dot2 = new Border { Width = dotSize, Height = dotSize, CornerRadius = new CornerRadius(dotSize / 2), Background = dotBrush, Opacity = 0.65 };
        var dot3 = new Border { Width = dotSize, Height = dotSize, CornerRadius = new CornerRadius(dotSize / 2), Background = dotBrush, Opacity = 0.85 };
        var dotsArray = new[] { dot1, dot2, dot3 };
        var frame = 0;

        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Math.Max(3, dotSize * 0.55),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { dot1, dot2, dot3 }
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) =>
        {
            if (dots.Parent == null)
            {
                timer.Stop();
                return;
            }

            frame = (frame + 1) % dotsArray.Length;
            for (var i = 0; i < dotsArray.Length; i++)
                dotsArray[i].Opacity = i == frame ? 1.0 : 0.42;
        };
        dots.AttachedToVisualTree += (_, _) => timer.Start();
        dots.DetachedFromVisualTree += (_, _) => timer.Stop();
        return dots;
    }

    private static Control BuildTypingBubbleIndicator()
    {
        var bubble = new Border
        {
            MaxWidth = 76,
            Background = Ui.Surface2,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 11),
            Child = BuildTypingDots(6, Ui.Muted)
        };

        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 2),
            Children = { bubble }
        };
    }

    private void SetChatTypingIndicator(bool isTyping, string? _ = null)
    {
        if (!isTyping)
        {
            _typingIndicator.Content = null;
            _typingIndicator.IsVisible = false;
            return;
        }

        _typingIndicator.Content = BuildTypingBubbleIndicator();
        _typingIndicator.IsVisible = true;
        if (IsNearMessagesBottom(220))
            ScrollToEnd();
    }

    private const int MaxMessagePreviewLines = 8;
    private const double MessageLineHeight = 20.0;
    private const double CollapsedMessageHeight = MaxMessagePreviewLines * MessageLineHeight;

    private Control BuildMessageBubble(MessageVm? vm)
    {
        if (vm == null) return new Border();
        if (vm.IsDateSeparator) return BuildDateSeparator(vm.Text);
        if (vm.IsSystem) return BuildSystemMessageBubble(vm);

        var text = Ui.TextBlock(vm.Text, 14, Ui.Text);
        text.MaxWidth = 620;
        text.TextWrapping = TextWrapping.Wrap;
        text.LineHeight = MessageLineHeight;

        var body = BuildExpandableMessageBody(vm, text);

        var meta = Ui.TextBlock(vm.Meta, 11, vm.IsMine ? Ui.MessageStatusSent : Ui.Muted2);
        var statusIcon = vm.IsMine ? Ui.TextBlock(vm.StatusIcon, 12, vm.StatusBrush) : Ui.TextBlock("", 1, Brushes.Transparent);
        var metaRow = Ui.H(5, meta, statusIcon);
        metaRow.HorizontalAlignment = HorizontalAlignment.Right;

        var bubble = new Border
        {
            MaxWidth = 690,
            Background = vm.IsMine ? Ui.Accent : Ui.Surface2,
            BorderBrush = vm.IsMine ? Ui.Accent : Ui.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 9),
            Child = Ui.V(4, body, metaRow)
        };

        var menu = new ContextMenu();
        var deleteMe = new MenuItem { Header = "Apagar para mim" };
        deleteMe.Click += async (_, _) => await DeleteMessageAsync(vm, "me");
        var deleteAll = new MenuItem { Header = "Apagar para todos", IsEnabled = vm.CanDeleteForAll };
        deleteAll.Click += async (_, _) => await DeleteMessageAsync(vm, "all");
        menu.ItemsSource = new MenuItem[] { deleteMe, deleteAll };
        bubble.ContextMenu = menu;

        var host = new StackPanel
        {
            HorizontalAlignment = vm.IsMine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(0, 4),
            Children = { bubble }
        };
        Ui.SoftFadeIn(host);
        return host;
    }

    private Control BuildExpandableMessageBody(MessageVm vm, TextBlock text)
    {
        var normalized = (vm.Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var explicitLines = normalized.Split('\n').Length;
        var estimatedWrappedLines = normalized
            .Split('\n')
            .Select(line => Math.Max(1, (int)Math.Ceiling(line.Length / 72.0)))
            .Sum();

        if (Math.Max(explicitLines, estimatedWrappedLines) <= MaxMessagePreviewLines)
            return text;

        var clipBorder = new Border
        {
            MaxHeight = CollapsedMessageHeight,
            ClipToBounds = true,
            Child = text
        };

        var toggle = new Button
        {
            Content = "Ver mais",
            FontSize = 11,
            Height = 26,
            Padding = new Thickness(9, 0),
            Margin = new Thickness(0, 6, 0, 0),
            CornerRadius = new CornerRadius(7),
            Background = vm.IsMine ? new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)) : Ui.AccentSoft,
            Foreground = vm.IsMine ? Brushes.White : Ui.Accent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = vm.IsMine ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };

        var expanded = false;
        toggle.Click += (_, e) =>
        {
            e.Handled = true;
            expanded = !expanded;
            clipBorder.MaxHeight = expanded ? 100000 : CollapsedMessageHeight;
            toggle.Content = expanded ? "Ver menos" : "Ver mais";
        };

        return Ui.V(0, clipBorder, toggle);
    }

    private Control BuildSystemMessageBubble(MessageVm vm)
    {
        var label = Ui.TextBlock(vm.Text, 12, vm.SystemScope == "notice" ? Ui.Warning : Ui.Muted);
        label.FontStyle = FontStyle.Italic;

        Control content;
        if (vm.SystemScope == "notice")
        {
            content = Ui.H(8, Ui.Icon("info", 12, Ui.Warning), label);
        }
        else
        {
            var pill = new Border
            {
                Background = vm.SystemScope == "all" ? Ui.AccentSoft : Ui.Surface2,
                BorderBrush = vm.SystemScope == "all" ? Ui.Accent : Ui.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 2),
                Child = Ui.TextBlock(vm.SystemScope == "all" ? "Para todos" : "Só para mim", 10, vm.SystemScope == "all" ? Ui.Accent : Ui.Muted2, FontWeight.SemiBold)
            };
            content = Ui.H(8, Ui.Icon("trash", 12, Ui.Muted2), label, pill);
        }

        var bubble = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8),
            Padding = new Thickness(12, 7),
            CornerRadius = new CornerRadius(14),
            Background = vm.SystemScope == "notice" ? new SolidColorBrush(Color.FromArgb(28, Ui.Warning.Color.R, Ui.Warning.Color.G, Ui.Warning.Color.B)) : Ui.Surface2,
            BorderBrush = vm.SystemScope == "notice" ? Ui.Warning : Ui.BorderSoft,
            BorderThickness = new Thickness(1),
            Opacity = 0.95,
            Child = content
        };
        Ui.SoftFadeIn(bubble);
        return bubble;
    }

    private Control BuildDateSeparator(string label)
    {
        var lineLeft = new Border { Height = 1, Background = Ui.BorderSoft, VerticalAlignment = VerticalAlignment.Center };
        var lineRight = new Border { Height = 1, Background = Ui.BorderSoft, VerticalAlignment = VerticalAlignment.Center };
        var pill = new Border
        {
            Background = Ui.Surface,
            BorderBrush = Ui.BorderSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(12, 5),
            Child = Ui.TextBlock(label, 12, Ui.Muted, FontWeight.SemiBold)
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 14, 0, 10),
            Children = { lineLeft, pill, lineRight }
        };
        Grid.SetColumn(pill, 1);
        Grid.SetColumn(lineRight, 2);
        Ui.SoftFadeIn(grid);
        return grid;
    }

    private Control BuildEmptyState()
        => new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children =
            {
                new StackPanel
                {
                    Width = 360,
                    Spacing = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.78,
                    Children =
                    {
                        BuildLogoMark(),
                        CenterText("Seleciona uma conversa para começar.", 18, Ui.Text, FontWeight.SemiBold),
                        CenterText("Pesquisa um contacto ou aceita um pedido de mensagem.", 13, Ui.Muted, FontWeight.Normal)
                    }
                }
            }
        };

    private static Control BuildLogoMark()
    {
        try
        {
            var uri = new Uri("avares://Vanished/Resources/Logo/LogoWithoutText.png");
            using var stream = AssetLoader.Open(uri);
            return new Image
            {
                Source = new Bitmap(stream),
                Width = 110,
                Height = 110,
                Opacity = 0.86,
                HorizontalAlignment = HorizontalAlignment.Center,
                Stretch = Stretch.Uniform
            };
        }
        catch
        {
            return Ui.Avatar("Vanished", 110);
        }
    }

    private static TextBlock CenterText(string text, double size, IBrush brush, FontWeight weight)
    {
        var block = Ui.TextBlock(text, size, brush, weight);
        block.TextAlignment = TextAlignment.Center;
        block.HorizontalAlignment = HorizontalAlignment.Center;
        return block;
    }

    private Control CurrentUserAvatarWithStatus(double size)
    {
        _profileAvatarSize = size;
        _profileAvatarInitial.FontSize = size * 0.38;
        _profileAvatarImageHost.Width = size;
        _profileAvatarImageHost.Height = size;
        _profileAvatarImageHost.Content = Ui.AvatarImage(SessionContext.AvatarBase64, CurrentUserDisplayName(), size);

        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(_profileAvatarImageHost);
        grid.Children.Add(new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = Ui.Success,
            BorderBrush = Ui.Surface,
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 1, 1)
        });
        return grid;
    }

    private static Control AvatarWithStatus(PublicUser user, double size)
        => AvatarWithStatus(user.avatar_base64, user.DisplayName, user.is_online, size);

    private static Control AvatarWithStatus(string text, bool online, double size)
        => AvatarWithStatus(null, text, online, size);

    private static Control AvatarWithStatus(string? avatarBase64, string text, bool online, double size)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(Ui.AvatarImage(avatarBase64, text, size));
        grid.Children.Add(new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = online ? Ui.Success : Ui.Muted2,
            BorderBrush = Ui.Surface,
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 1, 1)
        });
        return grid;
    }

    private void RestartConversationFilterDebounce()
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void ApplyConversationFilter()
    {
        var q = (_search.Text ?? string.Empty).Trim().ToLowerInvariant();
        IEnumerable<ConversationVm> filtered = _allThreads;
        if (!string.IsNullOrWhiteSpace(q))
            filtered = filtered.Where(x => x.Title.ToLowerInvariant().Contains(q) || x.Preview.ToLowerInvariant().Contains(q) || x.Peer.username.ToLowerInvariant().Contains(q));

        var list = filtered.ToList();
        if (_threads.Count == list.Count && _threads.Zip(list).All(x => x.First.SameAs(x.Second)))
            return;
        _threads.Clear();
        foreach (var item in list)
            _threads.Add(item);
    }

    private void RestartNewConversationSearchDebounce()
    {
        _newSearchDebounceTimer.Stop();
        var q = (_newSearch?.Text ?? string.Empty).Trim().TrimStart('@');
        _searchResults.Clear();
        if (string.IsNullOrWhiteSpace(q))
        {
            if (_newSearchState != null) { _newSearchState.Text = "Pesquisa utilizadores pelo seu @handle."; _newSearchState.Foreground = Ui.Muted; }
            if (_newSearchIconHost != null) _newSearchIconHost.Content = Ui.Icon("search", 16, Ui.Muted2);
            return;
        }
        if (q.Length < 2)
        {
            if (_newSearchState != null) { _newSearchState.Text = "Escreve pelo menos 2 caracteres."; _newSearchState.Foreground = Ui.Muted; }
            if (_newSearchIconHost != null) _newSearchIconHost.Content = Ui.Icon("search", 16, Ui.Muted2);
            return;
        }
        if (_newSearchState != null) { _newSearchState.Text = "A pesquisar..."; _newSearchState.Foreground = Ui.Muted; }
        if (_newSearchIconHost != null) _newSearchIconHost.Content = Ui.LoadingSpinner(16, 2, Ui.Accent);
        _newSearchDebounceTimer.Start();
    }

    private async Task SearchUsersAsync()
    {
        if (_searching) return;
        var rawText = (_newSearch?.Text ?? string.Empty).Trim().TrimStart('@');
        _searchResults.Clear();
        if (rawText.Length < 2)
        {
            RestartNewConversationSearchDebounce();
            return;
        }
        var raw = "@" + rawText;
        var q = rawText;

        try
        {
            _searching = true;
            if (_newSearch != null) _newSearch.IsEnabled = false;
            if (_newSearchIconHost != null) _newSearchIconHost.Content = Ui.LoadingSpinner(16, 2, Ui.Accent);
            if (_newSearchState != null) { _newSearchState.Text = "A pesquisar..."; _newSearchState.Foreground = Ui.Muted; }
            var resp = await ApiService.Chat.SearchUsersAsync(q);
            if (SessionExpiredEvent.IsShown) return;
            if (resp?.success == true)
            {
                foreach (var user in resp.users.Where(u => ("@" + u.username).StartsWith(raw, StringComparison.OrdinalIgnoreCase)).Take(8))
                    _searchResults.Add(user);
                if (_newSearchState != null) { _newSearchState.Text = _searchResults.Count == 0 ? $"Nenhum utilizador encontrado com @{rawText}." : string.Empty; _newSearchState.Foreground = Ui.Muted; }
            }
            else
            {
                if (_newSearchState != null) { _newSearchState.Text = resp?.message ?? "Erro de rede."; _newSearchState.Foreground = Ui.Danger; }
            }
        }
        catch (Exception ex)
        {
            if (_newSearchState != null) { _newSearchState.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Erro de rede." : ex.Message; _newSearchState.Foreground = Ui.Danger; }
        }
        finally
        {
            _searching = false;
            if (_newSearch != null) _newSearch.IsEnabled = true;
            if (_newSearchIconHost != null) _newSearchIconHost.Content = Ui.Icon("search", 16, Ui.Muted2);
        }
    }

    private async Task RefreshAsync(bool showStatus)
    {
        if (_loading || !SessionContext.IsReady) return;
        try
        {
            _loading = true;
            if (showStatus) ShowStatus("A sincronizar...", Ui.Muted);

            var requests = await ApiService.Chat.GetMessageRequestsAsync();
            if (SessionExpiredEvent.IsShown) return;
            var reqCount = requests?.success == true ? requests.conversations.Where(x => x.peer != null).Select(x => x.peer.id).Distinct().Count() : 0;
            UpdateRequestBadge(reqCount);

            var resp = await ApiService.Chat.GetConversationsAsync();
            if (SessionExpiredEvent.IsShown) return;
            if (resp?.success == true)
            {
                ApplyThreadSnapshot(resp.conversations.Where(x => x.peer != null).ToList());
                if (_selectedPeer != null && !_selectedFromRequests)
                {
                    await LoadConversationAsync(_selectedPeer, false, _selectedStatus, false);
                    await RefreshTypingAsync();
                }
                if (showStatus) ShowStatus(_threads.Count == 0 ? "Sem conversas." : "Sincronizado.", Ui.Muted);
            }
            else if (showStatus) ShowStatus(resp?.message ?? "Não foi possível carregar.", Ui.Danger);
        }
        finally { _loading = false; }
    }


    private async Task RefreshConversationListsAsync(bool showStatus)
    {
        if (_loading || !SessionContext.IsReady) return;
        try
        {
            _loading = true;
            if (showStatus) ShowStatus("A sincronizar...", Ui.Muted);

            var requests = await ApiService.Chat.GetMessageRequestsAsync();
            if (SessionExpiredEvent.IsShown) return;
            var reqCount = requests?.success == true ? requests.conversations.Where(x => x.peer != null).Select(x => x.peer.id).Distinct().Count() : 0;
            UpdateRequestBadge(reqCount);

            var resp = await ApiService.Chat.GetConversationsAsync();
            if (SessionExpiredEvent.IsShown) return;
            if (resp?.success == true)
            {
                ApplyThreadSnapshot(resp.conversations.Where(x => x.peer != null).ToList());
                if (showStatus) ShowStatus(_threads.Count == 0 ? "Sem conversas." : "Sincronizado.", Ui.Muted);
            }
            else if (showStatus) ShowStatus(resp?.message ?? "Não foi possível carregar.", Ui.Danger);
        }
        finally { _loading = false; }
    }

    private async Task RefreshSelectedConversationFromEventAsync(WsEvent ev)
    {
        if (_selectedPeer == null || _selectedFromRequests) return;

        var peerId = ev.Get<int>("peer_id");
        if (peerId <= 0) peerId = ev.Get<int>("sender_id");
        if (peerId <= 0) peerId = ev.Get<int>("read_by");
        if (peerId > 0 && peerId != _selectedPeer.id) return;

        await LoadConversationAsync(_selectedPeer, false, _selectedStatus, false);
        await RefreshConversationListsAsync(false);
    }

    private void UpdateRequestBadge(int count)
    {
        _requestBadgeText.Text = count > 99 ? "99+" : count.ToString();
        if (_requestBadgeText.Parent is Border b) b.IsVisible = count > 0;
    }

    private void MergeCachedPresenceSnapshot()
    {
        foreach (var peerId in ApiService.WebSocket.GetOnlinePeerSnapshot())
        {
            if (peerId > 0 && peerId != SessionContext.UserId)
                _onlinePeers.Add(peerId);
        }
    }

    private void ApplyCachedPresenceSnapshot()
    {
        MergeCachedPresenceSnapshot();
        foreach (var peerId in _onlinePeers.ToList())
            SetPeerOnline(peerId, true);
    }

    private void ApplyThreadSnapshot(IReadOnlyList<ConversationSummary> rows)
    {
        MergeCachedPresenceSnapshot();
        var selectedId = _selectedPeer?.id;
        var selectedConversationIsOpen = selectedId.HasValue && !_selectedFromRequests && _selectedStatus == "accepted";
        var next = rows
            .Where(c => c.peer != null)
            .GroupBy(c => c.peer.id)
            .Select(g => g.OrderByDescending(x => x.last?.id ?? x.thread_id).First())
            .Select(c =>
            {
                var isSelected = c.peer.id == selectedId;
                var unread = selectedConversationIsOpen && isSelected ? 0 : c.unread_count;
                c.peer.is_online = c.peer.is_online || _onlinePeers.Contains(c.peer.id);
                return new ConversationVm(
                    c.peer,
                    DecryptPreview(c.last),
                    FormatRelativeDate(c.last?.created_at),
                    unread,
                    isSelected,
                    c.status,
                    c.last?.sender_id == SessionContext.UserId,
                    _typingPeers.Contains(c.peer.id));
            }).ToList();

        if (selectedConversationIsOpen && selectedId.HasValue && next.All(x => x.Peer.id != selectedId.Value))
        {
            _selectedPeer = null;
            _selectedStatus = "accepted";
            _selectedFromRequests = false;
            _messages.Clear();
            SetChatTypingIndicator(false);
            ShowStatus("Esta conversa já não está disponível.", Ui.Warning);
            UpdateChatState();
        }

        if (_allThreads.Count == next.Count && _allThreads.Zip(next).All(x => x.First.SameAs(x.Second)))
        {
            ApplyConversationFilter();
            return;
        }
        _allThreads.Clear();
        foreach (var item in next) _allThreads.Add(item);
        ApplyConversationFilter();
    }

    private async Task SelectThreadAsync(ConversationVm vm)
        => await SelectPeerAsync(vm.Peer, vm.Status, _showingRequests && vm.Status == "pending", false);

    private async Task SelectPeerAsync(PublicUser peer, string status, bool fromRequests, bool fromSearch)
    {
        _conversationLoadCts?.Cancel();
        _conversationLoadCts = new CancellationTokenSource();
        var ct = _conversationLoadCts.Token;

        var previousPeer = _selectedPeer;
        if (previousPeer != null && previousPeer.id != peer.id && _lastTypingSent)
            _ = SendTypingToPeerAsync(previousPeer, false);

        _selectedPeer = peer;
        _selectedStatus = string.IsNullOrWhiteSpace(status) ? "none" : status;
        _selectedFromRequests = fromRequests;
        _lastTypingSent = false;
        _title.Text = peer.DisplayName;
        _subtitle.Text = $"@{peer.username} · {(peer.is_online ? "online" : "offline")}";
        _searchResults.Clear();
        _messages.Clear();
        _searchState.Text = string.Empty;
        if (_newSearchState != null) _newSearchState.Text = string.Empty;
        UpdateThreadSelection();
        UpdateActions();
        _composer.IsVisible = true;
        _closeChatButton.IsVisible = true;
        SetMessageHostContent(BuildLoadingMessagesState(), 18);
        ShowStatus(string.Empty, Ui.Muted);

        try
        {
            await Task.Delay(180, ct);
            await LoadConversationAsync(peer, true, _selectedStatus, fromRequests, ct);
            if (ct.IsCancellationRequested) return;
            SetMessageHostContent(_messagesArea, 18);
            if (fromSearch)
                ShowStatus("Pedido criado. Aguarda que o destinatário aceite para poderes enviar mensagens.", Ui.Muted);
            else if (_selectedStatus == "pending")
                ShowStatus("Aguarda que o utilizador aceite o pedido para poderes enviar mensagens.", Ui.Warning);
            else
                ShowStatus(string.Empty, Ui.Muted);
        }
        catch (OperationCanceledException){}
    }

    private Control BuildLoadingMessagesState()
    {
        var state = Ui.V(12,
            Ui.LoadingSpinner(28, 3, Ui.Accent),
            Ui.TextBlock("A carregar mensagens...", 13, Ui.Muted));
        state.HorizontalAlignment = HorizontalAlignment.Center;
        state.VerticalAlignment = VerticalAlignment.Center;

        return new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children = { state }
        };
    }

    private void UpdateThreadSelection()
    {
        var selectedId = _selectedPeer?.id;
        var selectedConversationIsOpen = selectedId.HasValue && !_selectedFromRequests && _selectedStatus == "accepted";
        var next = _allThreads.Select(x =>
        {
            var isSelected = x.Peer.id == selectedId;
            var unread = selectedConversationIsOpen && isSelected ? 0 : x.Unread;
            return new ConversationVm(x.Peer, x.Preview, x.Time, unread, isSelected, x.Status, x.IsMineLast, x.IsTyping);
        }).ToList();
        _allThreads.Clear();
        foreach (var item in next) _allThreads.Add(item);
        ApplyConversationFilter();
    }

    private void ClearUnreadForPeer(int peerId)
    {
        if (peerId <= 0) return;

        bool replace(ObservableCollection<ConversationVm> list)
        {
            var changed = false;
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item.Peer.id != peerId || item.Unread == 0) continue;
                list[i] = new ConversationVm(item.Peer, item.Preview, item.Time, 0, item.IsSelected, item.Status, item.IsMineLast, item.IsTyping);
                changed = true;
            }
            return changed;
        }

        var changedAll = replace(_allThreads);
        replace(_threads);
        if (changedAll) ApplyConversationFilter();
    }

    private bool IsPendingRequestSelected() => _selectedPeer != null && _selectedFromRequests && _selectedStatus == "pending";

    private bool IsPendingConversationSelected() => _selectedPeer != null && _selectedStatus == "pending";

    private bool CanCompose() => _selectedPeer != null && !IsPendingConversationSelected() && _selectedPeer.is_blocked != true;

    private void UpdateActions()
    {
        _actions.Children.Clear();
        var canCompose = CanCompose();
        _messageInput.IsEnabled = canCompose;
        _send.IsEnabled = canCompose;
        if (_selectedPeer == null) return;

        if (IsPendingRequestSelected())
        {
            var accept = Ui.PrimaryButton("Aceitar pedido");
            var reject = Ui.GhostButton("Rejeitar");
            accept.Width = 150;
            reject.Width = 110;
            accept.Click += async (_, e) => { e.Handled = true; await AcceptRequestAsync(); };
            reject.Click += async (_, e) => { e.Handled = true; await RejectRequestAsync(); };
            _actions.Children.Add(accept);
            _actions.Children.Add(reject);
            ShowStatus("Pedido de mensagem: aceita para mover para a inbox.", Ui.Warning);
        }
        else if (_selectedPeer.is_blocked)
        {
            var unblock = Ui.GhostButton("Desbloquear utilizador");
            unblock.Width = 180;
            unblock.Click += async (_, e) =>
            {
                e.Handled = true;
                var resp = await ApiService.Messages.UnblockUserAsync(_selectedPeer.id);
                if (resp?.success == true)
                {
                    _selectedPeer.is_blocked = false;
                    UpdateActions();
                    ShowStatus("Utilizador desbloqueado.", Ui.Success);
                    await RefreshConversationListsAsync(false);
                }
                else ShowStatus(resp?.message ?? "Não foi possível desbloquear.", Ui.Danger);
            };
            _actions.Children.Add(unblock);
            ShowStatus("Este utilizador está bloqueado. Não podes enviar mensagens até desbloqueares.", Ui.Warning);
        }
        else if (_selectedStatus == "pending")
        {
            var banner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(28, Ui.Warning.Color.R, Ui.Warning.Color.G, Ui.Warning.Color.B)),
                BorderBrush = Ui.BorderSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 8),
                Child = Ui.TextBlock("Aguarda que o utilizador aceite o teu pedido para poderes enviar mensagens.", 12, Ui.Muted)
            };
            _actions.Children.Add(banner);
            ShowStatus("Pedido enviado. A conversa entra na inbox quando for aceite.", Ui.Muted);
        }
    }

    private void SetMessageHostContent(Control content, double fromX = 16)
    {
        if (!ReferenceEquals(_messageHost.Content, content))
            _messageHost.Content = content;
        Ui.SoftSlideIn(content, fromX);
    }

    private void UpdateChatState()
    {
        var hasPeer = _selectedPeer != null;
        SetMessageHostContent(hasPeer ? _messagesArea : BuildEmptyState(), hasPeer ? 18 : -18);
        _composer.IsVisible = hasPeer;
        _closeChatButton.IsVisible = hasPeer;
        if (hasPeer) ToolTip.SetTip(_closeChatButton, "Fechar conversa");
        if (!hasPeer)
        {
            _messages.Clear();
            _title.Text = "Vanished";
            _subtitle.Text = "Seleciona uma conversa";
            SetChatTypingIndicator(false);
        }
        UpdateActions();
    }



    private async Task ShowRequestsMainAsync()
    {
        _showingRequests = true;
        UpdateTabsVisual();
        _selectedPeer = null;
        _selectedFromRequests = false;
        _selectedStatus = "accepted";
        _messages.Clear();
        _composer.IsVisible = false;
        _closeChatButton.IsVisible = true;
        ToolTip.SetTip(_closeChatButton, "Fechar pedidos");
        _title.Text = "Pedidos de mensagem";
        _subtitle.Text = "Gere pedidos recebidos e enviados.";

        var state = Ui.TextBlock("A carregar pedidos...", 13, Ui.Muted);
        var contentHost = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var receivedTab = Ui.GhostButton("Recebidos");
        var sentTab = Ui.GhostButton("Enviados");
        receivedTab.MinWidth = 124;
        sentTab.MinWidth = 124;
        receivedTab.Height = 40;
        sentTab.Height = 40;
        receivedTab.HorizontalContentAlignment = HorizontalAlignment.Center;
        sentTab.HorizontalContentAlignment = HorizontalAlignment.Center;
        receivedTab.VerticalContentAlignment = VerticalAlignment.Center;
        sentTab.VerticalContentAlignment = VerticalAlignment.Center;

        var tabs = Ui.H(8, receivedTab, sentTab);
        tabs.HorizontalAlignment = HorizontalAlignment.Left;
        tabs.VerticalAlignment = VerticalAlignment.Center;
        tabs.Margin = new Thickness(0, 4, 0, 0);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 16,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(2, 0, 2, 0),
            Children = { tabs, state, contentHost }
        };
        Grid.SetRow(state, 1);
        Grid.SetRow(contentHost, 2);

        SetMessageHostContent(root);

        var receivedResp = await ApiService.Chat.GetMessageRequestsAsync();
        if (SessionExpiredEvent.IsShown) return;
        var sentResp = await ApiService.Chat.GetSentMessageRequestsAsync();
        if (SessionExpiredEvent.IsShown) return;

        if (receivedResp?.success != true)
        {
            state.Text = receivedResp?.message ?? "Não foi possível carregar pedidos recebidos.";
            state.Foreground = Ui.Danger;
            contentHost.Content = BuildRequestsEmptyState(false);
            return;
        }
        if (sentResp?.success != true)
        {
            state.Text = sentResp?.message ?? "Não foi possível carregar pedidos enviados.";
            state.Foreground = Ui.Danger;
            contentHost.Content = BuildRequestsEmptyState(true);
            return;
        }

        var receivedRows = receivedResp.conversations
            .Where(x => x.peer != null)
            .GroupBy(x => x.peer.id)
            .Select(g => g.OrderByDescending(x => x.last?.id ?? x.thread_id).First())
            .Select(x => new ConversationVm(x.peer, DecryptPreview(x.last), FormatRelativeDate(x.last?.created_at), x.unread_count, false, x.status, x.last?.sender_id == SessionContext.UserId))
            .ToList();

        var sentRows = sentResp.conversations
            .Where(x => x.peer != null)
            .GroupBy(x => x.peer.id)
            .Select(g => g.OrderByDescending(x => x.last?.id ?? x.thread_id).First())
            .Select(x => new ConversationVm(x.peer, DecryptPreview(x.last), FormatRelativeDate(x.last?.created_at), x.unread_count, false, x.status, x.last?.sender_id == SessionContext.UserId))
            .ToList();

        UpdateRequestBadge(receivedRows.Count);

        void Render(bool sent)
        {
            receivedTab.Background = sent ? Brushes.Transparent : Ui.AccentSoft;
            receivedTab.Foreground = sent ? Ui.Muted : Ui.Text;
            sentTab.Background = sent ? Ui.AccentSoft : Brushes.Transparent;
            sentTab.Foreground = sent ? Ui.Text : Ui.Muted;

            var rows = sent ? sentRows : receivedRows;
            state.Text = sent
                ? rows.Count == 0 ? string.Empty : $"{rows.Count} pedido(s) enviado(s) pendente(s)."
                : rows.Count == 0 ? string.Empty : $"{rows.Count} pedido(s) recebido(s) pendente(s).";
            state.Foreground = Ui.Muted;
            state.IsVisible = !string.IsNullOrWhiteSpace(state.Text);

            if (rows.Count == 0)
            {
                contentHost.Content = BuildRequestsEmptyState(sent);
                return;
            }

            var list = new ItemsControl { ItemsSource = rows };
            list.ItemTemplate = sent
                ? new FuncDataTemplate<ConversationVm>((vm, _) => BuildSentRequestMainRow(vm), true)
                : new FuncDataTemplate<ConversationVm>((vm, _) => BuildRequestMainRow(vm), true);
            contentHost.Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = list
            };
        }

        receivedTab.Click += (_, e) => { e.Handled = true; Render(false); };
        sentTab.Click += (_, e) => { e.Handled = true; Render(true); };
        Render(false);
    }

    private void CloseRequestsView()
    {
        _showingRequests = false;
        _selectedPeer = null;
        _selectedFromRequests = false;
        _selectedStatus = "accepted";
        _messages.Clear();
        SetChatTypingIndicator(false);
        ToolTip.SetTip(_closeChatButton, "Fechar conversa");
        UpdateTabsVisual();
        UpdateChatState();
    }

    private Control BuildRequestsEmptyState(bool sent)
    {
        var title = sent ? "Sem pedidos enviados" : "Sem pedidos pendentes";
        var description = sent
            ? "Quando enviares pedidos de mensagem, os que ainda aguardam resposta aparecem aqui."
            : "Quando alguém te enviar um pedido de mensagem, aparece aqui.";

        var stack = Ui.V(10,
            Ui.Icon("request", 42, Ui.Muted2),
            Ui.TextBlock(title, 15, Ui.Text, FontWeight.SemiBold),
            Ui.TextBlock(description, 13, Ui.Muted));
        stack.HorizontalAlignment = HorizontalAlignment.Center;
        stack.VerticalAlignment = VerticalAlignment.Center;
        stack.MaxWidth = 300;
        foreach (var child in stack.Children.OfType<TextBlock>())
        {
            child.HorizontalAlignment = HorizontalAlignment.Center;
            child.TextAlignment = TextAlignment.Center;
        }

        return new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children = { stack }
        };
    }

    private Control BuildRequestMainRow(ConversationVm? vm)
    {
        if (vm == null) return new Border();
        var accept = Ui.CircleButton("✓", Ui.Success, Ui.Surface2);
        var reject = Ui.CircleButton("×", Ui.Danger, Ui.Surface2);
        accept.VerticalAlignment = VerticalAlignment.Center;
        reject.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(accept, "Aceitar pedido");
        ToolTip.SetTip(reject, "Recusar pedido");
        accept.Click += async (_, e) => { e.Handled = true; await AcceptRequestAsync(vm.Peer); };
        reject.Click += async (_, e) => { e.Handled = true; await RejectRequestAsync(vm.Peer); await ShowRequestsMainAsync(); };

        var info = Ui.V(2,
            Ui.TextBlock(vm.Title, 15, Ui.Text, FontWeight.SemiBold),
            Ui.TextBlock("@" + vm.Peer.username, 12, Ui.Muted),
            Ui.TextBlock(vm.Preview, 12, Ui.Muted2));
        info.Cursor = new Cursor(StandardCursorType.Hand);
        info.PointerPressed += (_, e) => { e.Handled = true; NavigationService.Navigate(new ProfilePage(vm.Peer, false)); };

        var avatar = AvatarWithStatus(vm.Peer, 40);
        avatar.Cursor = new Cursor(StandardCursorType.Hand);
        avatar.PointerPressed += (_, e) => { e.Handled = true; NavigationService.Navigate(new ProfilePage(vm.Peer, false)); };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 10,
            Children = { avatar, info, accept, reject }
        };
        Grid.SetColumn(info, 1);
        Grid.SetColumn(accept, 2);
        Grid.SetColumn(reject, 3);

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(10),
            Background = Ui.Surface,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1),
            Child = row
        };
    }

    private Control BuildSentRequestMainRow(ConversationVm? vm)
    {
        if (vm == null) return new Border();
        var cancel = Ui.DangerButton("Cancelar pedido");
        cancel.MinWidth = 136;
        cancel.Height = 32;
        cancel.FontSize = 12;
        cancel.VerticalAlignment = VerticalAlignment.Center;
        cancel.Click += async (_, e) =>
        {
            e.Handled = true;
            cancel.IsEnabled = false;
            cancel.Content = "A cancelar...";
            var resp = await ApiService.Chat.CancelMessageRequestAsync(vm.Peer.id);
            if (resp?.success == true)
            {
                ToastService.Show($"Pedido a @{vm.Peer.username} cancelado.", "check", ToastType.Success);
                await ShowRequestsMainAsync();
            }
            else
            {
                cancel.IsEnabled = true;
                cancel.Content = "Cancelar pedido";
                ToastService.Show(resp?.message ?? "Não foi possível cancelar o pedido.", "info", ToastType.Error);
            }
        };

        var info = Ui.V(2,
            Ui.TextBlock(vm.Title, 15, Ui.Text, FontWeight.SemiBold),
            Ui.TextBlock("@" + vm.Peer.username, 12, Ui.Muted),
            Ui.TextBlock("Aguarda resposta" + (string.IsNullOrWhiteSpace(vm.Time) ? "" : " · " + vm.Time), 12, Ui.Muted2));
        info.Cursor = new Cursor(StandardCursorType.Hand);
        info.PointerPressed += (_, e) => { e.Handled = true; NavigationService.Navigate(new ProfilePage(vm.Peer, false)); };

        var avatar = AvatarWithStatus(vm.Peer, 40);
        avatar.Cursor = new Cursor(StandardCursorType.Hand);
        avatar.PointerPressed += (_, e) => { e.Handled = true; NavigationService.Navigate(new ProfilePage(vm.Peer, false)); };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            Children = { avatar, info, cancel }
        };
        Grid.SetColumn(info, 1);
        Grid.SetColumn(cancel, 2);
        return new Border
        {
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(10),
            Background = Ui.Surface,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1),
            Child = row
        };
    }

    private Control BuildHandleSearchField(TextBox searchBox, ContentControl iconHost)
    {
        Ui.ApplyInnerTextBoxChromeFix(searchBox);
        searchBox.MinHeight = 42;
        searchBox.Padding = new Thickness(2, 8, 8, 8);
        searchBox.Watermark = "nome de utilizador";
        searchBox.FontSize = 15;
        searchBox.Classes.Add("new-conversation-search-box");
        searchBox.Styles.Add(new Style(x => x.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TextBox.BorderBrushProperty, Brushes.Transparent),
                new Setter(TextBox.BorderThicknessProperty, new Thickness(0)),
                new Setter(TextBox.BackgroundProperty, Brushes.Transparent),
                new Setter(TextBox.PaddingProperty, new Thickness(2, 8, 8, 8))
            }
        });
        searchBox.Styles.Add(new Style(x => x.OfType<TextBox>().Class(":focus"))
        {
            Setters =
            {
                new Setter(TextBox.BorderBrushProperty, Brushes.Transparent),
                new Setter(TextBox.BorderThicknessProperty, new Thickness(0)),
                new Setter(TextBox.BackgroundProperty, Brushes.Transparent)
            }
        });
        searchBox.Styles.Add(new Style(x => x.OfType<TextBox>().Class(":focus-visible"))
        {
            Setters =
            {
                new Setter(TextBox.BorderBrushProperty, Brushes.Transparent),
                new Setter(TextBox.BorderThicknessProperty, new Thickness(0)),
                new Setter(TextBox.BackgroundProperty, Brushes.Transparent)
            }
        });
        iconHost.Width = 22;
        iconHost.Height = 22;
        iconHost.HorizontalAlignment = HorizontalAlignment.Center;
        iconHost.VerticalAlignment = VerticalAlignment.Center;
        iconHost.Content = Ui.Icon("search", 16, Ui.Muted2);

        var border = new Border
        {
            Height = 44,
            Background = Ui.Surface2,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 0),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = "@",
                        FontSize = 15,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Ui.Accent,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 5, 0)
                    },
                    searchBox,
                    iconHost
                }
            }
        };
        if (border.Child is Grid grid)
        {
            Grid.SetColumn(searchBox, 1);
            Grid.SetColumn(iconHost, 2);
        }
        border.Styles.Add(new Style(x => x.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TextBox.BorderBrushProperty, Brushes.Transparent),
                new Setter(TextBox.BorderThicknessProperty, new Thickness(0)),
                new Setter(TextBox.BackgroundProperty, Brushes.Transparent)
            }
        });
        border.Styles.Add(new Style(x => x.OfType<TextBox>().Class(":focus"))
        {
            Setters =
            {
                new Setter(TextBox.BorderBrushProperty, Brushes.Transparent),
                new Setter(TextBox.BorderThicknessProperty, new Thickness(0))
            }
        });
        searchBox.GotFocus += (_, _) => border.BorderBrush = Ui.Accent;
        searchBox.LostFocus += (_, _) => border.BorderBrush = Ui.Border;
        return border;
    }

    private void CloseActiveConversation()
    {
        if (_selectedPeer == null || _closingConversation) return;
        var closingPeerId = _selectedPeer.id;
        _closingConversation = true;

        void FinalizeClose()
        {
            if (_selectedPeer?.id != closingPeerId)
            {
                _closingConversation = false;
                return;
            }

            _selectedPeer = null;
            _selectedStatus = "accepted";
            _selectedFromRequests = false;
            _messages.Clear();
            var reset = _allThreads.Select(x => new ConversationVm(x.Peer, x.Preview, x.Time, x.Unread, false, x.Status, x.IsMineLast, x.IsTyping)).ToList();
            _allThreads.Clear();
            foreach (var item in reset) _allThreads.Add(item);
            UpdateChatState();
            ApplyConversationFilter();
            _closingConversation = false;
        }

        if (_messageHost.Content is Control currentContent)
            Ui.SoftSlideOut(currentContent, FinalizeClose, -18);
        else
            FinalizeClose();
    }

    private void ShowNewConversationPanel()
    {
        _sidePanel.Content = null;

        _searchResults.Clear();
        var searchBox = Ui.TextBox("nome de utilizador");
        var iconHost = new ContentControl();
        var state = Ui.TextBlock("Pesquisa utilizadores pelo seu @handle.", 12, Ui.Muted);
        state.TextAlignment = TextAlignment.Center;
        var list = new ItemsControl
        {
            ItemsSource = _searchResults,
            ItemTemplate = new FuncDataTemplate<PublicUser>((u, _) => BuildSearchRow(u), true)
        };
        _newSearch = searchBox;
        _newSearchState = state;
        _newSearchList = list;
        _newSearchIconHost = iconHost;

        searchBox.TextChanged += (_, _) => RestartNewConversationSearchDebounce();
        searchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _newSearchDebounceTimer.Stop();
                if (_searchResults.Count > 0) await CreateMessageRequestAsync(_searchResults[0]);
                else await SearchUsersAsync();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                HideSidePanel();
            }
        };

        var close = Ui.IconButtonName("close");
        close.Opacity = 0.55;
        close.Width = 36;
        close.Height = 36;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Center;
        close.HorizontalContentAlignment = HorizontalAlignment.Center;
        close.VerticalContentAlignment = VerticalAlignment.Center;
        close.Click += (_, e) => { e.Handled = true; HideSidePanel(); };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { Ui.TextBlock("Nova conversa", 24, Ui.Text, FontWeight.Bold), close }
        };
        Grid.SetColumn(close, 1);

        _sidePanel.Content = new Border
        {
            Width = 420,
            Background = Ui.Surface,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(20),
            Child = Ui.V(14,
                header,
                Ui.Divider(),
                BuildHandleSearchField(searchBox, iconHost),
                new Border { Height = 1, Background = Ui.BorderSoft, Margin = new Thickness(0, 2) },
                Ui.TextBlock("RESULTADOS", 11, Ui.Muted, FontWeight.SemiBold),
                new ScrollViewer
                {
                    MaxHeight = 420,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = list
                },
                state)
        };
        _sidePanel.IsVisible = true;
        Ui.SoftFadeIn(_sidePanel);
        Dispatcher.UIThread.Post(() => searchBox.Focus());
    }

    private async Task CreateMessageRequestAsync(PublicUser user)
    {
        try
        {
            var resp = await ApiService.Chat.CreateMessageRequestAsync(user.id);
            if (resp?.success == true)
            {
                HideSidePanel();
                ToastService.Show($"Pedido enviado a @{user.username}", "check", ToastType.Success);
                await RefreshConversationListsAsync(false);
            }
            else
            {
                var msg = resp?.message ?? "Não foi possível enviar o pedido.";
                if (msg.Contains("conversa", StringComparison.OrdinalIgnoreCase))
                {
                    HideSidePanel();
                    await SelectPeerAsync(user, "accepted", false, false);
                    ToastService.Show("Esta conversa já existe.", "info", ToastType.Info);
                }
                else if (msg.Contains("existe", StringComparison.OrdinalIgnoreCase) || msg.Contains("pendente", StringComparison.OrdinalIgnoreCase))
                {
                    ToastService.Show($"Já existe um pedido pendente com @{user.username}", "info", ToastType.Warning);
                }
                else
                {
                    ToastService.Show(msg, "info", ToastType.Error);
                }

                if (_newSearchState != null)
                {
                    _newSearchState.Text = msg;
                    _newSearchState.Foreground = Ui.Danger;
                }
            }
        }
        catch (Exception ex)
        {
            var msg = string.IsNullOrWhiteSpace(ex.Message) ? "Erro de rede." : ex.Message;
            ToastService.Show("Não foi possível enviar o pedido.", "info", ToastType.Error);
            if (_newSearchState != null) { _newSearchState.Text = msg; _newSearchState.Foreground = Ui.Danger; }
        }
    }

    private async Task AcceptRequestAsync()
    {
        if (_selectedPeer == null) return;
        await AcceptRequestAsync(_selectedPeer);
    }

    private async Task AcceptRequestAsync(PublicUser peer)
    {
        var resp = await ApiService.Chat.AcceptMessageRequestAsync(peer.id);
        if (resp?.success == true)
        {
            _showingRequests = false;
            _selectedPeer = peer;
            _selectedStatus = "accepted";
            _selectedFromRequests = false;
            HideSidePanel();
            UpdateTabsVisual();
            UpdateActions();
            await RefreshAsync(true);
            await SelectPeerAsync(peer, "accepted", false, false);
            ShowStatus("Pedido aceite.", Ui.Success);
        }
        else ShowStatus(resp?.message ?? "Não foi possível aceitar.", Ui.Danger);
    }

    private async Task RejectRequestAsync()
    {
        if (_selectedPeer == null) return;
        await RejectRequestAsync(_selectedPeer);
    }

    private async Task RejectRequestAsync(PublicUser peer)
    {
        var resp = await ApiService.Chat.RejectMessageRequestAsync(peer.id);
        if (resp?.success == true)
        {
            if (_selectedPeer?.id == peer.id)
            {
                _selectedPeer = null;
                _messages.Clear();
                UpdateChatState();
            }
            HideSidePanel();
            await RefreshAsync(true);
            ShowStatus("Pedido rejeitado.", Ui.Muted);
        }
        else ShowStatus(resp?.message ?? "Não foi possível rejeitar.", Ui.Danger);
    }

    private async Task ShowRequestPreviewPanelAsync(ConversationVm vm)
    {
        _selectedPeer = vm.Peer;
        _selectedStatus = vm.Status;
        _selectedFromRequests = true;
        await LoadConversationAsync(vm.Peer, false, vm.Status, true);

        var accept = Ui.CircleButton("✓", Ui.Success, Brushes.Transparent);
        var reject = Ui.CircleButton("×", Ui.Danger, Brushes.Transparent);
        ToolTip.SetTip(accept, "Aceitar pedido");
        ToolTip.SetTip(reject, "Recusar pedido");
        accept.Click += async (_, e) => { e.Handled = true; await AcceptRequestAsync(vm.Peer); };
        reject.Click += async (_, e) => { e.Handled = true; await RejectRequestAsync(vm.Peer); };

        var preview = new ItemsControl
        {
            ItemsSource = _messages,
            ItemTemplate = new FuncDataTemplate<MessageVm>((msg, _) =>
            {
                if (msg == null) return new Border();
                var block = Ui.TextBlock(msg.Text, 13, Ui.Text);
                block.MaxWidth = 300;
                return new Border
                {
                    Margin = new Thickness(0, 4),
                    Padding = new Thickness(12, 8),
                    CornerRadius = new CornerRadius(14),
                    Background = Ui.Surface2,
                    BorderBrush = Ui.Border,
                    BorderThickness = new Thickness(1),
                    Child = block
                };
            }, true)
        };

        var close = Ui.IconButtonName("close");
        close.Width = 36;
        close.Height = 36;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Center;
        close.HorizontalContentAlignment = HorizontalAlignment.Center;
        close.VerticalContentAlignment = VerticalAlignment.Center;
        close.Click += (_, e) => { e.Handled = true; HideSidePanel(); };

        _sidePanel.Content = new Border
        {
            Width = 420,
            Background = Ui.Surface,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(24),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = Ui.V(16,
                    Ui.H(12, AvatarWithStatus(vm.Peer, 56), Ui.V(3, Ui.TextBlock(vm.Title, 20, Ui.Text, FontWeight.Bold), Ui.TextBlock("@" + vm.Peer.username, 12, Ui.Muted))),
                    Ui.TextBlock("Pedido de mensagem", 16, Ui.Text, FontWeight.SemiBold),
                    Ui.TextBlock("Podes ler antes de aceitar. Não há campo de resposta enquanto o pedido estiver pendente.", 13, Ui.Muted),
                    preview,
                    Ui.H(10, accept, reject),
                    close)
            }
        };
        _sidePanel.IsVisible = true;
        Ui.SoftFadeIn(_sidePanel);
    }

    private async Task LoadConversationAsync(PublicUser peer, bool showStatus, string status, bool fromRequests, CancellationToken ct = default)
    {
        var snapshot = await ApiService.Messages.GetConversationSnapshotAsync(peer.id, ct: ct);
        ct.ThrowIfCancellationRequested();
        if (SessionExpiredEvent.IsShown) return;
        if (snapshot?.success == true && snapshot.messages != null)
        {
            ApplyMessageSnapshot(BuildMessageSnapshot(snapshot.messages));
            var maxId = snapshot.messages.Count == 0 ? 0 : snapshot.messages.Max(x => x.id);
            if (maxId > 0 && status == "accepted")
            {
                ClearUnreadForPeer(peer.id);
                if (ShouldMarkRead(peer.id, maxId))
                {
                    _lastMarkedReadByPeer[peer.id] = maxId;
                    _ = ApiService.Messages.MarkReadAsync(peer.id, maxId);
                    if (ApiService.WebSocket.IsConnected)
                        _ = ApiService.WebSocket.SendAsync(new { type = "message.read", peer_id = peer.id, up_to_id = maxId });
                }
            }
            if (showStatus && _messages.Count == 0) ShowStatus("Conversa vazia.", Ui.Muted);
        }
        else if (showStatus) ShowStatus(snapshot?.message ?? "Não foi possível carregar a conversa.", Ui.Danger);
    }

    private bool ShouldMarkRead(int peerId, long maxId)
    {
        return !_lastMarkedReadByPeer.TryGetValue(peerId, out var last) || maxId > last;
    }

    private List<MessageVm> BuildMessageSnapshot(IEnumerable<EncryptedMessageEnvelope> rows)
    {
        var serverMessages = rows
            .OrderBy(x => x.id)
            .GroupBy(LogicalMessageId)
            .Select(g =>
            {
                var groupItems = g.ToList();
                var preferred = groupItems.FirstOrDefault(x => x.sender_id == SessionContext.UserId && !string.IsNullOrWhiteSpace(x.sender_ciphertext_b64)) ?? groupItems.First();
                return ToMessageVm(preferred, g.Key, groupItems);
            })
            .ToList();

        var merged = new List<MessageVm>(serverMessages);
        if (_selectedPeer != null && _localSystemMessages.TryGetValue(_selectedPeer.id, out var localEvents))
        {
            merged.AddRange(localEvents);
        }

        var ordered = merged
            .Where(x => !x.IsDateSeparator)
            .OrderBy(x => x.CreatedLocal)
            .ThenBy(x => x.SortId)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToList();

        var items = new List<MessageVm>();
        foreach (var message in ordered)
            AppendMessageWithDateSeparator(items, message);

        return items;
    }

    private static void AppendMessageWithDateSeparator(List<MessageVm> items, MessageVm message)
    {
        if (message.IsDateSeparator)
        {
            items.Add(message);
            return;
        }

        var messageDay = message.CreatedLocal.Date;
        var lastMessageDay = items.LastOrDefault(x => !x.IsDateSeparator)?.CreatedLocal.Date;
        if (lastMessageDay == null || lastMessageDay.Value != messageDay)
            items.Add(MessageVm.DateSeparator($"date:{messageDay:yyyyMMdd}", FormatDateSeparator(messageDay), messageDay));
        items.Add(message);
    }

    private static string LogicalMessageId(EncryptedMessageEnvelope m)
    {
        var raw = m.client_msg_id ?? string.Empty;
        return !string.IsNullOrWhiteSpace(raw) ? raw.Split(':')[0] : $"server:{m.id}";
    }

    private void ApplyMessageSnapshot(IReadOnlyList<MessageVm> next)
    {
        if (_messages.Count == next.Count && _messages.Zip(next).All(x => x.First.SameAs(x.Second))) return;
        var oldOffset = _messagesScroll.Offset.Y;
        var nearBottom = _messagesScroll.Extent.Height <= 0 || _messagesScroll.Extent.Height - (_messagesScroll.Offset.Y + _messagesScroll.Viewport.Height) < 90;
        var firstDiff = 0;
        while (firstDiff < _messages.Count && firstDiff < next.Count && _messages[firstDiff].SameAs(next[firstDiff])) firstDiff++;
        while (_messages.Count > firstDiff) _messages.RemoveAt(_messages.Count - 1);
        for (var i = firstDiff; i < next.Count; i++) _messages.Add(next[i]);
        if (nearBottom) ScrollToEnd();
        else Dispatcher.UIThread.Post(() => _messagesScroll.Offset = new Vector(_messagesScroll.Offset.X, oldOffset));
    }

    private MessageVm ToMessageVm(EncryptedMessageEnvelope m, string key, IReadOnlyList<EncryptedMessageEnvelope> logicalGroup)
    {
        var mine = m.sender_id == SessionContext.UserId;
        var deletedAll = logicalGroup.Any(x => x.is_deleted_for_all == true);
        var status = logicalGroup.Any(x => x.is_read == true)
            ? MessageDeliveryState.Read
            : logicalGroup.Any(x => x.is_delivered == true)
                ? MessageDeliveryState.Delivered
                : MessageDeliveryState.Sent;

        string text;
        if (deletedAll)
        {
            var actor = mine ? "Removeste uma mensagem para todos" : "Uma mensagem foi removida";
            return MessageVm.System(key, m.id, actor, FormatRelativeDate(m.created_at), "all", ParseLocalDate(m.created_at));
        }
        else
        {
            try { text = MessageCrypto.Decrypt(SessionContext.DeviceEncryptionPrivateKey!, m.eph_pub_b64, m.nonce_b64, m.ciphertext_b64); }
            catch { text = "[não desencriptável neste dispositivo]"; }
        }

        return new MessageVm(key, m.id, mine, text, FormatRelativeDate(m.created_at), status, deletedAll, CanDeleteForEveryone(m), createdLocal: ParseLocalDate(m.created_at));
    }

    private async Task SendAsync(string? explicitText = null)
    {
        if (_sending) return;
        var peer = _selectedPeer;
        var text = (explicitText ?? _messageInput.Text ?? string.Empty).Trim();
        if (peer == null) { ShowStatus("Escolhe uma conversa ou pesquisa um contacto.", Ui.Warning); return; }
        if (_selectedStatus == "pending") { ShowStatus("Aguarda que o utilizador aceite o pedido antes de enviares mensagens.", Ui.Warning); return; }
        if (peer.is_blocked) { ShowStatus("Este utilizador está bloqueado. Desbloqueia-o para enviares mensagens.", Ui.Warning); return; }
        if (string.IsNullOrWhiteSpace(text)) return;

        var logicalId = Guid.NewGuid().ToString("N");
        var tempMessage = new MessageVm(logicalId, -1, true, text, "agora", MessageDeliveryState.Sending, false, false, createdLocal: DateTime.Now);

        try
        {
            _sending = true;
            Ui.SetButtonLoading(_send, true);
            _send.IsEnabled = false;
            _messageInput.IsEnabled = false;
            _messageInput.Text = string.Empty;

            var optimistic = _messages.ToList();
            AppendMessageWithDateSeparator(optimistic, tempMessage);
            ApplyMessageSnapshot(optimistic);
            ScrollToEnd();

            await SendTypingAsync(false);

            var devices = await ApiService.Chat.GetUserDevicesAsync(peer.id);
            if (SessionExpiredEvent.IsShown) return;
            if (devices?.success != true || devices.devices.Count == 0)
            {
                ShowSendFailureNotice(logicalId, text);
                return;
            }

            var sent = 0;
            var lastStatus = "accepted";
            foreach (var target in devices.devices.Where(d => !string.IsNullOrWhiteSpace(d.device_encryption_public_key)))
            {
                var enc = MessageCrypto.Encrypt(text, target.device_encryption_public_key);
                var self = string.IsNullOrWhiteSpace(SessionContext.DeviceEncryptionPublicKeyBase64) ? null : MessageCrypto.Encrypt(text, SessionContext.DeviceEncryptionPublicKeyBase64);
                var resp = await ApiService.Messages.SendAsync(peer.id, enc.CiphertextB64, enc.NonceB64, enc.EphPubB64, logicalId + ":" + target.device_id, self?.CiphertextB64, self?.NonceB64, self?.EphPubB64, target.device_id);
                if (SessionExpiredEvent.IsShown) return;
                if (resp?.success == true) sent++;
                if (!string.IsNullOrWhiteSpace(resp?.thread_status)) lastStatus = resp.thread_status;
            }

            if (sent > 0)
            {
                _selectedStatus = lastStatus;
                if (lastStatus == "pending") ShowStatus("Pedido de mensagem enviado.", Ui.Warning);
                else ShowStatus("", Ui.Muted);
                await LoadConversationAsync(peer, false, _selectedStatus, false);
                await RefreshConversationListsAsync(false);
            }
            else
            {
                ShowSendFailureNotice(logicalId, text);
            }
        }
        catch
        {
            ShowSendFailureNotice(logicalId, text);
        }
        finally
        {
            _sending = false;
            Ui.SetButtonLoading(_send, false);
            _send.IsEnabled = CanCompose();
            _messageInput.IsEnabled = CanCompose();
            _messageInput.Focus();
        }
    }

    private void ShowSendFailureNotice(string logicalId, string? originalText = null)
    {
        RemoveTemporaryMessage(logicalId);
        if (!string.IsNullOrWhiteSpace(originalText) && string.IsNullOrWhiteSpace(_messageInput.Text))
        {
            _messageInput.Text = originalText;
            _messageInput.CaretIndex = _messageInput.Text.Length;
        }
        AddLocalSystemNotice("Não foi possível enviar a mensagem.");
        ShowStatus("Não foi possível enviar a mensagem.", Ui.Danger);
    }

    private void RemoveTemporaryMessage(string logicalId)
    {
        for (var i = _messages.Count - 1; i >= 0; i--)
        {
            if (_messages[i].Key == logicalId)
                _messages.RemoveAt(i);
        }
    }

    private void AddLocalSystemNotice(string text)
    {
        var peerId = _selectedPeer?.id ?? 0;
        if (peerId <= 0) return;

        var notice = MessageVm.System($"local:notice:{Guid.NewGuid():N}", -1, text, "agora", "notice", DateTime.Now);
        if (!_localSystemMessages.TryGetValue(peerId, out var events))
        {
            events = new List<MessageVm>();
            _localSystemMessages[peerId] = events;
        }
        events.Add(notice);

        var snapshot = _messages.Where(x => x.Key != notice.Key).ToList();
        AppendMessageWithDateSeparator(snapshot, notice);
        ApplyMessageSnapshot(snapshot);
        ScrollToEnd();
    }

    private async Task DeleteMessageAsync(MessageVm message, string scope)
    {
        if (message.ServerId <= 0) return;
        if (scope == "all" && !message.CanDeleteForAll)
        {
            ShowStatus("Só podes apagar para todos mensagens tuas recentes.", Ui.Warning);
            return;
        }
        var resp = await ApiService.Messages.DeleteMessageAsync(message.ServerId, scope);
        if (SessionExpiredEvent.IsShown) return;
        if (resp?.success == true)
        {
            var index = _messages.IndexOf(message);
            if (index >= 0 && scope == "me")
            {
                _messages.RemoveAt(index);
                var sys = MessageVm.System($"local-delete:{message.Key}:{scope}", message.ServerId, "Removeste uma mensagem", "agora", scope, DateTime.Now);
                _messages.Insert(index, sys);
                if (_selectedPeer != null)
                {
                    if (!_localSystemMessages.TryGetValue(_selectedPeer.id, out var list))
                        _localSystemMessages[_selectedPeer.id] = list = new List<MessageVm>();
                    list.RemoveAll(x => x.Key == sys.Key);
                    list.Add(sys);
                }
            }
            if (scope == "all" && _selectedPeer != null) await LoadConversationAsync(_selectedPeer, false, _selectedStatus, _selectedFromRequests);
            await RefreshConversationListsAsync(false);
            ShowStatus(string.Empty, Ui.Muted);
        }
        else ShowStatus(resp?.message ?? "Não foi possível apagar.", Ui.Danger);
    }

    private void HandleLocalTypingChanged()
    {
        if (!CanCompose()) return;
        var hasText = !string.IsNullOrWhiteSpace(_messageInput.Text);
        if (hasText)
        {
            _ = SendTypingAsync(true, forceHeartbeat: true);
            _typingStopTimer.Stop();
            _typingStopTimer.Start();
        }
        else
        {
            _typingStopTimer.Stop();
            _ = SendTypingAsync(false);
        }
    }

    private async Task SendTypingAsync(bool isTyping, bool forceHeartbeat = false)
    {
        if (!CanCompose()) return;
        if (_selectedPeer == null) return;

        var now = DateTime.UtcNow;
        if (_lastTypingSent == isTyping)
        {
            if (!isTyping) return;
            if (!forceHeartbeat || now - _lastTypingSentAtUtc < TimeSpan.FromSeconds(2))
                return;
        }

        _lastTypingSent = isTyping;
        _lastTypingSentAtUtc = now;
        await SendTypingToPeerAsync(_selectedPeer, isTyping);
    }

    private async Task SendTypingToPeerAsync(PublicUser peer, bool isTyping)
    {
        try
        {
            if (ApiService.WebSocket.IsConnected)
                await ApiService.WebSocket.SendAsync(new { type = isTyping ? "typing.start" : "typing.stop", peer_id = peer.id });
            else
                await ApiService.Messages.SetTypingAsync(peer.id, isTyping);
        }
        catch { }
    }

    private async Task RefreshTypingAsync()
    {
        var selectedPeer = _selectedPeer;
        if (selectedPeer == null || !CanCompose())
        {
            SetChatTypingIndicator(false);
            return;
        }
        if (ApiService.WebSocket.IsConnected)
            return;

        try
        {
            var resp = await ApiService.Messages.GetTypingStatusAsync(selectedPeer.id);
            SetChatTypingIndicator(resp?.success == true && resp.is_typing, selectedPeer.DisplayName);
        }
        catch { SetChatTypingIndicator(false); }
    }



    private async Task MaybeShowMessageNotificationAsync(WsEvent ev)
    {
        var peerId = ev.Get<int>("peer_id");
        if (peerId <= 0) peerId = ev.Get<int>("sender_id");
        var selectedPeer = _selectedPeer;
        if (selectedPeer != null && !_selectedFromRequests && peerId == selectedPeer.id && AppShellWindow.Instance?.IsActive == true)
            return;

        var peer = _allThreads.FirstOrDefault(x => x.Peer.id == peerId)?.Peer;
        var senderName = peer?.DisplayName ?? ev.Get<string>("sender_display_name") ?? "Vanished";
        var preview = SafeNotificationPreview(ev);
        await NotificationService.ShowMessageNotificationAsync(senderName, preview, peerId.ToString(CultureInfo.InvariantCulture));
    }

    private static string SafeNotificationPreview(WsEvent ev)
    {
        var preview = ev.Get<string>("preview") ?? string.Empty;
        preview = preview.Trim();

        if (string.IsNullOrWhiteSpace(preview))
            return "Nova mensagem cifrada.";

        if (LooksLikeEncryptedEnvelope(preview))
            return "Nova mensagem cifrada.";

        return preview;
    }

    private static bool LooksLikeEncryptedEnvelope(string value)
    {
        var trimmed = (value ?? string.Empty).TrimStart();
        return trimmed.StartsWith("{", StringComparison.Ordinal)
            || trimmed.StartsWith("[", StringComparison.Ordinal)
            || trimmed.Contains("ciphertext_b64", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("eph_pub_b64", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("nonce_b64", StringComparison.OrdinalIgnoreCase);
    }

    private async Task MaybeShowRequestNotificationAsync(WsEvent ev)
    {
        var senderName = ev.Get<string>("sender_display_name") ?? ev.Get<string>("username") ?? "Alguém";
        await NotificationService.ShowRequestNotificationAsync(senderName);
    }

    private void SubscribeWebSocketEvents()
    {
        if (_wsEventsSubscribed) return;
        ApiService.WebSocket.EventReceived += HandleWebSocketEventAsync;
        _wsEventsSubscribed = true;
    }

    private void UnsubscribeWebSocketEvents()
    {
        if (!_wsEventsSubscribed) return;
        ApiService.WebSocket.EventReceived -= HandleWebSocketEventAsync;
        _wsEventsSubscribed = false;
    }

    private Task HandleWebSocketEventAsync(WsEvent ev)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (SessionExpiredEvent.IsShown) return;
            switch (ev.Type)
            {
                case "typing.start":
                    SetPeerTyping(ev.Get<int>("peer_id"), true);
                    break;
                case "typing.stop":
                    SetPeerTyping(ev.Get<int>("peer_id"), false);
                    break;
                case "message.new":
                    await MaybeShowMessageNotificationAsync(ev);
                    var incomingPeerId = ev.Get<int>("peer_id");
                    if (incomingPeerId <= 0) incomingPeerId = ev.Get<int>("sender_id");
                    var isOpenPeer = _selectedPeer != null && !_selectedFromRequests && incomingPeerId == _selectedPeer.id;

                    if (isOpenPeer && !IsNearMessagesBottom())
                        _unreadWhileScrolled++;

                    UpdateScrollToBottomButton();

                    if (isOpenPeer)
                        await RefreshSelectedConversationFromEventAsync(ev);
                    else
                        await RefreshConversationListsAsync(false);
                    break;
                case "message.sent":
                case "message.delivered":
                case "message.read":
                case "message.deleted":
                    await RefreshSelectedConversationFromEventAsync(ev);
                    break;
                case "user.status":
                    SetPeerOnline(ev.Get<int>("peer_id"), ev.Get<bool>("online"));
                    break;
                case "user.deleted":
                    HandlePeerDeleted(ev.Get<int>("peer_id"));
                    break;
                case "user.profile.updated":
                    ApplyPeerProfileUpdate(ev.Get<PublicUser>("user"));
                    break;
                case "request.accepted":
                    await RefreshConversationListsAsync(false);
                    if (_selectedPeer != null && ev.Get<int>("peer_id") == _selectedPeer.id)
                    {
                        _selectedStatus = "accepted";
                        _selectedFromRequests = false;
                        UpdateActions();
                        await LoadConversationAsync(_selectedPeer, false, _selectedStatus, false);
                        SetMessageHostContent(_messagesArea, 18);
                        ShowStatus("Pedido aceite. Já podes enviar mensagens.", Ui.Success);
                    }
                    if (_showingRequests) await ShowRequestsMainAsync();
                    break;
                case "request.rejected":
                case "request.cancelled":
                    await RefreshConversationListsAsync(false);
                    if (_selectedPeer != null && ev.Get<int>("peer_id") == _selectedPeer.id)
                    {
                        _selectedStatus = "none";
                        _selectedFromRequests = false;
                        UpdateActions();
                        ShowStatus("Pedido de mensagem terminado.", Ui.Muted);
                    }
                    if (_showingRequests) await ShowRequestsMainAsync();
                    break;
                case "request.new":
                    await MaybeShowRequestNotificationAsync(ev);
                    await RefreshConversationListsAsync(false);
                    if (_showingRequests) await ShowRequestsMainAsync();
                    if (_selectedPeer != null) UpdateActions();
                    break;
                case "user.blocked":
                    ClearTypingState(ev.Get<int>("peer_id"));
                    await RefreshConversationListsAsync(false);
                    if (_showingRequests) await ShowRequestsMainAsync();
                    if (_selectedPeer != null) UpdateActions();
                    break;
                case "user.unblocked":
                    await RefreshConversationListsAsync(false);
                    if (_showingRequests) await ShowRequestsMainAsync();
                    if (_selectedPeer != null) UpdateActions();
                    break;
            }
        });
        return Task.CompletedTask;
    }


    private void SetPeerOnline(int peerId, bool online)
    {
        if (peerId <= 0) return;
        if (online) _onlinePeers.Add(peerId);
        else _onlinePeers.Remove(peerId);

        bool replace(ObservableCollection<ConversationVm> list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item.Peer.id != peerId) continue;
                item.Peer.is_online = online;
                list[i] = new ConversationVm(item.Peer, item.Preview, item.Time, item.Unread, item.IsSelected, item.Status, item.IsMineLast, item.IsTyping);
                return true;
            }
            return false;
        }
        replace(_allThreads);
        replace(_threads);
        if (_selectedPeer?.id == peerId)
        {
            _selectedPeer.is_online = online;
            _subtitle.Text = $"@{_selectedPeer.username} · {(online ? "online" : "offline")}";
        }
    }

    private void ApplyPeerProfileUpdate(PublicUser? updated)
    {
        if (updated == null || updated.id <= 0) return;

        PublicUser merge(PublicUser current)
        {
            current.username = string.IsNullOrWhiteSpace(updated.username) ? current.username : updated.username;
            current.full_name = string.IsNullOrWhiteSpace(updated.full_name) ? current.full_name : updated.full_name;
            current.bio = updated.bio ?? current.bio;
            current.avatar_base64 = updated.avatar_base64 ?? current.avatar_base64;
            current.avatar_mime = updated.avatar_mime ?? current.avatar_mime;
            current.public_key = string.IsNullOrWhiteSpace(updated.public_key) ? current.public_key : updated.public_key;
            current.key_version = updated.key_version <= 0 ? current.key_version : updated.key_version;
            current.is_online = updated.is_online || current.is_online || _onlinePeers.Contains(updated.id);
            current.last_seen_at = string.IsNullOrWhiteSpace(updated.last_seen_at) ? current.last_seen_at : updated.last_seen_at;
            return current;
        }

        bool replace(ObservableCollection<ConversationVm> list)
        {
            var changed = false;
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item.Peer.id != updated.id) continue;
                var peer = merge(item.Peer);
                list[i] = new ConversationVm(peer, item.Preview, item.Time, item.Unread, item.IsSelected, item.Status, item.IsMineLast, item.IsTyping);
                changed = true;
            }
            return changed;
        }

        replace(_allThreads);
        replace(_threads);
        for (var i = 0; i < _searchResults.Count; i++)
        {
            if (_searchResults[i].id == updated.id)
                _searchResults[i] = merge(_searchResults[i]);
        }

        if (_selectedPeer?.id == updated.id)
        {
            _selectedPeer = merge(_selectedPeer);
            _title.Text = _selectedPeer.DisplayName;
            _subtitle.Text = $"@{_selectedPeer.username} · {(_selectedPeer.is_online ? "online" : "offline")}";
        }
    }

    private void SetPeerTyping(int peerId, bool isTyping)
    {
        if (peerId <= 0) return;

        if (isTyping && IsPeerBlockedLocally(peerId))
        {
            ClearTypingState(peerId);
            return;
        }

        if (isTyping)
        {
            _typingPeers.Add(peerId);
            if (_typingExpiryTimers.TryGetValue(peerId, out var oldTimer))
                oldTimer.Stop();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _typingExpiryTimers.Remove(peerId);
                SetPeerTyping(peerId, false);
            };
            _typingExpiryTimers[peerId] = timer;
            timer.Start();
        }
        else
        {
            ClearTypingState(peerId);
        }

        if (_selectedPeer?.id == peerId)
            SetChatTypingIndicator(isTyping && !IsPeerBlockedLocally(peerId), _selectedPeer.DisplayName);

        UpdateTypingInThread(peerId, isTyping && !IsPeerBlockedLocally(peerId));
    }

    private void ClearTypingState(int peerId)
    {
        _typingPeers.Remove(peerId);
        if (_typingExpiryTimers.TryGetValue(peerId, out var timer))
        {
            timer.Stop();
            _typingExpiryTimers.Remove(peerId);
        }

        if (_selectedPeer?.id == peerId)
            SetChatTypingIndicator(false);
        UpdateTypingInThread(peerId, false);
    }

    private void HandlePeerDeleted(int peerId)
    {
        if (peerId <= 0) return;

        ClearTypingState(peerId);
        _onlinePeers.Remove(peerId);
        _localSystemMessages.Remove(peerId);
        _lastMarkedReadByPeer.Remove(peerId);

        RemoveThreadByPeer(_allThreads, peerId);
        RemoveThreadByPeer(_threads, peerId);

        if (_selectedPeer?.id == peerId)
        {
            _selectedPeer = null;
            _selectedStatus = "accepted";
            _selectedFromRequests = false;
            _messages.Clear();
            UpdateChatState();
            ShowStatus("Esta conta foi apagada e a conversa já não está disponível.", Ui.Warning);
            ToastService.Show("A conta deste utilizador foi apagada.", "info", ToastType.Warning);
        }

        ApplyConversationFilter();
    }

    private static void RemoveThreadByPeer(ObservableCollection<ConversationVm> collection, int peerId)
    {
        for (var i = collection.Count - 1; i >= 0; i--)
        {
            if (collection[i].Peer.id == peerId)
                collection.RemoveAt(i);
        }
    }

    private bool IsPeerBlockedLocally(int peerId)
    {
        if (peerId <= 0) return false;
        if (_selectedPeer?.id == peerId && _selectedPeer.is_blocked)
            return true;

        var threadPeer = _allThreads.FirstOrDefault(x => x.Peer.id == peerId)?.Peer
                         ?? _threads.FirstOrDefault(x => x.Peer.id == peerId)?.Peer;

        return threadPeer?.is_blocked == true;
    }

    private void UpdateTypingInThread(int peerId, bool isTyping)
    {
        bool replace(ObservableCollection<ConversationVm> list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item.Peer.id != peerId) continue;
                var updated = new ConversationVm(item.Peer, item.Preview, item.Time, item.Unread, item.IsSelected, item.Status, item.IsMineLast, isTyping);
                if (!item.SameAs(updated)) list[i] = updated;
                return true;
            }
            return false;
        }

        replace(_allThreads);
        replace(_threads);
    }

    private void SubscribeProfileEvents()
    {
        if (_profileEventsSubscribed) return;
        SessionContext.ProfileUpdated += OnProfileUpdated;
        UserSession.Current.PropertyChanged += OnUserSessionPropertyChanged;
        _profileEventsSubscribed = true;
    }

    private void UnsubscribeProfileEvents()
    {
        if (!_profileEventsSubscribed) return;
        SessionContext.ProfileUpdated -= OnProfileUpdated;
        UserSession.Current.PropertyChanged -= OnUserSessionPropertyChanged;
        _profileEventsSubscribed = false;
    }

    private void OnUserSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UserSession.DisplayName) or nameof(UserSession.Username) or nameof(UserSession.Email) or nameof(UserSession.AvatarInitial) or nameof(UserSession.DisplayLabel) or nameof(UserSession.AvatarBase64))
            Dispatcher.UIThread.Post(UpdateProfileHeader);
    }

    private void OnProfileUpdated() => Dispatcher.UIThread.Post(async () =>
    {
        UpdateProfileHeader();
        await RefreshConversationListsAsync(false);
    });
    private void OnThemeChanged() => Dispatcher.UIThread.Post(() => { Background = Ui.Bg; UpdateChatState(); UpdateProfileHeader(); });

    private static string CurrentUserDisplayName() => UserSession.Current.DisplayLabel;

    private void UpdateProfileHeader()
    {
        var name = CurrentUserDisplayName();
        _profileName.Text = string.IsNullOrWhiteSpace(name) ? "Vanished" : name;
        _profileEmail.Text = UserSession.Current.Email;
        _profileAvatarInitial.Text = UserSession.Current.AvatarInitial;
        _profileAvatarImageHost.Content = Ui.AvatarImage(SessionContext.AvatarBase64, name, _profileAvatarSize);
    }

    private void ShowProfilePanel()
    {
        _sidePanel.Content = null;

        var dismiss = Ui.IconButtonName("close");
        dismiss.Opacity = 0.55;
        dismiss.Width = 36;
        dismiss.Height = 36;
        dismiss.HorizontalAlignment = HorizontalAlignment.Right;
        dismiss.VerticalAlignment = VerticalAlignment.Center;
        dismiss.HorizontalContentAlignment = HorizontalAlignment.Center;
        dismiss.VerticalContentAlignment = VerticalAlignment.Center;
        dismiss.Click += (_, e) => { e.Handled = true; HideSidePanel(); };

        var viewProfile = Ui.MenuOption("profile", "Ver perfil");
        var editProfile = Ui.MenuOption("edit", "Editar perfil");
        var settings = Ui.MenuOption("settings", "Definições");
        var keysSessions = Ui.MenuOption("key", "Chaves e sessões");
        var logout = Ui.MenuOption("logout", "Terminar sessão", Ui.Danger);

        viewProfile.Click += (_, _) => { HideSidePanel(); NavigationService.Navigate(new ProfilePage()); };
        editProfile.Click += (_, _) => { HideSidePanel(); NavigationService.Navigate(new EditProfilePage()); };
        settings.Click += (_, _) => { HideSidePanel(); NavigationService.Navigate(new SettingsPage()); };
        keysSessions.Click += async (_, _) => await ShowKeysSessionsFromMenuAsync();
        logout.Click += (_, _) => { HideSidePanel(); ShowConfirmLogoutDialog(); };

        var top = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                Ui.H(12,
                    AvatarWithStatus(SessionContext.AvatarBase64, string.IsNullOrWhiteSpace(SessionContext.DisplayName) ? SessionContext.Username : SessionContext.DisplayName, true, 52),
                    Ui.V(2,
                        Ui.TextBlock(string.IsNullOrWhiteSpace(SessionContext.DisplayName) ? SessionContext.Username : SessionContext.DisplayName, 18, Ui.Text, FontWeight.Bold),
                        Ui.TextBlock(SessionContext.Email, 12, Ui.Muted))),
                dismiss
            }
        };
        Grid.SetColumn(dismiss, 1);
        if (top.Children.Count > 0) top.Children[0].IsHitTestVisible = false;

        _sidePanel.Content = new Border
        {
            Width = 360,
            Height = double.NaN,
            Background = Ui.Surface,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(20),
            Child = Ui.V(16,
                top,
                Ui.Divider(),
                Ui.MenuPanel(viewProfile, editProfile, Ui.Divider(), settings, keysSessions, Ui.Divider(), logout))
        };
        _sidePanel.IsVisible = true;
        Ui.SoftFadeIn(_sidePanel);
    }

    private async Task ShowKeysSessionsFromMenuAsync()
    {
        var result = await ModalService.ShowSensitiveActionAsync(new SensitiveActionRequest
        {
            Title = "Chaves e sessões",
            Description = "Confirma Vanished PIN e TOTP para consultar recovery key, devices e sessões da conta.",
            RequireAccountPin = true,
            RequireLocalPassword = false,
            OnConfirmWithPin = async (_, accountPin, mfa) =>
            {
                if (!SessionContext.VerifyCurrentMfa(mfa))
                    return SensitiveActionResult.Fail("Código MFA incorreto. Se acabaste de abrir a app por sessão persistente, termina sessão e inicia novamente para desbloquear o MFA em memória.");

                var response = await ApiService.Auth.GetAccountKeysSessionsAsync(accountPin);
                if (!response.success)
                    return SensitiveActionResult.Fail(response.message ?? "Não foi possível carregar gestão de chaves e sessões.");

                Dispatcher.UIThread.Post(() => ShowKeysSessionsPanel(response));
                return SensitiveActionResult.Ok("Gestão carregada.");
            }
        });

        if (result?.IsSuccess != true)
            return;
    }

    private void ShowKeysSessionsPanel(AccountKeysSessionsResponse response)
    {
        var dismiss = Ui.IconButtonName("close");
        dismiss.Width = 36;
        dismiss.Height = 36;
        dismiss.HorizontalAlignment = HorizontalAlignment.Right;
        dismiss.Click += (_, e) => { e.Handled = true; HideSidePanel(); };

        var rows = new List<Control>
        {
            BuildRecoveryStateCard(response),
            Ui.TextBlock("Devices", 15, Ui.Text, FontWeight.SemiBold)
        };

        if (response.devices == null || response.devices.Count == 0)
            rows.Add(Ui.Card(Ui.TextBlock("Nenhum device registado.", 13, Ui.Muted), new Thickness(16), 12));
        else
        {
            foreach (var device in response.devices)
                rows.Add(BuildDeviceManagementRow(device, response));
        }

        rows.Add(Ui.Divider());
        rows.Add(Ui.TextBlock("Sessões", 15, Ui.Text, FontWeight.SemiBold));

        if (response.sessions == null || response.sessions.Count == 0)
            rows.Add(Ui.Card(Ui.TextBlock("Nenhuma sessão encontrada.", 13, Ui.Muted), new Thickness(16), 12));
        else
        {
            foreach (var session in response.sessions)
                rows.Add(BuildSessionManagementRow(session, response));
        }

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                Ui.V(2,
                    Ui.TextBlock("Chaves e sessões", 24, Ui.Text, FontWeight.Bold),
                    Ui.TextBlock("Gere o estado da recovery key, devices e sessões ativas.", 12, Ui.Muted)),
                dismiss
            }
        };
        Grid.SetColumn(dismiss, 1);

        _sidePanel.Content = new Border
        {
            Width = 460,
            Background = Ui.Surface,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(20),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = Ui.V(14, new List<Control> { header, Ui.Divider() }.Concat(rows).ToArray())
            }
        };
        _sidePanel.IsVisible = true;
        Ui.SoftFadeIn(_sidePanel);
    }

    private static Control BuildRecoveryStateCard(AccountKeysSessionsResponse response)
    {
        var status = string.Equals(response.recovery_key.status, "valid", StringComparison.OrdinalIgnoreCase)
            ? "Recovery key válida"
            : "Recovery key indisponível";
        var statusBrush = string.Equals(response.recovery_key.status, "valid", StringComparison.OrdinalIgnoreCase) ? Ui.Success : Ui.Warning;
        var keyVersion = response.recovery_key.key_version > 0 ? response.recovery_key.key_version : response.identity.key_version;

        return Ui.Card(
            Ui.V(6,
                Ui.H(8, Ui.Icon("key", 16, statusBrush), Ui.TextBlock(status, 15, Ui.Text, FontWeight.SemiBold)),
                Ui.TextBlock($"Fingerprint: {response.recovery_key.fingerprint}", 12, Ui.Muted),
                Ui.TextBlock($"Identity key version: {keyVersion}", 12, Ui.Muted2),
                Ui.TextBlock("A recovery key em plaintext nunca é guardada no servidor.", 11, Ui.Muted2)),
            new Thickness(14),
            12);
    }

    private Control BuildDeviceManagementRow(MyDeviceDescriptor device, AccountKeysSessionsResponse response)
    {
        var isRevoked = !string.IsNullOrWhiteSpace(device.revoked_at);
        var status = isRevoked ? "revogado" : (device.is_current ? "atual" : "ativo");
        var statusBrush = isRevoked ? Ui.Muted2 : (device.is_current ? Ui.Accent : Ui.Success);
        var title = string.IsNullOrWhiteSpace(device.name) ? "Desktop" : device.name;
        var subtitle = $"{(string.IsNullOrWhiteSpace(device.platform) ? "unknown" : device.platform)} · {FormatDeviceDate(device.last_seen_at, "visto")}";

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                Ui.V(2,
                    Ui.TextBlock(title, 14, Ui.Text, FontWeight.SemiBold),
                    Ui.TextBlock(subtitle, 11, Ui.Muted2)),
                Ui.Pill(Ui.TextBlock(status, 11, statusBrush, FontWeight.SemiBold), isRevoked ? Ui.Surface2 : Ui.AccentSoft, isRevoked ? Ui.Border : Ui.Accent)
            }
        };
        Grid.SetColumn(header.Children[1], 1);

        var children = new List<Control>
        {
            header,
            Ui.TextBlock($"Device ID: {ShortRef(device.device_id)}", 11, Ui.Muted2)
        };

        if (!device.is_current && !isRevoked)
        {
            var revoke = Ui.DangerButton("Revogar device");
            revoke.HorizontalAlignment = HorizontalAlignment.Left;
            revoke.Click += async (_, _) =>
            {
                revoke.IsEnabled = false;
                var apiResult = await ApiService.Devices.RevokeDeviceAsync(device.device_id);
                if (apiResult?.success == true)
                {
                    device.revoked_at = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    ShowStatus("Device revogado.", Ui.Success);
                    ShowKeysSessionsPanel(response);
                }
                else
                {
                    ShowStatus(apiResult?.message ?? "Não foi possível revogar device.", Ui.Danger);
                    revoke.IsEnabled = true;
                }
            };
            children.Add(revoke);
        }

        return Ui.Card(Ui.V(7, children.ToArray()), new Thickness(14), 12);
    }

    private Control BuildSessionManagementRow(AccountSessionDescriptor session, AccountKeysSessionsResponse response)
    {
        var status = session.is_current_session ? "atual" : (session.is_active ? "ativa" : "revogada/expirada");
        var statusBrush = session.is_active ? Ui.Success : Ui.Muted2;
        var created = FormatDeviceDate(session.created_at, "criada");
        var expires = FormatDeviceDate(session.expires_at, "expira");

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                Ui.V(2,
                    Ui.TextBlock($"Sessão #{session.id}", 14, Ui.Text, FontWeight.SemiBold),
                    Ui.TextBlock($"{created} · {expires}", 11, Ui.Muted2)),
                Ui.Pill(Ui.TextBlock(status, 11, statusBrush, FontWeight.SemiBold), session.is_active ? Ui.AccentSoft : Ui.Surface2, session.is_active ? Ui.Accent : Ui.Border)
            }
        };
        Grid.SetColumn(header.Children[1], 1);

        var children = new List<Control>
        {
            header,
            Ui.TextBlock($"Device ID: {ShortRef(session.device_id)}", 11, Ui.Muted2)
        };

        if (session.is_active && !session.is_current_session)
        {
            var revoke = Ui.DangerButton("Revogar sessão");
            revoke.HorizontalAlignment = HorizontalAlignment.Left;
            revoke.Click += async (_, _) =>
            {
                revoke.IsEnabled = false;
                var apiResult = await ApiService.Auth.RevokeSessionAsync(session.id);
                if (apiResult?.success == true)
                {
                    session.revoked_at = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    session.is_active = false;
                    ShowStatus("Sessão revogada.", Ui.Success);
                    ShowKeysSessionsPanel(response);
                }
                else
                {
                    ShowStatus(apiResult?.message ?? "Não foi possível revogar sessão.", Ui.Danger);
                    revoke.IsEnabled = true;
                }
            };
            children.Add(revoke);
        }
        else if (session.is_current_session && session.is_active)
        {
            children.Add(Ui.TextBlock("Sessão atual. Usa Terminar sessão para encerrar.", 11, Ui.Muted2));
        }

        return Ui.Card(Ui.V(7, children.ToArray()), new Thickness(14), 12);
    }

    private static string FormatDeviceDate(string value, string label)
    {
        if (DateTime.TryParse(value, out var dt))
            return $"{label} {dt.ToLocalTime():yyyy-MM-dd HH:mm}";
        return $"{label} desconhecida";
    }

    private static string ShortRef(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "n/a";
        return value.Length <= 12 ? value : value[..6] + "…" + value[^4..];
    }

    private void ShowConfirmLogoutDialog()
    {
        var cancel = Ui.GhostButton("Cancelar");
        var confirm = Ui.DangerButton("Terminar sessão");
        cancel.MinWidth = 90;
        cancel.Height = 38;
        cancel.HorizontalContentAlignment = HorizontalAlignment.Center;
        cancel.VerticalContentAlignment = VerticalAlignment.Center;
        confirm.MinWidth = 140;
        confirm.Height = 38;
        confirm.HorizontalContentAlignment = HorizontalAlignment.Center;
        confirm.VerticalContentAlignment = VerticalAlignment.Center;
        cancel.Click += (_, _) => HideDialog();
        confirm.Click += async (_, _) => { HideDialog(); await LogoutAsync(); };

        var buttonsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 20, 0, 0),
            Children = { cancel, confirm }
        };

        _dialogOverlay.Child = new Grid
        {
            Children =
            {
                Ui.Card(Ui.V(12,
                    Ui.TextBlock("Terminar sessão", 22, Ui.Text, FontWeight.Bold),
                    Ui.TextBlock("Tens a certeza que queres terminar sessão? Precisarás de fazer login novamente.", 13, Ui.Muted),
                    buttonsRow),
                    new Thickness(24), 18)
            }
        };
        if (_dialogOverlay.Child is Grid grid && grid.Children.Count > 0)
        {
            grid.Children[0].HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children[0].VerticalAlignment = VerticalAlignment.Center;
            grid.Children[0].MaxWidth = 420;
        }
        _dialogOverlay.IsVisible = true;
        _dialogOverlay.Opacity = 0;
        Ui.SoftFadeIn(_dialogOverlay);
        _dialogOverlay.Focus();
    }

    private void HideDialog()
    {
        _dialogOverlay.Child = null;
        _dialogOverlay.IsVisible = false;
    }

    private async Task LogoutAsync()
    {
        try { await ApiService.Connection.StopAsync(); } catch { }
        try { await ApiService.Auth.LogoutAsync(); } catch { }
        TokenHelper.ClearToken();
        SessionContext.Clear();
        NavigationService.Reset(new AuthPage());
    }

    private string DecryptPreview(EncryptedMessageEnvelope? last)
    {
        if (last == null) return "Sem mensagens";
        if (last.is_deleted_for_all == true) return "Esta mensagem foi apagada.";
        try
        {
            var text = MessageCrypto.Decrypt(SessionContext.DeviceEncryptionPrivateKey!, last.eph_pub_b64, last.nonce_b64, last.ciphertext_b64);
            var prefix = last.sender_id == SessionContext.UserId ? "Tu: " : "";
            var preview = prefix + text;
            return preview.Length > 44 ? preview[..44] + "..." : preview;
        }
        catch { return "[cifrada]"; }
    }

    private static bool CanDeleteForEveryone(EncryptedMessageEnvelope m)
    {
        if (m.sender_id != SessionContext.UserId) return false;
        return DateTime.TryParse(m.created_at, out var dt) && DateTime.UtcNow - dt.ToUniversalTime() <= TimeSpan.FromMinutes(5);
    }

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() => _messagesScroll.Offset = new Vector(_messagesScroll.Offset.X, double.MaxValue));
    private void ShowStatus(string message, IBrush brush)
    {
        _status.Text = message;
        _status.Foreground = brush;
        _status.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private static string FormatRelativeDate(string? value)
    {
        var local = ParseLocalDate(value);
        if (local == DateTime.MinValue) return string.Empty;
        var now = DateTime.Now;
        var diff = now - local;
        if (diff.TotalSeconds < 60) return "agora";
        if (diff.TotalMinutes < 60) return $"há {(int)diff.TotalMinutes} min";
        if (local.Date == now.Date) return local.ToString("HH:mm");
        if (local.Date == now.Date.AddDays(-1)) return "ontem";
        if (diff.TotalDays < 7) return local.ToString("ddd", CultureInfo.GetCultureInfo("pt-PT"));
        return local.ToString("dd/MM");
    }

    private static DateTime ParseLocalDate(string? value)
    {
        if (!DateTime.TryParse(value, out var dt)) return DateTime.MinValue;
        return dt.ToLocalTime();
    }

    private static string FormatDateSeparator(DateTime localDate)
    {
        var culture = CultureInfo.GetCultureInfo("pt-PT");
        var day = culture.DateTimeFormat.GetDayName(localDate.DayOfWeek);
        day = string.IsNullOrWhiteSpace(day) ? string.Empty : char.ToUpper(day[0], culture) + day[1..];
        return $"{localDate:dd/MM/yyyy} - {day}";
    }

    private sealed class ConversationVm
    {
        public ConversationVm(PublicUser peer, string preview, string time, int unread, bool isSelected, string status, bool isMineLast, bool isTyping = false)
        {
            Peer = peer;
            Preview = preview;
            Time = time;
            Unread = unread;
            IsSelected = isSelected;
            Status = string.IsNullOrWhiteSpace(status) ? "accepted" : status;
            IsMineLast = isMineLast;
            IsTyping = isTyping;
        }
        public PublicUser Peer { get; }
        public string Title => Peer.DisplayName;
        public string Preview { get; }
        public string Time { get; }
        public int Unread { get; }
        public bool IsSelected { get; }
        public string Status { get; }
        public bool IsMineLast { get; }
        public bool IsTyping { get; }
        public bool SameAs(ConversationVm other) => Peer.id == other.Peer.id && Preview == other.Preview && Time == other.Time && Unread == other.Unread && IsSelected == other.IsSelected && Status == other.Status && Peer.is_online == other.Peer.is_online && IsTyping == other.IsTyping;
    }

    private enum MessageDeliveryState { Sending, Sent, Delivered, Read }

    private sealed class MessageVm
    {
        public MessageVm(
            string key,
            long serverId,
            bool isMine,
            string text,
            string meta,
            MessageDeliveryState state,
            bool isDeletedForAll,
            bool canDeleteForAll,
            bool isSystem = false,
            string systemScope = "",
            DateTime? createdLocal = null,
            bool isDateSeparator = false)
        {
            Key = key;
            ServerId = serverId;
            IsMine = isMine;
            Text = text;
            Meta = meta;
            State = state;
            IsDeletedForAll = isDeletedForAll;
            CanDeleteForAll = canDeleteForAll && !isDeletedForAll && !isSystem && !isDateSeparator;
            IsSystem = isSystem;
            SystemScope = systemScope;
            CreatedLocal = createdLocal ?? DateTime.Now;
            IsDateSeparator = isDateSeparator;
        }

        public static MessageVm System(string key, long serverId, string text, string meta, string scope, DateTime? createdLocal = null)
            => new(key, serverId, false, text, meta, MessageDeliveryState.Sent, scope == "all", false, true, scope, createdLocal);

        public static MessageVm DateSeparator(string key, string text, DateTime day)
            => new(key, -1, false, text, string.Empty, MessageDeliveryState.Sent, false, false, false, "date", day, true);

        public string Key { get; }
        public long ServerId { get; }
        public long SortId => ServerId <= 0 ? long.MaxValue : ServerId;
        public bool IsMine { get; }
        public string Text { get; }
        public string Meta { get; }
        public MessageDeliveryState State { get; }
        public bool IsDeletedForAll { get; }
        public bool CanDeleteForAll { get; }
        public bool IsSystem { get; }
        public string SystemScope { get; }
        public DateTime CreatedLocal { get; }
        public bool IsDateSeparator { get; }
        public string StatusIcon => State switch
        {
            MessageDeliveryState.Sending => "…",
            MessageDeliveryState.Sent => "✓",
            MessageDeliveryState.Delivered => "✓✓",
            MessageDeliveryState.Read => "✓✓",
            _ => "✓"
        };
        public IBrush StatusBrush => State switch
        {
            MessageDeliveryState.Sending => Ui.MessageStatusPending,
            MessageDeliveryState.Sent => Ui.MessageStatusSent,
            MessageDeliveryState.Delivered => Ui.MessageStatusDelivered,
            MessageDeliveryState.Read => Ui.MessageStatusRead,
            _ => Ui.MessageStatusSent
        };
        public bool SameAs(MessageVm other) =>
            Key == other.Key &&
            ServerId == other.ServerId &&
            IsMine == other.IsMine &&
            Text == other.Text &&
            Meta == other.Meta &&
            State == other.State &&
            IsDeletedForAll == other.IsDeletedForAll &&
            IsSystem == other.IsSystem &&
            SystemScope == other.SystemScope &&
            CreatedLocal == other.CreatedLocal &&
            IsDateSeparator == other.IsDateSeparator;
    }
}
