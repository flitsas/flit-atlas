using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Paquetes;
using Flit.Atlas.Modulos.Contratos.Poda;
using Flit.Atlas.Modulos.Contratos.Portal;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static Flit.Atlas.Modulos.Contratos.EndpointsContratos;

namespace Flit.Atlas.Modulos.Contratos.Sandbox;

/// <summary>Un contrato importado en Microcks, como se guarda en <c>contratos_sandbox.contratos</c>.</summary>
/// <param name="Segmento">El tramo de la URL pública del contrato (su código; con sufijo si dos contratos del paquete lo comparten).</param>
/// <param name="Servicio">Nombre del servicio en Microcks: <c>atlas-&lt;ruta opaca&gt;-&lt;segmento&gt;</c>.</param>
/// <param name="Version">Versión del servicio en Microcks: <c>r&lt;revisión&gt;-&lt;huella&gt;</c> (cambia con lo importado).</param>
internal sealed record ContratoEnSandbox(Guid ContratoId, Guid VersionId, string Segmento, string Servicio, string Version, int Operaciones);

/// <summary>Un contrato que el sandbox puede importar: OpenAPI, compartible, disponible y con operaciones compartidas.</summary>
internal sealed record EntradaSandbox(Contrato Contrato, VersionContratoSinContenido Version, IReadOnlyList<string> Operaciones);

/// <summary>Resultado de activar: el sandbox y la llave en claro (la única vez que existe), o un código de error.</summary>
/// <param name="Codigo"><c>sin_contratos_mock</c> (409) o <c>microcks_fallo</c> (502).</param>
internal sealed record ResultadoActivacion(SandboxPaquete? Sandbox, string? Llave, bool Creado, string? Codigo, string? Mensaje);

/// <summary>
/// Sandbox nivel 1 (HU 4.7, plan 00 §8.8, ATLAS-ADR-0045): importa a Microcks el spec <b>podado</b> de cada contrato
/// OpenAPI del paquete (solo las operaciones compartidas, así que las demás no existen en el mock y responden 404), con un
/// nombre de servicio opaco por (organización, paquete), y lo mantiene: reimporta si cambia la revisión que ve la
/// organización o lo compartido, lo vuelve a importar si Microcks lo perdió (la imagen «uber» guarda en memoria) y lo apaga
/// y lo borra al revocar o vencer la compartición, al quitarle el sandbox o al archivar el paquete. La llave (256 bits) solo
/// se guarda como hash.
/// </summary>
internal sealed partial class ServicioSandbox(
    ContratosDbContext contexto, PlataformaDbContext plataforma, SpecsDelPortal specs, IClienteMicrocks microcks, TimeProvider reloj,
    ILogger<ServicioSandbox> log)
{
    public const string RecursoAuditoria = "contratos.sandbox";
    public const string AccionActivado = "contratos.sandbox_activado";
    public const string AccionLlave = "contratos.sandbox_llave_regenerada";
    public const string AccionReimportado = "contratos.sandbox_reimportado";
    public const string AccionApagado = "contratos.sandbox_apagado";

    /// <summary>Prefijo de la llave: la reconocen los escáneres de secretos y la persona sabe qué es.</summary>
    public const string PrefijoLlave = "atlsbx_";

    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    public bool Disponible => microcks.Disponible;

    public static string NuevaRuta() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public static string NuevaLlave() => PrefijoLlave + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Hash(string llave) => SHA256.HashData(Encoding.UTF8.GetBytes(llave));

    public static IReadOnlyList<ContratoEnSandbox> Leer(string json) => JsonSerializer.Deserialize<List<ContratoEnSandbox>>(json, Opciones) ?? [];

    public static IReadOnlyList<string> LeerPorBorrar(string json) => JsonSerializer.Deserialize<List<string>>(json, Opciones) ?? [];

    /// <summary>Lo que importa una persona del portal: los contratos OpenAPI que ve con nivel técnico y con operaciones.</summary>
    public static IReadOnlyList<EntradaSandbox> PlanDe(PaqueteDelPortal paquete)
    {
        ArgumentNullException.ThrowIfNull(paquete);
        return
        [
            .. paquete.Contratos
                .Where(c => c.Tecnico && c.Contrato.Tipo == TipoContrato.Openapi && c.Entrada.Operaciones.Count > 0)
                .Select(c => new EntradaSandbox(c.Contrato, c.Version, c.Entrada.Operaciones))
                .OrderBy(e => e.Contrato.Id),
        ];
    }

    /// <summary>SHA-256 de la revisión y de cada contrato, versión y operación (ordenados): si cambia, hay que reimportar.</summary>
    public static string Huella(int revision, IReadOnlyList<EntradaSandbox> plan)
    {
        var texto = new StringBuilder().Append(revision.ToString(CultureInfo.InvariantCulture));
        foreach (var e in plan.OrderBy(e => e.Contrato.Id))
        {
            texto.Append('\n').Append(e.Contrato.Id.ToString("N")).Append(':').Append(e.Version.Id.ToString("N")).Append(':')
                .AppendJoin(',', e.Operaciones.Order(StringComparer.Ordinal));
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto.ToString())));
    }

    /// <summary>
    /// Activa el sandbox de un paquete del portal (quien llama ya comprobó <c>PuedeUsarSandboxAsync</c>): si no existe o
    /// estaba apagado, lo importa con una ruta nueva; si está activo, reimporta solo si cambió lo compartido y regenera la
    /// llave (la anterior deja de servir en ese instante).
    /// </summary>
    public async Task<ResultadoActivacion> ActivarAsync(PaqueteDelPortal paquete, Guid persona, IPAddress? ip, string? correlacion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paquete);
        var plan = PlanDe(paquete);
        if (plan.Count == 0)
        {
            return new(null, null, false, "sin_contratos_mock",
                "Este paquete no comparte operaciones de contratos OpenAPI: el sandbox nivel 1 solo simula APIs REST.");
        }
        var comparticion = paquete.Comparticion;
        var ahora = reloj.GetUtcNow();
        var huella = Huella(paquete.Revision, plan);
        var sandbox = await contexto.Sandboxes
            .FirstOrDefaultAsync(s => s.PaqueteId == paquete.Paquete.Id && s.OrganizacionId == comparticion.OrganizacionId, ct).ConfigureAwait(false);
        var activo = sandbox is { Estado: EstadoSandbox.Activo };
        var reimportar = !activo || sandbox!.Huella != huella || sandbox.ComparticionId != comparticion.Id;
        var ruta = activo ? sandbox!.Ruta : NuevaRuta();

        IReadOnlyList<ContratoEnSandbox> importados = [];
        if (reimportar)
        {
            try
            {
                importados = await ImportarAsync(ruta, paquete.Paquete.Id, paquete.Revision, huella, plan, ct).ConfigureAwait(false);
            }
            catch (ErrorMicrocks ex)
            {
                return new(null, null, false, "microcks_fallo", ex.Message);
            }
        }

        var llave = NuevaLlave();
        var creado = sandbox is null;
        if (sandbox is null)
        {
            sandbox = new SandboxPaquete
            {
                OrganizacionId = comparticion.OrganizacionId, PaqueteId = paquete.Paquete.Id, ComparticionId = comparticion.Id, Ruta = ruta,
                Revision = paquete.Revision, Contratos = JsonSerializer.Serialize(importados, Opciones), Huella = huella, LlaveHash = Hash(llave),
                LlaveCreadaEn = ahora, ActivadoPor = persona, ActivadoEn = ahora, ExpiraEn = comparticion.VigenteHasta, ImportadoEn = ahora,
            };
            contexto.Sandboxes.Add(sandbox);
        }
        else
        {
            if (reimportar)
            {
                // Lo que estaba importado y ya no se usa queda por borrar (el trabajo periódico lo reintenta si Microcks falla).
                var nuevos = importados.Select(Clave).ToHashSet(StringComparer.Ordinal);
                sandbox.PorBorrar = JsonSerializer.Serialize(
                    LeerPorBorrar(sandbox.PorBorrar).Concat(Leer(sandbox.Contratos).Select(Clave)).Where(c => !nuevos.Contains(c)).Distinct().ToList(), Opciones);
                sandbox.Contratos = JsonSerializer.Serialize(importados, Opciones);
                sandbox.Huella = huella;
                sandbox.Revision = paquete.Revision;
                sandbox.ImportadoEn = ahora;
                sandbox.Ruta = ruta;
            }
            if (!activo)
            {
                sandbox.Estado = EstadoSandbox.Activo;
                sandbox.ApagadoEn = null;
                sandbox.MotivoApagado = null;
            }
            sandbox.LlaveHash = Hash(llave);
            sandbox.LlaveCreadaEn = ahora;
            sandbox.ActivadoPor = persona;
            sandbox.ActivadoEn = ahora;
            sandbox.ExpiraEn = comparticion.VigenteHasta;
        }
        // La compartición que lo permite hoy (si se revocó y se volvió a compartir, la fila es la misma).
        sandbox.ComparticionId = comparticion.Id;

        // Auditado sin la llave ni su hash (§8.9): quién, qué paquete, qué revisión y qué contratos.
        await Guias.EndpointsGuias.GuardarConAuditoriaAsync(contexto, plataforma, new EventoAuditoria
        {
            OcurridoEn = ahora,
            ActorTipo = TipoActor.Persona,
            ActorId = persona.ToString(),
            OrganizacionId = comparticion.OrganizacionId,
            Accion = !activo ? AccionActivado : reimportar ? AccionReimportado : AccionLlave,
            RecursoTipo = RecursoAuditoria,
            RecursoId = sandbox.Id.ToString(),
            Version = paquete.Revision.ToString(CultureInfo.InvariantCulture),
            Ip = ip,
            Datos = JsonSerializer.Serialize(new
            {
                paquete = paquete.Paquete.Id, comparticion = comparticion.Id, reimportado = reimportar, llaveRegenerada = true,
                contratos = Leer(sandbox.Contratos).Select(c => new { c.ContratoId, c.VersionId, c.Operaciones }),
            }, JsonSerializerOptions.Web),
            CorrelacionId = correlacion,
        }, ct).ConfigureAwait(false);
        await BorrarPendientesAsync(sandbox, ct).ConfigureAwait(false);
        return new(sandbox, llave, creado || !activo, null, null);
    }

    /// <summary>
    /// Revisa un sandbox contra su compartición (la usan el trabajo periódico y los cambios de una compartición): lo apaga si
    /// ya no se puede usar, lo reimporta si cambió la revisión o lo compartido, y vuelve a importar lo que Microcks perdió.
    /// </summary>
    public async Task RevisarAsync(SandboxPaquete sandbox, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        if (sandbox.Estado != EstadoSandbox.Activo)
        {
            return;
        }
        var ahora = reloj.GetUtcNow();
        var comparticion = await contexto.Comparticiones.AsNoTracking().FirstOrDefaultAsync(c => c.Id == sandbox.ComparticionId, ct).ConfigureAwait(false);
        var paquete = await contexto.Paquetes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == sandbox.PaqueteId, ct).ConfigureAwait(false);
        var motivo = MotivoParaApagar(comparticion, paquete, ahora);
        if (motivo is not null)
        {
            await ApagarAsync(sandbox, motivo, ct).ConfigureAwait(false);
            return;
        }
        sandbox.ExpiraEn = comparticion!.VigenteHasta;
        var revision = comparticion.RevisionFijada ?? paquete!.RevisionPublicada!.Value;
        // Nunca agrega contratos: solo los que importó quien lo activó (y que aún se comparten).
        var permitidos = Leer(sandbox.Contratos).Select(c => c.ContratoId).ToHashSet();
        var plan = await PlanDeRevisionAsync(sandbox.PaqueteId, revision, permitidos, ct).ConfigureAwait(false);
        if (plan.Count == 0)
        {
            await ApagarAsync(sandbox, "sin_contratos", ct).ConfigureAwait(false);
            return;
        }
        var huella = Huella(revision, plan);
        try
        {
            if (huella != sandbox.Huella)
            {
                var anteriores = Leer(sandbox.Contratos);
                var importados = await ImportarAsync(sandbox.Ruta, sandbox.PaqueteId, revision, huella, plan, ct).ConfigureAwait(false);
                var nuevos = importados.Select(Clave).ToHashSet(StringComparer.Ordinal);
                sandbox.PorBorrar = JsonSerializer.Serialize(
                    LeerPorBorrar(sandbox.PorBorrar).Concat(anteriores.Select(Clave)).Where(c => !nuevos.Contains(c)).Distinct().ToList(), Opciones);
                sandbox.Contratos = JsonSerializer.Serialize(importados, Opciones);
                sandbox.Huella = huella;
                sandbox.Revision = revision;
                sandbox.ImportadoEn = ahora;
                await GuardarConEventoAsync(sandbox, AccionReimportado, new { revision, contratos = importados.Count }, ct).ConfigureAwait(false);
            }
            else
            {
                // Microcks (la imagen «uber») guarda en memoria: si se reinició, se vuelve a importar lo mismo.
                foreach (var importado in Leer(sandbox.Contratos))
                {
                    if (!await microcks.ExisteAsync(importado.Servicio, importado.Version, ct).ConfigureAwait(false)
                        && plan.FirstOrDefault(p => p.Contrato.Id == importado.ContratoId) is { } entrada)
                    {
                        await ImportarUnoAsync(importado, sandbox.PaqueteId, revision, entrada, ct).ConfigureAwait(false);
                        Restaurado(log, sandbox.Id);
                    }
                }
                await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        catch (ErrorMicrocks ex)
        {
            // Se reintenta en la siguiente vuelta; el proxy sigue revisando la compartición en cada llamada.
            FalloMicrocks(log, sandbox.Id, ex.Message);
            await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        await BorrarPendientesAsync(sandbox, ct).ConfigureAwait(false);
    }

    /// <summary>Revisa los sandboxes de una compartición (al cambiarla o revocarla): se apagan en ese momento, sin esperar al trabajo.</summary>
    public async Task RevisarComparticionAsync(Guid comparticionId, CancellationToken ct)
    {
        var sandboxes = await contexto.Sandboxes.Where(s => s.ComparticionId == comparticionId && s.Estado == EstadoSandbox.Activo).ToListAsync(ct).ConfigureAwait(false);
        foreach (var sandbox in sandboxes)
        {
            await RevisarAsync(sandbox, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Una vuelta del trabajo periódico: revisa los activos y reintenta los borrados pendientes. Devuelve cuántos revisó.</summary>
    public async Task<int> MantenerAsync(CancellationToken ct)
    {
        if (!microcks.Disponible)
        {
            return 0;
        }
        var activos = await contexto.Sandboxes.Where(s => s.Estado == EstadoSandbox.Activo).OrderBy(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var sandbox in activos)
        {
            await RevisarAsync(sandbox, ct).ConfigureAwait(false);
        }
        var pendientes = await contexto.Sandboxes.Where(s => s.Estado == EstadoSandbox.Apagado).ToListAsync(ct).ConfigureAwait(false);
        foreach (var sandbox in pendientes.Where(s => LeerPorBorrar(s.PorBorrar).Count > 0))
        {
            await BorrarPendientesAsync(sandbox, ct).ConfigureAwait(false);
        }
        return activos.Count;
    }

    /// <summary>Por qué un sandbox ya no se puede usar; <c>null</c> si sigue valiendo. La usa también el proxy en cada llamada.</summary>
    public static string? MotivoParaApagar(ComparticionPaquete? comparticion, PaqueteContratos? paquete, DateTimeOffset ahora) =>
        comparticion is null || comparticion.RevocadoEn is not null ? "revocada"
        : comparticion.VigenteHasta is { } hasta && hasta <= ahora ? "vencida"
        : !comparticion.PuedeSandbox || comparticion.VigenteDesde > ahora ? "sin_sandbox"
        : paquete is not { Estado: EstadoPaquete.Publicado, RevisionPublicada: not null } ? "paquete_no_disponible"
        : null;

    private async Task ApagarAsync(SandboxPaquete sandbox, string motivo, CancellationToken ct)
    {
        sandbox.Estado = EstadoSandbox.Apagado;
        sandbox.ApagadoEn = reloj.GetUtcNow();
        sandbox.MotivoApagado = motivo;
        sandbox.PorBorrar = JsonSerializer.Serialize(LeerPorBorrar(sandbox.PorBorrar).Concat(Leer(sandbox.Contratos).Select(Clave)).Distinct().ToList(), Opciones);
        await GuardarConEventoAsync(sandbox, AccionApagado, new { motivo }, ct).ConfigureAwait(false);
        await BorrarPendientesAsync(sandbox, ct).ConfigureAwait(false);
    }

    /// <summary>Borra de Microcks lo pendiente; lo que falle queda para la siguiente vuelta.</summary>
    private async Task BorrarPendientesAsync(SandboxPaquete sandbox, CancellationToken ct)
    {
        var pendientes = LeerPorBorrar(sandbox.PorBorrar);
        if (pendientes.Count == 0 || !microcks.Disponible)
        {
            return;
        }
        var quedan = new List<string>();
        foreach (var clave in pendientes)
        {
            var separador = clave.LastIndexOf(':');
            try
            {
                await microcks.BorrarAsync(clave[..separador], clave[(separador + 1)..], ct).ConfigureAwait(false);
            }
            catch (ErrorMicrocks ex)
            {
                FalloMicrocks(log, sandbox.Id, ex.Message);
                quedan.Add(clave);
            }
        }
        sandbox.PorBorrar = JsonSerializer.Serialize(quedan, Opciones);
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Los contratos de una revisión que el sandbox puede importar hoy, limitados a <paramref name="permitidos"/>.</summary>
    private async Task<IReadOnlyList<EntradaSandbox>> PlanDeRevisionAsync(Guid paqueteId, int revision, HashSet<Guid> permitidos, CancellationToken ct)
    {
        var definicion = await contexto.RevisionesPaquete.AsNoTracking().Where(r => r.PaqueteId == paqueteId && r.Numero == revision)
            .Select(r => r.Definicion).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (definicion is null)
        {
            return [];
        }
        var entradas = FotoPaquete.LeerDefinicion(definicion).Where(e => permitidos.Contains(e.ContratoId) && e.Operaciones.Count > 0).ToList();
        var ids = entradas.Select(e => e.ContratoId).ToList();
        var versiones = entradas.Select(e => e.VersionId).ToList();
        var contratos = await contexto.Contratos.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var leidas = await SinContenido(contexto.Versiones.AsNoTracking().Where(v => versiones.Contains(v.Id))).ToDictionaryAsync(v => v.Id, ct).ConfigureAwait(false);
        return
        [
            .. entradas
                .Where(e => contratos.GetValueOrDefault(e.ContratoId) is { Visibilidad: VisibilidadContrato.Compartible, Tipo: TipoContrato.Openapi }
                    && leidas.GetValueOrDefault(e.VersionId) is { } v && v.ContratoId == e.ContratoId && v.Estado != EstadoVersion.Retirada)
                .Select(e => new EntradaSandbox(contratos[e.ContratoId], leidas[e.VersionId], e.Operaciones))
                .OrderBy(e => e.Contrato.Id),
        ];
    }

    /// <summary>Importa cada contrato con su nombre opaco; si uno falla, borra los que alcanzó a importar en este intento.</summary>
    private async Task<IReadOnlyList<ContratoEnSandbox>> ImportarAsync(
        string ruta, Guid paqueteId, int revision, string huella, IReadOnlyList<EntradaSandbox> plan, CancellationToken ct)
    {
        var version = $"r{revision.ToString(CultureInfo.InvariantCulture)}-{huella[..12]}";
        var usados = new HashSet<string>(StringComparer.Ordinal);
        var importados = new List<ContratoEnSandbox>();
        try
        {
            foreach (var entrada in plan)
            {
                var segmento = entrada.Contrato.Codigo;
                for (var n = 2; !usados.Add(segmento); n++)
                {
                    segmento = $"{entrada.Contrato.Codigo}-{n.ToString(CultureInfo.InvariantCulture)}";
                }
                var importado = new ContratoEnSandbox(entrada.Contrato.Id, entrada.Version.Id, segmento, $"atlas-{ruta}-{segmento}", version, entrada.Operaciones.Count);
                await ImportarUnoAsync(importado, paqueteId, revision, entrada, ct).ConfigureAwait(false);
                importados.Add(importado);
            }
            return importados;
        }
        catch (ErrorMicrocks)
        {
            foreach (var hecho in importados)
            {
                try
                {
                    await microcks.BorrarAsync(hecho.Servicio, hecho.Version, ct).ConfigureAwait(false);
                }
                catch (ErrorMicrocks)
                {
                    // Queda huérfano con un nombre opaco y sin llave que lo alcance; el proxy nunca lo enruta.
                }
            }
            throw;
        }
    }

    /// <summary>El spec podado al ambiente sandbox, con <c>info.title</c> e <c>info.version</c> = el servicio opaco, en JSON.</summary>
    private async Task ImportarUnoAsync(ContratoEnSandbox importado, Guid paqueteId, int revision, EntradaSandbox entrada, CancellationToken ct)
    {
        var podado = await specs.PodarAsync(paqueteId, revision, entrada.Contrato, entrada.Version.Id, entrada.Operaciones, AmbientesPoda.Sandbox, ct).ConfigureAwait(false);
        if (podado.Spec is not { } spec)
        {
            throw new ErrorMicrocks($"No se pudo podar el contrato para el sandbox ({podado.Codigo}).");
        }
        await microcks.ImportarAsync(importado.Servicio, importado.Version, ParaMicrocks(spec.Contenido, importado.Servicio, importado.Version), ct).ConfigureAwait(false);
    }

    /// <summary>Microcks toma el nombre y la versión del servicio de <c>info</c>: se reemplazan por los opacos.</summary>
    internal static string ParaMicrocks(string podado, string servicio, string version)
    {
        var raiz = TextoSpec.Leer(podado, out _);
        if (raiz["info"] is not JsonObject info)
        {
            info = [];
            raiz["info"] = info;
        }
        info["title"] = servicio;
        info["version"] = version;
        return raiz.ToJsonString();
    }

    private static string Clave(ContratoEnSandbox c) => $"{c.Servicio}:{c.Version}";

    private async Task GuardarConEventoAsync(SandboxPaquete sandbox, string accion, object datos, CancellationToken ct) =>
        await Guias.EndpointsGuias.GuardarConAuditoriaAsync(contexto, plataforma, new EventoAuditoria
        {
            OcurridoEn = reloj.GetUtcNow(),
            ActorTipo = TipoActor.Sistema,
            OrganizacionId = sandbox.OrganizacionId,
            Accion = accion,
            RecursoTipo = RecursoAuditoria,
            RecursoId = sandbox.Id.ToString(),
            Version = sandbox.Revision.ToString(CultureInfo.InvariantCulture),
            Datos = JsonSerializer.Serialize(datos, JsonSerializerOptions.Web),
        }, ct).ConfigureAwait(false);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sandbox {Sandbox}: Microcks falló ({Error}); se reintenta en la siguiente vuelta")]
    private static partial void FalloMicrocks(ILogger log, Guid sandbox, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sandbox {Sandbox}: se volvió a importar un servicio que Microcks ya no tenía")]
    private static partial void Restaurado(ILogger log, Guid sandbox);
}
