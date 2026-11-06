---
id: ATLAS-ADR-0055
titulo: Gobierno de IA — inventario, riesgo, políticas e incidentes sin depender del gateway
estado: propuesto
fecha: 2026-11-06
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §7.1, §7.2, §7.4, §8.8, §9 y §10, plan 02 §4, §6, §7, §8, §11, §12 y §15, ATLAS-ADR-0016, ATLAS-ADR-0023, ATLAS-ADR-0053]
---

# ATLAS-ADR-0055 · Gobierno de IA

## Contexto y problema

La F8 lleva a Atlas el inventario de casos de IA, la evaluación de riesgo, las políticas, las herramientas del equipo y los
incidentes (plan 02). Parte de esa fase depende del gateway LiteLLM (llaves virtuales, costos y alertas), que todavía no
existe: F-IA necesita un ADR aprobado, proveedores y llaves, y el líder la dejó para el final (06/11).

El contenido es delicado: el plan describe debilidades reales y las políticas son documentos corporativos. Atlas no debe
presentar como vigente algo que nadie aprobó, ni registrar responsables que nadie confirmó.

## Factores de decisión

- Lo que se pueda hacer sin el gateway debe servir por sí mismo; lo demás se muestra como «sin datos», no como cero.
- Cambiar los permisos de §7.4 es una decisión del líder (§1, regla 4).
- Agregar un módulo no toca a los demás (§10).
- Las aprobaciones deben poder reconstruirse después: quién votó, en qué calidad y sobre qué versión.

## Decisión

### 1. Módulo, bandera y permisos

- `Flit.Atlas.Modulos.Gia`, con id `gia`, rutas `/api/v1/gia` y tablas `gia_*`, igual que el prefijo del plan 02.
- **Bandera `modulo.gia`, apagada** en el backend y en la PWA. Encenderla por ambiente es decisión del líder, porque antes
  debe revisar lo sembrado.
- **El manifiesto se muda al módulo:** a diferencia de ATLAS-ADR-0053 y 0054, `atlas.gia.*` sí cumple el prefijo. Códigos y
  concesiones no cambian: `ver` y `auditar` para `atlas.gobierno_ia` y `atlas.auditor`; `gestionar` para `atlas.gobierno_ia`.
- **No se amplían las concesiones.** Por eso no se implementan los roles `atlas.ia.*` del plan 02 §11.1 ni el dueño como
  rol: leer exige `atlas.gia.ver` y escribir, `atlas.gia.gestionar`. Ver «Por verificar».

### 2. Roles de gobierno

Los roles del plan 02 §4.1 (Responsable de IA, OPD, Comité y Seguridad) no son roles de Atlas, sino la calidad en la que
una persona vota. Se guardan en `gia_rol_gobierno` (persona + rol) y su asignación se audita. **Sin asignaciones nadie
puede aprobar**, que es el estado inicial: el plan marca esas personas como `[definir]` y no se siembran.

### 3. Casos de uso

- `gia_caso_uso` guarda la ficha del plan 02 §7.1 y `gia_caso_uso_version` su historial, inmutable por disparador.
- Estados: `borrador → en_revision → aprobado → en_produccion`, además de `suspendido` y `retirado`. La ficha **solo se edita
  en `borrador`**: cambiar lo aprobado exige reabrirla, con motivo, y volver a aprobarla.
- Los campos obligatorios de §7.1 se exigen al enviar a revisión, no al guardar: las fichas sembradas están incompletas.
- Enviar a revisión exige un dueño y una evaluación de riesgo.

### 4. Semilla

- CU-01 a CU-13 y POL-SGSI-IA-01 a 07, idempotente por código; **nunca pisa lo editado**.
- **Todos los casos entran en `borrador`**, sin dueño, aunque alguno corra hoy en producción: el plan dice que el dueño debe
  validar cada ficha. El riesgo del plan queda como `riesgo_preliminar`, distinto del nivel que da una evaluación.
- Los campos estructurados solo se llenan si el plan los afirma. El texto de la ficha se conserva en `detalle_plan`, con sus
  marcas `[por verificar]`. No se copia el número del proyecto de GCP (prompt maestro §1, regla 5).
- No se siembran herramientas ni asientos: el plan 02 §12.1 los hace salir de una encuesta que no se ha hecho.

### 5. Evaluación de riesgo y aprobación

- Seis dimensiones (D1 a D6), probabilidad e impacto de 1 a 5, inherente y residual. El nivel es el de la **peor dimensión**:
  1–4 bajo, 5–9 medio, 10–15 alto y 16–25 crítico (plan 02 §8.2). Lo calcula el servidor. Cada evaluación es una versión nueva.
- **Quién aprueba se decide por el nivel inherente**, como en el ejemplo del plan 02 §8.5 (inherente alto, residual medio,
  aprueba el Comité):

| Inherente | Votos necesarios | Requisitos | Próxima revisión |
|---|---|---|---|
| Bajo | Responsable de IA | Ficha completa | 12 meses |
| Medio | Responsable de IA, y OPD si hay datos personales | Evals declarados | 12 meses |
| Alto o crítico | Comité | Lo anterior, EIPD aprobada si hay datos personales, humano en el bucle, aviso si hay usuarios externos y plan de rollback | 6 meses |

- **Con residual crítico no se aprueba** («no se despliega hasta reducir el riesgo»), aunque haya votos.
- Un voto de rechazo rechaza la evaluación y devuelve el caso a `borrador`. Un voto con condiciones deja la decisión en
  `aprobado_condiciones`.
- Hay datos personales si las categorías incluyen `personal` o `sensible`, o si hay tipos de PII.

### 6. EIPD

`gia_eipd` guarda las seis secciones del plan 02 §8.4 y un enlace opcional. Es obligatoria para casos altos con datos
personales. Pasa de `borrador` a `en_revision` y a `aprobada` o `rechazada`; **solo decide quien tenga el rol OPD**, que es
quien rinde cuentas en la matriz RACI del plan 02 §4.2.

### 7. Políticas

- `gia_politica` guarda `codigo + version`, con estados `borrador`, `vigente` y `reemplazada`. Solo se edita el borrador.
- **El plan no trae el texto de las políticas, solo índices y puntos clave.** Se siembran como borrador con la marca
  `[PENDIENTE DE REDACCIÓN]` y lo que dice el plan, citado como insumo. **Publicar un texto con esa marca da 422**: nadie puede
  volverla vigente por accidente.
- Publicar exige el rol Comité (plan 02 §4.2) y reemplaza la versión vigente anterior.
- `gia_aceptacion_politica` registra persona, fecha e IP, una vez por versión, solo sobre políticas vigentes.

### 8. Herramientas e incidentes

- `gia_herramienta` y `gia_asiento`, según el plan 02 §11.2, con el proveedor como texto: no se construyen `gia_proveedor` ni
  `gia_alias_modelo`, que sirven sobre todo al gateway.
- `gia_incidente`, con código `INC-IA-<año>-<NNN>` asignado por el servidor. Los tipos, las severidades y los tres playbooks
  salen del plan 02 §6.6 y son una lista fija en el código.
- La respuesta dice el plazo de cada severidad. Atlas **no envía avisos ni reporta a la SIC**: `reportado_sic` solo registra lo
  que hizo una persona.

### 9. Tablero

`GET /api/v1/gia/tablero` calcula lo que hay en Atlas: casos por estado y riesgo, revisiones vencidas, casos altos con ficha
aprobada y con EIPD, casos medio o más con evals declarados, aceptación de POL-SGSI-IA-01, asientos con configuración
verificada e incidentes S1 y S2 abiertos hace más de 30 días.

Los KPI que dependen del gateway o de otros sistemas responden `disponible: false` con su motivo, nunca cero. **No hay envío
automático al Comité.**

### 10. Auditoría

Se usa `plataforma_auditoria`, ya inmutable, en la misma transacción, sin textos ni datos personales. No se crea
`gia_auditoria`.

## Consecuencias

- Buenas: inventario, riesgo y políticas sirven sin el gateway; nada sembrado parece aprobado; las aprobaciones quedan
  reconstruibles.
- Malas: con las concesiones actuales los empleados no pueden leer ni aceptar las políticas (ver «Por verificar»), así que el
  KPI de aceptación se queda bajo hasta decidirlo. El rol de gobierno es un dato, no un permiso nuevo.
- A vigilar: la ficha guarda texto libre que puede mencionar debilidades reales; por eso el módulo es solo de `atlas.gia.*`.

## Opciones descartadas

- **Votar declarando la calidad en cada voto:** cualquiera con `gestionar` podría aprobar como Comité.
- **Sembrar los casos en `en_produccion`:** el plan pide que el dueño valide las fichas.
- **Redactar las políticas a partir de los puntos clave:** son documentos corporativos que aprueba el Comité.
- **Mostrar los KPI sin datos en cero:** cero parece una medición.

## Fuera de este ADR

HU 8.4 (llaves y costos), alertas del gateway y webhook, proveedores y alias, reporte mensual, avisos de revisiones vencidas,
y exportar a PDF. Lo que dependa de LiteLLM vuelve con F-IA.

## Por verificar

- **[por verificar con el líder]** ¿Los empleados leen y aceptan las políticas? El plan 02 §11.3 lo da a «lector+», pero el
  manifiesto solo da `atlas.gia.ver` a gobierno de IA y auditor. El líder técnico tampoco lo tiene, salvo como superadmin.
- **[por verificar con el líder]** Quién asigna los roles de gobierno. Hoy es `atlas.gia.gestionar`, así que quien lo tiene puede
  darse el rol de Comité; la asignación queda auditada, pero no lo impide.
- **[por verificar con el líder]** Si quien evalúa o redacta puede también aprobar. Es la misma pregunta abierta de ATLAS-ADR-0053.
- **[por verificar con el líder]** Las personas de cada rol (plan 02 §4.1), los dueños de cada caso y el contenido sembrado.
- **[por verificar]** La numeración `POL-SGSI-IA-*` se ajusta a la siguiente libre del SGSI (plan 02 §6).
- **[por verificar]** Dónde vive el texto oficial de cada política: el plan pide Markdown en `flit-infra-studio` además de Atlas.
