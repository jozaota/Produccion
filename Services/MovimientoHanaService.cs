using DocumentoElectronico.Models;
using DocumentosElectronicos.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sap.Data.Hana;
using System.Data;
using System.Data.Common;

namespace DocumentosElectronicos.Services
{
    /// <summary>
    /// Ejecuta los SP Ventas_RealizadasV2 y Cobros_RealizadosV2 en HANA
    /// para las tres empresas, tanto para hoy como para la misma fecha del año anterior.
    /// </summary>
    public class MovimientoHanaService
    {
        private readonly AppSettings _settings;
        private readonly ILogger<MovimientoHanaService> _logger;

        public MovimientoHanaService(IOptions<AppSettings> settings, ILogger<MovimientoHanaService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────
        // MÉTODO PRINCIPAL – llama todo para todas las empresas
        // ─────────────────────────────────────────────────────────────────────

        public async Task<MovimientoReporte> ObtenerReporteAsync()
        {
            var hasta = DateTime.Today;
            var desde = new DateTime(hasta.Year, hasta.Month, 1); // 1ro del mes actual

            var hastaAnt = hasta.AddYears(-1);
            var desdeAnt = new DateTime(hastaAnt.Year, hastaAnt.Month, 1); // 1ro del mes del año anterior

            var reporte = new MovimientoReporte
            {
                FechaDesde = desde,
                FechaHasta = hasta,
                FechaDesdeAnt = desdeAnt,
                FechaHastaAnt = hastaAnt
            };

            foreach (var kv in _settings.Empresas.OrderBy(e => e.Key))
            {
                var empresa = kv.Value;
                _logger.LogInformation("MovimientoHana: procesando {Empresa}...", empresa.Nombre);

                var movEmpresa = new MovimientoEmpresa { NombreEmpresa = empresa.Nombre };

                try
                {
                    // Rango actual: 1ro del mes → hoy
                    movEmpresa.VentasHoy = await ObtenerVentasAsync(empresa, desde, hasta);
                    movEmpresa.CobrosHoy = await ObtenerCobrosAsync(empresa, desde, hasta);

                    // Rango año anterior: 1ro del mes → mismo día del año pasado
                    movEmpresa.VentasAnt = await ObtenerVentasAsync(empresa, desdeAnt, hastaAnt);
                    movEmpresa.CobrosAnt = await ObtenerCobrosAsync(empresa, desdeAnt, hastaAnt);

                    _logger.LogInformation(
                        "{Empresa} [{Desde:dd/MM} - {Hasta:dd/MM}] → Ventas: {VH:N0} | Ant: {VA:N0} | Cobros: {CH:N0} | Ant: {CA:N0}",
                        empresa.Nombre,
                        desde, hasta,
                        movEmpresa.TotalVentasHoy, movEmpresa.TotalVentasAnt,
                        movEmpresa.TotalCobrosHoy, movEmpresa.TotalCobrosAnt);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error obteniendo movimiento de {Empresa}.", empresa.Nombre);
                }

                reporte.Empresas.Add(movEmpresa);
            }

            return reporte;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Ventas_RealizadasV2
        // ─────────────────────────────────────────────────────────────────────

        private async Task<List<VentaItem>> ObtenerVentasAsync(EmpresaSapConfig empresa, DateTime fechadesde, DateTime fechahasta)
        {
            var result = new List<VentaItem>();

            await using var conn = new HanaConnection(BuildConnString());
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"\"{empresa.CompanyDb}\".\"Ventas_RealizadasV2\"";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add(new HanaParameter("fechadesde", HanaDbType.Date) { Value = fechadesde.Date });
            cmd.Parameters.Add(new HanaParameter("fechahasta", HanaDbType.Date) { Value = fechahasta.Date });

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new VentaItem
                {
                    Fecha = reader.GetDateTime("fecha"),
                    NroFactura = SafeString(reader, "nrofactura"),
                    Moneda = SafeString(reader, "moneda"),
                    Monto1 = SafeDecimal(reader, "monto1"),
                    Monto2 = SafeDecimal(reader, "monto2"),
                    CodVen = SafeString(reader, "codVen"),
                    CodClie = SafeString(reader, "codClie"),
                    Monto11 = SafeDecimal(reader, "monto11"),
                    Monto22 = SafeDecimal(reader, "monto22")
                });
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Cobros_RealizadosV2
        // ─────────────────────────────────────────────────────────────────────

        private async Task<List<CobroItem>> ObtenerCobrosAsync(EmpresaSapConfig empresa, DateTime fechadesde, DateTime fechahasta)
        {
            var result = new List<CobroItem>();

            await using var conn = new HanaConnection(BuildConnString());
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"\"{empresa.CompanyDb}\".\"Cobros_RealizadosV2\"";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add(new HanaParameter("fechadesde", HanaDbType.Date) { Value = fechadesde.Date });
            cmd.Parameters.Add(new HanaParameter("fechahasta", HanaDbType.Date) { Value = fechahasta.Date });

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new CobroItem
                {
                    Codigo = SafeInt(reader, "codigo"),
                    NroRecibo = SafeString(reader, "nrorecibo"),
                    Fecha = reader.GetDateTime("fecha"),
                    Moneda = SafeString(reader, "moneda"),
                    Monto1 = SafeDecimal(reader, "monto1"),
                    Monto2 = SafeDecimal(reader, "monto2"),
                    CodCob = SafeString(reader, "CodCob")
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
        private static int SafeInt(DbDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? 0 : r.GetInt32(r.GetOrdinal(col));
    }
}