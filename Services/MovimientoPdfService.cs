using DocumentoElectronico.Models;
using DocumentosElectronicos.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace DocumentosElectronicos.Services
{
    /// <summary>
    /// Genera el PDF de Movimiento Diario usando QuestPDF.
    /// NuGet requerido: dotnet add package QuestPDF
    /// Poner en Program.cs: QuestPDF.Settings.License = LicenseType.Community;
    /// </summary>
    public class MovimientoPdfService
    {
        private static readonly CultureInfo CulturePy = new("es-PY");

        // Paleta de colores (mismo estilo que el mail HTML)
        private static readonly string ColorNegro = "#111111";
        private static readonly string ColorRojo = "#C8102E";
        private static readonly string ColorGrisClaro = "#F5F5F5";
        private static readonly string ColorGrisBorde = "#E0E0E0";
        private static readonly string ColorBlancoRoto = "#FAFAFA";

        public byte[] Generar(MovimientoReporte reporte)
        {
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));

                    page.Header().Element(Header);

                    page.Content().Column(col =>
                    {
                        col.Spacing(12);

                        // Fecha del reporte
                        col.Item().PaddingTop(8).Text(t =>
                        {
                            t.Span("Fecha: ").Bold();
                            t.Span(reporte.FechaDesde.ToString("dd/MM/yyyy"));
                            t.Span(" al ").Bold();
                            t.Span(reporte.FechaHasta.ToString("dd/MM/yyyy"));
                            t.Span("     Comparativo con: ").Bold();
                            t.Span(reporte.FechaDesdeAnt.ToString("dd/MM/yyyy"));
                            t.Span(" al ").Bold();
                            t.Span(reporte.FechaHastaAnt.ToString("dd/MM/yyyy"));
                        });

                        // ── Resumen global ────────────────────────────────────────────────
                        col.Item().Element(c => SeccionResumenGlobal(c, reporte));

                        // ── Detalle por empresa ───────────────────────────────────────────
                        foreach (var empresa in reporte.Empresas)
                        {
                            col.Item().Element(c => SeccionEmpresa(c, empresa, reporte));
                        }
                    });

                    page.Footer().Element(Footer);
                });
            });

            return doc.GeneratePdf();
        }

        // ─────────────────────────────────────────────────────────────────────
        // HEADER
        // ─────────────────────────────────────────────────────────────────────

        private void Header(IContainer container)
        {
            container
                .Background(ColorNegro)
                .Padding(12)
                .Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item()
                            .DefaultTextStyle(t => t.FontSize(16).Bold().FontColor("#FFFFFF"))
                            .Text("Movimiento Diario");

                        col.Item()
                            .DefaultTextStyle(t => t.FontSize(8).FontColor("#AAAAAA"))
                            .Text("Reporte consolidado de ventas y cobranzas");
                    });

                    // Logo "impackta" con spans de colores distintos
                    row.ConstantItem(120).AlignRight().AlignMiddle()
                        .Text(t =>
                        {
                            t.DefaultTextStyle(x => x.FontSize(18).Bold());
                            t.Span("im").FontColor("#AAAAAA");
                            t.Span("pack").FontColor(ColorRojo);
                            t.Span("ta").FontColor("#AAAAAA");
                        });
                });
        }

        // ─────────────────────────────────────────────────────────────────────
        // RESUMEN GLOBAL (todas las empresas consolidadas)
        // ─────────────────────────────────────────────────────────────────────

        private void SeccionResumenGlobal(IContainer container, MovimientoReporte reporte)
        {
            container
                .Background(ColorGrisClaro)
                .Border(1).BorderColor(ColorGrisBorde)
                .Padding(10)
                .Column(col =>
                {
                    col.Item().DefaultTextStyle(t => t.Bold().FontSize(10).FontColor(ColorRojo))
                        .Text("CONSOLIDADO — TODAS LAS EMPRESAS");

                    col.Item().PaddingTop(6).Row(row =>
                    {
                        // Ventas
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().DefaultTextStyle(t => t.Bold().FontSize(8)).Text("VENTAS");
                            c.Item().Text(t =>
                            {
                                t.Span("Hoy:  ").Bold();
                                t.Span(Gs(reporte.TotalVentasHoy));
                            });
                            c.Item().Text(t =>
                            {
                                t.Span($"Año ant.:  ").FontColor("#666666");
                                t.Span(Gs(reporte.TotalVentasAnt)).FontColor("#666666");
                            });
                            c.Item().PaddingTop(2).Text(t =>
                            {
                                var v = reporte.VariacionVentas;
                                t.Span(v >= 0 ? $"▲ +{v}%" : $"▼ {v}%")
                                    .Bold()
                                    .FontColor(v >= 0 ? "#1A7A1A" : ColorRojo);
                            });
                        });

                        row.ConstantItem(1).Background(ColorGrisBorde);

                        // Cobranzas
                        row.RelativeItem().PaddingLeft(10).Column(c =>
                        {
                            c.Item().DefaultTextStyle(t => t.Bold().FontSize(8)).Text("COBRANZAS");
                            c.Item().Text(t =>
                            {
                                t.Span("Hoy:  ").Bold();
                                t.Span(Gs(reporte.TotalCobrosHoy));
                            });
                            c.Item().Text(t =>
                            {
                                t.Span("Año ant.:  ").FontColor("#666666");
                                t.Span(Gs(reporte.TotalCobrosAnt)).FontColor("#666666");
                            });
                            c.Item().PaddingTop(2).Text(t =>
                            {
                                var v = reporte.VariacionCobros;
                                t.Span(v >= 0 ? $"▲ +{v}%" : $"▼ {v}%")
                                    .Bold()
                                    .FontColor(v >= 0 ? "#1A7A1A" : ColorRojo);
                            });
                        });
                    });
                });
        }

        // ─────────────────────────────────────────────────────────────────────
        // SECCIÓN POR EMPRESA
        // ─────────────────────────────────────────────────────────────────────

        private void SeccionEmpresa(IContainer container, MovimientoEmpresa empresa, MovimientoReporte reporte)
        {
            container.Column(col =>
            {
                // Encabezado empresa
                col.Item()
                    .BorderLeft(3).BorderColor(ColorRojo)
                    .PaddingLeft(8).PaddingVertical(3)
                    .DefaultTextStyle(t => t.Bold().FontSize(11))
                    .Text(empresa.NombreEmpresa.ToUpper());

                col.Spacing(6);

                // ── VENTAS ────────────────────────────────────────────────────
                col.Item().DefaultTextStyle(t => t.Bold().FontSize(10).FontColor(ColorRojo)).Text("VENTAS");
                col.Item().Element(c => TablaVentas(c, empresa, reporte));

                col.Item().PaddingTop(4);

                // ── COBRANZAS ─────────────────────────────────────────────────
                col.Item().DefaultTextStyle(t => t.Bold().FontSize(10).FontColor(ColorRojo)).Text("COBRANZAS");
                col.Item().Element(c => TablaCobranzas(c, empresa, reporte));
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // TABLA VENTAS
        // ─────────────────────────────────────────────────────────────────────

        private void TablaVentas(IContainer container, MovimientoEmpresa empresa, MovimientoReporte reporte)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3); // Vendedor
                    c.RelativeColumn(2); // Total Ventas
                });

                // Encabezado
                table.Header(h =>
                {
                    h.Cell().Background(ColorNegro).Padding(4)
                        .DefaultTextStyle(t => t.Bold().FontColor("#FFFFFF").FontSize(8))
                        .Text("Vendedor");
                    h.Cell().Background(ColorNegro).Padding(4).AlignRight()
                        .DefaultTextStyle(t => t.Bold().FontColor("#FFFFFF").FontSize(8))
                        .Text("Total Ventas");
                });

                // Filas agrupadas por vendedor (hoy)
                bool par = false;
                foreach (var v in empresa.VentasHoyAgrupadas)
                {
                    var bg = par ? ColorBlancoRoto : "#FFFFFF";
                    table.Cell().Background(bg).Padding(4)
                        .DefaultTextStyle(t => t.FontSize(8)).Text(v.CodVen);
                    table.Cell().Background(bg).Padding(4).AlignRight()
                        .DefaultTextStyle(t => t.FontSize(8)).Text(Gs(v.Total));
                    par = !par;
                }

                // Fila Total año actual
                table.Cell().Background(ColorGrisClaro).Padding(4)
                    .DefaultTextStyle(t => t.Bold().FontSize(8))
                    .Text($"Total general: {reporte.FechaHasta.Year}");
                table.Cell().Background(ColorGrisClaro).Padding(4).AlignRight()
                    .DefaultTextStyle(t => t.Bold().FontSize(8))
                    .Text(Gs(empresa.TotalVentasHoy));

                // Fila Total año anterior
                table.Cell().Background(ColorGrisClaro).Padding(4)
                    .DefaultTextStyle(t => t.FontSize(8).FontColor("#666666"))
                    .Text($"Total general: {reporte.FechaHastaAnt.Year}");
                table.Cell().Background(ColorGrisClaro).Padding(4).AlignRight()
                    .DefaultTextStyle(t => t.FontSize(8).FontColor("#666666"))
                    .Text(Gs(empresa.TotalVentasAnt));
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // TABLA COBRANZAS
        // ─────────────────────────────────────────────────────────────────────

        private void TablaCobranzas(IContainer container, MovimientoEmpresa empresa, MovimientoReporte reporte)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3); // Cobrador
                    c.RelativeColumn(2); // Total Cobranzas
                });

                table.Header(h =>
                {
                    h.Cell().Background(ColorNegro).Padding(4)
                        .DefaultTextStyle(t => t.Bold().FontColor("#FFFFFF").FontSize(8))
                        .Text("Cobrador");
                    h.Cell().Background(ColorNegro).Padding(4).AlignRight()
                        .DefaultTextStyle(t => t.Bold().FontColor("#FFFFFF").FontSize(8))
                        .Text("Total Cobranzas");
                });

                bool par = false;
                foreach (var c in empresa.CobrosHoyAgrupados)
                {
                    var bg = par ? ColorBlancoRoto : "#FFFFFF";
                    table.Cell().Background(bg).Padding(4)
                        .DefaultTextStyle(t => t.FontSize(8)).Text(c.CodCob);
                    table.Cell().Background(bg).Padding(4).AlignRight()
                        .DefaultTextStyle(t => t.FontSize(8)).Text(Gs(c.Total));
                    par = !par;
                }

                table.Cell().Background(ColorGrisClaro).Padding(4)
                    .DefaultTextStyle(t => t.Bold().FontSize(8))
                    .Text($"Total general: {reporte.FechaHasta.Year}");
                table.Cell().Background(ColorGrisClaro).Padding(4).AlignRight()
                    .DefaultTextStyle(t => t.Bold().FontSize(8))
                    .Text(Gs(empresa.TotalCobrosHoy));

                table.Cell().Background(ColorGrisClaro).Padding(4)
                    .DefaultTextStyle(t => t.FontSize(8).FontColor("#666666"))
                    .Text($"Total general: {reporte.FechaHastaAnt.Year}");
                table.Cell().Background(ColorGrisClaro).Padding(4).AlignRight()
                    .DefaultTextStyle(t => t.FontSize(8).FontColor("#666666"))
                    .Text(Gs(empresa.TotalCobrosAnt));
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // FOOTER
        // ─────────────────────────────────────────────────────────────────────

        private void Footer(IContainer container)
        {
            container
                .Background(ColorNegro)
                .Padding(8)
                .Row(row =>
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm} · Sistema de Movimientos")
                            .FontColor("#AAAAAA").FontSize(7);
                    });

                    row.ConstantItem(80).AlignRight().Text(t =>
                    {
                        t.CurrentPageNumber().FontColor("#FFFFFF").FontSize(7);
                        t.Span(" / ").FontColor("#AAAAAA").FontSize(7);
                        t.TotalPages().FontColor("#FFFFFF").FontSize(7);
                    });
                });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helper formato Guaraníes: 167.853.202
        // ─────────────────────────────────────────────────────────────────────

        private static string Gs(decimal valor) => valor.ToString("N0", CulturePy);
    }
}