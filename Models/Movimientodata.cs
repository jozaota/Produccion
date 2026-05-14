using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocumentoElectronico.Models
{
    // ── Resultado de Ventas_RealizadasV2 ─────────────────────────────────────
    public class VentaItem
    {
        public DateTime Fecha { get; set; }
        public string NroFactura { get; set; } = "";
        public string Moneda { get; set; } = "";
        public decimal Monto1 { get; set; }   // siempre 0 en ventas locales
        public decimal Monto2 { get; set; }   // DocTotal / 1.1  (PYG neto IVA)
        public string CodVen { get; set; } = "";
        public string CodClie { get; set; } = "";
        public decimal Monto11 { get; set; }
        public decimal Monto22 { get; set; }   // DocTotalFC / 1.1 (moneda extranjera)
    }

    // ── Resultado de Cobros_RealizadosV2 ─────────────────────────────────────
    public class CobroItem
    {
        public int Codigo { get; set; }
        public string NroRecibo { get; set; } = "";
        public DateTime Fecha { get; set; }
        public string Moneda { get; set; } = "";
        public decimal Monto1 { get; set; }   // cobro "en negro" (fact. U_LIIV=2)
        public decimal Monto2 { get; set; }   // cobro normal
        public string CodCob { get; set; } = "";

        public decimal Total => Monto1 + Monto2;
    }

    // ── Agrupación por Vendedor / Cobrador para el PDF ────────────────────────
    public class VentasPorVendedor
    {
        public string CodVen { get; set; } = "";
        public decimal Total { get; set; }
    }

    public class CobrosPorCobrador
    {
        public string CodCob { get; set; } = "";
        public decimal Total { get; set; }
    }

    // ── Datos de una empresa (hoy + año anterior) ─────────────────────────────
    public class MovimientoEmpresa
    {
        public string NombreEmpresa { get; set; } = "";

        // Detalle hoy
        public List<VentaItem> VentasHoy { get; set; } = new();
        public List<CobroItem> CobrosHoy { get; set; } = new();

        // Detalle año anterior
        public List<VentaItem> VentasAnt { get; set; } = new();
        public List<CobroItem> CobrosAnt { get; set; } = new();

        // ── Totales calculados ────────────────────────────────────────────────
        public decimal TotalVentasHoy => VentasHoy.Sum(v => v.Monto2);
        public decimal TotalVentasAnt => VentasAnt.Sum(v => v.Monto2);
        public decimal TotalCobrosHoy => CobrosHoy.Sum(c => c.Total);
        public decimal TotalCobrosAnt => CobrosAnt.Sum(c => c.Total);

        // ── Agrupaciones para el PDF ──────────────────────────────────────────
        public IEnumerable<VentasPorVendedor> VentasHoyAgrupadas =>
            VentasHoy
                .GroupBy(v => v.CodVen)
                .Select(g => new VentasPorVendedor { CodVen = g.Key, Total = g.Sum(v => v.Monto2) })
                .OrderBy(v => v.CodVen);

        public IEnumerable<VentasPorVendedor> VentasAntAgrupadas =>
            VentasAnt
                .GroupBy(v => v.CodVen)
                .Select(g => new VentasPorVendedor { CodVen = g.Key, Total = g.Sum(v => v.Monto2) })
                .OrderBy(v => v.CodVen);

        public IEnumerable<CobrosPorCobrador> CobrosHoyAgrupados =>
            CobrosHoy
                .GroupBy(c => c.CodCob)
                .Select(g => new CobrosPorCobrador { CodCob = g.Key, Total = g.Sum(c => c.Total) })
                .OrderBy(c => c.CodCob);

        public IEnumerable<CobrosPorCobrador> CobrosAntAgrupados =>
            CobrosAnt
                .GroupBy(c => c.CodCob)
                .Select(g => new CobrosPorCobrador { CodCob = g.Key, Total = g.Sum(c => c.Total) })
                .OrderBy(c => c.CodCob);
    }

    // ── Reporte consolidado de todas las empresas ─────────────────────────────
    public class MovimientoReporte
    {
        public DateTime Fecha { get; set; }
        public DateTime FechaAnterior { get; set; }

        public List<MovimientoEmpresa> Empresas { get; set; } = new();

        // Totales globales
        public decimal TotalVentasHoy => Empresas.Sum(e => e.TotalVentasHoy);
        public decimal TotalVentasAnt => Empresas.Sum(e => e.TotalVentasAnt);
        public decimal TotalCobrosHoy => Empresas.Sum(e => e.TotalCobrosHoy);
        public decimal TotalCobrosAnt => Empresas.Sum(e => e.TotalCobrosAnt);

        // Variaciones porcentuales
        public decimal VariacionVentas =>
            TotalVentasAnt == 0 ? 0 : Math.Round((TotalVentasHoy - TotalVentasAnt) / TotalVentasAnt * 100, 1);

        public decimal VariacionCobros =>
            TotalCobrosAnt == 0 ? 0 : Math.Round((TotalCobrosHoy - TotalCobrosAnt) / TotalCobrosAnt * 100, 1);
    }
}
