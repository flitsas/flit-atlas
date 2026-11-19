---
id: ATLAS-ADR-0027
titulo: Cuenta de administrador de emergencia — una cuenta local con superadmin, sin contraseña por defecto, activada con un comando de consola y un enlace de un solo uso
estado: aceptado (decisión del líder, 23/10/2026)
fecha: 2026-10-23
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-03, §6, §6.1, §6.3, §7.2 y §12, ATLAS-ADR-0022, ATLAS-ADR-0023, ATLAS-ADR-0025]
---

# ATLAS-ADR-0027 · Cuenta de administrador de emergencia

## Contexto y problema

En qa nadie puede administrar Atlas. Los roles internos salen de mapear grupos de Entra a roles (`AdministracionRoles.CrearMapeoAsync`), pero crear un mapeo exige `atlas.roles.gestionar`, que hoy solo da `atlas.superadmin`, y ese rol solo llega por un mapeo o por una asignación manual, que también exige ser superadmin. Todos los empleados entran solo con `atlas.lector` (ATLAS-ADR-0023) y la administración (`/administracion/invitaciones`, roles, organizaciones) no aparece. En local y dev el proveedor simulado tiene un grupo de administración (`Identidad:Simulado:GrupoAdministracion`), pero el simulado nunca existe en qa ni en pdn (HU 1.3).

Hay además un segundo problema que el primero deja ver: si Entra ID no responde, o si el registro de la aplicación de un ambiente queda mal configurado, ningún empleado entra y nadie puede corregir nada desde Atlas.

El líder decidió (textual): «crear un usuario admin por defecto y de emergencia, que no tiene entrar por entra id y que sea administrador principal de la plataforma». Esto se aparta de A-03, que dice que los empleados entran solo con Microsoft y que las cuentas locales son solo de externos invitados. Por eso este ADR.

Hechos verificados en el código:

- La HU 1.5 (ATLAS-ADR-0025) ya trae todo lo de una cuenta local: passkeys de Identity, contraseña de 12+ con TOTP obligatorio, códigos de recuperación con hash, bloqueo progresivo, límite de tasa de externos, pasos temporales en Redis y una sesión del BFF más corta.
- El `DataProtectorTokenProvider` de Identity emite tokens de propósito ligados al sello de seguridad: al cambiar el sello, el token deja de servir (así funciona el enlace de recuperación).
- `IInvalidadorSesiones` cambia el sello y revoca sesiones y tokens de OpenIddict (ATLAS-ADR-0022).
- La imagen de atlas-api es «chiseled» (sin shell): lo único que se puede ejecutar dentro es `dotnet`.
- Agregar `Emergencia` a `OrigenAsignacion` no cambia el largo de la columna `origen` (11, el de `grupo_entra`): no hay migración.

## Factores de decisión

- Un camino para el arranque de un ambiente y para entrar si Entra cae, sin abrir el ingreso local a los demás empleados.
- Ninguna contraseña, token ni enlace por defecto en el repo, en la imagen, en `flit-gitops` ni en los logs.
- Quien puede activar la cuenta es quien ya tiene acceso al clúster (`kubectl exec`): no se agrega otra raíz de confianza.
- Reutilizar lo de la HU 1.5 (factores, MFA, bloqueo, límite de tasa, sesión) en lugar de construir otro flujo; nada de criptografía propia (A-03).
- Cada uso debe dejar rastro de alta severidad y verse en la PWA.

## Opciones consideradas

1. **Una cuenta local de emergencia de la organización interna, con `atlas.superadmin` de origen `emergencia`, sin factor hasta que se active con un comando de consola dentro de atlas-api que imprime un enlace de un solo uso; se activa y entra como una cuenta externa (passkey, o contraseña con TOTP).**
2. Sembrar el primer mapeo de grupo de Entra a superadmin desde la configuración (`Identidad:Arranque:GrupoSuperadmin`).
3. Contraseña inicial en un SealedSecret de `flit-gitops`.
4. Un endpoint de arranque que da superadmin al primer empleado que entra.

## Decisión

Opción elegida: **1**.

### 1. La cuenta

- **Una sola** por ambiente: persona `empleado` de la organización interna (FLIT S.A.S.), sin `oid` de Entra, con un usuario de Identity sin contraseña ni factor y una asignación de `atlas.superadmin` **sin vencimiento** con `OrigenAsignacion.Emergencia`. La cuenta se reconoce por esa asignación, no por el correo.
- Correo ficticio configurable: `Identidad:Emergencia:Correo` (por defecto `admin.emergencia@atlas.local`; solo se usa al crearla) y nombre en `Identidad:Emergencia:Nombre`. Nunca un buzón real ni un correo del tenant: no recibe correos (no hay recuperación por correo).
- La sincronización de grupos de Entra solo toca asignaciones `grupo_entra` y el vigilante de vencimientos solo las que tienen `vigente_hasta`: ninguno la quita. El aprovisionamiento de empleados nunca enlaza esta persona a una cuenta de Entra, aunque el correo coincidiera.
- Desde la administración no se desarma: retirar su asignación y suspenderla responden 409 `cuenta_de_emergencia`; el restablecimiento forzado ya era solo para externos. Cerrar sus sesiones sí se permite. Se rota con el comando.

### 2. El comando

- `dotnet Flit.Atlas.Api.dll emergencia activar`, dentro del contenedor: `kubectl -n atlas-qa exec -it deploy/atlas-api -- dotnet Flit.Atlas.Api.dll emergencia activar`. `Program.cs` reconoce el argumento, arma los mismos servicios con la configuración del contenedor (base, Redis, llaves de Data Protection), no levanta el servidor ni los trabajos en segundo plano y quita los proveedores de log.
- Es idempotente: crea la cuenta si no existe (con un candado consultivo de Postgres para que dos comandos a la vez no creen dos), la reactiva si estaba suspendida, le **quita todos los factores** (passkeys, contraseña, TOTP y códigos), quita el bloqueo y **cierra sus sesiones** con `IInvalidadorSesiones` (sello nuevo). Por eso también sirve para recuperarla o para cortar una cuenta comprometida.
- Emite un enlace `https://<host>/emergencia#<usuario>.<token>`: token de propósito `atlas.emergencia.activacion` del proveedor `AtlasEmergencia` (un `DataProtectorTokenProvider` de Identity con su propio protector, así un enlace de recuperación no sirve aquí ni al revés), ligado al sello y con vigencia de 1 h (`Identidad:Emergencia:VigenciaEnlace`). El token va en el fragmento, como en la HU 1.5: no llega a nginx, al Ingress ni al `Referer`.
- El enlace solo se escribe en la **salida estándar del comando**. Nunca en el log (ni en el del pod, que es el del proceso principal, ni en OpenTelemetry), ni en la auditoría, ni en la base. Volver a correr el comando cambia el sello: el enlace anterior deja de servir.

### 3. Activación e ingreso

- `/api/v1/identidad/emergencia/{consultar, passkey/opciones, totp/clave, activar}` (públicos, con el límite de tasa de los externos y apartando la cookie anterior): el mismo flujo de la recuperación de una cuenta externa. Passkey (preferida) o contraseña de 12+ revisada contra filtraciones con TOTP obligatorio; diez códigos de recuperación; el sello cambia al activar (un solo uso) y queda la sesión abierta. Sin términos ni aviso de privacidad: son de los externos (Ley 1581) y esta cuenta es interna.
- Entra por la pantalla de cuentas locales que ya existe (`entrar/local` + `segundo-factor`, o `entrar/passkey`), con MFA obligatorio, bloqueo progresivo y límite de tasa. La regla «solo externos» se mantiene para todos los demás: un empleado con contraseña (no debería tener) sigue recibiendo el 401 genérico.
- Su sesión es de empleado (`tipo_principal = empleado`, organización interna, alcance de datos interno) con `proveedor = emergencia`, 2 h deslizantes y 24 h de tope (las de las cuentas locales). `GET /api/v1/plataforma/yo` devuelve `emergencia = true` y la PWA muestra un aviso fijo en el shell. La reautenticación de acciones sensibles se hace saliendo y volviendo a entrar.

### 4. Controles

- Eventos de seguridad de alta severidad, en `identidad_evento_seguridad` y en `plataforma_auditoria` (`identidad.emergencia.*`) y además como advertencia en el log (solo el tipo y el id del evento): `emergencia.cuenta_creada`, `emergencia.enlace_emitido`, `emergencia.activada` y `emergencia.ingreso`, cada uno con `severidad = alta` en el detalle. Los fallos usan los eventos de siempre (`inicio_fallido`, `mfa_fallido`, `enlace_rechazado`, `cuenta_bloqueada`).
- Procedimiento (en `deploy/README.md`): correr el comando, abrir el enlace de inmediato, registrar una passkey, guardar los códigos de recuperación fuera de línea, usar la cuenta solo para crear los mapeos de grupos (o lo que falte) y cerrar la sesión. Revisar la auditoría después de cada uso.

## Consecuencias

### Positivas

- qa y pdn se pueden arrancar sin tocar la base a mano y sin secretos nuevos.
- Si Entra cae, quien tenga acceso al clúster puede entrar y operar.
- Nada nuevo de criptografía: tokens, passkeys, TOTP y hashes son de Identity; la cuenta pasa por el mismo MFA, bloqueo y límite de tasa que las de externos.

### Negativas

- Existe una cuenta con todos los permisos que no pasa por el acceso condicional de Entra: el control es el acceso al clúster, el MFA local y la auditoría. Quien tenga `kubectl exec` en el namespace puede volverse superadmin (ya podía leer los secretos del ambiente).
- La cuenta no recibe correos ni se recupera por correo: se recupera solo con el comando.
- Es una excepción a A-03 que hay que tener presente en la revisión de seguridad de F4 y en la prueba de penetración del módulo de identidad.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| 2. Sembrar el primer mapeo desde la configuración | Resuelve el arranque, pero no la caída de Entra, que es la mitad del pedido; un error en la variable daría superadmin a un grupo equivocado sin rastro de quién lo decidió |
| 3. Contraseña inicial en un SealedSecret | Una credencial fija que vive en otro repo, que alguien tiene que rotar y que funciona sin MFA hasta el primer ingreso |
| 4. Superadmin al primer empleado que entra | Carrera: el primero que llegue (o un atacante con cualquier cuenta del tenant) se queda con todo |

## Más información

- Comando y pasos: `deploy/README.md` («Cuenta de administrador de emergencia») y `services/api/README.md`.
- Pendientes **[por verificar]**: si `kubectl exec` al namespace queda limitado a quienes deben usar la cuenta (RBAC del clúster, en `flit-gitops`); una alerta sobre la advertencia `identidad.emergencia.*` del log en Loki; si la cuenta debe existir también en pdn o solo en qa.
- Relacionados: ATLAS-ADR-0004, ATLAS-ADR-0022, ATLAS-ADR-0023, ATLAS-ADR-0025.
