using DocumentosElectronicos.Models;
using DocumentosElectronicos.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentosElectronicos
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly AppSettings _settings;
        private readonly PostgresService _postgresService;
        private readonly HanaService _hanaService;
        private readonly SapServiceLayerService _sapService;
        private readonly EmailService _emailService;

        private TimeSpan _horarioMañana;
        private TimeSpan _horarioTarde;

        public Worker(
            ILogger<Worker> logger,
            IOptions<AppSettings> settings,
            PostgresService postgresService,
            HanaService hanaService,
            SapServiceLayerService sapService,
            EmailService emailService)
        {
            _logger = logger;
            _settings = settings.Value;
            _postgresService = postgresService;
            _hanaService = hanaService;
            _sapService = sapService;
            _emailService = emailService;

            _horarioMañana = TimeSpan.Parse(_settings.HorarioMañana);
            _horarioTarde = TimeSpan.Parse(_settings.HorarioTarde);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Servicio iniciado. Ejecutará a las {Mañana} y {Tarde}.",
                _horarioMañana, _horarioTarde);

            while (!stoppingToken.IsCancellationRequested)
            {
                var espera = CalcularEspera(DateTime.Now.TimeOfDay);

                _logger.LogInformation(
                    "Próxima ejecución: {Hora:HH:mm:ss} (en {Espera:mm\\:ss} minutos).",
                    DateTime.Now.Add(espera), espera);

                try { await Task.Delay(espera, stoppingToken); }
                catch (OperationCanceledException) { break; }

                if (!stoppingToken.IsCancellationRequested)
                    await EjecutarProcesoAsync(stoppingToken);
            }

            _logger.LogInformation("Servicio detenido.");
        }

        // ─────────────────────────────────────────────
        // PROCESO PRINCIPAL
        // ─────────────────────────────────────────────

        private async Task EjecutarProcesoAsync(CancellationToken ct)
        {
            _logger.LogInformation("═══════ INICIO DEL PROCESO [{Hora}] ═══════",
                DateTime.Now.ToString("HH:mm:ss"));

            try
            {
                // 1. Obtener todos los documentos CANCELADOS desde PostgreSQL
                _logger.LogInformation("Paso 1: Consultando documentos CANCELADOS en PostgreSQL...");
                var cancelados = await _postgresService.ObtenerDocumentosCanceladosAsync();
                _logger.LogInformation("{Count} documentos cancelados obtenidos.", cancelados.Count);

                // 2. Procesar por empresa en orden: primero 1, luego 2
                var empresaIds = _settings.Empresas.Keys.OrderBy(id => id).ToList();

                foreach (var empresaId in empresaIds)
                {
                    if (ct.IsCancellationRequested) break;

                    var empresa = _settings.Empresas[empresaId];
                    var docs = cancelados.Where(d => d.EmpresaId == empresaId).ToList();

                    if (docs.Count == 0)
                    {
                        _logger.LogInformation(
                            "Empresa {Nombre}: sin documentos cancelados para procesar.", empresa.Nombre);
                        continue;
                    }

                    _logger.LogInformation(
                        "── Procesando empresa {Nombre} ({Count} documentos) ──",
                        empresa.Nombre, docs.Count);

                    await ProcesarEmpresaAsync(docs, empresa.Nombre, ct);
                }

                // 3. Datos para el reporte de mail
                _logger.LogInformation("Paso 3: Obteniendo datos para el reporte...");
                var documentosDelDia = await _postgresService.ObtenerDocumentosDelDiaAsync();
                var rechazadosHistorico = await _postgresService.ObtenerDocumentosRechazadosHistoricoAsync();

                // 4. Enviar mail
                _logger.LogInformation("Paso 4: Enviando reporte por mail...");
                await _emailService.EnviarReporteAsync(documentosDelDia, rechazadosHistorico);

                _logger.LogInformation("═══════ PROCESO COMPLETADO EXITOSAMENTE ═══════");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante la ejecución del proceso.");
            }
            finally
            {
                await _sapService.LogoutAllAsync();
            }
        }

        // ─────────────────────────────────────────────
        // PROCESAMIENTO POR EMPRESA
        // ─────────────────────────────────────────────

        private async Task ProcesarEmpresaAsync(
            List<DocumentoElectronico> documentos,
            string nombreEmpresa,
            CancellationToken ct)
        {
            int actualizados = 0;
            int noEncontrados = 0;
            int fallidos = 0;

            foreach (var doc in documentos)
            {
                if (ct.IsCancellationRequested) break;

                // Buscar DocEntry en HANA según tipo de documento
                var docEntry = await _hanaService.BuscarDocEntryAsync(doc);

                if (docEntry <= 0)
                {
                    noEncontrados++;
                    continue;
                }

                // Actualizar en SAP B1 vía Service Layer
                var ok = await _sapService.ActualizarDocumentoCanceladoAsync(doc, docEntry);

                if (ok) actualizados++;
                else fallidos++;

                // Pausa para no saturar SAP
                await Task.Delay(200, ct);
            }

            _logger.LogInformation(
                "Empresa {Nombre} → Actualizados: {Ok} | No encontrados en HANA: {NF} | Fallidos SL: {Fail}",
                nombreEmpresa, actualizados, noEncontrados, fallidos);
        }

        // ─────────────────────────────────────────────
        // SCHEDULER
        // ─────────────────────────────────────────────

        private TimeSpan CalcularEspera(TimeSpan ahora)
        {
            var horarios = new[] { _horarioMañana, _horarioTarde }.OrderBy(h => h).ToArray();

            foreach (var h in horarios)
                if (ahora < h) return h - ahora;

            // Todos los horarios del día ya pasaron → esperar al primero de mañana
            return TimeSpan.FromHours(24) - ahora + horarios[0];
        }
    }
}