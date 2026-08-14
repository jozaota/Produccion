namespace DocumentosElectronicos.Models
{
    /// <summary>
    /// Fila de la vista "V_OF_General" (existe con el mismo nombre en el schema
    /// de cada empresa: BOLSI_2020, HANSA_PRD, ENVA_PRD, con los datos propios
    /// de esa planta — no confundir con "V_OF_General_Impackta", que ya es
    /// el UNION de las tres).
    /// </summary>
    public class OrdenFabricacion
    {
        public string Planta { get; set; } = string.Empty;
        public long DocEntry { get; set; }
        public long DocNum { get; set; }
        public string CodProd { get; set; } = string.Empty;
        public string NomProd { get; set; } = string.Empty;
        public decimal CantPlanificada { get; set; }
        public DateTime Fecha { get; set; }
        public string NroOt { get; set; } = string.Empty;
        public string Estacion { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public string Situacion { get; set; } = string.Empty;
        public string TieneMovimientos { get; set; } = string.Empty;
    }

    /// <summary>
    /// Códigos de estado de Orden de Fabricación en SAP Business One (OWOR.Status).
    /// </summary>
    public static class EstadoOfSap
    {
        public const string Planificada = "P";
        public const string Liberada = "R";
        public const string Cerrada = "L";
        public const string Cancelada = "C";

        public static readonly (string Codigo, string Descripcion)[] Orden =
        {
            (Planificada, "Planificada"),
            (Liberada, "Liberada"),
            (Cerrada, "Cerrada"),
            (Cancelada, "Cancelada")
        };

        public static string Descripcion(string codigo) => codigo switch
        {
            Planificada => "Planificada",
            Liberada => "Liberada",
            Cerrada => "Cerrada",
            Cancelada => "Cancelada",
            _ => codigo
        };
    }

    /// <summary>
    /// Órdenes de fabricación de una planta, con el resumen por estado para el KPI del mail.
    /// </summary>
    public class OrdenFabricacionPlanta
    {
        public string NombrePlanta { get; set; } = string.Empty;
        public List<OrdenFabricacion> Ordenes { get; set; } = new();

        public IEnumerable<(string Codigo, string Descripcion, int Cantidad)> ResumenPorEstado()
        {
            var conteos = Ordenes
                .GroupBy(o => o.Estado)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var (codigo, descripcion) in EstadoOfSap.Orden)
            {
                conteos.TryGetValue(codigo, out var cantidad);
                yield return (codigo, descripcion, cantidad);
            }

            var otros = conteos
                .Where(kv => EstadoOfSap.Orden.All(e => e.Codigo != kv.Key))
                .Sum(kv => kv.Value);

            if (otros > 0)
                yield return ("?", "Otros", otros);
        }
    }

    /// <summary>
    /// Reporte consolidado de Estado de Órdenes de Fabricación de todas las plantas.
    /// </summary>
    public class OrdenFabricacionReporte
    {
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
        public List<OrdenFabricacionPlanta> Plantas { get; set; } = new();
    }
}
