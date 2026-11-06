using System.Text.Json;
using Flit.Atlas.Modulos.Gia.Datos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Gia.Semilla;

/// <summary>
/// Fichas del plan 02 §7.2 y políticas del §6 (ATLAS-ADR-0055 §4 y §7). Los campos estructurados solo se llenan si el plan los
/// afirma; el resto queda en <c>DetallePlan</c>, con sus marcas. Todo entra en borrador, sin dueño: lo valida una persona.
/// </summary>
public static class SemillaGia
{
    public sealed record CasoSembrado(
        string Codigo, string Nombre, string Aplicacion, string Proposito, string RiesgoPreliminar, string Detalle,
        string? Tipo = null, string[]? Usuarios = null, string? Humano = null, string[]? Categorias = null, string[]? Pii = null, string[]? Alias = null, string? Proveedor = null);

    public sealed record PoliticaSembrada(string Codigo, string Titulo, string Extiende, string Insumo);

    public static IReadOnlyList<CasoSembrado> Casos { get; } =
    [
        new("flit2-ocr-documentos", "OCR de documentos de trámites (Flit 2.0)",
            "core-api (Infrastructure/Ocr/Anthropic*.cs, DocumentOcrPrompts.cs); endpoints /ocr/{tipo} y /ocr/lote",
            "Extraer campos de cédulas, licencias, tarjetas de propiedad y otros soportes para prellenar trámites vehiculares.",
            "alto",
            """
            **CU-01.** **Decisiones:** no decide; prellena campos que el gestor confirma **[verificar que siempre haya confirmación]**.
            **Datos:** personales y sensibles (foto, firma, número de documento, nombre, dirección, placa).
            **Proveedor actual:** Anthropic `claude-haiku-4-5` con failover a OpenAI, Kimi y DeepSeek configurable. **Riesgo: el failover podría enviar imágenes de cédulas a China.**
            **Alias propuesto:** `flit-ocr` (+ `flit-ocr-preciso`).
            **Presidio:** no aplica a imágenes. Aplicar a cualquier texto libre que acompañe.
            **Riesgo preliminar:** ALTO (datos sensibles, volumen, ciudadanos).
            **Controles pendientes:** retirar Kimi y DeepSeek del failover para este caso; evaluación de impacto en privacidad; aviso en la UI; evals de exactitud por tipo de documento.
            """,
            Tipo: "ocr", Usuarios: ["cliente", "ciudadano"], Humano: "revision_posterior", Categorias: ["personal", "sensible"],
            Pii: ["foto", "firma", "cedula", "nombre", "direccion", "placa"], Alias: ["flit-ocr", "flit-ocr-preciso"],
            Proveedor: "Anthropic claude-haiku-4-5 con failover a OpenAI, Kimi y DeepSeek configurable"),
        new("flit2-dr-flit", "Asistente Dr. FLIT",
            "core-api (Flit.DrFlit.*), /api/v1/dr-flit; frontend /manual",
            "Responder dudas de uso del aplicativo de Trámites desde el manual; escalar casos de soporte a ADO.",
            "medio",
            """
            **CU-02.** **Tipo:** chat / asistente (catálogo del manual dentro del prompt).
            **Usuarios afectados:** usuarios de clientes (gestores, funcionarios).
            **Humano en el bucle:** no aplica (informativo); el escalamiento lo crea un humano **[por verificar]**.
            **Datos:** lo que el usuario escriba (puede incluir PII sin querer). Hay consentimiento (`dr_flit.*`).
            **Proveedor:** Anthropic Haiku 4.5 vía router.
            **Alias propuesto:** `flit-rapido` (o `flit-asistente-principal` para preguntas complejas).
            **Presidio:** sí, sobre el mensaje del usuario (cédula, placa, teléfono, correo).
            **Cuota:** 30 mensajes por día (ya existe). Agregar presupuesto en el gateway.
            **Riesgo preliminar:** MEDIO (confabulación; inyección de prompt).
            **Controles pendientes:** evals de respuestas frecuentes; prueba de inyección de prompt; aviso «respuesta generada con IA». Evoluciona hacia el agente experto (documento 03).
            """,
            Tipo: "chat", Usuarios: ["cliente"], Alias: ["flit-rapido", "flit-asistente-principal"], Proveedor: "Anthropic Haiku 4.5 vía router"),
        new("comparendos-ocr", "OCR de comparendos (GDC V2)",
            "core-comparendos (Modulos/Ocr/ProveedorOcr.cs, PromptsOcrEndpoints.cs)",
            "Extraer datos de comparendos (infractor, placa, código de infracción, fecha, lugar).",
            "alto",
            """
            **CU-03.** **Datos:** personales (infractor, documento, placa, dirección).
            **Proveedor:** Anthropic `claude-haiku-5-5` (semilla); admite OPENAI y GEMINI.
            **Alias propuesto:** `flit-ocr`.
            **Controles existentes:** prompts versionados con evaluación antes de publicar. Reutilizar este patrón como estándar corporativo.
            **Riesgo preliminar:** ALTO (datos personales; un comparendo mal leído tiene efecto sobre una persona).
            """,
            Tipo: "ocr", Categorias: ["personal"], Pii: ["cedula", "placa", "direccion"], Alias: ["flit-ocr"],
            Proveedor: "Anthropic claude-haiku-5-5 (semilla); admite OPENAI y GEMINI"),
        new("flito-ocr-documentos", "OCR e identidad en FLITO",
            "FLITO (ocr-docs.routes.ts, identidad.routes.ts, drive/procesador.service.ts)",
            "OCR de documentos de trámites y validación de identidad para organismos de tránsito y CEA.",
            "alto",
            """
            **CU-04.** **Datos:** personales y sensibles.
            **Proveedor:** Anthropic `claude-haiku-4-5-20251001` y `claude-sonnet-4-6` (escritos en el código).
            **Alias propuesto:** `flit-ocr`, `flit-ocr-preciso`.
            **Riesgo preliminar:** ALTO.
            **Controles pendientes:** sacar los modelos del código; gateway; evaluación de impacto en privacidad.
            """,
            Categorias: ["personal", "sensible"], Alias: ["flit-ocr", "flit-ocr-preciso"],
            Proveedor: "Anthropic claude-haiku-4-5-20251001 y claude-sonnet-4-6 (escritos en el código)"),
        new("flito-copiloto-checklist", "Copiloto del checklist de trámites",
            "FLITO (modules/tramites/copiloto.ts)",
            "Sugerir al gestor qué ítems pendientes priorizar.",
            "bajo",
            """
            **CU-05.** **Humano en el bucle:** aprobación previa: nunca marca ni envía nada por su cuenta; muestra un aviso legal.
            **Datos:** sin PII (ids, etiquetas y banderas).
            **Proveedor:** Anthropic Haiku 4.5. **Alias propuesto:** `flit-rapido`.
            **Riesgo preliminar:** BAJO. Caso de referencia de buen diseño.
            """,
            Humano: "aprobacion_previa", Alias: ["flit-rapido"], Proveedor: "Anthropic Haiku 4.5"),
        new("flito-runt-soat-impuestos", "Asistencia IA en RUNT, SOAT e impuestos (FLITO)",
            "FLITO (runt/runt.routes.ts y módulos de SOAT e impuestos [verificar el propósito exacto de cada llamada])",
            "Interpretar respuestas o documentos de RUNT, SOAT e impuestos [por verificar].",
            "medio_alto",
            """
            **CU-06.** **Datos:** personales del vehículo y del propietario; posiblemente financieros (impuestos, Ley 1266 **[por verificar]**).
            **Proveedor:** Sonnet 4.6 / Haiku 4.5.
            **Riesgo preliminar:** MEDIO-ALTO **[pendiente de la revisión del dueño]**.
            """,
            Categorias: ["personal"], Proveedor: "Sonnet 4.6 / Haiku 4.5"),
        new("maps-inteligencia-comparendos", "Inteligencia de comparendos (flitsas-maps)",
            "flitsas-maps (api/inteligencia/ai/route.ts)",
            "Análisis e insights sobre datos de comparendos en el mapa [por verificar el alcance exacto].",
            "alto",
            """
            **CU-07.** **Datos:** comparendos (posible PII: infractor, placa, ubicación).
            **Proveedor:** Anthropic `claude-haiku-4-5-20251001`.
            **Riesgo preliminar:** ALTO por el contexto: repo en cuenta personal, app sin autenticación, archivos sensibles versionados.
            **Controles pendientes:** trasladar el repo a la organización; purgar los archivos sensibles; agregar autenticación o restringir a red interna; gateway; enviar al modelo solo agregados, sin PII.
            """,
            Proveedor: "Anthropic claude-haiku-4-5-20251001"),
        new("maps-normalizacion-ubicaciones", "Normalización de direcciones (flitsas-maps)",
            "flitsas-maps (api/locations/process/route.ts)",
            "Normalizar o geocodificar direcciones de comparendos [por verificar].",
            "medio",
            """
            **CU-08.** **Datos:** direcciones (dato personal si se asocian a una persona).
            **Proveedor:** Anthropic.
            **Riesgo preliminar:** MEDIO. Enviar solo la dirección, sin nombre ni documento.
            """,
            Proveedor: "Anthropic"),
        new("vigia-chat-operativo", "Chat operativo de Panel Vigía",
            "flit-infra-studio/panel-vigia (backend/chat.py)",
            "Asistente de operación de las VPS; propone comandos de una whitelist; el humano ejecuta.",
            "medio",
            """
            **CU-09.** **Tipo:** agente con herramientas.
            **Usuarios:** internos (operación y seguridad).
            **Datos:** salidas de comandos de infraestructura (pueden contener IP, usuarios, fragmentos de logs con PII).
            **Proveedor:** Anthropic (API o perfil de CLI).
            **Alias propuesto:** `flit-asistente-principal`.
            **Riesgo preliminar:** MEDIO (agencia: mitigada por la whitelist y el humano en el bucle; fuga de secretos en las salidas de comandos).
            **Controles pendientes:** redactar secretos en las salidas antes de enviarlas al modelo; prohibir el modo «perfil personal de CLI» en el servidor; llave virtual propia.
            """,
            Tipo: "agente", Usuarios: ["interno"], Alias: ["flit-asistente-principal"], Proveedor: "Anthropic (API o perfil de CLI)"),
        new("legado-infraction-ocr-documentai", "OCR de infracciones con Google Document AI (legado)",
            "BackCrudInfractionOcr (gcpApiProxy.ts; procesadores en us-documentai.googleapis.com)",
            "OCR de infracciones y VIN.",
            "alto",
            """
            **CU-10.** **Tipo:** OCR (modelo de Google, no LLM).
            **Datos:** personales.
            **Proveedor:** Google Cloud (EE. UU.).
            **Credenciales:** el plan registra una llave de servicio versionada en git. CRÍTICO.
            **Riesgo preliminar:** ALTO hasta rotar la llave.
            **Decisión pendiente:** ¿se mantiene o se reemplaza por CU-03 (comparendos en Flit 2.0)? Mientras siga, se registra y se controla. Document AI no pasa por LiteLLM: se gobierna con una cuenta de servicio dedicada, rotación y cuota en GCP.
            """,
            Tipo: "ocr", Categorias: ["personal"], Proveedor: "Google Cloud (EE. UU.)"),
        new("equipo-asistentes-codigo", "Asistentes de código del equipo",
            "Equipo: Claude Code, Cursor, Antigravity [por verificar]",
            "Asistentes de código del equipo.",
            "medio",
            """
            **CU-11.** **Herramientas:** Claude Code, Cursor, Antigravity **[por verificar]**.
            **Datos:** código fuente (interno), configuración; riesgo de leer `.env` con secretos reales (27 repos legados).
            **Riesgo preliminar:** MEDIO.
            **Controles:** plan 02 §12.
            """,
            Categorias: ["interno"]),
        new("equipo-agentes-infra", "Agentes de infraestructura y seguridad (flit-infra-studio)",
            "flit-infra-studio: .claude/agents/*, orquestadores/, subagentes/, AgenterAWS/",
            "Agentes de infraestructura y seguridad.",
            "medio_alto",
            """
            **CU-12.** **Componentes:** `.claude/agents/*` (dba-sentinel, netsec-agent, patch-agent, security-auditor, sentinel-waf, vps-vigia…), `orquestadores/`, `subagentes/`, `AgenterAWS/`.
            **Datos:** configuración e infraestructura, credenciales de BD de agentes.
            **Riesgo preliminar:** MEDIO-ALTO (agencia sobre infraestructura).
            **Controles:** usuarios de BD con privilegio mínimo (ya existen para algunos); managed settings con `deny` de comandos destructivos; ejecución solo desde equipos con la política instalada.
            """),
        new("atlas-agente-experto", "Agente experto en el funcionamiento de los aplicativos (futuro)",
            "Flit Atlas (documento 03-plan-arquitectura-agente-experto.md)",
            "Agente experto en el funcionamiento de los aplicativos. Se registra desde el diseño (privacidad desde el diseño).",
            "medio",
            """
            **CU-13 (futuro).** Ver el plan del agente experto.
            **Riesgo preliminar:** MEDIO.
            """),
    ];

    public static IReadOnlyList<PoliticaSembrada> Politicas { get; } =
    [
        new("POL-SGSI-IA-01", "Uso aceptable de IA", "POL-SGSI-07 (navegación), POL-16 (software)",
            """
            **Índice:** objetivo y alcance; definiciones; herramientas autorizadas; usos permitidos; usos prohibidos; reglas sobre datos; verificación humana y responsabilidad sobre la salida; propiedad intelectual y confidencialidad; transparencia hacia clientes y ciudadanos; reporte de incidentes; sanciones; aceptación.

            **Puntos clave del plan:**
            - Solo se usan las herramientas de la lista aprobada, con cuentas corporativas.
            - Prohibido pegar en un chat de IA no aprobado: datos personales de ciudadanos, credenciales, código de repos privados, contratos, información de clientes y datos de bases de producción.
            - Prohibido que un agente o asistente ejecute acciones irreversibles en pdn sin aprobación humana.
            - Quien usa la salida de la IA responde por ella.
            - Ninguna decisión con efecto jurídico sobre un ciudadano puede tomarla solo la IA: humano en el bucle obligatorio.
            - Uso de IA en productos: aviso visible al usuario.
            """),
        new("POL-SGSI-IA-02", "Clasificación de datos para IA", "POL-SGSI-02 (clasificación)",
            """
            **Índice:** niveles de clasificación; matriz nivel × destino de IA; reglas de minimización; enmascaramiento obligatorio; datos de entrenamiento y retención del proveedor; excepciones.

            **Puntos clave del plan:** matriz propuesta en el plan 02 §6.2 (nombres de nivel **[por verificar]**). Enviar al modelo solo los campos necesarios; recortar imágenes cuando sea viable; no registrar en logs propios el contenido completo con PII; Presidio en modo `mask` para texto libre.
            """),
        new("POL-SGSI-IA-03", "Proveedores y modelos aprobados", "POL-20 / PRO-20 (proveedores TIC), Política de nube",
            """
            **Índice:** criterios de evaluación; niveles de proveedor; lista aprobada; proceso de alta y baja; revisión anual; salida.

            **Puntos clave del plan:** criterios y lista inicial propuesta en el plan 02 §6.3, con condiciones **[por verificar]**. La lista no está aprobada.
            """),
        new("POL-SGSI-IA-04", "Gestión de llaves de IA", "Política de criptografía y gestión de llaves; POL-SGSI-09",
            """
            **Puntos clave del plan:** las llaves de proveedor solo existen en el gateway; las aplicaciones usan llaves virtuales por aplicación, ambiente y caso; rotación de llaves de proveedor cada 90 días y virtuales cada 180; revocación de llaves virtuales sin uso en 60 días; el valor se muestra una sola vez. Depende del gateway, que todavía no existe.
            """),
        new("POL-SGSI-IA-05", "Desarrollo con asistentes de código", "PRO-15 (gestión de cambios DevSecOps)",
            """
            **Puntos clave del plan:** herramientas autorizadas **[decidir]**; configuración gestionada; prohibido que el asistente lea secretos o ejecute comandos destructivos o contra pdn; el asistente nunca se conecta a BD de pdn; todo código generado pasa por PR con revisión humana; repos en cuentas personales prohibidos; gitleaks en pre-commit y CI.
            """),
        new("POL-SGSI-IA-06", "Gestión de incidentes de IA", "Procedimiento de incidentes del SGSI [por verificar si existe]",
            """
            **Puntos clave del plan:** tipos INC-IA-01 a 07, severidades S1 a S4 con su tiempo de respuesta y playbooks mínimos (plan 02 §6.6). El plazo de reporte a la SIC está **[por verificar]**.
            """),
        new("POL-SGSI-IA-07", "Retención de prompts, respuestas y logs", "SGSI-POL-A815 (logging), POL-02 (eliminación segura)",
            """
            **Puntos clave del plan:** tabla de retención por tipo de dato en el plan 02 §6.7, con plazos **[decidir]**. Los metadatos de llamadas no llevan contenido; los casos con imágenes de documentos no guardan contenido en logs de IA.
            """),
    ];

    /// <summary>El texto de una política sembrada: la marca de pendiente y el insumo del plan, nunca un texto como si fuera oficial.</summary>
    public static string ContenidoPendiente(PoliticaSembrada politica)
    {
        ArgumentNullException.ThrowIfNull(politica);
        return $"""
            > **{ReglasGia.MarcaPendiente}** Esta política no está redactada ni aprobada y no tiene vigencia. Abajo solo está lo que propone el plan 02, como insumo.

            **Extiende:** {politica.Extiende}

            ## Insumo del plan 02

            {politica.Insumo}
            """;
    }

    /// <summary>Idempotente por código: crea lo que falta y nunca pisa lo que ya existe.</summary>
    public static async Task<(int Casos, int Politicas)> AplicarAsync(GiaDbContext contexto, TimeProvider reloj, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(reloj);
        var ahora = reloj.GetUtcNow();
        var casosExistentes = await contexto.Casos.Select(c => c.Codigo).ToListAsync(ct).ConfigureAwait(false);
        var politicasExistentes = await contexto.Politicas.Select(p => p.Codigo).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var casos = 0;
        foreach (var s in Casos.Where(s => !casosExistentes.Contains(s.Codigo)))
        {
            var caso = new CasoUso
            {
                Codigo = s.Codigo,
                Nombre = s.Nombre,
                Aplicacion = s.Aplicacion,
                Proposito = s.Proposito,
                RiesgoPreliminar = s.RiesgoPreliminar,
                DetallePlan = s.Detalle,
                Tipo = s.Tipo,
                UsuariosAfectados = [.. s.Usuarios ?? []],
                HumanoEnBucle = s.Humano,
                CategoriasDato = [.. s.Categorias ?? []],
                TiposPii = [.. s.Pii ?? []],
                AliasUsados = [.. s.Alias ?? []],
                ProveedorActual = s.Proveedor,
            };
            contexto.Casos.Add(caso);
            contexto.VersionesCaso.Add(new VersionCasoUso
            {
                CasoUsoId = caso.Id,
                Numero = 1,
                Foto = JsonSerializer.Serialize(FichaCaso.De(caso), JsonSerializerOptions.Web),
                Motivo = "Carga inicial desde el plan 02 §7.2; el dueño debe validarla.",
                GuardadaEn = ahora,
            });
            casos++;
        }
        var politicas = 0;
        foreach (var s in Politicas.Where(s => !politicasExistentes.Contains(s.Codigo)))
        {
            contexto.Politicas.Add(new Politica { Codigo = s.Codigo, Version = "0.1", Titulo = s.Titulo, Contenido = ContenidoPendiente(s) });
            politicas++;
        }
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        return (casos, politicas);
    }
}
