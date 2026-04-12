using DocumentosElectronicos.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DocumentosElectronicos.Services
{
    /// <summary>
    /// Cliente HTTP para SAP B1 Service Layer.
    /// Maneja sesiones independientes por empresa y recibe el DocEntry
    /// directamente desde HANA (sin consultar el OData de SL).
    /// </summary>
    public class SapServiceLayerService : IDisposable
    {
        private readonly AppSettings _settings;
        private readonly ILogger<SapServiceLayerService> _logger;

        private HttpClient? _httpClient;

        // Sesión por empresa: key = empresaId
        private readonly Dictionary<int, SesionEmpresa> _sesiones = new();
        private readonly SemaphoreSlim _loginLock = new(1, 1);

        private const int SESSION_TIMEOUT_MIN = 25;

        private record SesionEmpresa(
            string SessionId,
            string RouteId,
            DateTime LoginTime);

        public SapServiceLayerService(IOptions<AppSettings> settings, ILogger<SapServiceLayerService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
            InicializarCliente();
        }

        // ─────────────────────────────────────────────
        // INICIALIZACIÓN
        // ─────────────────────────────────────────────

        private void InicializarCliente()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 |
                                                    SecurityProtocolType.Tls11 |
                                                    SecurityProtocolType.Tls;
            ServicePointManager.Expect100Continue = false;

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PostmanRuntime/7.45.0");

            _logger.LogInformation("SAP SL HttpClient inicializado.");
        }

        // ─────────────────────────────────────────────
        // SESIÓN POR EMPRESA
        // ─────────────────────────────────────────────

        private async Task EnsureSessionAsync(int empresaId)
        {
            if (_sesiones.TryGetValue(empresaId, out var sesion) &&
                DateTime.Now.Subtract(sesion.LoginTime).TotalMinutes < SESSION_TIMEOUT_MIN)
                return;

            await LoginAsync(empresaId);
        }

        private async Task LoginAsync(int empresaId)
        {
            await _loginLock.WaitAsync();
            try
            {
                // Doble check tras adquirir el lock
                if (_sesiones.TryGetValue(empresaId, out var sesionActual) &&
                    DateTime.Now.Subtract(sesionActual.LoginTime).TotalMinutes < SESSION_TIMEOUT_MIN)
                    return;

                if (!_settings.Empresas.TryGetValue(empresaId, out var empresa))
                    throw new InvalidOperationException($"EmpresaId {empresaId} no encontrado en configuración.");

                var baseUrl = _settings.SapServiceLayerUrl.TrimEnd('/');

                _logger.LogInformation("SAP SL Login → Empresa: {Nombre} ({Db}) | URL: {Url}/Login",
                    empresa.Nombre, empresa.CompanyDb, baseUrl);

                var json = string.Format(
                    "{{\"CompanyDB\":\"{0}\",\"UserName\":\"{1}\",\"Password\":\"{2}\"}}",
                    empresa.CompanyDb,
                    empresa.Usuario,
                    empresa.Password);

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/Login")
                {
                    Content = content
                };
                request.Headers.Add("B1S-WCFCompatible", "true");
                request.Headers.Add("B1S-MetadataWithoutSession", "true");

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient!.SendAsync(request);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "SAP SL: No se pudo conectar para empresa {Nombre}.", empresa.Nombre);
                    throw;
                }

                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("SAP SL Login falló | Empresa: {Nombre} | HTTP {Code} | {Body}",
                        empresa.Nombre, (int)response.StatusCode, body);
                    throw new Exception($"SAP SL Login falló [{empresa.Nombre}]: HTTP {(int)response.StatusCode} — {body}");
                }

                using var doc = JsonDocument.Parse(body);
                var sessionId = doc.RootElement.GetProperty("SessionId").GetString() ?? string.Empty;
                var routeId = doc.RootElement.TryGetProperty("RouteId", out var rid)
                    ? rid.GetString() ?? string.Empty
                    : string.Empty;

                _sesiones[empresaId] = new SesionEmpresa(sessionId, routeId, DateTime.Now);

                _logger.LogInformation(
                    "SAP SL Login exitoso | Empresa: {Nombre} | SessionId: {Session}... | RouteId: {Route}",
                    empresa.Nombre,
                    sessionId.Length > 8 ? sessionId[..8] : sessionId,
                    string.IsNullOrEmpty(routeId) ? "(ninguno)" : routeId);
            }
            finally
            {
                _loginLock.Release();
            }
        }

        /// <summary>
        /// Construye el header Cookie para la empresa indicada.
        /// </summary>
        private string BuildCookieHeader(int empresaId)
        {
            if (!_sesiones.TryGetValue(empresaId, out var sesion))
                return string.Empty;

            var cookie = $"B1SESSION={sesion.SessionId}";
            if (!string.IsNullOrEmpty(sesion.RouteId))
                cookie += $"; ROUTEID={sesion.RouteId}";

            return cookie;
        }

        // ─────────────────────────────────────────────
        // OPERACIONES
        // ─────────────────────────────────────────────

        // Valores del campo U_ESTDOC en SAP B1
        private static class EstadoDocSap
        {
            public const string Aprobado = "4";
            public const string Rechazado = "6";
            public const string Cancelado = "8";
        }

        /// <summary>
        /// Resuelve el valor de U_ESTDOC según el estado del documento en PostgreSQL.
        /// </summary>
        private static string? ResolverEstadoSap(string estado) => estado.ToUpper() switch
        {
            "APROBADO" => EstadoDocSap.Aprobado,
            "RECHAZADO" => EstadoDocSap.Rechazado,
            "CANCELADO" => EstadoDocSap.Cancelado,
            _ => null
        };

        /// <summary>
        /// Actualiza el campo U_ESTDOC en SAP B1 usando el DocEntry obtenido desde HANA.
        /// </summary>
        public async Task<bool> ActualizarDocumentoCanceladoAsync(
            DocumentoElectronico documento, int docEntry)
        {
            try
            {
                await EnsureSessionAsync(documento.EmpresaId);

                if (!_settings.Empresas.TryGetValue(documento.EmpresaId, out var empresa))
                {
                    _logger.LogWarning("EmpresaId {Id} no encontrado.", documento.EmpresaId);
                    return false;
                }

                var estdoc = ResolverEstadoSap(documento.Estado);
                if (estdoc is null)
                {
                    _logger.LogWarning(
                        "Estado '{Estado}' no tiene valor U_ESTDOC definido. CDC: {Cdc}",
                        documento.Estado, documento.Cdc);
                    return false;
                }

                var baseUrl = _settings.SapServiceLayerUrl.TrimEnd('/');
                var endpoint = ResolverEndpoint(documento.TipoDocumento);
                var cookie = BuildCookieHeader(documento.EmpresaId);

                if (string.IsNullOrEmpty(endpoint))
                {
                    _logger.LogWarning("TipoDocumento {Tipo} no tiene endpoint SAP SL definido.",
                        documento.TipoDocumento);
                    return false;
                }

                // Solo se actualiza U_ESTDOC; el CDC se usa únicamente para obtener el DocEntry
                var json = $"{{\"U_ESTDOC\":\"{estdoc}\"}}";

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                var request = new HttpRequestMessage(
                    new HttpMethod("PATCH"),
                    $"{baseUrl}/{endpoint}({docEntry})")
                {
                    Content = content
                };
                request.Headers.Add("Cookie", cookie);

                _logger.LogInformation(
                    "SAP SL PATCH → [{Empresa}] {Endpoint}({DocEntry}) | CDC: {Cdc}",
                    empresa.Nombre, endpoint, docEntry, documento.Cdc);

                var response = await _httpClient!.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "CDC {Cdc} actualizado en SAP [{Empresa}] | {Endpoint}({DocEntry}).",
                        documento.Cdc, empresa.Nombre, endpoint, docEntry);
                    return true;
                }

                var error = await response.Content.ReadAsStringAsync();
                error = ExtraerMensajeSap(error);

                _logger.LogWarning(
                    "Error al actualizar CDC {Cdc} [{Empresa}] | HTTP {Code} | {Error}",
                    documento.Cdc, empresa.Nombre, (int)response.StatusCode, error);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción al actualizar CDC {Cdc} en SAP.", documento.Cdc);
                return false;
            }
        }

        /// <summary>
        /// Resuelve el endpoint de SAP SL según el tipo de documento.
        /// En caso de tipo 7 (Remisión) se usa ODLN por defecto;
        /// si el DocEntry vino de OWTR se debe ajustar el endpoint externamente.
        /// </summary>
        private static string? ResolverEndpoint(int tipoDocumento) => tipoDocumento switch
        {
            TipoDocumentoSap.FacturaVenta => "Invoices",
            TipoDocumentoSap.NotaDebito => "Invoices",
            TipoDocumentoSap.NotaCredito => "CreditNotes",
            TipoDocumentoSap.Remision => "DeliveryNotes",
            _ => null
        };

        private static string ExtraerMensajeSap(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement
                    .GetProperty("error")
                    .GetProperty("message")
                    .GetProperty("value")
                    .GetString() ?? body;
            }
            catch { return body; }
        }

        // ─────────────────────────────────────────────
        // LOGOUT
        // ─────────────────────────────────────────────

        public async Task LogoutAllAsync()
        {
            var baseUrl = _settings.SapServiceLayerUrl.TrimEnd('/');

            foreach (var (empresaId, sesion) in _sesiones)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/Logout");
                    request.Headers.Add("Cookie", $"B1SESSION={sesion.SessionId}");
                    await _httpClient!.SendAsync(request);

                    _settings.Empresas.TryGetValue(empresaId, out var emp);
                    _logger.LogInformation("SAP SL Logout exitoso | Empresa: {Nombre}", emp?.Nombre ?? empresaId.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error al hacer logout empresa {Id}.", empresaId);
                }
            }

            _sesiones.Clear();
        }

        public void Dispose()
        {
            _loginLock.Dispose();
            _httpClient?.Dispose();
        }
    }
}