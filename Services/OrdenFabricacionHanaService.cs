using DocumentosElectronicos.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sap.Data.Hana;
using System.Data.Common;

namespace DocumentosElectronicos.Services
{
    /// <summary>
    /// Consulta la vista "V_OF_General" en SAP HANA (existe en el schema
    /// de cada empresa: BOLSI_2020, HANSA_PRD, ENVA_PRD) para el reporte
    /// de Estado de Orden de Fabricación.
    /// </summary>
    public class OrdenFabricacionHanaService
    {
        private readonly AppSettings _settings;
        private readonly ILogger<OrdenFabricacionHanaService> _logger;

        public OrdenFabricacionHanaService(IOptions<AppSettings> settings, ILogger<OrdenFabricacionHanaService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────
        // MÉTODO PRINCIPAL – todas las plantas, del 1ro del mes hasta hoy
        // ─────────────────────────────────────────────────────────────────────

        public async Task<OrdenFabricacionReporte> ObtenerReporteAsync()
        {
            var hasta = DateTime.Today;
            var desde = new DateTime(hasta.Year, hasta.Month, 1);

            var reporte = new OrdenFabricacionReporte { FechaDesde = desde, FechaHasta = hasta };

            foreach (var kv in _settings.Empresas.OrderBy(e => e.Key))
            {
                var empresa = kv.Value;
                _logger.LogInformation("OrdenFabricacionHana: procesando {Empresa}...", empresa.Nombre);

                try
                {
                    var ordenes = await ObtenerOrdenesAsync(empresa, desde, hasta);

                    reporte.Plantas.Add(new OrdenFabricacionPlanta
                    {
                        NombrePlanta = ordenes.Count > 0 ? ordenes[0].Planta : empresa.Nombre,
                        Ordenes = ordenes
                    });

                    _logger.LogInformation("{Empresa} [{Desde:dd/MM} - {Hasta:dd/MM}] → {Count} OF.",
                        empresa.Nombre, desde, hasta, ordenes.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error obteniendo Órdenes de Fabricación de {Empresa}.", empresa.Nombre);
                    reporte.Plantas.Add(new OrdenFabricacionPlanta { NombrePlanta = empresa.Nombre });
                }
            }

            return reporte;
        }

        // ─────────────────────────────────────────────────────────────────────
        // V_OF_General
        // ─────────────────────────────────────────────────────────────────────

        private async Task<List<OrdenFabricacion>> ObtenerOrdenesAsync(EmpresaSapConfig empresa, DateTime desde, DateTime hasta)
        {
            var result = new List<OrdenFabricacion>();

            var sql = $@"
                SELECT
                    ""Planta"", ""DocEntry"", ""DocNum"", ""CodProd"", ""NomProd"",
                    ""CantPlanificada"", ""Fecha"", ""NroOt"", ""Estacion"",
                    ""Estado"", ""Situacion"", ""TieneMovimientos""
                FROM ""{empresa.CompanyDb}"".""V_OF_General""
                WHERE ""Fecha"" >= ? AND ""Fecha"" < ?";

            await using var conn = new HanaConnection(BuildConnString());
            await conn.OpenAsync();

            await using var cmd = new HanaCommand(sql, conn);
            cmd.Parameters.Add(new HanaParameter { HanaDbType = HanaDbType.Date, Value = desde.Date });
            cmd.Parameters.Add(new HanaParameter { HanaDbType = HanaDbType.Date, Value = hasta.Date.AddDays(1) });

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new OrdenFabricacion
                {
                    Planta = SafeString(reader, "Planta"),
                    DocEntry = SafeLong(reader, "DocEntry"),
                    DocNum = SafeLong(reader, "DocNum"),
                    CodProd = SafeString(reader, "CodProd"),
                    NomProd = SafeString(reader, "NomProd"),
                    CantPlanificada = SafeDecimal(reader, "CantPlanificada"),
                    Fecha = reader.GetDateTime(reader.GetOrdinal("Fecha")),
                    NroOt = SafeString(reader, "NroOt"),
                    Estacion = SafeString(reader, "Estacion"),
                    Estado = SafeString(reader, "Estado"),
                    Situacion = SafeString(reader, "Situacion"),
                    TieneMovimientos = SafeString(reader, "TieneMovimientos")
                });
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private string BuildConnString()
            => $"Server={_settings.HanaServer};" +
               $"UserID={_settings.HanaUsuario};" +
               $"Password={_settings.HanaPassword};";

        private static string SafeString(DbDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? "" : r.GetString(r.GetOrdinal(col));
        private static decimal SafeDecimal(DbDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? 0m : Convert.ToDecimal(r.GetValue(r.GetOrdinal(col)));
        private static long SafeLong(DbDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? 0L : Convert.ToInt64(r.GetValue(r.GetOrdinal(col)));
    }
}
