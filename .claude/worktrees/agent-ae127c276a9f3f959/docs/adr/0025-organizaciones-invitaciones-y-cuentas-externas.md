---
id: ATLAS-ADR-0025
titulo: Organizaciones, invitaciones y cuentas locales de externos — enlace de un solo uso, passkeys de Identity, TOTP, recuperación, términos versionados y correo por Graph o SMTP
estado: propuesto
fecha: 2026-10-22
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-03, §6.2, §6.3, §7.3, §9, §11 y §12, HU 1.5, ATLAS-ADR-0004 §5 y §6, ATLAS-ADR-0022, ATLAS-ADR-0023]
---

# ATLAS-ADR-0025 · Organizaciones, invitaciones y cuentas locales de externos

## Contexto y problema

La HU 1.5 abre Atlas a personas externas (clientes, asesores y proveedores): CRUD de organizaciones, invitación con rol, alcance, vencimiento y permisos extra, correo enviado por Atlas, enlace de un solo uso de 72 h guardado como hash, cuenta local con passkey (preferida) o contraseña más TOTP, códigos de recuperación, recuperación por correo, términos y aviso de privacidad versionados (Ley 1581), reenviar, suspender, renovar y revocar, y `ext.admin_organizacion` habilitable por organización. CA: invitar → aceptar → registrar passkey → entrar → vencer en escritorio, Android e iOS; un enlace usado o vencido no sirve; una cuenta externa sin MFA no entra; todo queda en la auditoría.

ATLAS-ADR-0004 §5 fijó el qué. Hechos verificados en el código y en .NET 10.0.11:

- `Microsoft.AspNetCore.Identity` trae `IPasskeyHandler<TUser>` / `PasskeyHandler<TUser>` (`MakeCreationOptionsAsync`, `PerformAttestationAsync`, `MakeRequestOptionsAsync`, `PerformAssertionAsync`) y `IdentityPasskeyOptions` (por defecto `UserVerificationRequirement = "required"`). Los métodos de `SignInManager` que citaba ATLAS-ADR-0004 guardan el desafío en la cookie `Identity.TwoFactorUserId` y emiten la cookie de Identity, que Atlas no usa (su sesión es la del BFF, ATLAS-ADR-0008).
- El almacén de Identity guarda la clave TOTP y los códigos de recuperación **en claro** en `identidad_usuario_token`.
- `AdministracionRoles.AsignarAsync(..., OrigenAsignacion.Invitacion)` (HU 1.4) y el vigilante de vencimientos ya cortan el acceso al vencer una asignación; el máximo de 90 días estaba fijo en el código.
- `RenovacionAlIngresar` (HU 1.3b) aparta la cookie anterior en las rutas de ingreso; `LimitacionTasa` limita por IP y usuario.

## Factores de decisión

- Nada de criptografía propia (A-03): WebAuthn, hash de contraseñas, TOTP y tokens de recuperación de ASP.NET Core Identity; generador aleatorio, SHA-256 y comparación en tiempo constante del framework; Data Protection.
- OWASP ASVS nivel 2: V2.1 (contraseñas), V2.5 (recuperación), V2.6 (códigos de un solo uso, no legibles), V2.8 (TOTP de un solo uso), V3 (sesión), y la defensa contra enumeración.
- El token del enlace nunca se registra (logs, auditoría, outbox) ni llega a un servidor que no lo necesite.
- Aislamiento por organización (predicado y RLS, ATLAS-ADR-0023): el administrador de la organización A no ve ni invita en B y no amplía alcances.
- Tocar lo mínimo de lo compartido con las HU paralelas (1.3c y 1.9): ninguna migración del contexto de Identidad; una del de Plataforma.

## Opciones consideradas

1. **Passkeys con `IPasskeyHandler<TUser>` de Identity**, estado del desafío en Redis (cifrado y de un solo uso), sesión del BFF de siempre; transacción compartida entre los contextos de Plataforma e Identidad para crear la cuenta; correo por `IEnviadorCorreo` (Graph o SMTP) dentro de la petición.
2. `SignInManager` de Identity (sus métodos de passkey y su cookie) además de la cookie del BFF.
3. Librería de terceros para WebAuthn (p. ej. Fido2NetLib).
4. Enviar el correo por la outbox (RabbitMQ) o por core-notificaciones.

## Decisión

Opción elegida: **1**.

### 1. Organizaciones

`/api/v1/plataforma/organizaciones` con `atlas.organizaciones.gestionar` y reautenticación para cambiar: listar, crear (cliente o proveedor; la interna ya existe), editar (nombre, tipo, `adminOrganizacionHabilitado`), suspender (nadie de ella entra ni conserva permisos y sus sesiones se cierran), reactivar y eliminar solo si está vacía. Auditoría `plataforma.organizacion_*`. `ext.admin_organizacion` solo concede permisos si su organización lo tiene habilitado: lo aplica `CalculadoraPermisos` en cada petición.

### 2. Invitaciones

- `/api/v1/plataforma/invitaciones` con `atlas.invitaciones.gestionar`: listar, opciones, crear, reenviar, suspender, renovar y revocar. El estado se **calcula al leer** (`pendiente`, `enlace_vencido`, `aceptada`, `acceso_vencido`, `suspendida`, `revocada`): no depende de un proceso que lo marque.
- La invitación lleva organización, rol externo, alcance (lista de `{tipo, id}` de los tipos de los manifiestos: aplicativo, paquete, documento; vacío = lo que se comparta con la organización), vencimiento y permisos extra (`exportar`, `sandbox`, que leerán F3–F4).
- **Vencimiento**: para todos los roles externos hay vencimiento; si no se indica, el máximo. Es obligatorio (con alcance explícito) para `ext.proveedor`. El máximo es configurable, `Identidad:Externos:VigenciaMaximaAcceso` (90 días por defecto); `AdministracionRoles` lo lee de ahí en lugar de la constante de la HU 1.4.
- **Administrador de organización**: solo su organización (el predicado y RLS ya le ocultan las demás; un correo que choque con otra organización se revisa con el alcance del sistema y responde un 409 genérico), nunca el rol `ext.admin_organizacion` (no amplía privilegios), alcance dentro del suyo, vencimiento no posterior al suyo y solo permisos extra que él tenga. No actúa sobre su propia invitación. Si no, 403 `fuera_de_tu_alcance`.
- **Token**: 256 bits de `RandomNumberGenerator`, base64url; en `plataforma_invitacion.token_hash` solo su SHA-256; se busca por el hash (índice único) y se compara con `CryptographicOperations.FixedTimeEquals`. Vence a las 72 h de cada envío (`Identidad:Externos:VigenciaEnlace`); reenviar o renovar genera otro y el anterior deja de servir. La aceptación bloquea la fila (`FOR UPDATE`): no se acepta dos veces.
- **Enlace en el fragmento**: `https://<host>/invitacion#<token>` (y `/recuperar#…`). El navegador no envía el fragmento a ningún servidor (nginx, Ingress, Vite) ni en el `Referer`; la PWA lo quita de la barra de direcciones y lo manda en el cuerpo de un POST. Es un cambio deliberado frente a `/invitacion/<token>` del encargo, por el requisito de que el token no quede en logs.
- **Renovar**: si se aceptó, extiende las asignaciones de la invitación (auditoría `plataforma.asignacion_renovada`) y reactiva a la persona; si no, vuelve a pendiente con un enlace nuevo. **Suspender**: pendiente, el enlace no sirve; aceptada, la persona queda suspendida y sus sesiones se invalidan. **Revocar**: definitivo; retira las asignaciones de la invitación con `RetirarAsync` (invalida las sesiones).

### 3. Cuenta local del externo

- Aceptar (`/api/v1/identidad/invitaciones/{consultar, passkey/opciones, totp/clave, aceptar}`, públicos): nombre, aceptación de las versiones vigentes de los documentos y el factor. Una **transacción compartida** (`TransaccionCompartida`: el contexto de Identidad usa la conexión y la transacción del de Plataforma) crea la persona externa, el usuario, el factor, los diez códigos de recuperación, la evidencia de los términos, las asignaciones (`AsignarAsync(..., OrigenAsignacion.Invitacion)`, una por recurso del alcance) y la auditoría. Si el código TOTP no coincide o una asignación no se puede crear, no queda nada. Después se abre la sesión.
- **Passkey** con `IPasskeyHandler<UsuarioAtlas>` (registrado a mano: `AddIdentityCore` no lo hace), `UserVerificationRequirement = "required"` y `ResidentKeyRequirement = "required"`: la passkey es factor fuerte y se entra sin escribir el correo. Sin validación de attestation (ATLAS-ADR-0004 §5). El dominio es el host de la petición (`Identidad:Passkeys:Dominio` si hiciera falta fijarlo). El estado del desafío vive en Redis (`PasosTemporales`: cifrado con Data Protection, 5 minutos, se **toma** con `GETDEL`); el navegador solo recibe un id. Al entrar se guarda el contador de firmas.
- **Contraseña**: mínimo 12, sin reglas de composición (NIST SP 800-63B), revisada por `ValidadorContrasenaFiltrada`: **Have I Been Pwned por rango** (k-anonimato: solo los 5 primeros caracteres del SHA-1 salen del servidor, con `Add-Padding`) y una lista local de comunes (más el correo dentro de la contraseña) como respaldo si HIBP no responde. Se eligió HIBP porque una lista local de filtraciones útil pesa cientos de MB y envejece; el respaldo local evita que una caída de un tercero bloquee el registro. Egreso a `api.pwnedpasswords.com` desde las VPS **[por verificar]**; si no hay, queda la lista local (`Identidad:Externos:ConsultarContrasenasFiltradas=false`).
- **TOTP obligatorio con contraseña**: `AuthenticatorTokenProvider` de Identity; la clave se genera con `GenerateNewAuthenticatorKey`, se muestra con QR (SVG de QRCoder, en `<img>` con data URI: `img-src data:` de la CSP) y se confirma con el primer código al aceptar.
- **Secretos del segundo factor**: `AlmacenUsuarios` (subclase de `UserStore`) cifra la clave TOTP con Data Protection y guarda los códigos de recuperación como hashes del `PasswordHasher` de Identity (PBKDF2): quien lea la base no los puede usar.

### 4. Ingreso de externos

- Contraseña (`/entrar/local`): `VerificadorContrasena.VerificarAsync` (bloqueo progresivo); nunca abre sesión: responde el paso del TOTP. Correo desconocido, contraseña errada y cuenta bloqueada responden el **mismo 401** y un correo desconocido hace un hash ficticio (tiempo parejo).
- Segundo paso (`/entrar/local/segundo-factor`): TOTP o un código de recuperación, por `VerificadorContrasena.VerificarTotpAsync` / `CanjearCodigoRecuperacionAsync`: los fallos suman al mismo bloqueo. Un código TOTP no se acepta dos veces (marca en Redis por usuario y código durante 3 minutos, ASVS V2.8.4).
- Passkey (`/entrar/passkey/opciones` y `/entrar/passkey`).
- **Sin MFA no entra** (CA): con contraseña correcta pero sin TOTP activo, 403 `mfa_requerido` y evento `identidad.mfa_requerido`; la persona recupera la cuenta.
- Solo entra una persona externa activa, de una organización activa y con al menos un rol vigente; al vencer, el acceso se corta sin intervención (permisos calculados en cada petición y vigilante de la HU 1.4, que invalida las sesiones).
- Si hay versiones nuevas de los documentos, 409 `terminos_pendientes` y `/entrar/terminos` guarda la aceptación antes de abrir la sesión.
- La sesión es la del BFF (`identidad_sesion`) con `tipo_principal = externo`, `organizacion_id` de la persona, `proveedor = local` y `amr` (`hwk`, `user`, `mfa` o `pwd`, `otp`, `mfa`). **Más corta**: 2 h deslizantes y 24 h de tope (`Identidad:Sesion:DeslizanteExternos` y `AbsolutaExternos`; los de empleados siguen en 8 h y 7 días).
- Todas estas rutas públicas apartan la cookie anterior (sesión nueva, con alcance de datos del sistema) y tienen un límite propio y más estricto: `Identidad:LimiteTasa:ExternosPorIp` (10 por minuto), sumado a los demás.

### 5. Recuperación y restablecimiento

- `/recuperacion` responde 202 siempre; si el correo es de una cuenta externa activa, envía un enlace con el token de propósito `atlas.recuperacion` del `DataProtectorTokenProvider` de Identity (ligado al sello de seguridad, 30 minutos, `Identidad:Externos:VigenciaRecuperacion`), hasta 3 por hora. Completar quita todos los factores, registra el nuevo, genera códigos nuevos, quita el bloqueo y **cambia el sello** en la misma transacción: el enlace ya no sirve (un solo uso) y las sesiones se invalidan.
- `POST /api/v1/identidad/admin/personas/{id}/restablecer` (`atlas.identidad.administrar` y reautenticación): quita los factores, invalida las sesiones y envía el enlace de recuperación. No aplica a empleados.

### 6. Términos y aviso de privacidad

Textos en el código (`Externos/Legal/*.md`, recursos del ensamblado) con su versión en `DocumentosLegales`. La evidencia en `plataforma_aceptacion_terminos` guarda versión, **SHA-256 del texto mostrado**, invitación, fecha, IP y navegador. Cambiar un texto exige subir la versión: cada externo la acepta en su siguiente ingreso. **Los textos actuales son un borrador [por verificar]**: los define el líder con jurídica antes de abrir pdn.

### 7. Correo

`IEnviadorCorreo` en Plataforma (también para las notificaciones por correo de §8.9): `EnviadorSmtp` (MailKit 4.18.1, MIT) y `EnviadorGraph` (`POST /users/{buzón}/sendMail` con token de aplicación del registro de Entra del ambiente, `saveToSentItems = false`). `Plataforma:Correo:Proveedor` = `graph` o `smtp`, obligatorio en qa y pdn junto con `Plataforma:Correo:Remitente` y `Atlas:UrlPublica` (los enlaces no se arman con el `Host` de la petición). Plantilla HTML con la marca FLIT, en español. El correo sale **dentro de la petición, después de guardar**: el token no puede ir a la outbox ni a RabbitMQ (quedaría en claro); si falla, la invitación queda, la respuesta dice `correoEnviado = false`, se registra `identidad.correo_no_enviado` y se reenvía. En local, Mailpit en el AppHost (imagen fijada por digest).

### 8. Eventos de seguridad

Tipos nuevos en `TiposEventoSeguridad` (cada uno en `identidad_evento_seguridad` y en `plataforma_auditoria` en la misma transacción): `invitacion_creada|reenviada|suspendida|renovada|revocada|aceptada`, `enlace_rechazado`, `correo_no_enviado`, `cuenta_creada`, `factor_registrado`, `factores_retirados`, `mfa_verificado`, `mfa_fallido`, `mfa_requerido`, `codigo_recuperacion_usado`, `recuperacion_solicitada|completada`, `restablecimiento_forzado` y `terminos_aceptados`. Los de invitaciones llevan el recurso `invitacion`. Nunca el token, la contraseña, la clave TOTP ni los códigos.

## Consecuencias

### Positivas

- Todo lo criptográfico es de Identity y del framework; las passkeys son resistentes a phishing y cuentan como MFA.
- El token del enlace no queda en ningún log ni en la base; los secretos del segundo factor tampoco quedan legibles.
- La cuenta se crea entera o no se crea; el aislamiento por organización sigue en el servidor y en SQL.
- Sin migración del contexto de Identidad (solo la de Plataforma `InvitacionesYCuentasExternas`).

### Negativas

- Si se pierden las llaves de Data Protection, las claves TOTP ya no se pueden leer: esas cuentas se recuperan por correo (igual que las sesiones).
- Canjear un código de recuperación verifica hasta diez hashes PBKDF2 (≈ medio segundo): aceptable para algo excepcional.
- qa y pdn no arrancan sin la configuración de correo y la URL pública (paso del líder en flit-gitops).
- El correo dentro de la petición agrega hasta `Plataforma:Correo:Tiempo` (15 s) en el peor caso.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| 2. `SignInManager` | Exige la cookie de Identity y su esquema `TwoFactorUserId` además de la del BFF; dos sesiones distintas y el desafío en una cookie del cliente |
| 3. Librería WebAuthn de terceros | .NET 10 ya la trae en Identity, mantenida por Microsoft |
| 4. Outbox o core-notificaciones | El correo lleva el token en claro: no puede quedar en una tabla ni en un broker; core-notificaciones solo si el líder lo pide (§6.2) |
| Lista local de filtraciones completa | Cientos de MB que envejecen; HIBP por rango no expone la contraseña |
| Token en la ruta (`/invitacion/<token>`) | La ruta queda en los logs de acceso de nginx y del Ingress |

## Más información

- Pasos del líder: buzón compartido de Atlas, permiso de aplicación `Mail.Send` limitado a ese buzón con una política de acceso de aplicaciones de Exchange Online, variables y SealedSecret de correo en flit-gitops (ver `docs/preparacion/04-prerrequisitos.md`).
- [por verificar] Texto definitivo de los términos y del aviso de privacidad; egreso a `api.pwnedpasswords.com`; passkeys en iOS (prueba manual: WebKit no tiene autenticador virtual en Playwright); puerto local 4038 de Mailpit; si `ext.admin_organizacion` puede invitar a otro administrador (hoy no).
- Relacionados: ATLAS-ADR-0004, ATLAS-ADR-0008, ATLAS-ADR-0020, ATLAS-ADR-0022, ATLAS-ADR-0023.
