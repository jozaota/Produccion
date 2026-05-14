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
            var hoy = DateTime.Today;
            var antAnio = hoy.AddYears(-1);

            var reporte = new MovimientoReporte
            {
                Fecha = hoy,
                FechaAnterior = antAnio
            };

            foreach (var kv in _settings.Empresas.OrderBy(e => e.Key))
            {
                var empresa = kv.Value;
                _logger.LogInformation("MovimientoHana: procesando {Empresa}...", empresa.Nombre);

                var movEmpresa = new MovimientoEmpresa { NombreEmpresa = empresa.Nombre };

                try
                {
                    // Hoy
                    movEmpresa.VentasHoy = await ObtenerVentasAsync(empresa, hoy);
                    movEmpresa.CobrosHoy = await ObtenerCobrosAsync(empresa, hoy);

                    // Misma fecha hace 1 año
                    movEmpresa.VentasAnt = await ObtenerVentasAsync(empresa, antAnio);
                    movEmpresa.CobrosAnt = await ObtenerCobrosAsync(empresa, antAnio);

                    _logger.LogInformation(
                        "{Empresa} → Ventas hoy: {VH:N0} | Ventas ant: {VA:N0} | Cobros hoy: {CH:N0} | Cobros ant: {CA:N0}",
                        empresa.Nombre,
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

        private async Task<List<VentaItem>> ObtenerVentasAsync(EmpresaSapConfig empresa, DateTime fecha)
        {
            var result = new List<VentaItem>();

            await using var conn = new HanaConnection(BuildConnString(empresa));
            await conn.OpenAsync();

            // Los SP en HANA se llaman con el schema como prefijo
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"\"{empresa.CompanyDb}\".\"Ventas_RealizadasV2\"";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add(new HanaParameter("fecha", HanaDbType.Date) { Value = fecha.Date });

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

        private async Task<List<CobroItem>> ObtenerCobrosAsync(EmpresaSapConfig empresa, DateTime fecha)
        {
            var result = new List<CobroItem>();

            await using var conn = new HanaConnection(BuildConnString(empresa));
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"\"{empresa.CompanyDb}\".\"Cobros_RealizadosV2\"";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add(new HanaParameter("fecha", HanaDbType.Date) { Value = fecha.Date });

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

        private string BuildConnString(EmpresaSapConfig e)
            => $"Server={_settings.HanaServer};UserName={e.Usuario};Password={e.Password};CurrentSchema={e.CompanyDb}";

        private static string SafeString(DbDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? "" : r.GetString(r.GetOrdinal(col));
        private static decimal SafeDecimal(DbDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? 0m : Convert.ToDecimal(r.GetValue(r.GetOrdinal(col)));
        private static int SafeInt(DbDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? 0 : r.GetInt32(r.GetOrdinal(col));
    }
}