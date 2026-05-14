using DocumentosElectronicos.Models;
using DocumentosElectronicos.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentosElectronicos
{
    // Identifica qué proceso debe ejecutarse en cada disparo del scheduler
    

    public class Worker : BackgroundService
    {
        private enum TipoEjecucion
        {
            DocumentosElectronicos,
            MovimientoDiario
        }

        private readonly ILogger<Worker> _logger;
        private readonly AppSettings _settings;
        private readonly PostgresService _postgresService;
        private readonly HanaService _hanaService;
        private readonly SapServiceLayerService _sapService;
        private readonly EmailService _emailService;

        // ── NUEVO ─────────────────────────────────────────────────────────────
        private readonly MovimientoHanaService _movimientoHana;
        private readonly MovimientoEmailService _movimientoEmail;

        // Horarios existentes
        private TimeSpan _horarioMañana;
        private TimeSpan _horarioTarde;

        // Horarios nuevos: Movimiento Diario
        private TimeSpan _horarioMovSemana;   // Lun–Vie
        private TimeSpan _horarioMovSabado;   // Sáb

        public Worker(
            ILogger<Worker> logger,
            IOptions<AppSettings> settings,
            PostgresService postgresService,
            HanaService hanaService,
            SapServiceLayerService sapService,
            EmailService emailService,
            // ── NUEVO ──────────────────────────────────────────────────────
            MovimientoHanaService movimientoHana,
            MovimientoEmailService movimientoEmail)
        {
            _logger = logger;
            _settings = settings.Value;
            _postgresService = postgresService;
            _hanaService = hanaService;
            _sapService = sapService;
            _emailService = emailService;
            _movimientoHana = movimientoHana;
            _movimientoEmail = movimientoEmail;

            _horarioMañana = TimeSpan.Parse(_settings.HorarioMañana);
            _horarioTarde = TimeSpan.Parse(_settings.HorarioTarde);
            _horarioMovSemana = TimeSpan.Parse(_settings.HorarioMovimientoSemana);
            _horarioMovSabado = TimeSpan.Parse(_settings.HorarioMovimientoSabado);
        }

        // ─────────────────────────────────────────────────────────────────────
        // LOOP PRINCIPAL
        // ─────────────────────────────────────────────────────────────────────

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Servicio iniciado. DocElect: {M} y {T}. MovDiario: Lun-Vie {S}, Sáb {Sa}.",
                _horarioMañana, _horarioTarde, _horarioMovSemana, _horarioMovSabado);

            while (!stoppingToken.IsCancellationRequested)
            {
                var (espera, tipo) = CalcularProximaEjecucion(DateTime.Now);

                _logger.LogInformation(
                    "Próxima ejecución [{Tipo}]: {Hora:HH:mm:ss} (en {Espera:hh\\:mm\\:ss}).",
                    tipo, DateTime.Now.Add(espera), espera);

                try { await Task.Delay(espera, stoppingToken); }
                catch (OperationCanceledException) { break; }

                if (stoppingToken.IsCancellationRequested) break;

                if (tipo == TipoEjecucion.DocumentosElectronicos)
                    await EjecutarProcesoAsync(stoppingToken);
                else
                    await EjecutarMovimientoDiarioAsync(stoppingToken);
            }

            _logger.LogInformation("Servicio detenido.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // PROCESO EXISTENTE: Documentos Electrónicos
        // ─────────────────────────────────────────────────────────────────────

        private async Task EjecutarProcesoAsync(CancellationToken ct)
        {
            _logger.LogInformation("═══════ INICIO: Documentos Electrónicos [{Hora}] ═══════",
                DateTime.Now.ToString("HH:mm:ss"));

            try
            {
                // 1. Obtener cancelados desde PostgreSQL
                _logger.LogInformation("Paso 1: Consultando documentos CANCELADOS en PostgreSQL...");
                var cancelados = await _postgresService.ObtenerDocumentosCanceladosAsync();
                _logger.LogInformation("{Count} documentos cancelados obtenidos.", cancelados.Count);

                // 2. Procesar por empresa
                var empresaIds = _settings.Empresas.Keys.OrderBy(id => id).ToList();

                foreach (var empresaId in empresaIds)
                {
                    if (ct.IsCancellationRequested) break;

                    var empresa = _settings.Empresas[empresaId];
                    var docs = cancelados.Where(d => d.EmpresaId == empresaId).ToList();

                    if (docs.Count == 0)
                    {
                        _logger.LogInformation("Empresa {Nombre}: sin documentos cancelados.", empresa.Nombre);
                        continue;
                    }

                    _logger.LogInformation("── {Nombre} ({Count} documentos) ──", empresa.Nombre, docs.Count);
                    await ProcesarEmpresaAsync(docs, empresa.Nombre, ct);
                }

                // 3. Reporte y mail
                _logger.LogInformation("Paso 3: Obteniendo datos para el reporte...");
                var documentosDelDia = await _postgresService.ObtenerDocumentosDelDiaAsync();
                var rechazadosHistorico = await _postgresService.ObtenerDocumentosRechazadosHistoricoAsync();

                _logger.LogInformation("Paso 4: Enviando reporte por mail...");
                await _emailService.EnviarReporteAsync(documentosDelDia, rechazadosHistorico);

                _logger.LogInformation("═══════ PROCESO COMPLETADO EXITOSAMENTE ═══════");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en proceso Documentos Electrónicos.");
            }
            finally
            {
                await _sapService.LogoutAllAsync();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // PROCESO NUEVO: Movimiento Diario
        // ─────────────────────────────────────────────────────────────────────

        private async Task EjecutarMovimientoDiarioAsync(CancellationToken ct)
        {
            _logger.LogInformation("═══════ INICIO: Movimiento Diario [{Hora}] ═══════",
                DateTime.Now.ToString("HH:mm:ss"));

            try
            {
                // 1. Consultar HANA para las 3 empresas (hoy + hace 1 año)
                _logger.LogInformation("MovDiario Paso 1: Consultando HANA...");
                var reporte = await _movimientoHana.ObtenerReporteAsync();

                _logger.LogInformation(
                    "MovDiario: Ventas hoy={VH:N0} | Ant={VA:N0} | Cobros hoy={CH:N0} | Ant={CA:N0}",
                    reporte.TotalVentasHoy, reporte.TotalVentasAnt,
                    reporte.TotalCobrosHoy, reporte.TotalCobrosAnt);

                // 2. Generar PDF y enviar mail
                _logger.LogInformation("MovDiario Paso 2: Enviando mail con PDF...");
                await _movimientoEmail.EnviarAsync(reporte);

                _logger.LogInformation("═══════ MOVIMIENTO DIARIO COMPLETADO ═══════");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en proceso Movimiento Diario.");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // PROCESAMIENTO POR EMPRESA (existente, sin cambios)
        // ─────────────────────────────────────────────────────────────────────

        private async Task ProcesarEmpresaAsync(
            List<DocumentosElectronicos.Models.DocumentoElectronico> documentos,
            string nombreEmpresa,
            CancellationToken ct)
        {
            int actualizados = 0, noEncontrados = 0, fallidos = 0;

            foreach (var doc in documentos)
            {
                if (ct.IsCancellationRequested) break;

                // Mapear a la instancia que espera HanaService (evita errores si hay tipos con el mismo nombre en distintos espacios)
                var hanaDoc = new DocumentosElectronicos.Models.DocumentoElectronico
                {
                    Cdc = doc.Cdc,
                    Establecimiento = doc.Establecimiento,
                    PuntoExpedicion = doc.PuntoExpedicion,
                    Numero = doc.Numero,
                    NroDocumento = doc.NroDocumento,
                    Estado = doc.Estado,
                    FechaEmision = doc.FechaEmision,
                    Secuencia = doc.Secuencia,
                    EmpresaId = doc.EmpresaId,
                    TipoDocumento = doc.TipoDocumento
                };

                var docEntry = await _hanaService.BuscarDocEntryAsync(hanaDoc);
                if (docEntry <= 0) { noEncontrados++; continue; }

                var ok = await _sapService.ActualizarDocumentoCanceladoAsync(doc, docEntry);
                if (ok) actualizados++; else fallidos++;

                await Task.Delay(200, ct);
            }

            _logger.LogInformation(
                "Empresa {Nombre} → Actualizados: {Ok} | No encontrados: {NF} | Fallidos: {Fail}",
                nombreEmpresa, actualizados, noEncontrados, fallidos);
        }

        // ─────────────────────────────────────────────────────────────────────
        // SCHEDULER  — soporta múltiples horarios y tipos
        // ─────────────────────────────────────────────────────────────────────

        private (TimeSpan Wait, TipoEjecucion Tipo) CalcularProximaEjecucion(DateTime ahora)
        {
            // Buscamos en el día de hoy y mañana hasta encontrar un candidato futuro
            for (int offset = 0; offset <= 1; offset++)
            {
                var fecha = ahora.Date.AddDays(offset);
                var dia = fecha.DayOfWeek;

                var candidatos = new List<(DateTime Cuando, TipoEjecucion Tipo)>();

                // Documentos Electrónicos: todos los días, mañana y tarde
                candidatos.Add((fecha + _horarioMañana, TipoEjecucion.DocumentosElectronicos));
                candidatos.Add((fecha + _horarioTarde, TipoEjecucion.DocumentosElectronicos));

                // Movimiento Diario: Lun–Sáb (no Domingo)
                if (dia != DayOfWeek.Sunday)
                {
                    var h = dia == DayOfWeek.Saturday ? _horarioMovSabado : _horarioMovSemana;
                    candidatos.Add((fecha + h, TipoEjecucion.MovimientoDiario));
                }

                // Filtrar solo los futuros (con 1 seg de margen para evitar re-disparos)
                var futuros = candidatos
                    .Where(c => c.Cuando > ahora.AddSeconds(1))
                    .OrderBy(c => c.Cuando)
                    .ToList();

                if (futuros.Any())
                {
                    var prox = futuros.First();
                    return (prox.Cuando - ahora, prox.Tipo);
                }
            }

            // Fallback: esperar 1 hora y recalcular (no debería llegar aquí)
            _logger.LogWarning("CalcularProximaEjecucion: no se encontró horario futuro. Esperando 1 hora.");
            return (TimeSpan.FromHours(1), TipoEjecucion.DocumentosElectronicos);
        }
    }
}