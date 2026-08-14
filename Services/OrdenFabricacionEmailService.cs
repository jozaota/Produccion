using DocumentosElectronicos.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Text;

namespace DocumentosElectronicos.Services
{
    /// <summary>
    /// Genera y envía el email de Estado de Orden de Fabricación con:
    ///   - Cuerpo HTML con el resumen KPI de OF por estado, por planta
    ///   - Excel adjunto con el detalle completo (una hoja por planta)
    /// </summary>
    public class OrdenFabricacionEmailService
    {
        private readonly AppSettings _settings;
        private readonly OrdenFabricacionExcelService _excelService;
        private readonly ILogger<OrdenFabricacionEmailService> _logger;

        // Colores corporativos Impackta (mismos que EmailService/MovimientoEmailService)
        private const string ColorNegro = "#1a1a1a";
        private const string ColorRojo = "#D0103A";
        private const string ColorRojoSuave = "#fff0f0";
        private const string ColorRojoBorde = "#f5c4c4";

        // Verde (mismo tono que la variación positiva en MovimientoEmailService)
        private const string ColorVerde = "#1A7A1A";
        private const string ColorVerdeSuave = "#E8F5E9";

        // Ámbar (Planificada: pendiente de iniciar)
        private const string ColorAmbar = "#B8860B";
        private const string ColorAmbarSuave = "#fdf6e3";

        public OrdenFabricacionEmailService(
            IOptions<AppSettings> settings,
            OrdenFabricacionExcelService excelService,
            ILogger<OrdenFabricacionEmailService> logger)
        {
            _settings = settings.Value;
            _excelService = excelService;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ENVIAR
        // ─────────────────────────────────────────────────────────────────────

        public async Task EnviarAsync(OrdenFabricacionReporte reporte)
        {
            if (_settings.DestinatariosOF == null || _settings.DestinatariosOF.Count == 0)
            {
                _logger.LogWarning("OrdenFabricacionEmail: sin destinatarios configurados, se omite el envío.");
                return;
            }

            _logger.LogInformation("OrdenFabricacionEmail: generando Excel...");
            byte[] excelBytes = _excelService.Generar(reporte);

            var mensaje = new MimeMessage();
            mensaje.From.Add(MailboxAddress.Parse(_settings.GmailUser));

            foreach (var dest in _settings.DestinatariosOF)
                mensaje.To.Add(MailboxAddress.Parse(dest));

            mensaje.Subject = $"Estado de Orden de Fabricación — {reporte.FechaDesde:dd/MM/yyyy} al {reporte.FechaHasta:dd/MM/yyyy}";

            var builder = new BodyBuilder { HtmlBody = GenerarHtml(reporte) };

            var nombreExcel = $"EstadoOF_{reporte.FechaDesde:yyyyMMdd}_{reporte.FechaHasta:yyyyMMdd}.xlsx";
            builder.Attachments.Add(nombreExcel, excelBytes,
                new ContentType("application", "vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

            mensaje.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_settings.GmailUser, _settings.GmailAppPassword);
            await smtp.SendAsync(mensaje);
            await smtp.DisconnectAsync(true);

            _logger.LogInformation("OrdenFabricacionEmail: enviado a {Cant} destinatarios.",
                _settings.DestinatariosOF.Count);
        }

        // ─────────────────────────────────────────────────────────────────────
        // TEMPLATE HTML — Identidad Impackta
        // ─────────────────────────────────────────────────────────────────────

        private static string GenerarHtml(OrdenFabricacionReporte reporte)
        {
            var cultura = new System.Globalization.CultureInfo("es-PY");
            var fechaGeneracion = DateTime.Now.ToString("dddd, dd 'de' MMMM 'de' yyyy 'a las' HH:mm", cultura);
            var periodo = $"{reporte.FechaDesde:dd/MM/yyyy} al {reporte.FechaHasta:dd/MM/yyyy}";

            var sb = new StringBuilder();

            sb.Append($@"<!DOCTYPE html>
<html lang='es'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<title>Estado de Orden de Fabricación · Impackta</title>
<style>
  * {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{
    font-family: 'Segoe UI', Arial, sans-serif;
    background: #f0f0f0;
    padding: 24px;
    color: {ColorNegro};
  }}
  .wrap {{
    max-width: 860px;
    margin: 0 auto;
    background: #ffffff;
    border-radius: 10px;
    overflow: hidden;
    border: 1px solid #ddd;
  }}

  /* ── HEADER ── */
  .header {{
    background: {ColorNegro};
    padding: 22px 36px;
    display: flex;
    align-items: center;
    justify-content: space-between;
  }}
  .header-left {{ display: flex; align-items: center; gap: 14px; }}
  .header-title {{ color: #ffffff; font-size: 17px; font-weight: 500; }}
  .header-fecha {{ color: #888; font-size: 12px; margin-top: 3px; }}
  .header-brand {{ text-align: right; }}
  .header-brand .brand-name {{ font-size: 19px; font-weight: 500; letter-spacing: -0.02em; color: #ffffff; }}
  .header-brand .brand-name span {{ color: {ColorRojo}; }}
  .header-brand .brand-sub {{ color: #555; font-size: 11px; margin-top: 2px; }}

  /* ── FRANJA ROJA ── */
  .stripe {{ height: 3px; background: {ColorRojo}; }}

  /* ── BODY ── */
  .body {{ padding: 28px 36px; }}

  /* ── SECCION PLANTA ── */
  .planta {{ margin-bottom: 30px; }}
  .planta:last-child {{ margin-bottom: 0; }}
  .seccion-titulo {{
    display: flex;
    align-items: center;
    gap: 10px;
    margin-bottom: 14px;
  }}
  .seccion-titulo .bar {{
    width: 3px;
    height: 18px;
    background: {ColorRojo};
    border-radius: 2px;
    flex-shrink: 0;
  }}
  .seccion-titulo span {{
    font-size: 14px;
    font-weight: 500;
    color: {ColorNegro};
  }}

  /* ── KPI ── */
  .kpi-grid {{
    display: flex;
    flex-wrap: wrap;
    gap: 14px;
  }}
  .kpi {{
    flex: 1 1 150px;
    border-radius: 8px;
    padding: 16px 18px;
  }}
  .kpi.neutro {{
    background: #f5f5f5;
    border-left: 4px solid {ColorNegro};
  }}
  .kpi.alerta {{
    background: {ColorRojoSuave};
    border-left: 4px solid {ColorRojo};
  }}
  .kpi.exito {{
    background: {ColorVerdeSuave};
    border-left: 4px solid {ColorVerde};
  }}
  .kpi.pendiente {{
    background: {ColorAmbarSuave};
    border-left: 4px solid {ColorAmbar};
  }}
  .kpi-label {{
    font-size: 10px;
    font-weight: 500;
    text-transform: uppercase;
    letter-spacing: 0.08em;
    margin-bottom: 8px;
  }}
  .kpi.neutro .kpi-label {{ color: #888; }}
  .kpi.alerta .kpi-label {{ color: {ColorRojo}; }}
  .kpi.exito .kpi-label {{ color: {ColorVerde}; }}
  .kpi.pendiente .kpi-label {{ color: {ColorAmbar}; }}
  .kpi-numero {{
    font-size: 36px;
    font-weight: 500;
    line-height: 1;
  }}
  .kpi.neutro .kpi-numero {{ color: {ColorNegro}; }}
  .kpi.alerta .kpi-numero {{ color: {ColorRojo}; }}
  .kpi.exito .kpi-numero {{ color: {ColorVerde}; }}
  .kpi.pendiente .kpi-numero {{ color: {ColorAmbar}; }}
  .kpi-sub {{ font-size: 11px; margin-top: 6px; color: #aaa; }}

  .sep {{ height: 0.5px; background: #e5e5e5; margin: 26px 0; }}

  /* ── FOOTER ── */
  .footer-stripe {{ height: 3px; background: {ColorRojo}; }}
  .footer {{
    background: {ColorNegro};
    padding: 14px 36px;
    display: flex;
    align-items: center;
    justify-content: space-between;
  }}
  .footer-copy {{ font-size: 12px; color: #666; }}
  .footer-brand {{ font-size: 13px; font-weight: 500; color: #ffffff; }}
  .footer-brand span {{ color: {ColorRojo}; }}
</style>
</head>
<body>
<div class='wrap'>

  <!-- HEADER -->
  <div class='header'>
    <div class='header-left'>
      <!-- Logo Impackta (SVG) -->
      <svg width='44' height='44' viewBox='0 0 100 100' fill='none' xmlns='http://www.w3.org/2000/svg'>
        <rect x='18' y='18' width='64' height='64' rx='3' transform='rotate(45 50 50)' stroke='white' stroke-width='7' fill='none'/>
        <rect x='26' y='26' width='48' height='48' rx='2' transform='rotate(45 50 50)' stroke='white' stroke-width='5' fill='none'/>
        <rect x='34' y='34' width='32' height='32' rx='1' transform='rotate(45 50 50)' stroke='white' stroke-width='4' fill='none'/>
        <rect x='44' y='28' width='10' height='44' rx='3' fill='{ColorRojo}'/>
        <rect x='44' y='23' width='10' height='10' rx='2' fill='{ColorRojo}' opacity='0.65'/>
      </svg>
      <div>
        <div class='header-title'>Estado de Órdenes de Fabricación</div>
        <div class='header-fecha'>{periodo} · Generado {fechaGeneracion}</div>
      </div>
    </div>
    <div class='header-brand'>
      <div class='brand-name'>im<span>pack</span>ta</div>
      <div class='brand-sub'>Sistema de Producción</div>
    </div>
  </div>

  <div class='stripe'></div>

  <div class='body'>");

            for (int i = 0; i < reporte.Plantas.Count; i++)
            {
                var planta = reporte.Plantas[i];

                sb.Append($@"
    <div class='planta'>
      <div class='seccion-titulo'>
        <div class='bar'></div>
        <span>{planta.NombrePlanta} — {planta.Ordenes.Count} OF en el período</span>
      </div>
      <div class='kpi-grid'>");

                foreach (var (codigo, descripcion, cantidad) in planta.ResumenPorEstado())
                {
                    var clase = codigo switch
                    {
                        EstadoOfSap.Cancelada => "alerta",
                        EstadoOfSap.Cerrada => "exito",
                        EstadoOfSap.Planificada => "pendiente",
                        _ => "neutro"
                    };

                    sb.Append($@"
        <div class='kpi {clase}'>
          <div class='kpi-label'>{descripcion}</div>
          <div class='kpi-numero'>{cantidad}</div>
          <div class='kpi-sub'>órdenes de fabricación</div>
        </div>");
                }

                sb.Append(@"
      </div>
    </div>");

                if (i < reporte.Plantas.Count - 1)
                    sb.Append("\n    <div class='sep'></div>");
            }

            sb.Append($@"
  </div>

  <!-- FOOTER -->
  <div class='footer-stripe'></div>
  <div class='footer'>
    <span class='footer-copy'>Se adjunta el detalle completo en Excel, una hoja por planta · No responder este correo</span>
    <span class='footer-brand'>im<span>pack</span>ta © {DateTime.Now.Year}</span>
  </div>

</div>
</body>
</html>");

            return sb.ToString();
        }
    }
}
