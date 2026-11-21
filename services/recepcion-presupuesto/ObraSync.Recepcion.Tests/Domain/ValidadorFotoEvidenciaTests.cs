using System.Text;
using ObraSync.Recepcion.Domain.Constants;
using ObraSync.Recepcion.Domain.Recepciones;
using Xunit;

namespace ObraSync.Recepcion.Tests.Domain;

public class ValidadorFotoEvidenciaTests
{
    public static byte[] CrearJpegValido(int tamano = 64)
    {
        var bytes = new byte[Math.Max(tamano, 4)];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        bytes[3] = 0xE0;
        return bytes;
    }

    public static byte[] CrearPngValido(int tamano = 64)
    {
        var bytes = new byte[Math.Max(tamano, 8)];
        byte[] firma = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Array.Copy(firma, bytes, firma.Length);
        return bytes;
    }

    public static byte[] CrearWebpValido(int tamano = 64)
    {
        var bytes = new byte[Math.Max(tamano, 12)];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BitConverter.GetBytes(tamano - 8).CopyTo(bytes, 4);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8);
        return bytes;
    }

    [Fact]
    public void Validar_JpegValido_DetectaImageJpeg()
    {
        var (contentType, nombre) = ValidadorFotoEvidencia.Validar(CrearJpegValido(), "entrega.jpg");

        Assert.Equal("image/jpeg", contentType);
        Assert.Equal("entrega.jpg", nombre);
    }

    [Fact]
    public void Validar_PngValido_DetectaImagePng()
    {
        var (contentType, nombre) = ValidadorFotoEvidencia.Validar(CrearPngValido(), "danio.png");

        Assert.Equal("image/png", contentType);
        Assert.Equal("danio.png", nombre);
    }

    [Fact]
    public void Validar_WebpValido_DetectaImageWebp()
    {
        var (contentType, nombre) = ValidadorFotoEvidencia.Validar(CrearWebpValido(), "evidencia.webp");

        Assert.Equal("image/webp", contentType);
        Assert.Equal("evidencia.webp", nombre);
    }

    [Fact]
    public void Validar_TamanoExacto2Mb_EsAceptado()
    {
        var bytes = CrearJpegValido(ValidadorFotoEvidencia.MaxTamanoFotoBytes);

        var (contentType, _) = ValidadorFotoEvidencia.Validar(bytes, "limite.jpg");

        Assert.Equal("image/jpeg", contentType);
    }

    [Fact]
    public void Validar_Supera2Mb_LanzaArgumentException()
    {
        var bytes = CrearJpegValido(ValidadorFotoEvidencia.MaxTamanoFotoBytes + 1);

        var ex = Assert.Throws<ArgumentException>(() =>
            ValidadorFotoEvidencia.Validar(bytes, "grande.jpg"));

        Assert.Contains("2 MB", ex.Message);
    }

    [Fact]
    public void Validar_ArchivoVacio_LanzaArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ValidadorFotoEvidencia.Validar(Array.Empty<byte>(), "vacio.jpg"));
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>", "falso.jpg")]
    [InlineData("%PDF-1.7 documento", "remito.png")]
    [InlineData("GIF89a......", "animacion.webp")]
    [InlineData("texto plano cualquiera", "foto.jpeg")]
    public void Validar_ContenidoInvalidoConExtensionImagen_RechazaPorMagicBytes(string contenidoTexto, string nombreArchivo)
    {
        var bytes = Encoding.UTF8.GetBytes(contenidoTexto);

        var ex = Assert.Throws<ArgumentException>(() =>
            ValidadorFotoEvidencia.Validar(bytes, nombreArchivo));

        Assert.Contains("JPEG, PNG o WebP", ex.Message);
    }

    [Fact]
    public void Validar_RiffQueNoEsWebp_RechazaPorMagicBytes()
    {
        var bytes = Encoding.ASCII.GetBytes("RIFF1234WAVEfmt ");

        Assert.Throws<ArgumentException>(() =>
            ValidadorFotoEvidencia.Validar(bytes, "audio.webp"));
    }

    [Fact]
    public void SanearNombreArchivo_EliminaRutaYCaracteresDeControl()
    {
        var (_, nombre) = ValidadorFotoEvidencia.Validar(CrearPngValido(), "../../secret\\foto\r\n1.png");

        Assert.Equal("foto1.png", nombre);
    }

    [Theory]
    [InlineData("No llegó nada", "No llegó nada")]
    [InlineData("no llegó nada", "No llegó nada")]
    [InlineData("  Llegó incompleta  ", "Llegó incompleta")]
    [InlineData("Llegó dañada", "Llegó dañada")]
    [InlineData("No es lo pedido", "No es lo pedido")]
    public void MotivosProblema_Permitidos_NormalizanCorrectamente(string entrada, string esperado)
    {
        Assert.Equal(esperado, MotivosProblemaRecepcion.Normalizar(entrada));
        Assert.True(MotivosProblemaRecepcion.EsValido(entrada));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Otro motivo")]
    [InlineData("No llego nada")]
    public void MotivosProblema_NoPermitidos_DevuelvenNull(string? entrada)
    {
        Assert.Null(MotivosProblemaRecepcion.Normalizar(entrada));
        Assert.False(MotivosProblemaRecepcion.EsValido(entrada));
    }
}
