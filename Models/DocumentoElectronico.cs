namespace DocumentosElectronicos.Models
{
    public class DocumentoElectronico
    {
        public string Cdc { get; set; } = string.Empty;
        public string Establecimiento { get; set; } = string.Empty;
        public string PuntoExpedicion { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string NroDocumento { get; set; } = string.Empty;   // 001-001-0000001
        public string Estado { get; set; } = string.Empty;
        public DateTime FechaEmision { get; set; }
        public long Secuencia { get; set; }
        public int EmpresaId { get; set; }
        public int TipoDocumento { get; set; }
    }

    /// <summary>
    /// Tipos de documento según la tabla documentos_electronicos.
    /// Determina en qué tabla SAP HANA se busca el DocEntry.
    /// </summary>
    public static class TipoDocumentoSap
    {
        public const int FacturaVenta = 1;   // OINV (primero) → ODPI (fallback)
        public const int NotaDebito = 6;   // OINV
        public const int NotaCredito = 5;   // ORIN
        public const int Remision = 7;   // ODLN (primero) → OWTR (fallback)
    }

    public enum EstadoDocumento
    {
        APROBADO,
        CANCELADO,
        RECHAZADO
    }
}
