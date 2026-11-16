using ObraSync.Asistente.Api.Controllers;

namespace ObraSync.Asistente.Tests;

/// <summary>La foto del remito se valida por sus bytes, no por lo que declare el cliente (#209).</summary>
public class ValidadorImagenRemitoTests
{
    internal static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46 };
    internal static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 };
    internal static readonly byte[] Webp = { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };

    [Fact]
    public void Reconoce_jpeg_png_y_webp()
    {
        Assert.Equal("image/jpeg", ValidadorImagenRemito.Validar(Jpeg).TipoMedia);
        Assert.Equal("image/png", ValidadorImagenRemito.Validar(Png).TipoMedia);
        Assert.Equal("image/webp", ValidadorImagenRemito.Validar(Webp).TipoMedia);
    }

    [Fact]
    public void Rechaza_lo_vacio_y_lo_nulo()
    {
        Assert.NotNull(ValidadorImagenRemito.Validar(null).Error);
        Assert.NotNull(ValidadorImagenRemito.Validar(Array.Empty<byte>()).Error);
    }

    [Fact]
    public void Rechaza_lo_que_no_es_una_imagen_aunque_se_llame_remito_jpg()
    {
        var texto = System.Text.Encoding.UTF8.GetBytes("<html>no soy una imagen</html>");
        var pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 ...");
        var gif = System.Text.Encoding.ASCII.GetBytes("GIF89a....");

        Assert.NotNull(ValidadorImagenRemito.Validar(texto).Error);
        Assert.NotNull(ValidadorImagenRemito.Validar(pdf).Error);
        Assert.NotNull(ValidadorImagenRemito.Validar(gif).Error);
    }

    [Fact]
    public void Rechaza_un_riff_que_no_es_webp()
    {
        var wav = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E' };

        Assert.NotNull(ValidadorImagenRemito.Validar(wav).Error);
    }

    [Fact]
    public void Acepta_hasta_5_MB_y_rechaza_mas()
    {
        var justo = new byte[ValidadorImagenRemito.MaxBytes];
        Jpeg.CopyTo(justo, 0);
        var pasado = new byte[ValidadorImagenRemito.MaxBytes + 1];
        Jpeg.CopyTo(pasado, 0);

        Assert.Null(ValidadorImagenRemito.Validar(justo).Error);
        Assert.NotNull(ValidadorImagenRemito.Validar(pasado).Error);
    }
}
