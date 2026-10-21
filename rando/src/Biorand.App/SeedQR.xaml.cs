// Ported from BioRand (biorand/classic, biorand/SeedQR.xaml.cs), MIT, Copyright (c) 2022-2026 Ted John.
// Biorand++: changes marked "Biorand++".

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace IntelOrca.Biohazard.BioRand
{
    /// <summary>
    /// Interaction logic for SeedQR.xaml
    /// </summary>
    public partial class SeedQR : UserControl
    {
        public static readonly DependencyProperty SeedProperty =
            DependencyProperty.Register(
                nameof(Seed),
                typeof(RandoConfig),
                typeof(SeedQR),
                new PropertyMetadata(Seed_Changed));

        public RandoConfig Seed
        {
            get => (RandoConfig)GetValue(SeedProperty);
            set => SetValue(SeedProperty, value);
        }

        private const double MaxSize = 123;

        private string _code;

        /// <summary>Biorand++: a text to show instead of <see cref="Seed"/> (CVX share codes).</summary>
        public string Code
        {
            get => _code;
            set
            {
                _code = value;
                UpdateImage();
            }
        }

        public SeedQR()
        {
            InitializeComponent();
            UpdateImage();
        }

        private void UpdateImage()
        {
            var config = Seed;
            if (config == null)
            {
                config = new RandoConfig();
            }

            var seed = _code ?? config.ToString();
            var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(seed, QRCodeGenerator.ECCLevel.M);
            var qrCode = new QRCode(qrCodeData);
            var qrCodeImage = qrCode.GetGraphic(3);
            image.Source = ConvertBitmap(qrCodeImage);
            image.Stretch = Stretch.None;
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            // Biorand++: a long code (CVX share codes) is drawn at the size of BioRand's seeds' codes
            if (qrCodeImage.Width > MaxSize)
            {
                image.Stretch = Stretch.Uniform;
                image.Width = MaxSize;
                image.Height = MaxSize;
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            }
            else
            {
                image.Width = double.NaN;
                image.Height = double.NaN;
            }
        }

        private BitmapSource ConvertBitmap(System.Drawing.Bitmap bitmap)
        {
            var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = ms;
            bi.EndInit();
            return bi;
        }

        private static void Seed_Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as SeedQR)._code = null;
            (d as SeedQR).UpdateImage();
        }
    }
}
