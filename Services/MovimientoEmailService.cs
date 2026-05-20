using DocumentoElectronico.Models;
using DocumentosElectronicos.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Globalization;
using System.Text;

namespace DocumentosElectronicos.Services
{
    /// <summary>
    /// Genera y envía el email de Movimiento Diario con:
    ///   - Cuerpo HTML resumido (totales + variación anual por empresa)
    ///   - PDF adjunto con el detalle completo
    /// </summary>
    public class MovimientoEmailService
    {
        private readonly AppSettings _settings;
        private readonly MovimientoPdfService _pdfService;
        private readonly ILogger<MovimientoEmailService> _logger;

        private static readonly CultureInfo CulturePy = new("es-PY");

        public MovimientoEmailService(
            IOptions<AppSettings> settings,
            MovimientoPdfService pdfService,
            ILogger<MovimientoEmailService> logger)
        {
            _settings = settings.Value;
            _pdfService = pdfService;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ENVIAR
        // ─────────────────────────────────────────────────────────────────────

        public async Task EnviarAsync(MovimientoReporte reporte)
        {
            _logger.LogInformation("MovimientoEmail: generando PDF...");
            byte[] pdfBytes = _pdfService.Generar(reporte);

            _logger.LogInformation("MovimientoEmail: construyendo mensaje...");

            var mensaje = new MimeMessage();
            mensaje.From.Add(MailboxAddress.Parse(_settings.GmailUser));

            foreach (var dest in _settings.DestinatariosMovimiento)
                mensaje.To.Add(MailboxAddress.Parse(dest));

            var fechaStr = $"{reporte.FechaDesde:dd/MM/yyyy} al {reporte.FechaHasta:dd/MM/yyyy}";
            mensaje.Subject = $"Movimiento Diario — {fechaStr}";

            // ── Cuerpo HTML ──────────────────────────────────────────────────
            var builder = new BodyBuilder();
            builder.HtmlBody = GenerarHtml(reporte);

            // ── PDF adjunto ──────────────────────────────────────────────────
            var nombrePdf = $"MovimientoDiario_{reporte.FechaDesde:yyyyMMdd}_{reporte.FechaHasta:yyyyMMdd}.pdf";
            builder.Attachments.Add(nombrePdf, pdfBytes, new ContentType("application", "pdf"));

            mensaje.Body = builder.ToMessageBody();

            // ── Envío ────────────────────────────────────────────────────────
            using var smtp = new SmtpClient();
            await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_settings.GmailUser, _settings.GmailAppPassword);
            await smtp.SendAsync(mensaje);
            await smtp.DisconnectAsync(true);

            _logger.LogInformation("MovimientoEmail: enviado a {Cant} destinatarios.",
                _settings.DestinatariosMovimiento.Count);
        }

        // ─────────────────────────────────────────────────────────────────────
        // GENERADOR HTML
        // ─────────────────────────────────────────────────────────────────────

        private string GenerarHtml(MovimientoReporte reporte)
        {
            var sb = new StringBuilder();
            var fechaHoy = $"{reporte.FechaDesde:dd/MM/yyyy} al {reporte.FechaHasta.ToString("dd 'de' MMMM 'de' yyyy", new CultureInfo("es-PY"))}";
            var periodoAnt = $"{reporte.FechaDesdeAnt:dd/MM/yyyy} al {reporte.FechaHastaAnt:dd/MM/yyyy}";
            var añoHoy = reporte.FechaHasta.Year;
            var añoAnt = reporte.FechaHastaAnt.Year;

            sb.Append($@"
<!DOCTYPE html>
<html lang='es'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<style>
  body      {{ margin:0; padding:0; background:#F0F0F0; font-family:Arial,Helvetica,sans-serif; }}
  .wrap     {{ max-width:640px; margin:20px auto; background:#FFFFFF; border-radius:4px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,.15); }}

  /* HEADER */
  .hdr      {{ background:#111111; padding:16px 24px; display:flex; align-items:center; justify-content:space-between; }}
  .hdr-left {{ color:#FFFFFF; }}
  .hdr-left h1 {{ margin:0; font-size:18px; font-weight:700; letter-spacing:-.3px; }}
  .hdr-left p  {{ margin:4px 0 0; font-size:11px; color:#AAAAAA; }}
  .hdr-logo {{ font-size:22px; font-weight:700; }}
  .hdr-logo span.im {{ color:#AAAAAA; }}
  .hdr-logo span.pack {{ color:#C8102E; }}
  .hdr-logo span.ta {{ color:#AAAAAA; }}
  .red-line {{ height:3px; background:#C8102E; }}

  /* CONTENIDO */
  .body     {{ padding:24px; }}

  /* CARDS DE RESUMEN (global) */
  .cards    {{ display:flex; gap:12px; margin-bottom:24px; }}
  .card     {{ flex:1; border:1px solid #E0E0E0; border-radius:4px; padding:14px 16px; }}
  .card.ventas  {{ border-left:4px solid #111111; }}
  .card.cobros  {{ border-left:4px solid #C8102E; }}
  .card-label   {{ font-size:9px; font-weight:700; letter-spacing:1px; color:#888888; text-transform:uppercase; margin-bottom:4px; }}
  .card-hoy     {{ font-size:22px; font-weight:700; color:#111111; line-height:1.1; }}
  .card-ant     {{ font-size:11px; color:#888888; margin-top:4px; }}
  .card-var     {{ display:inline-block; margin-top:6px; font-size:11px; font-weight:700; padding:2px 6px; border-radius:3px; }}
  .card-var.pos {{ background:#E8F5E9; color:#1A7A1A; }}
  .card-var.neg {{ background:#FFEBEE; color:#C8102E; }}

  /* TABLA POR EMPRESA */
  .section-title {{ font-size:10px; font-weight:700; letter-spacing:.5px; color:#C8102E; border-left:3px solid #C8102E; padding-left:8px; margin:20px 0 8px; text-transform:uppercase; }}
  table   {{ width:100%; border-collapse:collapse; font-size:12px; }}
  th      {{ background:#111111; color:#FFFFFF; padding:7px 10px; text-align:left; font-size:10px; }}
  th.r    {{ text-align:right; }}
  td      {{ padding:7px 10px; border-bottom:1px solid #F0F0F0; }}
  td.r    {{ text-align:right; }}
  tr:nth-child(even) td {{ background:#FAFAFA; }}
  tr.total td {{ background:#F5F5F5; font-weight:700; }}
  tr.total-ant td {{ background:#F5F5F5; color:#888888; font-size:11px; }}

  /* NOTA PDF */
  .nota   {{ background:#F5F5F5; border:1px solid #E0E0E0; border-radius:4px; padding:10px 14px; font-size:11px; color:#555555; margin-top:20px; }}

  /* FOOTER */
  .ftr    {{ background:#111111; padding:12px 24px; text-align:center; font-size:10px; color:#888888; }}
  .ftr strong {{ color:#FFFFFF; }}
</style>
</head>
<body>
<div class='wrap'>

  <!-- HEADER -->
  <div class='hdr'>
    <div class='hdr-left'>
      <h1>Movimiento Diario</h1>
      <p>{fechaHoy} &middot; Sistema de Movimientos</p>    </div>
    <div class='hdr-logo'>
      <span class='im'>im</span><span class='pack'>pack</span><span class='ta'>ta</span>
    </div>
  </div>
  <div class='red-line'></div>

  <!-- CUERPO -->
  <div class='body'>

    <!-- CARDS RESUMEN GLOBAL -->
    <div class='cards'>
      {CardResumen("ventas", "Total Ventas", reporte.TotalVentasHoy, reporte.TotalVentasAnt, reporte.VariacionVentas, añoHoy, añoAnt, periodoAnt)}
      {CardResumen("cobros", "Total Cobranzas", reporte.TotalCobrosHoy, reporte.TotalCobrosAnt, reporte.VariacionCobros, añoHoy, añoAnt, periodoAnt)}
    </div>

    <!-- DETALLE POR EMPRESA -->
    {DetalleEmpresas(reporte, añoHoy, añoAnt, periodoAnt)}

    <!-- NOTA -->
    <div class='nota'>
      📎 Se adjunta el <strong>PDF con el detalle completo</strong> por vendedor y cobrador para cada empresa.
    </div>

  </div>

  <!-- FOOTER -->
  <div class='ftr'>
    Este reporte es generado automáticamente &middot; No responder este correo &middot;
    <strong><span style='color:#888'>im</span><span style='color:#C8102E'>pack</span><span style='color:#888'>ta</span></strong>
    &copy; {DateTime.Now.Year}
  </div>

</div>
</body>
</html>");

            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers HTML
        // ─────────────────────────────────────────────────────────────────────

        private string CardResumen(string tipo, string label, decimal hoy, decimal ant, decimal var, int añoHoy, int añoAnt, string periodoAnt)
        {
            var varClass = var >= 0 ? "pos" : "neg";
            var varSig = var >= 0 ? $"▲ +{var}%" : $"▼ {var}%";

            return $@"
<div class='card {tipo}'>
  <div class='card-label'>{label}</div>
  <div class='card-hoy'>Gs. {Gs(hoy)}</div>
  <div class='card-ant'>{periodoAnt}: Gs. {Gs(ant)}</div>
  <span class='card-var {varClass}'>{varSig} vs {añoAnt}</span>
</div>";
        }

        private string DetalleEmpresas(MovimientoReporte reporte, int añoHoy, int añoAnt, string periodoAnt)
        {
            var sb = new StringBuilder();

            foreach (var empresa in reporte.Empresas)
            {
                sb.Append($"<div class='section-title'>{empresa.NombreEmpresa}</div>");

                // Subtabla ventas
                sb.Append(@"
<p style='font-size:10px;font-weight:700;margin:4px 0 4px;'>VENTAS</p>
<table>
  <tr><th>Vendedor</th><th class='r'>Total Ventas</th></tr>");

                foreach (var v in empresa.VentasHoyAgrupadas)
                    sb.Append($"<tr><td>{v.CodVen}</td><td class='r'>Gs. {Gs(v.Total)}</td></tr>");

                sb.Append($@"
  <tr class='total'><td>Total {añoHoy}</td><td class='r'>Gs. {Gs(empresa.TotalVentasHoy)}</td></tr>
  <tr class='total-ant'><td>{periodoAnt}</td><td class='r'>Gs. {Gs(empresa.TotalVentasAnt)}</td></tr>
</table>");

                // Subtabla cobranzas
                sb.Append(@"
<p style='font-size:10px;font-weight:700;margin:12px 0 4px;'>COBRANZAS</p>
<table>
  <tr><th>Cobrador</th><th class='r'>Total Cobranzas</th></tr>");

                foreach (var c in empresa.CobrosHoyAgrupados)
                    sb.Append($"<tr><td>{c.CodCob}</td><td class='r'>Gs. {Gs(c.Total)}</td></tr>");

                sb.Append($@"
  <tr class='total'><td>Total {añoHoy}</td><td class='r'>Gs. {Gs(empresa.TotalCobrosHoy)}</td></tr>
  <tr class='total-ant'><td>{periodoAnt}</td><td class='r'>Gs. {Gs(empresa.TotalCobrosAnt)}</td></tr>
</table>");
            }

            return sb.ToString();
        }

        private static string Gs(decimal valor) => valor.ToString("N0", CulturePy);
    }
}