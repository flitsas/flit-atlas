---
id: ATLAS-ADR-0056
titulo: Arquitectura como código — modelos de LikeC4, snapshots de despliegue y drift en Atlas
estado: propuesto
fecha: 2026-11-06
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-08, §7.1, §7.3, §7.4, §8.6, §9 y §10, plan 01 §9, §10, §11, §12 y §16, ATLAS-ADR-0009, ATLAS-ADR-0016, ATLAS-ADR-0035, ATLAS-ADR-0039, ATLAS-ADR-0048, ATLAS-ADR-0053, ATLAS-ADR-0054]
---

# ATLAS-ADR-0056 · Arquitectura como código en Atlas

## Contexto y problema

ATLAS-ADR-0054 cubrió los diagramas que no viven en el código. Falta la otra mitad de la F6: mostrar los modelos de LikeC4 que publica cada repo (HU 6.3), lo desplegado según `flit-gitops` (HU 6.4), lo que se separa entre el modelo, gitops y Argo CD (HU 6.5), y el servidor MCP para el equipo (HU 6.9).

Hay cuatro restricciones que condicionan el diseño: `likec4/react` inyecta estilos y la CSP del shell no lo permite; el repo `flit` es público; el §7.4 le da `atlas.arquitectura.ver` a un rol externo; y Argo CD todavía no tiene credenciales para Atlas.

## Factores de decisión

- No se relaja la CSP del shell y el bundle inicial sigue por debajo de 200 KB.
- Seguro por defecto: visibilidad en SQL, nada de secretos, IP ni hosts internos en la base.
- «Sin datos» nunca se confunde con «sin drift».
- Un cambio en los permisos del §7.4 lo decide el líder (§1, regla 4). Un módulo no toca a los demás (§10).

## Decisión

### 1. Módulo, tablas, bandera y permisos

- `Flit.Atlas.Modulos.Arquitectura`, con id `arquitectura`, rutas `/api/v1/arquitectura` y prefijo `arquitectura_`, no `arq_`: el contrato de módulo deriva el prefijo del id (ATLAS-ADR-0016), como en ATLAS-ADR-0053 y ATLAS-ADR-0054. La ruta del plan 01 §10.2 (`/api/v1/atlas/publicaciones/arquitectura`) pasa a `/api/v1/arquitectura/publicaciones`.
- **Bandera `modulo.arquitectura` encendida**, como los demás módulos de negocio. Solo `hola` y `gia` vienen apagados: uno es un ejemplo y el otro espera al gateway. Este no tiene dependencias sin resolver y, sin publicaciones, solo muestra una lista vacía a quien tiene el permiso.
- **Los permisos se mudan al módulo** con los mismos códigos y concesiones (`docs/como-crear-un-modulo.md` §1.2). El manifiesto del núcleo se llamaba `arquitectura` y traía `atlas.diagramas.editar`, que no lleva el prefijo del módulo: ese permiso queda solo en el núcleo, con el id `diagramas_edicion`. Sus concesiones no cambian; en la matriz solo cambia la sección donde aparece. Esto deja desactualizado el §1 de ATLAS-ADR-0054 y un comentario de `ModuloDiagramas`, que no se tocó por no modificar otro módulo.

### 2. Publicación por CI

- `POST /api/v1/arquitectura/publicaciones`, solo con el token OIDC de GitHub Actions. **El sha, la rama y el repo salen del token, nunca del cuerpo.**
- Workflow reutilizable oficial `atlas-publicar-arquitectura.yml`, agregado al valor por defecto de `WorkflowsReutilizables`. Publica un proyecto por llamada, porque `likec4 export json` sin `--project` devuelve una lista, incluso con un solo proyecto, y Atlas la rechaza.
- **La lista blanca es `likec4` para las dos cosas.** El catálogo no admite un tipo `despliegue` (hay un `CHECK` en `catalogo_fuente_publicacion`) y cambiarlo es tocar otro módulo. Para que un repo con modelo no publique lo desplegado, los snapshots exigen además estar en `Arquitectura__RepositoriosDespliegue` (por defecto, `flitsas/flit-gitops`).
- **Un proyecto lo publica siempre el mismo repo y el mismo aplicativo:** el primero que publica queda dueño (403 `proyecto_de_otro_repo`).
- **Versiones por sha y canal.** Cada fila es inmutable por disparador, con índice único por proyecto, tipo, canal, clúster y sha. El canal de un modelo sale de la rama (`develop` → `dev`, `staging` → `qa`, `release` → `pdn`); el de un snapshot, de su ambiente. Lo vigente es lo último publicado en el canal, y por eso «volver en el tiempo» (`?en=`) no necesita otra tabla. Un mismo sha en dos canales ocupa dos filas.
- **Respuestas:** 201 si es nueva; 200 `igual` si el mismo sha ya publicó el mismo contenido; 409 `sha_con_otro_contenido` si lo publicó con otro. El plan pedía 409 también en el primer caso, pero un reintento de CI no es un error. La publicación de un snapshot no cuenta `generadoEn` en su hash: el cron de respaldo del plan 01 §11.5 republica el mismo sha cada 6 horas con otra fecha.
- `likec4 export json` dio la misma salida en dos corridas sobre el mismo modelo, así que un reintento da `igual`.

### 3. Visibilidad

- Cada publicación guarda su nivel, `interno_ti` por defecto o `restringido`, y el predicado va en todas las consultas. Lo que no se puede ver responde 404, igual que lo que no existe.
- `interno_ti`: superadmin, líder técnico y desarrollador. `restringido`: superadmin, líder técnico y gobierno de IA (decisión del 06/11, ATLAS-ADR-0053 §6). `publico_clientes` e `interno_negocio` se rechazan: el permiso lo tienen solo roles técnicos, y compartir con externos va al final.
- **Un externo no ve nada, aunque `ext.asesor_comercial` tenga `atlas.arquitectura.ver`.** Sus roles no dan ningún nivel: listas vacías y 404. No se quita la concesión porque el §7.3 prevé darle vistas de contexto cuando exista el flujo de aprobación.
- **Sin `ver_despliegue`, el modelo se entrega sin vistas ni nodos de despliegue**, que también pueden venir en el `.c4`. El filtro va en el servidor sobre lo que entrega; lo guardado no cambia.

### 4. Qué se acepta de la CI

- Un modelo debe traer su layout (`_stage: layouted`) y respetar los topes de tamaño. Sus enlaces deben ser https y sin credenciales.
- **Una forma de IP o de secreto en cualquier clave o valor rechaza el modelo entero** (422 `contenido_sensible`). No se limpia a escondidas: hay que corregirlo en el repo, porque `flit` es público y ya se filtró. El error nombra la ruta del campo, nunca el valor, porque queda en los logs del workflow.
- Se guarda el JSON tal cual, con dos proyecciones: elementos (ficha y llave del drift) y vistas. Faltan la de relaciones y el Mermaid por vista del plan 01 §12, que solo los usaría el asistente: llegan con F7.

### 5. Snapshots (HU 6.4)

- El generador vive en `tools/arquitectura-gen` y `flit-gitops` lo invoca con el workflow reutilizable. **Es una decisión distinta del plan 01 §11.2**, que lo ponía en `flit-gitops`, por lo que pidió el líder para esta HU. Es JavaScript de Node 22 sin compilar, con `yaml` como única dependencia. El plan decía TypeScript, pero así no hay paso de build en la CI.
- **Un archivo por ambiente y clúster**, no uno por ambiente como en el plan 01 §11.3: cada clúster tiene su Argo CD y su propia vigencia.
- **Lista blanca al leer:** solo `kind`, nombre, réplicas, imagen con digest y hosts de Ingress. Nunca se abren `Secret`, `SealedSecret`, `ConfigMap` ni `EndpointSlice`, y nunca se miran `env`, argumentos, volúmenes o anotaciones.
- **Las IP y los hosts internos se quitan, no se esconden:** no llegan a la base. Lo que queda, los hosts públicos de Ingress, solo se muestra con `ver_despliegue`. Al final, si algo tiene forma sensible, el generador se detiene.
- **El servidor vuelve a validar:** rechaza campos desconocidos, IP, hosts internos y registros internos, y reescribe el snapshot solo con lo conocido. El sha guardado es el del token.
- La tabla de ambientes y clústeres del plan vive en `arquitectura-gen.yaml` dentro de `flit-gitops`, no en este repo: nombres reales fuera de aquí.
- Con `--modelo`, también escribe el modelo de despliegue en DSL, sin hosts, imágenes ni digests. **No se publica ni se usa todavía:** las vistas de despliegue de Atlas salen del JSON. Comprobado con `likec4 validate` sobre el modelo ficticio.

### 6. Drift (HU 6.5)

- **Tres capas (modelo, gitops y Argo CD) y dos comparaciones** (modelo con gitops, gitops con Argo CD), como lo pidió el líder. La D3 del plan 01 §11.4 (contrato con modelo) no se implementa. Ver «Por verificar».
- **Regla central: sin datos no se resuelve nada.** Sin modelo, snapshot o lectura correcta de Argo CD, esa comparación no corre y lo abierto sigue abierto. La API dice `sin_datos` por capa y por comparación; `sin_drift` solo sale si esa comparación corrió hace poco y no encontró nada. Una lectura de más de dos intervalos cuenta como `lectura_vencida`, porque el trabajo pudo morir.
- **Argo CD va detrás de `ILectorArgoCd`.** `LectorArgoCdHttp` lee `GET /api/v1/applications` con un token de solo lectura, desde `Arquitectura__Argo__<i>__{Cluster,Url,Token}`. Sin esa configuración responde `sin_configurar` y no llama a nadie. Solo lee `metadata.name` y el `status` de resumen; ni `spec` ni manifiestos. Exige https salvo en loopback, y los logs no llevan el token, la URL ni el mensaje de la excepción.
- **D1:** `k8s_workload` y `ambientes` contra los workloads del snapshot. Da `ausente_en_gitops` (media, alta con `#critico`), `sin_modelo` y `fuera_de_ambiente`. `#objetivo` ausente no es drift. Un servicio de `fueraDeK3s` cuenta como presente. `ignorar` viene en el snapshot, porque la fuente es `drift-ignorar.yaml` en git; vencida su fecha, deja de aplicar. No hay tabla `arq_drift_ignorar`.
- **D2:** `sync.status`, `health.status`, imagen viva contra el digest, y `reconciledAt` de más de 30 minutos como `sin_reconciliar`, porque Argo llegó a decir «Synced» estando ciego (riesgo R6). Todo es severidad alta.
- **Cuándo corre:** un `BackgroundService` cada 15 minutos (D-4) y al publicar, fuera de la petición. Un nodo a la vez, con candado consultivo.
- **Alertas** con el tipo `arquitectura.alerta_drift` por bandeja, push y correo, según las preferencias de cada persona. Llegan al líder técnico y a los responsables del aplicativo o de su ancestro más cercano con responsables, solo empleados activos. **El texto solo dice cuántos hallazgos hay y en qué ambiente**, sin workloads, clústeres ni hosts, porque un responsable puede no tener `ver_despliegue`. Cada hallazgo alerta una vez; si se resuelve y reaparece, alerta de nuevo. Una caída entre el envío y la marca puede repetir un aviso; se prefirió a perderlo.
- **Solo hay estados `abierto` y `resuelto`.** Faltan `reconocido` e `ignorado` del plan porque no hay un permiso para gestionarlos: ignorar se hace en git.

### 7. Las vistas en la PWA (HU 6.3)

| Librería | Versión | Licencia | Dónde corre | Motivo |
|---|---|---|---|---|
| `likec4` (`likec4/react`) | 1.59.4 | MIT | `/visor/likec4.html`, iframe con `sandbox="allow-scripts"` | Inyecta estilos en una shadow root, y la CSP del shell no lo permite |
| `@likec4/core` | 1.59.4 | MIT | El mismo visor | `likec4/model` arrastra código de Node (`createRequire`) y rompe en el navegador |
| `yaml` | 2.9.1 | ISC | `tools/arquitectura-gen`, solo en CI | Ya se usa en la PWA |
| `kustomize` | 5.8.3 | Apache-2.0 | Binario del workflow, con suma verificada | Publicado el 08/10/2026 |

- `likec4` trae 57 paquetes transitivos nuevos en el lock: 49 MIT, 4 ISC, 3 Apache-2.0 (entre ellos Playwright y Graphviz en wasm, de su CLI, que no corre en la PWA) y `type-fest` (MIT o CC0-1.0). Todos tienen más de dos semanas. `esbuild` queda sin su script de instalación.
- **Se fija 1.59.4 y no 1.60.0** (15/10/2026, también con más de dos semanas) porque es la versión de la CI de `flit#807` y `flito#750`, y la del §8.6.
- **Aislado como Scalar, Mermaid y BPMN.** El visor no tiene cookie ni API (origen opaco). El shell le pasa el JSON por `postMessage`, y él solo devuelve ids validados. El shell no relaja nada; se usa la CSP del visor que ya existe. La fuente de LikeC4 sale de un CDN y queda apagada: usa la del sistema.
- **Enlaces de cada elemento:** tocarlo abre la ficha en el shell, con la API y con los permisos de la persona: catálogo, contratos (solo con `atlas.contratos.ver_internos`), ADR (lista de documentos filtrada por su código), enlaces https del modelo y drift abierto. Los paneles propios de LikeC4 van apagados, porque en un iframe sin `allow-popups` sus enlaces no abrirían.
- **El plan 01 §8.4 no define una clave para el aplicativo.** La ficha lo resuelve así: `metadata.aplicativo` (slug), luego `repo` + `repo_path` contra el catálogo, luego `k8s_workload` como slug, luego un `repo` propio contra el producto de ese repo y, al final, el aplicativo del proyecto si el elemento es de su repo. Un actor o un sistema externo queda sin enlace: uno equivocado es peor que ninguno.
- **Medido:** bundle inicial de 179,1 KB gzip, tope de 200. El visor pesa 739 KB gzip y se baja solo al abrir una vista. No entra en la precarga del service worker.
- **Lista «Elementos de esta vista»:** la alternativa accesible y legible al lienzo, que vive en otro documento y no llega a los lectores de pantalla. Cada fila abre la misma ficha.

### 8. Servidor MCP (HU 6.9)

`tools/likec4-mcp`: versión fijada, ejemplo de configuración, imagen, guía y una prueba de humo. **Corre en el equipo de cada persona:** no tiene autenticación y no se despliega. En contenedor, el modelo va en solo lectura y el puerto solo en `127.0.0.1`. T6.1 del plan (detrás de Atlas, sobre lo publicado) queda para F7.

## Consecuencias

- Buenas: el shell sigue igual; nada sensible llega a la base; un Argo sin configurar no da una falsa tranquilidad; el drift sirve desde que exista un snapshot.
- Malas: cada vista carga un iframe y 739 KB la primera vez. Un mismo sha en varios canales guarda varias copias del JSON. El contenido va en la base y no en S3 como en el plan 01 §12: son decenas o cientos de KB por publicación, pero hay que vigilarlo.
- A vigilar: tamaño de `arquitectura_publicacion` con snapshots por commit. No hay retención.

## Opciones descartadas

- **Correr `likec4/react` en el shell con `styleNonce`:** obliga a un nonce por respuesta en nginx y deja de ser una CSP estática.
- **Generar `gen react` en cada build:** ata el build de la PWA a los modelos de otros repos, y Atlas perdería las versiones.
- **Limpiar los valores sensibles del modelo al recibirlos:** quedarían en el repo público sin que nadie se entere.
- **Dejar pasar a gobierno de IA con otro permiso, como `atlas.gia.ver`:** cambia en silencio lo que significa ese permiso. Ver «Por verificar».

## Por verificar

- **[por verificar con el líder] Gobierno de IA hoy no ve `restringido`.** El predicado le da el nivel, pero su rol no tiene `atlas.arquitectura.ver` en el §7.4 y recibe 403. Cambiar concesiones quedó fuera de este trabajo. El arreglo es una línea en `PermisosArquitectura` más regenerar la matriz; solo vería lo `restringido`. Lo fija una prueba para que no pase inadvertido.
- **[por verificar con el líder] Quién recibe las alertas.** Un responsable sin `ver_despliegue` recibe la alerta, y el enlace le responde «No tienes acceso».
- **[por verificar con el líder] Gestionar hallazgos** (`reconocido`, `ignorado`) necesitaría un permiso nuevo.
- **[por verificar con el líder] D3 (contrato con modelo).** ¿Sigue haciendo falta? `ILectorContratos` ya da lo necesario.
- **[por verificar con el líder] `kustomize` en el workflow** y el canal de DevOps para severidad alta (plan 01 §11.4), que sigue sin definirse.
- **[por verificar] `LectorArgoCdHttp` no se ha probado contra un Argo CD real**, solo contra un servidor simulado con la forma documentada. Falta la cuenta de solo lectura y su token por clúster.
- **[por verificar] El workflow reutilizable nunca ha corrido.** Falta un tag `v*` que lo incluya y las fuentes `likec4` en la lista blanca, que hoy no tiene ninguna. La publicación por OIDC está probada con el emisor simulado.
- **[por verificar] Enlaces al catálogo con los modelos reales.** Sobre los modelos de `flit#807` y `flito#750`, ningún elemento trae `metadata.aplicativo`, y `repo` + `repo_path` solo resuelve 11 de 86 en `flit` y 2 de 48 en `flito`. Los demás caen en el producto o quedan sin enlace, hasta que el modelo diga el slug.
- **[por verificar] El cron de respaldo del plan 01 §11.5 no puede publicar.** `Plataforma__Publicacion__Eventos` admite `push` y `workflow_dispatch`, no `schedule`. Hay que agregarlo o quitar el cron.
- **[por verificar] Si `flit-atlas` pasa a privado** (P-01), el paso que baja `tools/arquitectura-gen` necesitará un token de lectura: el `GITHUB_TOKEN` del repo que llama no alcanza. Hoy el repo es público.
- **[por verificar] Legibilidad de las vistas grandes.** Se dibujaron una vez las 21 vistas de `flit#807` y `flito#750`, sin errores, y sin copiarlas a los fixtures. Las de 28 a 44 nodos (`landscape`, `flit2_contenedores`, `flito_contenedores`, `flito_api_componentes`) no se leen al abrir: el landscape mide 9.630 px de ancho y cada nodo queda de unos 30 px en un marco de 928 px. `autoLayout LeftRight` no lo mejora (26 px). **En móvil es peor:** el ajuste de LikeC4 tiene un zoom mínimo fijo de 5 % que `minZoom` no cambia, así que los bordes quedan cortados al abrir. Se leen con zoom, en pantalla completa o en la lista. Para leerlas sin zoom hay que partirlas en el modelo; una opción es un landscape resumido por grupos, con `navigateTo` a cada grupo.
- **[por verificar] Accesibilidad del lienzo.** axe encontró contraste insuficiente dentro del lienzo de LikeC4 y se excluye el iframe de esa revisión; la alternativa es la lista. Tampoco se ha probado con un lector de pantalla.
- **[por verificar] Gestos en iPhone.** Playwright no sintetiza un pellizco en WebKit: el zoom con gestos se probó en Pixel 7 emulado y falta la prueba a mano en un iPhone y un Android de gama media (§11).
- **[por verificar] Consola del visor.** LikeC4 intenta leer `sessionStorage`, que el sandbox no permite: lo captura y solo deja un error en la consola, sin efecto visible.
