using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PDFtoImage;
using PdfRenderOptions = PDFtoImage.RenderOptions;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Vanished.UI;

namespace Vanished.Pages;

public sealed class TermsAndConditionsWindow : Window
{
    private const string TermsConditionsPdfAsset = "avares://Vanished/Resources/Legal/TermsAndConditions.pdf";

    private static TermsAndConditionsWindow? _current;

    private readonly StackPanel _pagesPanel = Ui.V(18);
    private readonly TextBlock _status = Ui.StatusBlock();
    private readonly Button _openExternalButton;
    private bool _renderStarted;

    public TermsAndConditionsWindow()
    {
        Title = "Termos e Condições - Vanished";
        Width = 900;
        Height = 740;
        MinWidth = 700;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Bg;
        CanResize = true;

        SystemDecorations = SystemDecorations.None;

        TryApplyWindowIcon();

        _openExternalButton = Ui.SecondaryButton("Abrir PDF no leitor do sistema");
        _openExternalButton.Width = 230;
        _openExternalButton.HorizontalAlignment = HorizontalAlignment.Left;
        _openExternalButton.Click += (_, _) => OpenPdfInSystemReader();

        Content = BuildContent();
        Opened += async (_, _) => await RenderPdfOnceAsync();
    }

    public static void ShowOrActivate(Window? owner = null)
    {
        if (_current is not null)
        {
            try
            {
                _current.Activate();
                return;
            }
            catch
            {
                _current = null;
            }
        }

        var window = new TermsAndConditionsWindow();
        _current = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, window))
                _current = null;
        };

        if (owner is not null)
            window.Show(owner);
        else
            window.Show();

        window.Activate();
    }

    private Control BuildContent()
    {
        var title = Ui.TextBlock("Termos e Condições", 28, Ui.Text, FontWeight.Bold);
        var subtitle = Ui.TextBlock("Consulta os termos e condições antes de criares a tua conta Vanished.", 13, Ui.Muted);
        subtitle.TextWrapping = TextWrapping.Wrap;

        var header = Ui.V(4, title, subtitle);
        header.Margin = new Thickness(0, 0, 0, 12);
        header.PointerPressed += (_, e) => TryBeginWindowDrag(e);

        _pagesPanel.Children.Add(BuildLoadingState());

        var viewer = new Border
        {
            Background = Ui.Surface2,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = _pagesPanel
            }
        };

        var closeButton = Ui.PrimaryButton("Fechar");
        closeButton.Width = 120;
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        closeButton.Click += (_, _) => Close();

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                _openExternalButton,
                _status,
                closeButton
            }
        };
        Grid.SetColumn(_status, 1);
        Grid.SetColumn(closeButton, 2);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Background = Ui.Bg,
            Margin = new Thickness(24),
            Children =
            {
                header,
                viewer,
                footer
            }
        };
        Grid.SetRow(viewer, 1);
        Grid.SetRow(footer, 2);

        return root;
    }

    private async Task RenderPdfOnceAsync()
    {
        if (_renderStarted)
            return;

        _renderStarted = true;
        _status.Foreground = Ui.Muted;

        try
        {
            var pdfBytes = await Task.Run(ReadEmbeddedTermsConditionsPdf);
            var pages = await Task.Run(() => RenderPdfPages(pdfBytes));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _pagesPanel.Children.Clear();

                if (pages.Count == 0)
                {
                    _pagesPanel.Children.Add(BuildFallbackState("Não foi possível renderizar o PDF na app. Podes abrir o documento no leitor do sistema."));
                    _status.Text = "Pré-visualização indisponível.";
                    _status.Foreground = Ui.Warning;
                    return;
                }

                foreach (var page in pages)
                    _pagesPanel.Children.Add(BuildPageFrame(page));

                _status.Text = string.Empty;
                _status.Foreground = Ui.Muted;
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _pagesPanel.Children.Clear();
                _pagesPanel.Children.Add(BuildFallbackState("Não foi possível carregar o PDF na app. Podes abrir o documento no leitor do sistema."));
                _status.Text = string.IsNullOrWhiteSpace(ex.Message)
                    ? "Erro ao carregar PDF."
                    : ex.Message;
                _status.Foreground = Ui.Danger;
            });
        }
    }

    private static Control BuildLoadingState()
    {
        var text = Ui.TextBlock("A carregar PDF...", 14, Ui.Muted, FontWeight.SemiBold);
        text.HorizontalAlignment = HorizontalAlignment.Center;

        return new Border
        {
            MinHeight = 320,
            Background = Brushes.White,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24),
            Child = text
        };
    }

    private static Control BuildFallbackState(string message)
    {
        var text = Ui.TextBlock(message, 14, Ui.Warning, FontWeight.SemiBold);
        text.TextAlignment = TextAlignment.Center;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;

        return new Border
        {
            MinHeight = 320,
            Background = Ui.Surface,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24),
            Child = text
        };
    }

    private static Control BuildPageFrame(Bitmap page)
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Image
            {
                Source = page,
                MaxWidth = 800,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
    }

    private static byte[] ReadEmbeddedTermsConditionsPdf()
    {
        using var input = AssetLoader.Open(new Uri(TermsConditionsPdfAsset));
        using var memory = new MemoryStream();
        input.CopyTo(memory);
        return memory.ToArray();
    }

    private static List<Bitmap> RenderPdfPages(byte[] pdfBytes)
    {
        var pages = new List<Bitmap>();

       foreach (var skBitmap in Conversion.ToImages(pdfBytes, options: new PdfRenderOptions(Dpi: 180)))
        {
            using (skBitmap)
            {
                pages.Add(ConvertToAvaloniaBitmap(skBitmap));
            }
        }

        return pages;
    }

    private static Bitmap ConvertToAvaloniaBitmap(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        if (encoded is null)
            throw new InvalidOperationException("Falha ao converter página do PDF para imagem interna.");

        using var stream = encoded.AsStream();
        return new Bitmap(stream);
    }


    private void TryBeginWindowDrag(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OpenPdfInSystemReader()
    {
        try
        {
            var destination = CopyTermsConditionsPdfToTempFile();

            Process.Start(new ProcessStartInfo
            {
                FileName = destination,
                UseShellExecute = true
            });

            _status.Text = "PDF aberto no leitor do sistema.";
            _status.Foreground = Ui.Success;
        }
        catch (Exception ex)
        {
            _status.Text = string.IsNullOrWhiteSpace(ex.Message)
                ? "Não foi possível abrir o PDF."
                : ex.Message;
            _status.Foreground = Ui.Danger;
        }
    }

    private static string CopyTermsConditionsPdfToTempFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Vanished");
        Directory.CreateDirectory(tempDir);
        var destination = Path.Combine(tempDir, "Vanished-Terms&Conditions.pdf");

        using var input = AssetLoader.Open(new Uri(TermsConditionsPdfAsset));
        using var output = File.Create(destination);
        input.CopyTo(output);

        return destination;
    }

    private void TryApplyWindowIcon()
    {
        try
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Vanished/Resources/Logo/icon2.ico")));
        }
        catch
        {
            // --- IGNORE ---
        }
    }
}
