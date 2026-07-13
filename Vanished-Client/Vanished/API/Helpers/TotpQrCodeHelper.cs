using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using QRCoder;
using System;
using System.IO;

namespace Vanished.API.Helpers;

public static class TotpQrCodeHelper
{
    public static Image CreateImage(string otpAuthUri, double size = 220)
    {
        if (string.IsNullOrWhiteSpace(otpAuthUri))
            throw new ArgumentException("O URI TOTP não pode ser vazio.", nameof(otpAuthUri));

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(otpAuthUri, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(12);
        var stream = new MemoryStream(bytes);

        return new Image
        {
            Source = new Bitmap(stream),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform
        };
    }
}
