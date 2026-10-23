# aspire · Orquestación local

AppHost de **.NET Aspire 13.6** que levanta en desarrollo los cinco servicios de Atlas y sus dependencias: Postgres 16 con pgvector, Redis, RabbitMQ y un S3 local.

## Requisitos

| Herramienta | Versión |
|---|---|
| .NET SDK | 10.0.400 (fijado en `global.json`) |
| Docker | Desktop o Engine en ejecución |
| Go | 1.26 |
| Python y uv | 3.13 y uv 0.11 |
| Node y pnpm | 22 y pnpm 11 |

## Arranque

```bash
dotnet run --project aspire/Flit.Atlas.AppHost
```

El panel de Aspire queda en `http://localhost:15031` (el enlace con el token sale en la consola).

| Recurso | Puerto local | Salud |
|---|---|---|
| `migrador` | — | Aplica las migraciones y termina; `api` arranca solo si terminó bien |
| `api` (atlas-api) | 4031 | `/health/ready`: Postgres, Redis y RabbitMQ |
| `web` (PWA, Vite) | 4032 | `/`; hace de proxy de `/api` hacia `api` (mismo origen) |
| `render` (atlas-render) | 4033 | `/health/ready` |
| `ia` (atlas-ia) | 4034 | `/health/ready`: Postgres con la extensión `vector` |
| `docs` (atlas-docs) | 4035 | `/health/ready`: bucket `atlas` en el S3 local |
| `correo` (Mailpit 1.31.4) | 4038 (interfaz y API); SMTP dinámico | `/livez`. Recibe los correos de invitación y recuperación (HU 1.5); puerto [por verificar con el líder] |
| `postgres`, `redis`, `rabbitmq`, `s3` | dinámicos | Los asigna Aspire, para no chocar con el Postgres de FLIT 2.0 |

Los puertos de los servicios son los de la propuesta F0.3 (`docs/preparacion/03-puertos-y-namespaces.md`).

## HTTPS local

La web sirve `https://localhost:4032` con el certificado de desarrollo de .NET, porque la cookie de sesión `__Host-atlas` es `Secure` y WebKit (iOS) no la guarda en `http`. Una sola vez por máquina:

```bash
dotnet dev-certs https --trust
```

Sin un certificado de confianza, Aspire avisa «No trusted Aspire development certificate was found» y la web queda en `http`.

## Ingreso de empleados

- **Proveedor simulado** (solo local y dev): botón «Entrar (simulado)» con cualquier correo de prueba, p. ej. `ana@ejemplo.com`. La API se niega a arrancar si se habilita en qa o pdn. La casilla «Con rol de administración» da `atlas.superadmin` (grupo de `Identidad__Simulado__GrupoAdministracion`, que solo fija el AppHost) para probar organizaciones e invitaciones.
- **Microsoft (Entra ID)**: usa el registro de **dev**. Los valores van en los user secrets del AppHost, que quedan en tu perfil y nunca en el repo:

```bash
dotnet user-secrets set "Entra:TenantId" "<id del directorio>" --project aspire/Flit.Atlas.AppHost
dotnet user-secrets set "Entra:ClientId" "<id de la aplicación>" --project aspire/Flit.Atlas.AppHost

# Credencial: un certificado .pfx (recomendado)...
dotnet user-secrets set "Entra:CertificadoRuta" "<ruta del .pfx>" --project aspire/Flit.Atlas.AppHost
dotnet user-secrets set "Parameters:entra-clave-certificado" "<clave del .pfx>" --project aspire/Flit.Atlas.AppHost
# ...o un secreto de cliente
dotnet user-secrets set "Parameters:entra-secreto" "<secreto>" --project aspire/Flit.Atlas.AppHost
```

El registro de Entra ID necesita la redirect URI `https://localhost:4032/api/auth/callback/azure-ad` (plataforma **Web**). Sin `Entra:TenantId`, solo aparece el proveedor simulado.

## Web Push

Sin configurar nada, atlas-api genera llaves VAPID efímeras en cada arranque (la PWA renueva sola la suscripción del dispositivo). Para llaves estables en tu máquina, genera un par con `node scripts/generar-vapid.mjs` y guárdalo en los user secrets del AppHost:

```bash
dotnet user-secrets set "Vapid:ClavePublica" "<clave pública>" --project aspire/Flit.Atlas.AppHost
dotnet user-secrets set "Parameters:vapid-clave-privada" "<clave privada>" --project aspire/Flit.Atlas.AppHost
```

El service worker (y con él el push) solo existe en el build de producción; ver `apps/web/README.md`.

## Servidor de tokens (OpenIddict)

atlas-api trae registrados `atlas-cli` y `atlas-mcp` (clientes públicos, código con PKCE y redirect de loopback en cualquier puerto). Sin configurar nada usa llaves de firma y cifrado efímeras: los tokens emitidos dejan de valer al reiniciar. El descubrimiento queda en `https://localhost:4032/.well-known/openid-configuration` (Vite lo reenvía a la API).

Para probar un servicio interno con `client_credentials`, declara un cliente confidencial en los user secrets del AppHost (el secreto, de 32 caracteres o más, va como parámetro y nunca en el repo):

```bash
dotnet user-secrets set "Tokens:Clientes:svc-local:Tipo" "Confidencial" --project aspire/Flit.Atlas.AppHost
dotnet user-secrets set "Tokens:Clientes:svc-local:Flujos:0" "client_credentials" --project aspire/Flit.Atlas.AppHost
dotnet user-secrets set "Tokens:Clientes:svc-local:Scopes:0" "atlas.publicar" --project aspire/Flit.Atlas.AppHost
dotnet user-secrets set "Parameters:token-svc-local-secreto" "$(openssl rand -hex 32)" --project aspire/Flit.Atlas.AppHost
```

Ver `services/api/README.md` (servidor de tokens) para el resto de variables.

## Pruebas de extremo a extremo

Con el AppHost corriendo:

```bash
cd apps/web
pnpm exec playwright install chromium webkit   # una vez
pnpm e2e
```

Corren en escritorio (Chromium), Pixel 7 (Chromium) e iPhone 14 (WebKit), y verifican el shell (HU 1.6), la accesibilidad con axe y que la sesión vive solo en la cookie `HttpOnly`, sin tokens en `localStorage` ni `sessionStorage`.

## Notas

- Postgres usa la imagen `pgvector/pgvector` con Postgres 16, como las VPS. `postgres-init/01-atlas.sql` crea la base `atlas` y las extensiones `vector` y `unaccent`, que en dev, qa y pdn crea el administrador. El volumen `flit-atlas-postgres` guarda los datos entre corridas; bórralo con `docker volume rm flit-atlas-postgres` para volver a ejecutar el script.
- El S3 local es **RustFS** 1.0.1 (Apache-2.0, compatible con la API de MinIO), porque MinIO dejó de publicar imágenes públicas en Docker Hub y quay.io (revisado el 21/10/2026). La clave se genera la primera vez y queda en los user secrets del AppHost. En dev y qa la decisión sigue abierta: ver la pregunta N-18 de `docs/preparacion/05-preguntas-abiertas.md`.
- Solo en local, `atlas-docs` crea su bucket (`ATLAS_DOCS_S3_CREAR_BUCKET`). El servicio rechaza esa bandera en cualquier otro ambiente.
- Cada servicio falla al arrancar, nombrando todas las variables obligatorias que faltan, si se corre fuera del AppHost sin configurarlo.

## Externos e invitaciones (HU 1.5)

1. Entra con el proveedor simulado marcando «Con rol de administración».
2. En «Más» (móvil) o en la barra lateral (escritorio): **Organizaciones** → crea una; **Invitaciones** → invita un correo `@ejemplo.com`.
3. Abre `http://localhost:4038` (Mailpit), abre el correo y sigue el enlace `https://localhost:4032/invitacion#…` en una ventana privada.
4. Crea la cuenta con una passkey (o con contraseña y una aplicación de autenticación) y guarda los códigos de recuperación.

En local, los vencimientos se procesan cada 10 s y los límites de tasa de ingreso son más holgados, para que las pruebas E2E corran en paralelo desde la misma IP. La imagen de Mailpit va fijada por digest.
