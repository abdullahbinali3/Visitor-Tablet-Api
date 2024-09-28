using QRCoder;

namespace VisitorTabletAPITemplate.Utilities
{
    public static class QRCodeGeneratorHelpers
    {
        // Generate QR Code
        private static QRCodeGenerator qrGenerator = new QRCodeGenerator();

        public static string GenerateSvgQRCode(string input, int pixelsPerModule = 20)
        {
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(input, QRCodeGenerator.ECCLevel.Q);
            SvgQRCode qrCode = new SvgQRCode(qrCodeData);
            return qrCode.GetGraphic(pixelsPerModule);
        }

        public static byte[] GeneratePngQRCode(string input, int pixelsPerModule = 20)
        {
            // Generate QR Code
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(input, QRCodeGenerator.ECCLevel.Q);
            PngByteQRCode qrCode = new PngByteQRCode(qrCodeData);
            return qrCode.GetGraphic(pixelsPerModule);
        }
    }
}
