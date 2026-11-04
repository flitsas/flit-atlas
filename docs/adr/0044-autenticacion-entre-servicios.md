---
id: ATLAS-ADR-0044
titulo: Autenticación entre servicios — usuario de RabbitMQ por servicio para la cola y token client_credentials de OpenIddict, firmado sin cifrar y con la audiencia del servicio, para las llamadas HTTP internas
estado: propuesto
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-03, A-07, §5.1, §6 y §6.3, ATLAS-ADR-0008, ATLAS-ADR-0018, ATLAS-ADR-0024, ATLAS-ADR-0036, ATLAS-ADR-0042, HU 4.5]
---

# ATLAS-ADR-0044 · Autenticación entre servicios

## Contexto y problema

El prompt maestro §5.1 exige que Go y Python reciban «llamadas internas autenticadas (mTLS o token de servicio)», y el
§6 pone en OpenIddict los tokens de los servicios internos (`client_credentials`). Hasta la HU 4.5, ninguna llamada
interna estaba autenticada:

- atlas-api llama a atlas-render (`POST /internal/v1/contratos/diff`, ATLAS-ADR-0036) sin credenciales; lo protege solo
  la `NetworkPolicy` del namespace.
- atlas-docs expone `POST /exportes` (ATLAS-ADR-0042, parte 1) con el mismo [por verificar].
- La cola (ATLAS-ADR-0018) ya prevé un usuario de RabbitMQ por servicio y que solo atlas-api publique en `flit.atlas`.

Hechos verificados en el código:

- OpenIddict 7.7.1 ya permite clientes confidenciales con `client_credentials` y una lista de `Audiencias` por cliente
  (`Identidad:Tokens:Clientes:<id>:Audiencias`, ATLAS-ADR-0024). La validación de atlas-api solo acepta la audiencia
  `atlas-api`, así que un token para otro servicio no sirve contra atlas-api (lo prueba `TokensTests`).
- OpenIddict **cifra** los tokens de acceso por defecto (JWE con la llave de cifrado del servidor). Un servicio externo
  no puede validarlos sin esa llave privada, y repartirla rompe el aislamiento.
- El JWKS (`/.well-known/jwks`) publica las llaves públicas de firma; con certificados de SealedSecret son estables y
  rotan publicando las dos (ATLAS-ADR-0024).
- Los módulos declaran sus scopes en `IModuloAtlas.Scopes` y la composición los registra en la identidad.

## Factores de decisión

- Nada de criptografía ni emisión de tokens propias (A-03): solo OpenIddict, RabbitMQ y una librería JWT madura.
- Que un servicio interno no pueda hablar como otro, ni un pod cualquiera del clúster llamar a atlas-docs.
- Que el servicio valide sin depender de atlas-api en cada petición (sin introspección) y que la rotación de llaves no
  obligue a redesplegar.
- Lo mismo para atlas-docs hoy y para atlas-ia y atlas-render después.

## Opciones consideradas

1. **Cola**: usuario de RabbitMQ por servicio con permisos por exchange. **HTTP**: token `client_credentials` de
   OpenIddict con la audiencia del servicio, firmado y sin cifrar (JWS), validado contra el JWKS.
2. mTLS entre pods (malla de servicios o certificados por servicio).
3. Tokens cifrados validados por introspección (`/connect/introspect`) en cada petición.
4. Un secreto compartido (HMAC) entre atlas-api y cada servicio.
5. Solo la `NetworkPolicy`.

## Decisión

Opción elegida: **1**.

- **Cola (lo que usa la HU 4.5).** atlas-api y atlas-docs no se llaman por HTTP para exportar: atlas-api publica
  `atlas.contratos.exportacion_solicitada` en `flit.atlas` y atlas-docs publica sus resultados en su propio exchange
  `flit.atlas.docs` (ATLAS-ADR-0042). La autenticación es la del broker: un usuario por servicio con permisos de
  escritura solo en su exchange y de lectura solo en sus colas (`atlas.docs.*` para atlas-docs, `atlas.api.*` para
  atlas-api). Así atlas-docs no puede publicar en `flit.atlas` ni atlas-api suplantar un resultado de atlas-docs.
- **HTTP interno (`POST /exportes` de atlas-docs hoy; atlas-render y atlas-ia después).**
  - El llamador es un cliente confidencial de OpenIddict con `client_credentials`, su secreto en un SealedSecret y
    `Audiencias: [atlas-docs]` (o la del servicio). Pide el scope del módulo dueño: `atlas.contratos.exportar` para
    atlas-docs (declarado por el módulo de contratos).
  - Un token de acceso cuya audiencia **no** incluye `atlas-api` sale **firmado y sin cifrar** (JWS): lo decide un
    manejador de OpenIddict (`TokensParaServicios`) justo después del que pone las credenciales; firma, emisión,
    registro del token y auditoría siguen siendo de OpenIddict. Los tokens de atlas-api siguen cifrados.
  - El servicio valida con una librería madura (en Python, PyJWT con `PyJWKClient`): firma por `kid` contra el JWKS de
    atlas-api con caché, solo RS256, PS256 o ES256, `iss` exacto, `aud` del servicio, `exp` y `nbf` (60 s de
    tolerancia) y el scope. Sin JWKS o emisor configurados, el endpoint rechaza todo (401): falla cerrado. Variables de
    atlas-docs: `ATLAS_DOCS_AUTH_JWKS_URL`, `ATLAS_DOCS_AUTH_EMISOR`, `ATLAS_DOCS_AUTH_AUDIENCIA` (`atlas-docs`) y
    `ATLAS_DOCS_AUTH_SCOPE` (`atlas.contratos.exportar`); las dos primeras son obligatorias en dev, qa y pdn.
  - La `NetworkPolicy` sigue como primera barrera: solo atlas-api entra a los servicios internos.

## Consecuencias

### Positivas

- La exportación no expone ningún endpoint nuevo: la cola tiene credenciales por servicio y separa productores.
- Los servicios validan sin red hacia atlas-api salvo para refrescar el JWKS; una llave rotada se publica antes de usarse.
- El mismo esquema sirve para atlas-render y atlas-ia: un cliente por llamador, una audiencia por servicio.

### Negativas

- Un token de servicio se puede leer si se filtra (no va cifrado). Dura como máximo 15 minutos, solo sirve para su
  audiencia y nunca lleva datos personales.
- Un cliente confidencial más por llamador, con su secreto y su rotación.
- Hoy ningún llamador pide el token para atlas-docs: `POST /exportes` queda para diagnóstico y para una exportación
  síncrona si el líder la habilita; la vía normal es la cola.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| 2. mTLS | k3s de Atlas no tiene malla de servicios; emitir y rotar certificados por servicio es más operación que un cliente de OpenIddict. Se puede sumar después sin cambiar los tokens. |
| 3. Introspección | Una llamada a atlas-api por petición y un cliente más en cada servicio; acopla la disponibilidad de los servicios a la de atlas-api. |
| 4. HMAC compartido | Criptografía casera y un secreto por par de servicios; no tiene audiencias, vencimiento ni rotación estándar. |
| 5. Solo NetworkPolicy | Es lo que había: cualquier pod con acceso a la red llama sin identidad (prompt maestro §5.1). |

## Más información

- Código: `services/api/src/Flit.Atlas.Identidad/Tokens/TokensParaServicios.cs`; scope en `ModuloContratos.Scopes`;
  validación en `services/docs/src/atlas_docs/autenticacion.py`. Pruebas: `TokensTests` (JWS con la audiencia del
  servicio, validado con el JWKS; el de atlas-api sigue cifrado) y `services/docs/tests/test_autenticacion.py`.
- [por verificar] En flit-gitops: los usuarios de RabbitMQ por servicio con sus permisos (`configure`, `write`, `read`)
  y la `NetworkPolicy` de atlas-docs; el cliente confidencial del llamador y su SealedSecret cuando haya uno.
- [por verificar] Migrar atlas-render (ATLAS-ADR-0036) al mismo esquema: hoy sigue sin token.
