using DocumentosElectronicos.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Text;

namespace DocumentosElectronicos.Services
{
    public class EmailService
    {
        private readonly AppSettings _settings;
        private readonly ILogger<EmailService> _logger;

        // Colores corporativos Impackta
        private const string ColorNegro = "#1a1a1a";
        private const string ColorRojo = "#D0103A";
        private const string ColorRojoSuave = "#fff0f0";
        private const string ColorRojoBorde = "#f5c4c4";

        public EmailService(IOptions<AppSettings> settings, ILogger<EmailService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        // ─────────────────────────────────────────────
        // ENVÍO
        // ─────────────────────────────────────────────

        public async Task EnviarReporteAsync(
            List<DocumentoElectronico> documentosDelDia,
            List<DocumentoElectronico> rechazadosMesActualYAnterior)
        {
            // Envío separado por empresa con sus propios destinatarios
            var empresaIds = _settings.Empresas.Keys.OrderBy(id => id).ToList();

            foreach (var empresaId in empresaIds)
            {
                var empresa = _settings.Empresas[empresaId];

                var destinatarios = empresaId == 1
                    ? _settings.DestinatariosEmpresa1
                    : _settings.DestinatariosEmpresa2;

                if (destinatarios == null || destinatarios.Count == 0)
                {
                    _logger.LogWarning("Empresa {Nombre}: sin destinatarios configurados, se omite.", empresa.Nombre);
                    continue;
                }

                var docsDia = documentosDelDia.Where(d => d.EmpresaId == empresaId).ToList();
                var rechazados = rechazadosMesActualYAnterior.Where(d => d.EmpresaId == empresaId).ToList();

                var aprobadosHoy = docsDia.Count(d => d.Estado == "APROBADO");
                var rechazadosHoy = docsDia.Count(d => d.Estado == "RECHAZADO");

                var html = GenerarHtml(aprobadosHoy, rechazadosHoy, rechazados, empresa.Nombre);

                await EnviarMailAsync(destinatarios, _settings.EmailAsunto, html, empresa.Nombre);
            }
        }

        private async Task EnviarMailAsync(List<string> destinatarios, string asunto, string htmlBody, string nombreEmpresa)
        {
            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress("Impackta · Documentos Electrónicos", _settings.GmailUser));

            foreach (var dest in destinatarios)
                mensaje.To.Add(MailboxAddress.Parse(dest));

            mensaje.Subject = $"{asunto} — {nombreEmpresa} — {DateTime.Now:dd/MM/yyyy HH:mm}";

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            mensaje.Body = bodyBuilder.ToMessageBody();

            using var smtp = new SmtpClient();
            try
            {
                await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                await smtp.AuthenticateAsync(_settings.GmailUser, _settings.GmailAppPassword);
                await smtp.SendAsync(mensaje);
                await smtp.DisconnectAsync(true);

                _logger.LogInformation("Mail [{Empresa}] enviado a {Count} destinatarios.", nombreEmpresa, destinatarios.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al enviar mail [{Empresa}].", nombreEmpresa);
                throw;
            }
        }

        // ─────────────────────────────────────────────
        // TEMPLATE HTML — Identidad Impackta
        // ─────────────────────────────────────────────

        private static string GenerarHtml(
            int aprobadosHoy,
            int rechazadosHoy,
            List<DocumentoElectronico> rechazados,
            string nombreEmpresa)
        {
            var cultura = new System.Globalization.CultureInfo("es-PY");
            var fechaGeneracion = DateTime.Now.ToString("dddd, dd 'de' MMMM 'de' yyyy 'a las' HH:mm", cultura);

            var sb = new StringBuilder();

            sb.Append($@"<!DOCTYPE html>
<html lang='es'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<title>Reporte Documentos Electrónicos · Impackta</title>
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

  /* ── KPI ── */
  .kpi-grid {{
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 14px;
    margin-bottom: 28px;
  }}
  .kpi {{
    border-radius: 8px;
    padding: 18px 22px;
  }}
  .kpi.aprobado {{
    background: #f5f5f5;
    border-left: 4px solid {ColorNegro};
  }}
  .kpi.rechazado {{
    background: {ColorRojoSuave};
    border-left: 4px solid {ColorRojo};
  }}
  .kpi-label {{
    font-size: 10px;
    font-weight: 500;
    text-transform: uppercase;
    letter-spacing: 0.08em;
    margin-bottom: 8px;
  }}
  .kpi.aprobado .kpi-label {{ color: #888; }}
  .kpi.rechazado .kpi-label {{ color: {ColorRojo}; }}
  .kpi-numero {{
    font-size: 42px;
    font-weight: 500;
    line-height: 1;
  }}
  .kpi.aprobado .kpi-numero {{ color: {ColorNegro}; }}
  .kpi.rechazado .kpi-numero {{ color: {ColorRojo}; }}
  .kpi-sub {{ font-size: 12px; margin-top: 6px; color: #aaa; }}

  /* ── SEPARADOR ── */
  .sep {{ height: 0.5px; background: #e5e5e5; margin-bottom: 22px; }}

  /* ── SECCION TITULO ── */
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

  /* ── TABLA ── */
  .table-wrap {{ border-radius: 8px; overflow: hidden; border: 0.5px solid #e5e5e5; }}
  table {{ width: 100%; border-collapse: collapse; font-size: 13px; }}
  thead tr {{ background: {ColorNegro}; }}
  thead th {{
    padding: 10px 14px;
    text-align: left;
    font-weight: 500;
    font-size: 10px;
    text-transform: uppercase;
    letter-spacing: 0.07em;
    color: #999;
  }}
  thead th:not(:first-child) {{ color: #ffffff; }}
  tbody tr:nth-child(even) {{ background: #fafafa; }}
  tbody td {{ padding: 10px 14px; border-bottom: 0.5px solid #f0f0f0; color: #444; }}
  tbody tr:last-child td {{ border-bottom: none; }}
  .td-num {{ font-weight: 500; color: {ColorNegro}; }}
  .td-cdc {{ font-family: monospace; font-size: 11px; color: #888; }}
  .badge {{
    background: {ColorRojoSuave};
    color: {ColorRojo};
    padding: 3px 10px;
    border-radius: 20px;
    font-size: 11px;
    font-weight: 500;
    border: 0.5px solid {ColorRojoBorde};
    white-space: nowrap;
  }}
  .empty {{ text-align: center; color: #aaa; padding: 28px; font-size: 13px; }}
  .table-footer {{ margin-top: 10px; font-size: 12px; color: #bbb; text-align: right; }}

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
        <div class='header-title'>Reporte de Documentos Electrónicos</div>
        <div class='header-fecha'>{fechaGeneracion} · {nombreEmpresa}</div>
      </div>
    </div>
    <div class='header-brand'>
      <div class='brand-name'>im<span>pack</span>ta</div>
      <div class='brand-sub'>Sistema de Documentos</div>
    </div>
  </div>

  <div class='stripe'></div>

  <div class='body'>

    <!-- KPI -->
    <div class='kpi-grid'>
      <div class='kpi aprobado'>
        <div class='kpi-label'>Aprobados hoy</div>
        <div class='kpi-numero'>{aprobadosHoy}</div>
        <div class='kpi-sub'>documentos procesados</div>
      </div>
      <div class='kpi rechazado'>
        <div class='kpi-label'>Rechazados hoy</div>
        <div class='kpi-numero'>{rechazadosHoy}</div>
        <div class='kpi-sub'>requieren revisión</div>
      </div>
    </div>

    <div class='sep'></div>

    <!-- SECCION TITULO -->
    <div class='seccion-titulo'>
      <div class='bar'></div>
      <span>Documentos rechazados — mes actual y anterior</span>
    </div>

    <!-- TABLA -->
    <div class='table-wrap'>");

            if (rechazados.Count == 0)
            {
                sb.Append("<div class='empty'>No hay documentos rechazados en el período.</div>");
            }
            else
            {
                sb.Append(@"
      <table>
        <thead>
          <tr>
            <th>#</th>
            <th>Nro. Documento</th>
            <th>CDC</th>
            <th>Estado</th>
            <th>Fecha emisión</th>
            <th>Empresa</th>
          </tr>
        </thead>
        <tbody>");

                int i = 1;
                foreach (var doc in rechazados)
                {
                    // Truncar CDC largo para no romper el layout
                    var cdcCorto = doc.Cdc.Length > 20
                        ? doc.Cdc[..20] + "..."
                        : doc.Cdc;

                    // Nombre de empresa desde el ID
                    sb.Append($@"
          <tr>
            <td style='color:#bbb;'>{i++}</td>
            <td class='td-num'>{doc.NroDocumento}</td>
            <td class='td-cdc' title='{doc.Cdc}'>{cdcCorto}</td>
            <td><span class='badge'>RECHAZADO</span></td>
            <td>{doc.FechaEmision:dd/MM/yyyy HH:mm}</td>
            <td>Empresa {doc.EmpresaId}</td>
          </tr>");
                }

                sb.Append(@"
        </tbody>
      </table>");
            }

            sb.Append($@"
    </div>
    <div class='table-footer'>{rechazados.Count} documento(s) rechazado(s) en el período</div>

  </div>

  <!-- FOOTER -->
  <div class='footer-stripe'></div>
  <div class='footer'>
    <span class='footer-copy'>Este reporte es generado automáticamente · No responder este correo</span>
    <span class='footer-brand'>im<span>pack</span>ta © {DateTime.Now.Year}</span>
  </div>

</div>
</body>
</html>");

            return sb.ToString();
        }
    }
}