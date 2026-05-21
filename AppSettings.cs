namespace DocumentosElectronicos
{
    public class AppSettings
    {
        // PostgreSQL
        public string PostgresConnectionString { get; set; } = string.Empty;

        // SAP HANA - servidor (host:puerto), mismo para ambas empresas
        public string HanaDriver { get; set; } = string.Empty;
        public string HanaServer { get; set; } = string.Empty;
        public string HanaUsuario { get; set; } = string.Empty;
        public string HanaPassword { get; set; } = string.Empty;

        // SAP Service Layer - URL base, misma para ambas empresas
        public string SapServiceLayerUrl { get; set; } = string.Empty;

        // Empresas SAP (indexadas por empresa_id de PostgreSQL)
        public Dictionary<int, EmpresaSapConfig> Empresas { get; set; } = new();

        // Email - Gmail
        public string GmailUser { get; set; } = string.Empty;
        public string GmailAppPassword { get; set; } = string.Empty;
        public string EmailAsunto { get; set; } = "Reporte Documentos Electrónicos";

        // Destinatarios por empresa
        public List<string> DestinatariosEmpresa1 { get; set; } = new();
        public List<string> DestinatariosEmpresa2 { get; set; } = new();

        // Horarios de ejecución (formato HH:mm)
        public string HorarioMañana { get; set; } = "10:00";
        public string HorarioTarde { get; set; } = "16:00";

        // NUEVAS:
        public List<string> DestinatariosMovimiento { get; set; } = new();
        public string HorarioMovimientoSemana { get; set; } = "11:00";  // Lun–Vie
        public string HorarioMovimientoSabado { get; set; } = "12:00";  // Sáb
    }

    public class EmpresaSapConfig
    {
        public string Nombre { get; set; } = string.Empty;   // Ej: "Bolsi Plast"
        public string CompanyDb { get; set; } = string.Empty;   // Ej: "BOLSI_2020"
        public string Usuario { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}