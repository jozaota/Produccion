using DocumentosElectronicos.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DocumentosElectronicos.Services
{
    public class PostgresService
    {
        private readonly string _connectionString;
        private readonly ILogger<PostgresService> _logger;

        public PostgresService(IOptions<AppSettings> settings, ILogger<PostgresService> logger)
        {
            _connectionString = settings.Value.PostgresConnectionString;
            _logger = logger;
        }

        // ─────────────────────────────────────────────
        // SELECT BASE reutilizable
        // ─────────────────────────────────────────────

        /// <summary>
        /// Columnas comunes a todas las queries.
        /// nrodocumento se arma con LPAD para el formato 001-001-0000001.
        /// </summary>
        private const string SelectBase = @"
                SELECT
                    cdc,
                    tipodocumento,
                    establecimiento,
                    puntoexpedicion,
                    numero,
                    LPAD(establecimiento::text, 3, '0') || '-' ||
                    LPAD(puntoexpedicion::text, 3, '0') || '-' ||
                    LPAD(numero::text,          7, '0') AS nrodocumento,
                    estado,
                    fechaemision,
                    secuencia,
                    empresa_id";

        private const string FiltroMaxSecuencia = @"
                  AND (cdc, secuencia) IN (
                      SELECT cdc, MAX(secuencia)
                      FROM public.documentos_electronicos
                      GROUP BY cdc
                  )";

        /// <summary>
        /// Filtra registros del mes actual y del mes anterior.
        /// DATE_TRUNC('month', ...) trunca al primer día del mes,
        /// lo que permite comparar rangos sin depender del día exacto.
        /// </summary>
        private const string FiltroMesActualYAnterior = @"
                  AND fechaemision >= DATE_TRUNC('month', CURRENT_DATE - INTERVAL '1 month')
                  AND fechaemision <  DATE_TRUNC('month', CURRENT_DATE + INTERVAL '1 month')";

        // ─────────────────────────────────────────────
        // CONSULTAS
        // ─────────────────────────────────────────────

        /// <summary>
        /// Documentos CANCELADOS con mayor secuencia por CDC.
        /// Se procesan en SAP B1 vía Service Layer.
        /// </summary>
        public async Task<List<DocumentoElectronico>> ObtenerDocumentosCanceladosAsync()
        {
            var sql = $@"
                {SelectBase}
                FROM public.documentos_electronicos de
                WHERE estado = 'CANCELADO'
                {FiltroMesActualYAnterior}
                {FiltroMaxSecuencia}
                ORDER BY empresa_id ASC, fechaemision DESC";

            return await EjecutarConsultaAsync(sql, "CANCELADOS");
        }

        /// <summary>
        /// Documentos del día actual con mayor secuencia por CDC.
        /// Usado para calcular los KPIs del reporte de mail.
        /// </summary>
        public async Task<List<DocumentoElectronico>> ObtenerDocumentosDelDiaAsync()
        {
            var sql = $@"
                {SelectBase}
                FROM public.documentos_electronicos de
                WHERE DATE(fechaemision) = CURRENT_DATE
                  AND (cdc, secuencia) IN (
                      SELECT cdc, MAX(secuencia)
                      FROM public.documentos_electronicos
                      WHERE DATE(fechaemision) = CURRENT_DATE
                      GROUP BY cdc
                  )
                ORDER BY fechaemision DESC";

            return await EjecutarConsultaAsync(sql, "del día");
        }

        /// <summary>
        /// Documentos RECHAZADOS del mes actual y anterior con mayor secuencia por CDC.
        /// Se listan en el cuerpo del mail.
        /// </summary>
        public async Task<List<DocumentoElectronico>> ObtenerDocumentosRechazadosHistoricoAsync()
        {
            var sql = $@"
                {SelectBase}
                FROM public.documentos_electronicos de
                WHERE estado = 'RECHAZADO'
                {FiltroMesActualYAnterior}
                {FiltroMaxSecuencia}
                ORDER BY fechaemision DESC";

            return await EjecutarConsultaAsync(sql, "RECHAZADOS mes actual y anterior");
        }

        // ─────────────────────────────────────────────
        // EJECUCIÓN Y MAPEO
        // ─────────────────────────────────────────────

        private async Task<List<DocumentoElectronico>> EjecutarConsultaAsync(string sql, string descripcion)
        {
            var documentos = new List<DocumentoElectronico>();

            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                    documentos.Add(MapearDocumento(reader));

                _logger.LogInformation("PostgreSQL [{Desc}]: {Count} documentos obtenidos.",
                    descripcion, documentos.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar documentos [{Desc}] en PostgreSQL.", descripcion);
                throw;
            }

            return documentos;
        }

        private static DocumentoElectronico MapearDocumento(NpgsqlDataReader reader)
        {
            var establecimiento = reader["establecimiento"]?.ToString() ?? string.Empty;
            var puntoExpedicion = reader["puntoexpedicion"]?.ToString() ?? string.Empty;
            var numero = reader["numero"]?.ToString() ?? string.Empty;

            return new DocumentoElectronico
            {
                Cdc = reader["cdc"]?.ToString() ?? string.Empty,
                TipoDocumento = Convert.ToInt32(reader["tipodocumento"]),
                Establecimiento = establecimiento,
                PuntoExpedicion = puntoExpedicion,
                Numero = numero,
                NroDocumento = reader["nrodocumento"]?.ToString() ?? string.Empty,
                Estado = reader["estado"]?.ToString() ?? string.Empty,
                FechaEmision = Convert.ToDateTime(reader["fechaemision"]),
                Secuencia = Convert.ToInt64(reader["secuencia"]),
                EmpresaId = Convert.ToInt32(reader["empresa_id"]),
            };
        }
    }
}