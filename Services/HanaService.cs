using DocumentosElectronicos.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sap.Data.Hana;

namespace DocumentosElectronicos.Services
{
    public class HanaService
    {
        private readonly AppSettings _settings;
        private readonly ILogger<HanaService> _logger;

        public HanaService(IOptions<AppSettings> settings, ILogger<HanaService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        // ─────────────────────────────────────────────
        // CONEXIÓN
        // ─────────────────────────────────────────────

        /// <summary>
        /// Cadena de conexión sin CurrentSchema.
        /// Sap.Data.Hana.Net.v6.0 no reconoce CS ni "Current Schema" como parámetro válido.
        /// El esquema se setea por SQL luego de abrir la conexión.
        /// </summary>
        private string BuildConnectionString()
        {
            return $"Server={_settings.HanaServer};" +
                   $"UserID={_settings.HanaUsuario};" +
                   $"Password={_settings.HanaPassword};";
        }

        /// <summary>
        /// Setea el esquema activo en la conexión ya abierta.
        /// Equivale a hacer "USE database" en otros motores.
        /// </summary>
        private static async Task SetSchemaAsync(HanaConnection conn, string schema)
        {
            // Las comillas dobles son necesarias si el schema tiene caracteres especiales
            await using var cmd = new HanaCommand($"SET SCHEMA \"{schema}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<HanaConnection> AbrirConexionAsync(string schema)
        {
            var conn = new HanaConnection(BuildConnectionString());
            await conn.OpenAsync();
            await SetSchemaAsync(conn, schema);
            return conn;
        }

        // ─────────────────────────────────────────────
        // BÚSQUEDA DE DocEntry
        // ─────────────────────────────────────────────

        /// <summary>
        /// Busca el DocEntry en SAP HANA según el CDC y el tipo de documento.
        ///   TipoDoc 1 → OINV primero, si nulo → ODPI
        ///   TipoDoc 5 → ORIN
        ///   TipoDoc 6 → OINV
        ///   TipoDoc 7 → ODLN primero, si nulo → OWTR
        /// </summary>
        public async Task<int> BuscarDocEntryAsync(DocumentosElectronicos.Models.DocumentoElectronico documento)
        {
            if (!_settings.Empresas.TryGetValue(documento.EmpresaId, out var empresa))
            {
                _logger.LogWarning("EmpresaId {Id} no encontrado en configuración. CDC: {Cdc}",
                    documento.EmpresaId, documento.Cdc);
                return -1;
            }

            _logger.LogInformation(
                "Buscando DocEntry | CDC: {Cdc} | TipoDoc: {Tipo} | Empresa: {Empresa}",
                documento.Cdc, documento.TipoDocumento, empresa.Nombre);

            return documento.TipoDocumento switch
            {
                TipoDocumentoSap.FacturaVenta =>
                    await BuscarConFallbackAsync(documento.Cdc, "OINV", "ODPI", empresa),

                TipoDocumentoSap.NotaDebito =>
                    await BuscarEnTablaAsync(documento.Cdc, "OINV", empresa),

                TipoDocumentoSap.NotaCredito =>
                    await BuscarEnTablaAsync(documento.Cdc, "ORIN", empresa),

                TipoDocumentoSap.Remision =>
                    await BuscarConFallbackAsync(documento.Cdc, "ODLN", "OWTR", empresa),

                _ => LogTipoNoReconocido(documento)
            };
        }

        private async Task<int> BuscarConFallbackAsync(
            string cdc,
            string tablaPrincipal, string tablaFallback,
            EmpresaSapConfig empresa)
        {
            var docEntry = await BuscarEnTablaAsync(cdc, tablaPrincipal, empresa);

            if (docEntry > 0)
                return docEntry;

            _logger.LogInformation(
                "CDC {Cdc} no encontrado en {Tabla1}, buscando en {Tabla2} [{Empresa}]...",
                cdc, tablaPrincipal, tablaFallback, empresa.Nombre);

            return await BuscarEnTablaAsync(cdc, tablaFallback, empresa);
        }

        private async Task<int> BuscarEnTablaAsync(
            string cdc, string tabla,
            EmpresaSapConfig empresa)
        {
            // Calificar la tabla con el schema: "BOLSI_2020"."OINV"
            // Es más confiable que SET SCHEMA ya que aplica por sentencia.
            var sql = $"SELECT TOP 1 \"DocEntry\" FROM \"{empresa.CompanyDb}\".\"{tabla}\" WHERE \"U_CDC\" = ?";

            try
            {
                await using var conn = new HanaConnection(BuildConnectionString());
                await conn.OpenAsync();

                await using var cmd = new HanaCommand(sql, conn);

                cmd.Parameters.Add(new HanaParameter
                {
                    HanaDbType = HanaDbType.NVarChar,
                    Value = cdc
                });

                var result = await cmd.ExecuteScalarAsync();

                if (result is null or DBNull)
                {
                    _logger.LogInformation(
                        "CDC {Cdc} no encontrado en {Tabla} [{Empresa}].",
                        cdc, tabla, empresa.Nombre);
                    return -1;
                }

                var docEntry = Convert.ToInt32(result);
                _logger.LogInformation(
                    "CDC {Cdc} → {Tabla} [{Empresa}] → DocEntry: {DocEntry}",
                    cdc, tabla, empresa.Nombre, docEntry);

                return docEntry;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error al buscar CDC {Cdc} en {Tabla} [{Empresa}].",
                    cdc, tabla, empresa.Nombre);
                return -1;
            }
        }

        private int LogTipoNoReconocido(DocumentosElectronicos.Models.DocumentoElectronico documento)
        {
            _logger.LogWarning(
                "TipoDocumento {Tipo} no reconocido para CDC {Cdc}.",
                documento.TipoDocumento, documento.Cdc);
            return -1;
        }

        // ─────────────────────────────────────────────
        // CONSULTA GENÉRICA
        // ─────────────────────────────────────────────

        public async Task<List<Dictionary<string, object?>>> EjecutarConsultaAsync(
            int empresaId, string sql,
            Dictionary<string, object>? parametros = null)
        {
            var resultados = new List<Dictionary<string, object?>>();

            if (!_settings.Empresas.TryGetValue(empresaId, out var empresa))
            {
                _logger.LogWarning("EmpresaId {Id} no encontrado en configuración.", empresaId);
                return resultados;
            }

            try
            {
                await using var conn = new HanaConnection(BuildConnectionString());
                await conn.OpenAsync();
                await using var cmd = new HanaCommand(sql, conn);

                if (parametros != null)
                    foreach (var p in parametros)
                        cmd.Parameters.AddWithValue(p.Key, p.Value);

                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var fila = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        fila[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    resultados.Add(fila);
                }

                _logger.LogInformation(
                    "Consulta HANA [{Empresa}]: {Count} filas.", empresa.Nombre, resultados.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en consulta HANA [{Empresa}].", empresa.Nombre);
                throw;
            }

            return resultados;
        }
    }
}