using ClosedXML.Excel;
using DocumentosElectronicos.Models;

namespace DocumentosElectronicos.Services
{
    /// <summary>
    /// Genera el Excel de detalle de Estado de Orden de Fabricación:
    /// una hoja por planta, con el detalle completo de la vista V_OF_General.
    /// </summary>
    public class OrdenFabricacionExcelService
    {
        private static readonly string[] Encabezados =
        {
            "DocEntry", "DocNum", "Cod. Producto", "Nombre Producto", "Cant. Planificada",
            "Fecha", "Nro. OT", "Estación", "Estado", "Situación", "Tiene Movimientos"
        };

        public byte[] Generar(OrdenFabricacionReporte reporte)
        {
            using var workbook = new XLWorkbook();

            foreach (var planta in reporte.Plantas)
            {
                var hoja = workbook.Worksheets.Add(NombreHojaValido(planta.NombrePlanta, workbook));

                for (int col = 0; col < Encabezados.Length; col++)
                    hoja.Cell(1, col + 1).Value = Encabezados[col];

                var header = hoja.Range(1, 1, 1, Encabezados.Length);
                header.Style.Font.Bold = true;
                header.Style.Font.FontColor = XLColor.White;
                header.Style.Fill.BackgroundColor = XLColor.FromHtml("#111111");

                int fila = 2;
                foreach (var of in planta.Ordenes.OrderBy(o => o.Fecha).ThenBy(o => o.DocNum))
                {
                    hoja.Cell(fila, 1).Value = of.DocEntry;
                    hoja.Cell(fila, 2).Value = of.DocNum;
                    hoja.Cell(fila, 3).Value = of.CodProd;
                    hoja.Cell(fila, 4).Value = of.NomProd;
                    hoja.Cell(fila, 5).Value = of.CantPlanificada;
                    hoja.Cell(fila, 5).Style.NumberFormat.Format = "#,##0.00";
                    hoja.Cell(fila, 6).Value = of.Fecha;
                    hoja.Cell(fila, 6).Style.DateFormat.Format = "dd/MM/yyyy";
                    hoja.Cell(fila, 7).Value = of.NroOt;
                    hoja.Cell(fila, 8).Value = of.Estacion;
                    hoja.Cell(fila, 9).Value = of.Estado;
                    hoja.Cell(fila, 10).Value = of.Situacion;
                    hoja.Cell(fila, 11).Value = of.TieneMovimientos;
                    fila++;
                }

                if (planta.Ordenes.Count == 0)
                    hoja.Cell(2, 1).Value = "Sin órdenes de fabricación en el período.";

                hoja.SheetView.FreezeRows(1);
                hoja.Columns().AdjustToContents();
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static string NombreHojaValido(string nombrePlanta, XLWorkbook workbook)
        {
            // Los nombres de hoja de Excel no admiten : \ / ? * [ ] y tienen máximo 31 caracteres.
            var invalidos = new[] { ':', '\\', '/', '?', '*', '[', ']' };
            var limpio = new string(nombrePlanta.Select(c => invalidos.Contains(c) ? '-' : c).ToArray());

            if (limpio.Length > 31)
                limpio = limpio[..31];

            var nombre = limpio;
            int sufijo = 2;
            while (workbook.Worksheets.Any(w => w.Name.Equals(nombre, StringComparison.OrdinalIgnoreCase)))
                nombre = $"{limpio[..Math.Min(limpio.Length, 28)]} ({sufijo++})";

            return nombre;
        }
    }
}
