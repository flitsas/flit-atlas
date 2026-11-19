---
id: ATLAS-ADR-0037
titulo: Conversión del legado ICT — swagger de tsoa como base, Excel como enriquecimiento, un OpenAPI 3.1 por repo en contracts/legado/ y reglas de vacuum para lo compartible
estado: propuesto
fecha: 2026-10-23
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §8.4 y §12, plan 00 §2.2, §8.1, §8.6, §8.8 y §8.10, HU 3.8, ATLAS-ADR-0009, ATLAS-ADR-0010, ATLAS-ADR-0021]
---

# ATLAS-ADR-0037 · Conversión del legado ICT

## Contexto y problema

Los clientes integran hoy contra las APIs externas legadas (`BackApiExternal*`). Su contrato vive en dos lugares que
no coinciden: el `swagger.json` que genera tsoa en cada repo (título genérico, versión 1.0.0, esquemas vacíos o del
modelo interno) y el Excel `Contratos_Integraciones_31Jul2025.xlsx` (descripciones, longitudes, ejemplos, tablas de
referencia y obligatoriedad por tipo de trámite, pero con hosts mezclados, URL vacías y datos sensibles en los
ejemplos). El §8.4 pide un OpenAPI 3.1 enriquecido, en git, saneado y publicable por CI; Atlas no edita contratos.

Hechos verificados al convertir:

- Los 8 `BackApiExternal*` tienen `swagger.json` OpenAPI 3.0 de tsoa 5.1.1; todos con el título
  `api_express_typescript_boilerplate`, la versión 1.0.0 y un contacto personal.
- En `BackApiExternalTransact` la API recibe snake_case con `seller`, `buyer` y `lessee` y lo pasa a camelCase con el
  mapeador `ToCamelCase`; el DTO que publica tsoa describe el modelo interno. Las rutas se registran a mano en
  Express, no con las rutas de tsoa.
- La carga de adjuntos recibe `multipart/form-data` (multer); tsoa declara `application/json` vacío.
- El Excel no trae URL de dev ni de pdn; SearchPlate, StatusPlate y WebHookPlate no tienen servicio en ningún repo;
  RequestSign y StatusSign están en el índice y no tienen hoja.

## Factores de decisión

- Git es la fuente de verdad (§2) y el resultado tiene que ser reproducible sin editar a mano.
- Nada sensible llega a git ni a un cliente: el Excel no se versiona y el spec no trae tokens, correos reales, rutas
  locales, IP privadas, hosts internos ni nombres de personas.
- No adivinar: lo que el Excel y el código no confirman va al informe para el líder, no al spec.
- Lo compartible tiene que servir al sandbox (Microcks usa los ejemplos, plan 00 §8.8) y a la poda (plan 00 §8.6).
- No tocar los repos legados sin permiso del líder (§1, regla 4c).

## Opciones consideradas

### Base del spec

1. Swagger de tsoa como base y el Excel como enriquecimiento; el Excel manda solo donde tsoa no describe la API real
   (esquema vacío o DTO interno), y cada caso queda en el informe.
2. Spec escrito desde el Excel, sin tsoa.
3. Corregir los controladores para que tsoa genere el spec completo.

### Dónde vive el resultado

1. Un OpenAPI 3.1 por repo en `contracts/legado/` de flit-atlas, más el PR propuesto para cada repo legado como
   archivos de propuesta.
2. Directamente en cada repo legado.

### Obligatoriedad por tipo de trámite

1. `oneOf` de variantes (`allOf` del esquema base con `const` en `transaction_type`) y la matriz completa en
   `x-flit-obligatoriedad`.
2. `discriminator` de OpenAPI sobre `transaction_type`.
3. Solo la extensión, sin esquema.

## Decisión

- **Base:** opción 1. El script `atlas_docs.legado` (en `services/docs`, Python con openpyxl y PyYAML) lee todas las
  hojas sin depender de sus nombres (se guía por las etiquetas «Ambiente», «Parametros», «Salida», «Ejemplo»,
  «Tabla …») y aplica un mapeo explícito hoja → repo y operación (`mapeo.py`). Cada decisión que no sale del código ni
  del Excel (alias de campos, cuerpos tomados del Excel, ruta de StatusTransaction) tiene su motivo en el mapeo y en
  el informe.
- **Qué aporta el Excel:** descripciones; longitudes y rangos (`String(12)` → `maxLength`, `number(2)` → entero de 0
  a 99, `number(17,2)` → máximo y `x-flit-decimales`); ejemplos saneados y ajustados al esquema; códigos de respuesta
  (descripciones de la tabla del Excel; 401, 404 y 500 donde el Excel muestra el error); tablas de referencia como
  componentes `enum` con `x-enum-descriptions` y `x-flit-tabla`; webhooks en la sección `webhooks` de OpenAPI 3.1;
  `info.x-flit-changelog` con versión, fecha y hoja de cada hoja.
- **Obligatoriedad:** opción 1. El `discriminator` no es viable: `transaction_type` es numérico y la tabla de tipos de
  trámite tiene más valores que las cuatro columnas de obligatoriedad. Las reglas condicionales («si el vendedor es un
  NIT») quedan como texto en la extensión. La guía de adjuntos por tipo de trámite va igual en
  `DocumentoAdjunto.x-flit-obligatoriedad`.
- **Visibilidad:** lo documentado en el Excel lleva `x-public: true`; el resto del swagger, `x-internal: true` (la poda
  lo quita). `info.x-flit-visibilidad` dice si el contrato es compartible o interno. WebHookPlate queda interno con
  `x-flit-pendiente` hasta que el líder confirme su servicio.
- **Servidores:** solo el host de QA que trae el Excel, como variable de `https://{host}`, si es un host público de
  `flitsas.online`/`flitsas.com`. dev y pdn quedan «por confirmar» en `info.x-flit-ambientes` y en el informe; los
  hosts de API Gateway y el mock local nunca llegan al spec.
- **Saneamiento:** por patrón (JWT, valor después de `Bearer`, correos que no son `@ejemplo.com`, rutas locales, IP
  privadas, hosts internos) y por nombre de campo en los ejemplos (contraseñas y tokens → `$CLAVE`/`$TOKEN`; personas,
  empresas, documentos, teléfonos, placas y VIN → valores ficticios). Una última pasada recorre todo el árbol y la
  escritura falla si algo sobrevive. El informe cuenta lo sensible del Excel sin copiarlo.
- **Colaborador:** el changelog dice «Equipo de integraciones de FLIT» en lugar del nombre de la persona: los specs se
  comparten con clientes y el nombre es un dato personal (Ley 1581, minimización del §12).
- **Dónde vive:** opción 1 mientras el líder decide (§8.4, punto 9). `contracts/legado/propuestas/<repo>/` trae el
  `tsoa.json` corregido (título, versión semántica, descripción y contacto genérico) y el
  `.github/workflows/atlas-publicar.yml` que llama al workflow reutilizable previsto de la HU 3.3
  (`flitsas/flit-atlas/.github/workflows/atlas-publicar.yml@v1`, versión por verificar).
- **Lo compartible en vacuum:** `contracts/vacuum/openapi-compartible.yaml` extiende las reglas de los contratos
  propios y agrega: ejemplos obligatorios (en cada petición y respuesta, y `oas3-missing-example` y
  `oas3-valid-schema-example` como error), versión semántica, título propio, visibilidad declarada, servidores sin
  hosts internos y seguridad en toda operación pública salvo la que entrega el token. El job `contratos` lo corre con
  vacuum 0.32.0 sobre `contracts/legado/*.yaml`; el job `python` corre las pruebas del script con fixtures ficticias y
  la prueba que recorre los specs versionados buscando datos sensibles y comprobando la trazabilidad.

## Consecuencias

### Positivas

- Los contratos que usan los clientes quedan en git, versionados, validados y listos para publicar por CI.
- Cada campo del Excel tiene destino comprobable (`trazabilidad.json`): un puntero en un spec o un motivo en el
  informe.
- El líder recibe una lista concreta de lo que hay que confirmar en lugar de URL o reglas adivinadas.

### Negativas

- El spec se regenera desde una copia local del Excel: el CI no puede comprobar que esté al día con el Excel, solo
  que lo versionado es válido y está saneado.
- Mientras el spec viva en flit-atlas y no en cada repo, un cambio en un controlador legado no actualiza el contrato
  hasta que alguien regenere.
- Los ejemplos ficticios generados desde el esquema (para las operaciones internas) son genéricos; sirven al sandbox
  pero no explican el negocio.
- Las rutas de los legados se registran a mano en Express: el swagger de tsoa puede no coincidir con lo que corre.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Spec escrito desde el Excel | Pierde lo que el código sí declara (rutas, seguridad, esquemas) y no se puede regenerar desde el repo |
| Corregir los controladores para tsoa | Cambia repos legados sin permiso del líder; queda como mejora en los PR propuestos |
| Directo en cada repo legado | Requiere PR en 8 repos; se deja como propuesta hasta que el líder decida (§8.4, punto 9) |
| `discriminator` sobre `transaction_type` | Propiedad numérica y tabla compartida con otros trámites; los generadores lo manejan mal |
| Solo `x-flit-obligatoriedad` | No valida nada: el `oneOf` deja que vacuum y el sandbox comprueben los ejemplos por tipo |

## Más información

- Informe de la conversión: [`contracts/legado/informe.md`](../../contracts/legado/informe.md).
- [por verificar] URL vigentes de dev, qa y pdn; servicio de SearchPlate, StatusPlate y WebHookPlate; existencia de
  RequestSign y StatusSign; qué token envía FLIT en los webhooks.
- [por verificar] Nombre y versión fijada del workflow reutilizable (HU 3.3) y la variable `ATLAS_URL`.
- Relacionados: ATLAS-ADR-0009 (publicación con OIDC de GitHub), ATLAS-ADR-0010 (oasdiff y vacuum), ATLAS-ADR-0021
  (contratos propios).
